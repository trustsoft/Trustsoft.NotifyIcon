# M002/S01 measurement: toast identity and delivery, measured live

Scope: M002/S01/T01. This document is the observability artifact for the slice: it records, on this
machine, the two contracts the whole milestone rests on — how an unpackaged WPF process obtains a
usable AppUserModelID, and which hand-written WinRT activation-factory calls create and show a toast
and deliver a click back into the running process. It is the input to every later task in the slice.

Instrument: `scripts/probe-toast/` (ProbeToast.csproj + Program.cs + run.ps1), a throwaway probe
outside the solution that takes no reference to the library and hand-writes every COM and WinRT
declaration. All evidence below is the raw output of that probe, re-runnable with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run.ps1`.

## Environment

| Field | Value |
| --- | --- |
| OS | Microsoft Windows NT 10.0.26200.0 (Windows 11 24H2), machine `MINIBOOKX` |
| .NET | SDK 8.0.425, probe targets `net8.0-windows`, runs x64 |
| Session | T01's probe ran from an agent shell; **T05 corrected this row by measuring the session itself**: window station `WinSta0`, desktop `Default`, `Shell_TrayWnd` (the taskbar) present, 1920x1200 at 150% scale, `LogonUI` absent - an interactive console session with an active user. T01's "no interactive foreground" described the probe's own process, not the session. `RoInitialize(RO_INIT_SINGLETHREADED)` on the already-MTA thread returns `0x80010106` (`RPC_E_CHANGED_MODE`) |
| Windows SDK ABI reference | C++/WinRT headers `Windows Kits\10\Include\10.0.26100.0` and the `10.0.26100.0` winmds (authoritative GUID/vtable source) |

## Contract 1 — AppUserModelID registration (identity)

**Mechanism.** A per-user Start-menu shortcut whose `System.AppUserModelID` property
(`PKEY_AppUserModel_ID` = `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5`) carries the identity. It is
written through hand-declared COM interop: `CoCreateInstance(CLSID_ShellLink)` → `IShellLinkW`
(`SetPath`/`SetDescription`/`SetIconLocation`/`SetArguments`) → `IPropertyStore`
(`SetValue` VT_LPWSTR + `Commit`) → `IPersistFile.Save`, then read back from a *fresh* shell link.

**Measured (raw):**

```
identity: CoCreateInstance(CLSID_ShellLink) hr=0x00000000
identity: IShellLinkW.SetPath hr=0x00000000; SetDescription hr=0x00000000; SetIconLocation hr=0x00000000; SetArguments hr=0x00000000
identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x00000000
identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x00000000
identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, 'Trustsoft.NotifyIcon.ToastProbe') hr=0x00000000
identity: IPropertyStore.Commit hr=0x00000000
identity: IPersistFile.Save('…\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk') hr=0x00000000
identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
identity: AppUserModelID after write = 'Trustsoft.NotifyIcon.ToastProbe'
identity: read-back matches expected 'Trustsoft.NotifyIcon.ToastProbe' = True
```

**Ordering is the contract's one trap.** The property must be written and committed **before**
`IPersistFile.Save`: `Save` serialises the shortcut to the `.lnk` file, including the property-store
section, so a property written after `Save` stays in memory and never reaches the file. Measured: the
wrong order produced `read-back GetValue vt=0` and the AUMID string was absent from the `.lnk` bytes;
the corrected order reads back `vt=31` (VT_LPWSTR) with the exact value.

## Contract 2 — WinRT activation-factory path (toast show + activation)

**Mechanism.** `RoGetActivationFactory` for the two static runtime classes and `RoActivateInstance`
for the XML document, then the object calls over the raw vtables. The exact sequence the library must
later reproduce is:

```
RoGetActivationFactory("Windows.UI.Notifications.ToastNotificationManager", IToastNotificationManagerStatics)
RoGetActivationFactory("Windows.UI.Notifications.ToastNotification",       IToastNotificationFactory)
RoActivateInstance("Windows.Data.Xml.Dom.XmlDocument")
IToastNotificationManagerStatics.CreateToastNotifierWithId(aumid)          -> IToastNotifier
IToastNotifier.GetSetting                                                  -> NotificationSetting
QueryInterface(XmlDocument, IXmlDocumentIO);  IXmlDocumentIO.LoadXml(xml)
IToastNotificationFactory.CreateToastNotification(xmlDoc)                  -> IToastNotification
IToastNotification.add_Activated / add_Dismissed / add_Failed              (WinRT delegates)
IToastNotifier.Show(toast)
IToastNotification.remove_Activated / remove_Dismissed / remove_Failed
```

**Measured (raw):**

```
factory: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
factory: RoGetActivationFactory(ToastNotification -> IToastNotificationFactory) hr=0x00000000
factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
show: CreateToastNotifierWithId('Trustsoft.NotifyIcon.ToastProbe') hr=0x00000000
show: notifier.GetSetting hr=0x00000000 value=0 (Enabled)
show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x00000000
show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x00000000
show: IXmlDocumentIO.LoadXml hr=0x00000000
show: CreateToastNotification(xml) hr=0x00000000
subscribe: add_Activated hr=0x00000000 token=0x…; add_Dismissed hr=0x00000000; add_Failed hr=0x00000000
show: notifier.Show(toast) hr=0x00000000
teardown: remove_Activated hr=0x00000000; remove_Dismissed hr=0x00000000; remove_Failed hr=0x00000000
```

The toast XML payload is a `ToastGeneric` binding with `<toast launch="probe-activation">`, so a body
click would deliver `Arguments = "probe-activation"`.

## Out-of-process observation

Delivery is observed from a **separate process invocation** (`probe-toast --history <aumid>`), which
queries `IToastNotificationManagerStatics2.get_History` → `IToastNotificationHistory2.GetHistoryWithId`
and reads the `IVectorView<ToastNotification>.get_Size` count:

```
history: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics2) hr=0x00000000
history: get_History hr=0x00000000
history: QueryInterface(History -> IToastNotificationHistory2) hr=0x00000000
history: GetHistoryWithId('Trustsoft.NotifyIcon.ToastProbe') hr=0x00000000
history: IVectorView.get_Size hr=0x00000000 count=2
```

`count=2` after two positive runs: the toast the probe shows is present in the shell's Action Center
for the registered identity. `--clear-history` (`IToastNotificationHistory.ClearWithId`) returns the
count to 0, which is the clean-state precondition.

## Activation callback — what was and was NOT captured

> **Superseded by T05** (see the last section): the activation *was* captured in process, on the
> windowless sample, for the registered identity. This section is kept as the record of what the
> throwaway probe measured, and of the reasoning that was wrong about it: the probe's
> `--wait-seconds` window was shorter than the platform's own activation latency, so "no
> callback" was an artefact of the probe exiting too early.

The three event subscriptions (`add_Activated`/`add_Dismissed`/`add_Failed`) all return `S_OK` with
real tokens, so the subscription is live. **No activation callback was captured in-process**, because
an activation is only produced by a real click on the toast banner, and in this session the banner is
not reachable by an automated instrument: a UIAutomation `FindFirst` for the toast's title and body
text over `AutomationElement.RootElement` returned nothing (`TOAST NOT FOUND`), matching the existing
M001/S06 finding F1 ("a click the OS never delivers to this process cannot be observed"). The body
click therefore remains a human follow-up (it is the T05 end-to-end demonstration), and the activation
*delivery scope* is the one MEM174 already fixed: in-process only while the app is running.

## Negative control, and the finding it produced

The control runs the **same show path with the identity never registered** (`--skip-register`, a
never-used AUMID). Measured result:

```
show: notifier.Show(toast) hr=0x00000000
result: activated=0 dismissed=0 failed=0
history verdict: count=1 for 'Trustsoft.NotifyIcon.ToastProbe.Negative'
```

**Finding.** On this machine `CreateToastNotifierWithId` + `Show` succeed (`S_OK`) and the toast
*still* lands in the Action Center (`count=1`) even for an identity that was never registered. The
shortcut/AUMID registration is therefore **not a precondition for toast delivery**; what registration
gates is **activation routing** (which the unregistered run demonstrably did not produce —
`activated=0`). Two consequences for the later tasks: (a) the negative control cannot be judged from
the `Show` HRESULT — it must be judged from the absence of an activation callback; (b) deleting a
shortcut does **not** immediately unregister the identity from the shell's notification state
(measured: a just-deleted shortcut's AUMID still received a toast into history), so a "clean" negative
control must use an identity that was never registered at all.

## Interop gotchas that the library implementation (T03/T04) must carry

1. **The RCW path mis-dispatches WinRT interfaces.** Declaring a `[ComImport]` interface that
   *inherits* `IInspectable`, then `Marshal.GetObjectForIUnknown` + cast, produces wrong vtable
   dispatch on this runtime (measured: `CreateToastNotifierWithId` returned `E_OUTOFMEMORY`,
   `get_History` faulted with `AccessViolationException`). The working path is **raw vtable dispatch**:
   read the vtable slot (`IUnknown` 3 + `IInspectable` 3 + method index) and
   `Marshal.GetDelegateForFunctionPointer` it. Plain COM interfaces that do *not* inherit anything
   (`IShellLinkW`, `IPersistFile`, `IPropertyStore`) dispatch correctly through the ordinary RCW path.
2. **WinRT versioned interfaces are flat, not inherited.** `IToastNotification2`,
   `IToastNotificationManagerStatics2`, `IToastNotifier2`, `IVectorView<T>`, etc. each inherit
   `IInspectable` directly; their vtables contain *only their own* new methods. `IVectorView<T>` does
   **not** inherit `IIterable<T>` at the ABI level, so its `get_Size` is slot 7, not 8. The exact
   vtables and GUIDs used here come from the 26100 C++/WinRT headers / winmds.
3. **`ToastNotifier.Setting` returns `E_NOT_FOUND` (0x80070490) on first use** for a freshly-registered
   identity (no notification-setting entry exists yet), then `S_OK`; the value is `0` (Enabled) in both
   cases. Do not treat the first-use error code as a failure.
4. **`RoInitialize` on the default MTA thread returns `RPC_E_CHANGED_MODE`** and is benign —
   `RoGetActivationFactory`/`RoActivateInstance` still succeed. Do not gate on its HRESULT.

## What did NOT work

- The activation callback could not be captured in this session: no automated click reaches the toast
  banner (UIAutomation scan returned nothing), so the in-process activation payload remains
  unobserved until a human click (T05's demonstration) exercises it.
- The "no registration → no toast" hypothesis of the slice's negative control is **false on this
  machine**: the toast still reaches the Action Center. The real negative-control signal is the
  absence of an activation callback.

## Files

- `scripts/probe-toast/ProbeToast.csproj` — the probe project (absent from the solution, no package/projection references).
- `scripts/probe-toast/Program.cs` — the instrument (shortcut COM interop + raw-vtable WinRT interop).
- `scripts/probe-toast/run.ps1` — the orchestrator (clean state, positive control, negative control, out-of-process inventory).

## T03 re-measurement: the library's own read-back (ShortcutLink + ToastIdentity)

T03 moved the measured shortcut/AUMID write into the library's own interop layer
(`src/Trustsoft.NotifyIcon/Interop/ShortcutLink.cs` + `ToastIdentity.cs`), re-implementing the
Contract 1 sequence with raw-vtable dispatch over `IShellLinkW`/`IPropertyStore`/`IPersistFile`
(no RCW, no `[ComImport]` projection; `Marshal.Release` owns the references). The probe's read-back
was re-measured on this machine with that library code, not with the throwaway instrument, because a
shortcut that exists but carries no property is the silent failure this slice exists to rule out.

Raw capture (`ToastIdentityLiveProbeTests`, run on `MINIBOOKX`, Windows NT 10.0.26200.0):

```
[live] identity: shortcut=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.T03.LiveProbe.lnk
[live] identity: register aumid='Trustsoft.NotifyIcon.T03.LiveProbe' success=True operation='' code=0x00000000
[live] identity: read-back success=True value='Trustsoft.NotifyIcon.T03.LiveProbe'
[live] identity: read-back matches expected = True
[live] identity: remove success=True operation='' code=0x00000000
[live] identity: read-back after remove success=False operation='OpenShellLink' code=0x80070002
```

- The write (create shortcut → `IPropertyStore.SetValue(PKEY_AppUserModel_ID, VT_LPWSTR)` → `Commit`
  → `IPersistFile.Save`) reads back the exact value from a **fresh** shell link, reproducing the
  probe's `vt=31` result with the shipped interop. The library path confirms the measurement the
  probe made by hand.
- The read-back after removal fails at `OpenShellLink` with `0x80070002` (`ERROR_FILE_NOT_FOUND`):
  the identity carrier is gone - the "shortcut absent" case, distinct from the "shortcut present but
  carries no property" case that the mismatch check in the T03 contract tests (`ToastIdentityTests`)
  covers deterministically.

## T04 re-measurement: the library's own show path (ToastApi + ToastShow)

T04 moved the measured Contract-2 sequence into the library's own interop layer
(`src/Trustsoft.NotifyIcon/Interop/ToastApi.cs`: raw-vtable `RoGetActivationFactory` /
`RoActivateInstance` / `Windows.UI.Notifications` dispatch, hand-written HSTRING marshalling and a
COM-callable `TypedEventHandler`), with the sequence itself orchestrated by `ToastShow` over the
`IToastApi` seam and the payload rendered by `ToastPayload`. Both halves of the seam are now the
library's own code: `ShortcutLink` owns Contract 1, `ToastApi` owns Contract 2 and delegates
Contract 1 to `ShortcutLink`.

**Why a live run was still required.** A mocked `S_OK` proves the call sequence, not that Windows
accepted the toast; only a real run exercises the vtables, the HSTRING marshalling and the WinRT
event delegates against the actual runtime. The live proof is
`ToastApiLiveProbeTests.The_show_path_hands_a_real_toast_to_the_shell_for_a_registered_identity`
(run explicitly; it is a separate class from the deterministic `ToastApiContractTests` so the
contract filter stays shell-free), and the delivery is then observed from a **separate process**
with the probe's inventory mode.

Raw capture (`MINIBOOKX`, Windows NT 10.0.26200.0, .NET SDK 10.0.303, net8.0-windows):

```
[live] t04: register aumid='Trustsoft.NotifyIcon.T04.LiveProbe' success=True operation='' code=0x00000000
[live] t04 trace: toast identity: register attempt aumid='Trustsoft.NotifyIcon.T04.LiveProbe' shortcut='…\Programs\Trustsoft.NotifyIcon.T04.LiveProbe.lnk'
[live] t04 trace: toast identity: register succeeded aumid='Trustsoft.NotifyIcon.T04.LiveProbe'
[live] t04 trace: toast show: begin aumid='Trustsoft.NotifyIcon.T04.LiveProbe' title='Trustsoft.NotifyIcon T04 live probe' launch='t04-live-activation'
[live] t04 trace: toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001            (S_FALSE: already initialized)
[live] t04 trace: toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[live] t04 trace: toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[live] t04 trace: toast: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[live] t04 trace: toast: CreateToastNotifierWithId('Trustsoft.NotifyIcon.T04.LiveProbe') hr=0x00000000
[live] t04 trace: toast show: GetNotifierSetting hr=0x80070490 setting=0 (E_NOT_FOUND on first use is benign)
[live] t04 trace: toast show: LoadXml hr=0x00000000 xml=<toast launch="t04-live-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon T04 live probe</text><text>The library's own ToastApi + ToastShow handed this to the shell.</text></binding></visual></toast>
[live] t04 trace: toast show: subscribed activated=0xC3DF70570088CBD5 dismissed=0xAA91A327844029FD failed=0xD63F6312BB164642
[live] t04 trace: toast show: Show hr=0x00000000 (accepted by the shell; delivery is judged out of process)
[live] t04 trace: toast show: dispose
[live] t04 trace: toast show: UnsubscribeActivated(0xC3DF70570088CBD5) hr=0x00000000
[live] t04 trace: toast show: UnsubscribeDismissed(0xAA91A327844029FD) hr=0x00000000
[live] t04 trace: toast show: UnsubscribeFailed(0xD63F6312BB164642) hr=0x00000000
[live] t04: show success=True operation='' code=0x00000000 setting=0 aumid='Trustsoft.NotifyIcon.T04.LiveProbe'
[live] t04: after dispose shown=True (unsubscribed and released; no click was injected, so activated=False)
[live] t04: remove success=True operation='' code=0x00000000
```

Out-of-process observation, a **separate process invocation** of the probe inventory against the
same identity, run after the live test (and then cleared, so the machine is left as it was found):

```
history: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics2) hr=0x00000000
history: get_History hr=0x00000000
history: QueryInterface(History -> IToastNotificationHistory2) hr=0x00000000
history: GetHistoryWithId('Trustsoft.NotifyIcon.T04.LiveProbe') hr=0x00000000
history: IVectorView.get_Size hr=0x00000000 count=1
history verdict: count=1 for 'Trustsoft.NotifyIcon.T04.LiveProbe'
--- after --clear-history ---
history verdict: count=0 for 'Trustsoft.NotifyIcon.T04.LiveProbe'
```

**What the live run confirmed.**

1. The library's own activation-factory acquisition works: both factories and the XML document are
   obtained with `S_OK`, and the notifier is created with the explicit identity
   (`CreateToastNotifierWithId`), which is the only overload an unpackaged process can rely on.
2. `IXmlDocumentIO.LoadXml` accepts the payload `ToastPayload` renders, and the exact string the
   shell received is in the trace above (the `launch` attribute is the argument a body click would
   deliver).
3. All three events subscribe with real, non-zero tokens, and all three unsubscribe with `S_OK`
   before any handle is released - the teardown leaves no subscription behind.
4. `Show` returned `S_OK` and the toast is present in the shell's Action Center (`count=1`) as
   observed from a different process. `S_OK` here means *accepted*, not *seen* (see the negative
   control above, where an unregistered identity also reached the Action Center).
5. The measured first-use quirk reproduced live: `IToastNotifier.GetSetting` returned
   `0x80070490` (`E_NOT_FOUND`) for the freshly registered identity and the show continued, which is
   the benign case the seam documents. The library reports it in the trace and does not treat it as
   a failure.
6. `RoInitialize(RO_INIT_SINGLETHREADED)` returned `S_FALSE` on this STA test thread (already
   initialized); the code does not gate on it for the reason Contract 2's gotcha 4 records.

**What was NOT captured, still.** No activation callback fired, because no automated instrument can
click the toast banner on this machine (T01's finding stands). The activation *payload* therefore
remains unobserved by automation; it is the human demonstration of T05. What T04 proves about the
callback is the subscription mechanism itself: the three handlers subscribe and unsubscribe with
live tokens, and the in-process callback path is proven deterministically by the fake raising the
events (`Subscribed_handlers_receive_activation_dismissal_and_failure`).

**The other half of the evidence.** `ToastApiContractTests` (29 tests, `net8.0-windows`) pins what a
live shell cannot do: the exact measured acquisition and teardown sequence, the payload
string handed to `LoadXml`, the notifier bound to the caller's identity, the subscribe-before-show
and unsubscribe-before-release ordering, the failure-as-data unwinding at every step (each of the
11 steps is scripted to fail, and each unwind releases exactly the handles acquired and removes
exactly the subscriptions registered), the idempotent disposal, and the payload's escaping and
omission rules.

---

## T05 end-to-end: the windowless sample, the registration, and the activation that arrived

T05 wired the slice's exit condition into `samples/Trustsoft.NotifyIcon.Sample/App.xaml.cs` and ran it
on this machine. The demonstration is `--toast`: register the identity, show a toast from a process
with no visible window, subscribe to `Activated`/`Dismissed`/`Failed`, print what arrives, and
unregister on the way out. S01 has no public toast surface (that is S02/S03 work), so the sample
drives the internal seam through an `InternalsVisibleTo("Trustsoft.NotifyIcon.Sample")` grant in
`src/Trustsoft.NotifyIcon/Properties/AssemblyInfo.cs` - a grant to a non-shipping project in this
repository, documented there with its own removal condition.

### The runs, and how to reproduce them

```text
dotnet build samples/Trustsoft.NotifyIcon.Sample/Trustsoft.NotifyIcon.Sample.csproj -c Release

# positive: registered identity, repeated so a banner is available to click
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-after 5 --toast-repeat 8 --run-seconds 30

# negative control: same show path, identity never registered
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-aumid Trustsoft.NotifyIcon.Sample.NegativeControl --toast-skip-register \
    --toast-after 3 --toast-repeat 5 --run-seconds 26

# out of process delivery inventory (separate process, same identity)
dotnet scripts/probe-toast/bin/Release/net8.0-windows/probe-toast.dll --history Trustsoft.NotifyIcon.Sample

# windowless/alive observation, and a body click at the banner's measured position
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/observe-sample.ps1 -Seconds 32
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/click-toast-body.ps1 -DelaySeconds 7 -Clicks 3
```

### The canonical capture (registered identity, one run)

```
17:45:55 [sample] toast demonstration: identity='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' register=True repeat=every 8s
17:45:56 [sample] toast registration: success=True operation='' code=0x00000000 aumid='Trustsoft.NotifyIcon.Sample'
17:45:56 [sample] toast registration read-back (fresh shell link): success=True operation='' code=0x00000000 value='Trustsoft.NotifyIcon.Sample' matchesExpected=True
17:46:00 [sample] toast show #1: asking the shell for identity='Trustsoft.NotifyIcon.Sample' launch='sample-toast-1' title='Trustsoft.NotifyIcon sample toast'
17:46:00 [sample] toast show #1: the shell accepted it (setting=0 raw; S_OK is acceptance, not visibility)
17:46:03 [sample] toast show #2: the shell accepted it (...)
17:46:07 [sample] toast dismissed: reason=2 (timed out)
17:46:11 [sample] toast show #3: the shell accepted it (...)
17:46:14 [sample] toast dismissed: reason=2 (timed out)
17:46:19 [sample] toast show #4: the shell accepted it (...)
17:46:22 [sample] toast dismissed: reason=2 (timed out)
17:46:25 [sample] toast activated: arguments='sample-toast-4' (activation 1 of 4 show(s))
17:46:25 [sample] toast teardown: 4 show(s) unsubscribed and released - no activation subscription outlives the process.
17:46:25 [sample] toast unregistration: remove success=True operation='' code=0x00000000; read-back after remove success=False operation='OpenShellLink' code=0x80070002
17:46:26 [sample] toast totals: shows=4, accepted=4, activations=1 (last arguments='sample-toast-4'), dismissals=3, failures=0, registered=True, identity='Trustsoft.NotifyIcon.Sample'.
```

Out-of-process inventory, a **separate process** invoked twice during that run:

```
=== t+12s ===  history: IVectorView.get_Size hr=0x00000000 count=2   -> count=2 for 'Trustsoft.NotifyIcon.Sample'
=== t+22s ===  history: IVectorView.get_Size hr=0x00000000 count=3   -> count=3 for 'Trustsoft.NotifyIcon.Sample'
```

### Windowless and alive, measured from outside the process

`scripts/probe-toast/observe-sample.ps1` enumerated the sample's top-level windows once a second for
the whole run of a second capture:

```
observer: start 17:45:54.569 watching process name 'Trustsoft.NotifyIcon.Sample' for 34s
observer: t+1s  pid=11600 alive=True workingSet=78MB  windows=7 visible=0
observer: t+3s  pid=11600 alive=True workingSet=168MB windows=8 visible=0
observer: t+15s pid=11600 alive=True workingSet=175MB windows=8 visible=0
observer: t+23s pid=11600 alive=True workingSet=176MB windows=8 visible=0
observer: t+31s pid=11600 alive=True workingSet=176MB windows=6 visible=0
```

Exactly one process, alive for the whole run, owning its (hidden) host and WPF windows with **zero
visible top-level windows**. The exit condition "the process stays alive with no window" is therefore
a reading from another process, not a claim by the sample about itself.

### The activation callback, and what the capture does and does not attribute

Two further runs were made with **no instrument injecting any input at all** (only the history was
cleared beforehand):

```
clean run 1  shows=4 accepted=4 activations=4 (arguments 'sample-toast-1'..'sample-toast-4') dismissals=0
17:46:59 activated 'sample-toast-1'   17:47:03 activated 'sample-toast-2'
17:47:10 activated 'sample-toast-3'   17:47:17 activated 'sample-toast-4'

clean run 2  shows=4 accepted=4 activations=1 (arguments 'sample-toast-1') dismissals=2
17:47:33 activated 'sample-toast-1'   17:47:41 dismissed (timed out)   17:47:48 dismissed (timed out)
```

Across all runs of the registered identity this machine delivered 10 activations, each carrying the
exact `launch` argument of the toast it came from - the slice's exit condition, in the process's own
words. Three things are measured about *how* they arrived, and the document records the limits of the
attribution rather than a story:

1. **They arrive without any input this session injected.** Clean runs 1 and 2 had no click
   instrument running; the sample printed 5 activations. So an activation is not proof that *our*
   instrument clicked.
2. **An injected click does not reliably land on the banner.** `click-toast-body.ps1` injected three
   real clicks (`sendInputEvents=2` each) at the banner's position derived from the work area
   (`workArea=0,0,1920,1128`, click point `1654,1006`), and recorded the screen change across each
   click (`prePostChangedPixels=19494`, `88759`, `0`). In that run four activations arrived, one of
   them 0.4s *before* the third click - so the injected clicks cannot be credited with all of them.
3. **The candidate mechanisms cannot be separated from this capture.** This is an interactive session
   with an active user (foreground window `CASCADIA_HOSTING_WINDOW_CLASS`, i.e. a terminal), and a
   banner sits under the mouse pointer whenever the pointer is parked in the notification-area
   corner. Whether each activation was a click by that user or an activation the shell generated
   itself is not something these recordings can distinguish.

**An activation may therefore arrive for a toast that is still displayed, and without a body click.**
For S02/S03 this is a contract fact, not a curiosity: the public `Activated` event cannot be
specified as "the user clicked the body". What the activation *does* carry, deterministically, is the
`launch` argument of the toast it came from - which is what identifies the interaction, and why the
sample prints it next to the show that created it.

### The negative control, measured on the sample's own path

```
17:40:03 [sample] toast demonstration negative control: no shortcut is created; read-back success=False operation='OpenShellLink' code=0x80070002 value='(null)' carriesTheIdentity=False
17:40:06 [sample] toast show #1: asking the shell for identity='Trustsoft.NotifyIcon.Sample.NegativeControl' launch='sample-toast-1'
17:40:06 [sample] toast show #1: the shell accepted it (setting=0 raw; S_OK is acceptance, not visibility)
   ... shows #2..#6, all accepted, at 5s intervals, verbatim the same path ...
17:40:30 [sample] toast totals: shows=6, accepted=6, activations=0 (last arguments='(none)'), dismissals=0, failures=0, registered=False, identity='Trustsoft.NotifyIcon.Sample.NegativeControl'.
```

Out of process, the same identity's toasts *were* delivered:

```
history: GetHistoryWithId('Trustsoft.NotifyIcon.Sample.NegativeControl') hr=0x00000000
history: IVectorView.get_Size hr=0x00000000 count=2
```

**The documented failure is silence, and it is now measured over six shows.** An identity that was
never registered gets `S_OK` from every step, its toasts reach the Action Center, and the process
receives **no lifecycle callback at all** - no activation, and in this run not even a timeout
dismissal, while the registered identity's runs reported both (`dismissals` 0/1/2/3 and `activations`
1-4). Reading `Show`'s `S_OK` as "the user will see this" is exactly the mistake the registration and
the read-back check exist to prevent; the sample prints the read-back state up front precisely so a
control run can be judged clean or contaminated from its own capture.

### The confirmed end-to-end contract (what S02 and S03 build on)

1. **Identity.** Registration = per-user Start-menu shortcut + `System.AppUserModelID` written and
   committed before `Save`; success is only reported after the value reads back from a *fresh* shell
   link; removal is proven by a read-back that fails at `OpenShellLink` with `0x80070002`.
2. **Show.** The library's own `ToastApi` acquires both factories and the XML document with `S_OK`,
   binds the notifier to the explicit identity, loads `ToastPayload`'s XML, creates the notification,
   subscribes three handlers **before** `Show`, and tears all of it down on dispose - every step in
   the order `docs/TOAST-MEASUREMENT.md` (Contract 2) recorded.
3. **Delivery.** `Show` returning `S_OK` means *accepted*; the actual presence of the notification is
   observed out of process (`--history`, `count=1..3` in these runs) and the *lifecycle* is observed
   in process (`dismissed reason=2` for a banner that expired, `activated` with the launch argument).
4. **Activation.** The launch argument is delivered in process, verbatim, while the process runs; an
   activation can arrive while the banner is still displayed and without this session's instruments
   having clicked.
5. **Scope.** In-process only while the process runs (D054): in every run **no second process was
   ever created**, so a click never relaunches the exe. That is corroborated by the process observer
   above (exactly one pid, `count=0` before and after the runs).

### What measurement shows is NOT available to an unpackaged process

- **Closed-app activation.** A click on a toast after the process has exited has nothing to route to:
  no COM activator is registered (D054), and no click in these runs ever started a new instance
  (measured: the observer saw exactly one pid for the whole run, and the sample process list was
  empty between runs while the notifications stayed in the Action Center).
- **Any lifecycle callback for an identity that was never registered.** Six accepted shows produced
  zero callbacks in the control; the shell only reports lifecycle to a routable identity.
- **The banner as an addressable window of the process.** The sample is `visible=0` throughout; the
  banner belongs to the shell, not to the app. Enumerating this desktop's visible top-level windows
  while a toast was up produced no toast window (a 200ms scan for 14s; the banner's own timeout event
  is the only in-process evidence that it was displayed).
- **A reliable pixel oracle for the banner in this session's tooling.** Screen capture works (it saw
  the Start menu appear: `changedPixels=847713`), but the region-diff instrument that ran against a
  banner reported an 800x273 change that was *not* the banner (its click produced no activation), and
  the banner has no window to aim at. Clicking a banner from an automated instrument on this machine is
  therefore possible in principle but not attributable in practice - the honest conclusion is that
  click-driven activation is demonstrable here, and attributing a *specific* activation to a
  *specific* injected click is not.

### Instrument gotchas that cost a run each

1. **`INPUT` is 40 bytes on x64, not 32.** A struct sized by the keyboard union member makes
   `SendInput` accept nothing and return `0` with `ERROR_INVALID_PARAMETER`; the union must be sized by
   `MOUSEINPUT` (`[StructLayout(LayoutKind.Explicit, Size = 40)]` with the members at their offsets).
   A failed injection looks exactly like a session that refuses input, which is how it was first
   misread here.
2. **A DPI-unaware observer sees a virtualised screen.** This desktop is 1920x1200 at 150% scale;
   an unaware process captures 1280x800 and its `SetCursorPos` coordinates are not the ones
   `SendInput` needs. `click-toast-body.ps1` calls `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`
   before its first capture and prints the work area it measured.
3. **`ToastShow` is one show per instance, and disposing one unsubscribes it.** The sample therefore
   creates one per repeat and keeps them all alive until shutdown: a banner whose subscription has
   been removed is a banner whose click the process cannot report.

### Files added by T05

- `samples/Trustsoft.NotifyIcon.Sample/App.xaml.cs` - the `--toast` family of switches, the registration/read-back/show/subscribe/print path, the negative control, and the toast teardown.
- `scripts/probe-toast/observe-sample.ps1` - the external windowless/alive observer.
- `scripts/probe-toast/click-toast-body.ps1` - the banner body-click instrument (work-area geometry, `SendInput`, pre/post screen diff).
- `src/Trustsoft.NotifyIcon/Properties/AssemblyInfo.cs` - the `InternalsVisibleTo("Trustsoft.NotifyIcon.Sample")` grant and its removal condition.

---

## S02: the toast content model and the XML payload, measured live

Scope: M002/S02/T07. S02 gave the toast subsystem its public content vocabulary
(`ToastContent`, `ToastSeverity`, `ToastSound`, `ToastButton`, `ToastImage`,
`ToastImagePlacement`) and its exact payload: a consumer builds a `ToastContent` and shows it
through the public `ToastNotifier.Show`, and the three fields that are *not* toast XML (`Tag`,
`Group`, `Expiry`) are applied through the notification object's own properties instead.

This section is the contract the rest of the milestone reads before building on the toast path:
S03 (actions, events, the non-fatal error channel), S04 (images from an `ImageSource`) and S05 (the
consumer proof). Every claim below is one of exactly three kinds, and says which one it is:

1. a **byte-exact string** pinned by a test (`ToastPayloadContractTests`),
2. an **HRESULT or line measured on this machine in this task** (quoted verbatim from the capture),
3. a **schema/IDL reading**, named as such and not presented as a measurement.

Nothing here promises a *visible* shell effect. S02's demo is "a consumer builds a toast with
title, body and severity and shows it, and the contract tests pin the XML for every content
shape" - that is the whole of what was shown live, and item 5 records the shell rule that stops
"severity" from meaning "the user will notice".

### 1. The exact XML, shape by shape

Pinned one test per shape by `tests/Trustsoft.NotifyIcon.Tests/ToastPayloadContractTests.cs`, each
an `Assert.Equal` on the **whole document string** - not a parsed tree, because the measured
`IXmlDocumentIO.LoadXml` acceptance (Contract 2) is of this exact string, and a parse-and-compare
would accept a re-serialization the shell never sees.

| # | Content shape | Exact generated XML |
| --- | --- | --- |
| 1 | title only | `<toast><visual><binding template="ToastGeneric"><text>Title</text></binding></visual></toast>` |
| 2 | title + body | `<toast><visual><binding template="ToastGeneric"><text>Title</text><text>Body</text></binding></visual></toast>` |
| 3 | launch argument | `<toast launch="sample-toast-1"><visual><binding template="ToastGeneric"><text>Title</text></binding></visual></toast>` |
| 4 | severity to scenario | `<toast scenario="reminder"><visual><binding template="ToastGeneric"><text>Title</text></binding></visual></toast>` (`reminder` / `alarm` / `urgent`, lowercase; **no attribute** for `Default`) |
| 5 | image | `<toast><visual><binding template="ToastGeneric"><text>Title</text><image src="file:///C:/images/logo.png" placement="appLogoOverride"/></binding></visual></toast>` (and `placement="hero"`; `hint-crop="circle"` appended only when requested) |
| 6 | silent sound | `<toast><visual><binding template="ToastGeneric"><text>Title</text></binding></visual><audio silent="true"/></toast>` |
| 7 | action buttons | `<toast><visual><binding template="ToastGeneric"><text>Title</text></binding></visual><actions><action content="Yes" arguments="yes"/><action content="No" arguments="no"/></actions></toast>` |

**The combined payload, quoted verbatim from the contract test** (content: title `Title`, body
`Body`, launch `launch-arg`, severity `Urgent`, a hero image with the circle crop, silent sound, one
`Ok`/`ok` button, and tag/group/expiry set):

```xml
<toast launch="launch-arg" scenario="urgent"><visual><binding template="ToastGeneric"><text>Title</text><text>Body</text><image src="file:///img.png" placement="hero" hint-crop="circle"/></binding></visual><audio silent="true"/><actions><action content="Ok" arguments="ok"/></actions></toast>
```

**Attribute order is this library's pinned choice, not a schema requirement.** XML attribute order
carries no meaning, and the schema does not fix one; the order above - `launch` then `scenario` on
`<toast>`, and `src`, `placement`, `hint-crop` on `<image>` (which is the schema's own *syntax*
order for that element) - is what `ToastPayload` writes and what the tests pin. Any reordering is
now a deliberate edit to a test rather than a silent drift.

The rules that ride along with those shapes, each with its own test:

- **Empty means absent.** An absent or empty body, launch argument or button list omits its element
  or attribute entirely rather than emitting an empty one (an empty second `<text>` would render as
  a blank line).
- **A null title is an empty first line, never a missing element** - the binding always has a first
  `<text>`.
- **`Default` writes nothing**: no `scenario` attribute (there is no schema value for "default"), no
  `<audio>` element ("default" is the absence of a request, not a request for a specific sound).
- **Exactly one escaping path** (`EscapeText` for text nodes: `&`, `<`, `>`; `EscapeAttribute` adds
  `"`), so a caller-supplied value can only ever produce well-formed XML.
- **The image reference is escaped but never rewritten**: `https://example.com/a?x=1&y=2` renders as
  `src="https://example.com/a?x=1&amp;y=2"` - the `&` is XML-escaped, and no `%3F`/`%26`
  percent-encoding appears. The builder is not the layer that decides where the bytes live.

### 2. Finding: tag, group and expiry are not toast XML at all

This is S02's single biggest trap and the reason the show path grew four interop steps instead of a
few more lines of XML.

**Authority (local SDK IDL, the same source S01 used):**
`C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0\winrt\windows.ui.notifications.idl`.

| Interface | Where | Member used |
| --- | --- | --- |
| `IToastNotification` | lines 1263-1274, **slot 7** | `put_ExpirationTime(IReference<DateTime>*)` (slot 6 is `get_Content`, slot 8 `get_ExpirationTime` - propput precedes propget) |
| `IToastNotification2`, uuid **`9DFB9FD1-143A-490E-90BF-B9FBA7132DE7`** | lines 1276-1287, **slots 6 and 8** | `put_Tag(HSTRING)` and `put_Group(HSTRING)` (slot 7/9 are the getters, 10/11 `SuppressPopup`, not in D056) |

The `<toast>` element's own schema has five attributes (`launch`, `duration`, `displayTimestamp`,
`scenario`, `useButtonStyle`) and no place for a tag, a group or an expiry. The three fields are
properties **of the notification object the shell is handed**, applied after
`CreateToastNotification` and before `Show`.

**Consequence, stated plainly:** a string-only design - one that builds the document and stops -
**passes every XML test in item 1 while silently dropping three of D056's nine fields**. That is why
`ToastPayloadContractTests` contains an explicit *absence* assertion
(`Tag_group_and_expiry_produce_no_xml_at_all`): the document for content carrying
`Tag="orange-tag-value"`, `Group="orange-group-value"` and `Expiry=UnixEpoch` must be
**byte-identical** to the document for content carrying none of them. The live property probe below
shows the same thing from the other side: its content carries all three, and the `LoadXml` line it
produced contains only `<toast launch="s02-live-activation">`.

### 3. The expiry path: the library boxes its own `IReference<DateTime>`

Windows wants an `IReference<DateTime>` (a boxed `Windows.Foundation.DateTime`, i.e. a single
`INT64` of 100 ns ticks since **1601-01-01**), and the library does not hand-roll that box:

- Activation class **`Windows.Foundation.PropertyValue`**, interface `IPropertyValueStatics`, uuid
  **`629BDBC8-D932-4FF4-96B9-8D96C5C1E858`** (`windows.foundation.idl:677-759`), **`CreateDateTime` at
slot 21** (the 16th method from slot 6: CreateEmpty, UInt8, Int16, UInt16, Int32, UInt32, Int64,
UInt64, Single, Double, Char16, Boolean, String, Inspectable, Guid, DateTime). Its out parameter is
`IInspectable*`, so through the seam it is an `IntPtr`.
- The conversion is `UniversalTime = dto.UtcTicks - 504911232000000000` (in code the constant is
written `504_911_232_000_000_000L`; it is the 584 388 days from 0001-01-01 to 1601-01-01 in 100 ns
ticks), measured by `ToastShow.ToWinRtUniversalTime`.
- **The checkable value for the epoch:** `116444736000000000` is 1970-01-01T00:00:00Z in that
  counting, and `ToastApiContractTests.ToWinRtUniversalTime_maps_the_1601_epoch_and_round_trips_a_tick`
  pins it (`DateTimeOffset.UnixEpoch` -> `116444736000000000`), plus the tick-resolution property
  that subtracts exactly 1 tick per tick. A wrong epoch constant would set a nonsensical expiry
  instead of erroring, which is what makes this worth a test.
- **Independently re-checked in this task** against the live value: the probe's expiry
  `2031-02-03T04:05:06.0000000-05:00` is 1927875906 Unix seconds, and
  `116444736000000000 + 1927875906 * 10^7 = 135723495060000000` - exactly the `universalTime` the
  live run printed (item 4.2).
- The statics factory is acquired and released **inside** `CreateDateTimePropertyValue` (an
  implementation detail of that member, which hands out no handle of its own); the boxed property
  value is tracked by `ToastShow` so it is released exactly once, on teardown or during the failure
  unwinding.

### 4. The live capture, verbatim

Two runs carry item 4, because S02's four property steps are **conditional** (T03's decision: each
runs only when its field is present). The *sample's* demo content is title, body, launch and
severity - so it never touches `put_Tag`/`put_Group`/`put_ExpirationTime` and its capture cannot
contain those lines. The second run is a live probe over content that *does* set tag, group and
expiry; it exists precisely to measure what the sample cannot. Both runs were performed in this
task, and both blocks below are the raw output.

Reproduce with (from the repository root; the `export`s only matter because `gsd_exec`'s sandbox
strips Windows environment variables - a normal shell already has them):

```text
export APPDATA='C:\Users\Maxim\AppData\Roaming'; export ProgramData='C:\ProgramData'
export LOCALAPPDATA='C:\Users\Maxim\AppData\Local'; export DOTNET_CLI_HOME='C:\Users\Maxim'

dotnet build Trustsoft.NotifyIcon.sln -c Release

# 4.1 the S02 demo: title + body + severity through the public ToastNotifier
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-after 2 --toast-severity Reminder --run-seconds 12

# 4.3 windowless and alive, from a second process, while the sample runs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/observe-sample.ps1 -Seconds 14

# 4.2 the four non-XML steps, live (content that sets tag, group and expiry)
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --no-build \
    --filter "FullyQualifiedName~ToastApiLiveProbeTests.The_show_path_applies_tag_group_and_expiry" \
    --logger "console;verbosity=detailed"

# 4.4 the whole suite
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --no-build
```

#### 4.1 The S02 demo (public `ToastNotifier`, `severity=Reminder`)

The tray/balloon bootstrap lines the sample prints before the toast block are omitted here (they are
unchanged from T05's capture); everything from the toast trace attachment to the toast totals line
is verbatim:

```text
[sample] toast library trace: the library's own source 'Trustsoft.NotifyIcon' was raised to Verbose through the sample's InternalsVisibleTo grant, so its per-step toast lines - including the exact XML handed to LoadXml - print below as [trace] lines.
[sample] toast demonstration: identity='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' register=True severity=Reminder repeat=off - the shows go through the public ToastNotifier, which registers the identity on its first show; the sample's InternalsVisibleTo grant survives this slice for the fresh-link read-back, the --toast-skip-register control and the Verbose trace attachment, none of which has a public equivalent (S05's consumer proof retires it).
[sample] toast show scheduled: first show after 2s (--toast-after); the toast's launch argument is 'sample-toast-N', so the activation a click delivers names the show it came from.
[sample] toast content: title='Trustsoft.NotifyIcon sample toast' body='Click this banner's body: the sample prints the activation it receives.' severity=Reminder launch='sample-toast-1'
[sample] toast show #1: asking the shell for identity='Trustsoft.NotifyIcon.Sample' launch='sample-toast-1' title='Trustsoft.NotifyIcon sample toast'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: register attempt override='(default)'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register attempt aumid='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register succeeded aumid='Trustsoft.NotifyIcon.Sample'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: registered aumid='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: show title='Trustsoft.NotifyIcon sample toast' severity=Reminder launch='sample-toast-1' aumid='Trustsoft.NotifyIcon.Sample'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: begin aumid='Trustsoft.NotifyIcon.Sample' title='Trustsoft.NotifyIcon sample toast' launch='sample-toast-1'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: CreateToastNotifierWithId('Trustsoft.NotifyIcon.Sample') hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: GetNotifierSetting hr=0x00000000 setting=0
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: LoadXml hr=0x00000000 xml=<toast launch="sample-toast-1" scenario="reminder"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon sample toast</text><text>Click this banner's body: the sample prints the activation it receives.</text></binding></visual></toast>
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: subscribed activated=0x85B6E594F17564D0 dismissed=0x6B7C57A2213E2FE9 failed=0x47565F3B5AD41F
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: Show hr=0x00000000 (accepted by the shell; delivery is judged out of process)
[sample] toast show #1: the shell accepted it (ToastNotifier.Show returned; S_OK inside it is acceptance, not visibility); delivery is judged out of process with scripts/probe-toast --history 'Trustsoft.NotifyIcon.Sample'
[sample] toast registration read-back (fresh shell link): success=True operation='' code=0x00000000 value='Trustsoft.NotifyIcon.Sample' matchesExpected=True
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: dispose liveShows=1 createdShortcut=True
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: dispose
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeActivated(0x85B6E594F17564D0) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeDismissed(0x6B7C57A2213E2FE9) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeFailed(0x47565F3B5AD41F) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: remove succeeded shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: remove shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' removed=True operation='' code=0
[sample] toast teardown: 1 show(s) unsubscribed and released - no activation subscription outlives the process.
[sample] toast unregistration: Dispose removed the shortcut this run registered; read-back after dispose success=False operation='OpenShellLink' code=0x80070002 (a failure at OpenShellLink is the shortcut being gone)
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
[sample] toast totals: shows=1, accepted=1, activations=0 (last arguments='(none)'), dismissals=0, failures=0, refused=0, libraryTrace=24, registered=True, identity='Trustsoft.NotifyIcon.Sample'.
```

What this run is evidence for, and what it is not:

- The **content the consumer built** (`title`, `body`, `severity=Reminder`, `launch='sample-toast-1'`)
  is printed by the sample, and the **document the shell received** carries the same title and body
  text and the severity's own scenario name (`scenario="reminder"`, lowercase). That is S02's stated
  demo, measured.
- Registration happened **inside** the first `Show` (D060: register-on-first-show) and is proven by
  the fresh-link read-back that prints after it: `value='Trustsoft.NotifyIcon.Sample'`,
  `matchesExpected=True`.
- Acceptance is `Show hr=0x00000000`, which means *accepted*, not *seen* (Contract 2 / the negative
  control). No activation and no dismissal arrived in this 12 s run, so this capture says nothing
  about what the banner did - and is not quoted as if it did.
- Teardown is the same story as T03/T05: `Dispose` removed the shortcut, and the read-back after
  disposal fails at `OpenShellLink` with **`0x80070002`** (`ERROR_FILE_NOT_FOUND`), which is the
  shortcut being gone.
- `libraryTrace=24` and the tray totals' `library trace lines=0` in the *same* capture are the
  measured finding F2 of `docs/UAT-S02.md`: the documented consumer spelling (attaching a listener to
a same-named `TraceSource`) receives nothing while the library's own source sits at `Warning`; the
  capture is only verbose because the sample reaches the library's own source through its internals
  grant.

#### 4.2 The four non-XML steps, live

Run under `--filter "FullyQualifiedName~ToastApiLiveProbeTests.The_show_path_applies_tag_group_and_expiry"
--logger "console;verbosity=detailed"` (the test is the additive S02 probe added in this task); the
result line was `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`. Content: title
`Trustsoft.NotifyIcon S02 live probe`, body, launch `s02-live-activation`, `Tag="s02-live-tag"`,
`Group="s02-live-group"`, `Expiry=2031-02-03T04:05:06-05:00`.

```text
[live] s02: machine=MINIBOOKX; os=Microsoft Windows NT 10.0.26200.0; shortcut=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.S02.LiveProbe.lnk
[live] s02: register aumid='Trustsoft.NotifyIcon.S02.LiveProbe' success=True operation='' code=0x00000000
[live] s02: show success=True operation='' code=0x00000000 setting=0 aumid='Trustsoft.NotifyIcon.S02.LiveProbe'
[live] s02: expiry='2031-02-03T04:05:06.0000000-05:00' expectedUniversalTime=135723495060000000
[live] s02 trace: toast identity: register attempt aumid='Trustsoft.NotifyIcon.S02.LiveProbe' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.S02.LiveProbe.lnk'
[live] s02 trace: toast identity: register succeeded aumid='Trustsoft.NotifyIcon.S02.LiveProbe'
[live] s02 trace: toast show: begin aumid='Trustsoft.NotifyIcon.S02.LiveProbe' title='Trustsoft.NotifyIcon S02 live probe' launch='s02-live-activation'
[live] s02 trace: toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
[live] s02 trace: toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[live] s02 trace: toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
[live] s02 trace: toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[live] s02 trace: toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
[live] s02 trace: toast: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[live] s02 trace: toast: CreateToastNotifierWithId('Trustsoft.NotifyIcon.S02.LiveProbe') hr=0x00000000
[live] s02 trace: toast show: GetNotifierSetting hr=0x80070490 setting=0 (E_NOT_FOUND on first use is benign)
[live] s02 trace: toast show: LoadXml hr=0x00000000 xml=<toast launch="s02-live-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon S02 live probe</text><text>Tag, group and expiry are notification-object properties, not toast XML.</text></binding></visual></toast>
[live] s02 trace: toast: put_Tag('s02-live-tag') hr=0x00000000
[live] s02 trace: toast show: SetNotificationTag(put_Tag) hr=0x00000000 tag='s02-live-tag'
[live] s02 trace: toast: put_Group('s02-live-group') hr=0x00000000
[live] s02 trace: toast show: SetNotificationGroup(put_Group) hr=0x00000000 group='s02-live-group'
[live] s02 trace: toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
[live] s02 trace: toast: PropertyValue.CreateDateTime(universalTime=135723495060000000) hr=0x00000000
[live] s02 trace: toast show: CreateDateTimePropertyValue(CreateDateTime) hr=0x00000000 universalTime=135723495060000000
[live] s02 trace: toast show: SetNotificationExpirationTime(put_ExpirationTime) hr=0x00000000 expiry='2031-02-03T04:05:06.0000000-05:00'
[live] s02 trace: toast show: subscribed activated=0xF35AEAEC605F670E dismissed=0x25ABE972FE0174F4 failed=0x54C807F415C70C7B
[live] s02 trace: toast show: Show hr=0x00000000 (accepted by the shell; delivery is judged out of process)
[live] s02 trace: toast show: dispose
[live] s02 trace: toast show: UnsubscribeActivated(0xF35AEAEC605F670E) hr=0x00000000
[live] s02 trace: toast show: UnsubscribeDismissed(0x25ABE972FE0174F4) hr=0x00000000
[live] s02 trace: toast show: UnsubscribeFailed(0x54C807F415C70C7B) hr=0x00000000
[live] s02: remove success=True operation='' code=0x00000000
```

Three things this capture settles that no fake can:

1. **The four steps ran in the library's fixed order after `CreateToastNotification` and before the
   subscribes**, each returning `S_OK` against the real notification object:
   `put_Tag` (and `SetNotificationTag`) - `put_Group` (and `SetNotificationGroup`) -
   `CreateDateTime` (and `CreateDateTimePropertyValue`) - `put_ExpirationTime`
   (and `SetNotificationExpirationTime`). A wrong vtable slot, a wrong GUID or an unboxable
   `IReference<DateTime>` would have surfaced here as a non-zero HRESULT.
2. **The document really does not carry them**: the `LoadXml` line for content that sets all three
   fields is `<toast launch="s02-live-activation">` - no tag, group or expiry. Item 2's finding,
   measured live.
3. **The shell accepted the boxed expiry**: `CreateDateTime(universalTime=135723495060000000)` and
   `put_ExpirationTime` both `S_OK` for a concrete future instant (independently re-derived in
   item 3).

#### 4.3 Windowless and alive, from outside the process

`scripts/probe-toast/observe-sample.ps1 -Seconds 14`, started as a **separate process** immediately
before the 4.1 sample run:

```text
observer: start 19:01:51.996 watching process name 'Trustsoft.NotifyIcon.Sample' for 14s
observer: t+0s pid=11376 alive=True workingSet=76MB windows=8 visible=0
observer: t+1s pid=11376 alive=True workingSet=155MB windows=8 visible=0
observer: t+2s pid=11376 alive=True workingSet=156MB windows=8 visible=0
observer: t+3s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+4s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+5s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+6s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+7s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+8s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+9s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+10s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+11s pid=11376 alive=True workingSet=168MB windows=8 visible=0
observer: t+12s pid=11376 alive=True workingSet=168MB windows=6 visible=0
observer: t+13s no process named 'Trustsoft.NotifyIcon.Sample' is running
observer: complete at 19:02:06.422
```

**One pid** (`11376`) for the whole lifetime, `alive=True` on every sample of the run, and
**`visible=0`** throughout: the process that showed the toast owned 8 (later 6) top-level windows and
not one of them was visible. The exit condition "the process stays alive with no window while it
shows a toast" is therefore a reading taken from another process, not a claim the sample makes about
itself - exactly as in T05.

#### 4.4 The test sweep

`dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --no-build`:

```text
Passed!  - Failed:     0, Passed:   552, Skipped:     0, Total:   552, Duration: 1 m 18 s - Trustsoft.NotifyIcon.Tests.dll (net8.0)
```

- **552 passed / 0 failed**, whole assembly, no filter - so the live probes
  (`ToastApiLiveProbeTests`, `ToastIdentityLiveProbeTests`) ran live inside it, as they did in T06's
  551.
- The toast/pin subset (`--filter "FullyQualifiedName~Toast|FullyQualifiedName~PackagePurity"`):
  **148 passed / 0 failed**. The complement (`--filter "FullyQualifiedName!~Toast"`, i.e. the M001
  classes): **416 passed / 0 failed**. 148 + 416 = 564 counts the 12 `PackagePurity` tests twice,
  which is the 552 total.
- **No M001 regression**: the M001 suites are the 416-test complement and they are all green; the
  only test-count change from T06's sweep is the one live probe this task added.
- Honest note on the one intermediate failure seen in this task: the first sweep after adding the
  probe failed `NotifyIconTraceTests.Source_is_named_and_defaults_to_warning_level`
  (`Expected: Warning / Actual: Verbose`). It was **not** a product regression - see 4.5 - and the
  re-run above is green.

#### 4.5 Instrument gotcha this task exposed: a live probe must join the trace collection

`NotifyIconTrace.Source` is process-wide and its switch level is raised to `Verbose` by the live
probes for the duration of a show. Each probe restores the level it read at entry - so **two probes
raising it in parallel can restore `Verbose` over each other and leave the whole run's source level
raised**, which is exactly what `NotifyIconTraceTests` asserts against. The existing convention
(`TraceChannelCollection`, `DisableParallelization = true`, documented in `BalloonCallbackTests`)
is that every class which raises the level joins that collection; `ToastApiLiveProbeTests` had
missed it. It now carries `[Collection(TraceChannelCollection.Name)]`, which serializes the live
probes with each other and with the other switch-mutating classes - the sweep above is the proof
that the failure is gone. **Any future live probe belongs in that collection too.**

### 5. A scenario is not self-sufficient

The severity-to-scenario mapping (D053) is real as an *attribute*, and S02's contract stops there.
The toast schema says so itself (a schema reading, recorded in `.gsd/phases/02-winrt-toast-notifications/02-02-RESEARCH.md`,
not measured here):

- **`reminder` is silently ignored unless the toast carries a button action that activates in the
  background.** S02's `<action>` elements deliberately omit `activationType`, i.e. they are
  foreground actions (D054 scopes v1 to activation in the running application) - so a S02-era
  `scenario="reminder"` toast gets **no reminder treatment at all**.
- **`alarm` loops alarm audio and needs `duration`** - also not S02's to deliver.

Consequences, and they bind S03-S05:

- **S03-S05 must not promise a visible severity effect.** "A scenario is not self-sufficient" is the
  sentence to carry forward: S02's contract is the attribute plus `LoadXml`'s acceptance of it,
  which is what the 4.1 capture shows (`scenario="reminder"`, `hr=0x00000000`) and nothing more.
- The live run is consistent with that and is not quoted as contradicting it: the reminder toast was
  accepted, and in 12 s the process saw no activation and no dismissal (`activations=0,
  dismissals=0`) - it never reported a banner being displayed.
- Where a *visible* proof is wanted later, it needs the background-activation action (and, for
  `alarm`, the duration/audio contract) that a later slice owns.

### 6. The image contract handed to S04

- **`file:///` is supported for desktop applications** - explicitly, per the toast image schema - and
  that is the scheme S04 needs for a locally persisted WPF image. It is also what the 5th shape above
  renders.
- **The builder XML-escapes the reference and does nothing else to it**: no URL-encoding, no
  normalisation, no rewriting (item 1's last rule, with its own test). The reference is the
  consumer's statement of where the image is; S04 owns producing one.
- **S02 omits the image `id` attribute.** The schema page marks `id` as required on a `ToastGeneric`
  `<image>` while every Microsoft `ToastGeneric` example omits it, so S02 pinned a choice (no `id`)
  rather than guess: the section-1 image shapes have no `id`, and **S04's live image run is what
  settles it** - if the shell needs one, the fix is a one-line change to `ToastPayload` plus its two
  exact-string tests, and this paragraph is the pointer to it.
- `placement` is always written (`appLogoOverride` or `hero`) and `hint-crop="circle"` only when the
  consumer asks for the crop; both are rendering hints the shell may ignore.

### 7. The wording rule handed to S03

**The public `Activated` event must be documented from the toast's `launch` argument, never as "the
user clicked the body".** S01 measured that per-click attribution is not available on this machine
(an activation can arrive while the banner is still displayed, and without any instrument this
session injected - see T05's section above), while the launch argument is delivered verbatim and
deterministically. So the honest wording is "the activation carried the argument of the toast it
came from", which is also why the sample prints `launch='sample-toast-N'` beside each show and
`last arguments='...'` in its totals line. Two related facts S03 inherits from S02: the notifier
**registers on its first `Show`** (so `AppUserModelId` is frozen by that first show, D060), and a
failed `Show` currently **throws** rather than reporting through an event.

### 8. What S02 deliberately did not deliver

| Not delivered | Why / where it lands |
| --- | --- |
| **No public events** - `ToastNotifier` declares no `Activated`/`Dismissed` | S03 attaches the `ToastShow` callbacks; S02's shows leave them unset |
| **No `ToastError` channel** - a failed `Show` throws `ToastException(operation, code)` in the interim (D058) | S03 adds the non-fatal event channel; the throw is deliberate, not an oversight |
| **No `ImageSource` support** - `ToastImage.Reference` is an already-formed reference string (D059) | S04 turns a WPF `ImageSource` into a `Reference`; S02 consumes the resolved string only |
| **Nothing else from D056's deferred list** - no progress bar, no attribution text, no scheduled delivery, no text input | not in v1's nine fields; S02's model is exactly `Title`, `Body`, `Severity`, `Launch`, `Image`, `Buttons`, `Tag`, `Group`, `Expiry`, `Sound` |

Two smaller deliberate choices worth carrying forward: `ToastSeverity`'s numeric values are the
library's own ordinals (`Default=0, Reminder=1, Alarm=2, Urgent=3`), **not** `ToastScenario`'s numbers
(there is no numeric wire identity to preserve - the payload renders lower-case scenario *names*),
and `ToastContent.Buttons` is a get-only pre-initialized `IList<ToastButton>`, so "never null" is a
property of the type.

### Files added or changed by S02

- `src/Trustsoft.NotifyIcon/ToastContent.cs`, `ToastSeverity.cs`, `ToastSound.cs`, `ToastButton.cs`, `ToastImage.cs`, `ToastImagePlacement.cs` - the public content vocabulary (T01).
- `src/Trustsoft.NotifyIcon/Interop/ToastPayload.cs` - the payload builder over `ToastContent`, one escaping path (T02).
- `src/Trustsoft.NotifyIcon/Interop/IToastApi.cs`, `ToastApi.cs`, `ShortcutLink.cs` - the four non-XML steps and their raw-vtable implementations (T03).
- `src/Trustsoft.NotifyIcon/ToastNotifier.cs`, `ToastException.cs` - the public show surface and the failure type (T05).
- `samples/Trustsoft.NotifyIcon.Sample/App.xaml.cs`, `src/Trustsoft.NotifyIcon/Properties/AssemblyInfo.cs` - the sample's `--toast-severity` run on the public notifier and the internals-grant comment (T06).
- `tests/Trustsoft.NotifyIcon.Tests/ToastPayloadContractTests.cs`, `ToastNotificationPropertyTests.cs`, `ToastContentTests.cs`, `ToastNotifierTests.cs`, `FakeToastApiTests.cs`, `PackagePurityTests.cs` - the exact-XML contract, the property-step order/arguments/failure-unwinding contract, the model's dumb-data-shape pins, the notifier's behaviour, and the widened surface pins.
- `tests/Trustsoft.NotifyIcon.Tests/ToastApiContractTests.cs` - this task's additive live probe
  (`The_show_path_applies_tag_group_and_expiry_to_the_real_notification_object`) and the
  `[Collection(TraceChannelCollection.Name)]` under which the live probes now run (item 4.5).
- `docs/TOAST-MEASUREMENT.md` - this section.

---

## S03: the activation event surface, the failure split and the disposal guarantee, measured live

Scope: M002/S03/T05. S03 gave the toast subsystem its **public activation surface**: `ToastNotifier`
raises three typed events (`Activated`, `Dismissed`, `ToastError`) fed by the same three WinRT
subscriptions S01 measured and S02 left unassigned; a **failure the show path reports after a
successful registration** became a non-fatal event plus one Error-level trace line instead of a
throw; and **disposal** was closed on two layers, so nothing the shell can still deliver raises
anything after `Dispose`.

Every claim below is one of exactly three kinds and says which one it is:

1. a **line captured live in this task** (quoted verbatim, with the command that produced it),
2. a **string, ordinal or behaviour pinned by a named test**, or
3. a **source or SDK-IDL reading**, named as such.

### 1. The public event surface

Three events on `ToastNotifier`, each an ordinary `EventHandler<T>` (D052: not a routed event, not
XAML-wirable), each with its own public payload type:

| Event | Payload type | What the payload carries | Delivered verbatim? |
| --- | --- | --- | --- |
| `Activated` | `ToastActivatedEventArgs` | `Arguments` - the launch or button argument the shell handed back | yes, the exact string, `null` when the toast carried none |
| `Dismissed` | `ToastDismissedEventArgs` | `Reason` - the shell's dismissal reason, mapped into this library's vocabulary | the reason as delivered, mapped, never reinterpreted |
| `ToastError` | `ToastErrorEventArgs` | `Operation`, `ErrorCode`, `Exception?` - the same machine-readable pair `ToastException` uses | the failing operation name and the code that call reported |

**`ToastDismissalReason`'s ordinals are the platform's numbers, and one member is not.**

| Member | Value | Authority |
| --- | --- | --- |
| `Unknown` | `-1` | **this library's seam sentinel**, not a WinRT member. `ToastApi.ReadDismissedReason` returns `-1` when the payload's `Reason` getter fails (`hr < 0`), and the notifier maps any value outside the three known members onto `Unknown` as well - so a consumer always gets a total answer, and an unrecognised value is reported as unrecognised rather than folded into `UserCanceled`. |
| `UserCanceled` | `0` | `ToastDismissalReason.UserCanceled`, SDK IDL |
| `ApplicationHidden` | `1` | `ToastDismissalReason.ApplicationHidden`, SDK IDL |
| `TimedOut` | `2` | `ToastDismissalReason.TimedOut`, SDK IDL |

**The IDL reading behind the three known values** (the same local SDK source S01 and S02 used,
`C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0\winrt\windows.ui.notifications.idl`):
`enum ToastDismissalReason { UserCanceled = 0; ApplicationHidden = 1; TimedOut = 2 }` at lines
**743-748**; the value arrives through `IToastDismissedEventArgs` (uuid
**`3F89D935-D9CB-4538-A0F0-FFE7659938F8`**, line 1246), whose `[propget] HRESULT Reason(...)` is at
line **1249** and is read at vtable slot 6 (the same slot the activation argument getter uses).
A wrong slot here would return a garbage reason rather than fail, which is why the ordinals are
pinned: `ToastEventTests.Dismissal_reason_ordinals_are_the_platform_values`.

The three callbacks that feed these events are the subscriptions S01 already measured - the
`IToastNotification` event pairs at `windows.ui.notifications.idl:1268-1273` (`Dismissed` 9/10,
`Activated` 11/12, `Failed` 13/14) - and they are assigned **before** the show runs, so an activation
that races the display finds a handler in place instead of being dropped
(`ToastEventTests.No_activation_is_delivered_before_the_first_show_subscribes`).

**The shell's asynchronous delivery failure has a stable name:** `NotificationFailed`
(`ToastException.OperationNotificationFailed`, `ToastShow.OperationNotificationFailed`,
`"NotificationFailed"`). That is the operation a consumer branches on for a delivery failure; it is
distinct from the seam member names (`Show`, `LoadXml`, `CreateShellLink`, `OpenShellLink`, ...) that
report a failure of a specific step.

**Surface accounting.** S03 added exactly four public types - `ToastActivatedEventArgs`,
`ToastDismissedEventArgs`, `ToastDismissalReason`, `ToastErrorEventArgs` - bringing the exported
surface to **nineteen types**, pinned by three independent allow-lists: two in `PackagePurityTests`
(`Public_surface_is_only_the_documented_types` and the count sentence in
`TrayIcon_exposes_the_context_menu_property_without_adding_a_public_type`) and one in
`TrayIconExceptionTests`. A stringly activation surface (one event carrying a string, or a raw
`HRESULT`) would have added no types; it was rejected so a consumer can branch on data instead of on
message text.

**No per-show identity is exposed, and that is a platform fact, not an omission.** One notifier can
have several toasts live at once, and Windows delivers **no per-show event object** with an
activation or a dismissal: the payload carries the argument string and nothing else. The delivered
argument is therefore the only identifier, and a consumer that needs to tell its toasts apart must
make its launch and button arguments unique - which is exactly what the sample does with
`sample-toast-N`, `sample-button-1` and `sample-button-2`. A `null` argument is a legitimate
delivery (a toast that carried no launch argument), not a failure; "no argument" and "the empty
string" are different values.

### 2. The wording rule, and the measurement behind it

The sentence, verbatim, pinned in the generated documentation by
`ToastEventTests.The_two_boundary_sentences_appear_verbatim_in_the_generated_documentation`:

> Activated reports that the toast's launch or button argument arrived; it is not a report that the user clicked the body.

**The measured limitation behind the rule** (S01's out-of-process observation, recorded above in
"Activation callback - what was and was NOT captured"): an activation can arrive while the banner is
still displayed and with no instrumented click at all, and a specific activation **cannot be credited
to a specific click**. The platform reports that *something* was activated and what argument it
carried; it does not report which element the user touched. So an `Activated` handler may correctly
print the argument of the toast it came from, and must never be described - in this library's
documentation, in the sample or in later slices - as proof of where the user clicked.

Two consequences this slice carries forward:

- The rule is pinned in the generated `.xml` beside the assembly, not just in source comments, so a
  consumer reading only the shipped documentation still sees it.
- The same honesty applies one level down: `ToastDismissedEventArgs` says a dismissal "is not a click
  and carries no argument", so a dismissal cannot be used to infer an activation either.

### 3. The failure split: the show path reports, registration and caller errors still throw

**The decision is D055, with its show-path half fixed by D061 (which replaced D058's interim
semantics).** D055 (planning): a dedicated `ToastException` (operation + `HRESULT`) plus a
`ToastError` event and `TraceSource` output; **registration/startup failures throw, runtime failures
surface through the event and trace without terminating the process**. D058 had made the interim
public `Show` throw for a show-path failure while S03's event channel did not yet exist; D061
replaced that: once a notifier is registered, a failure its show reports is **reported, not thrown**.

Read from `ToastNotifier.Show`, the split is exact:

| Situation | Behaviour |
| --- | --- |
| `content` is `null` | `ArgumentNullException` (caller error, before the seam is touched) |
| `Title` is empty or whitespace | `ArgumentException` (caller error; a toast with no visible text is an empty banner) |
| the notifier was disposed | `ObjectDisposedException` |
| the **first** show cannot register its identity (`EnsureRegistered`, D060) | `ToastException` with the failing operation and code |
| a show the notifier **did** register reports `!result.Success` | `_liveShows.Remove(show)` -> `show.Dispose()` -> `RaiseError(result.Operation, result.Code, exception: null)`; **nothing is thrown** |

Both halves are pinned by tests: `ToastEventTests.A_show_path_failure_raises_ToastError_and_writes_one_Error_line_without_throwing`
(no exception leaves `Show`, one event, one Error line) and
`ToastEventTests.A_registration_failure_still_throws_and_raises_no_ToastError` (a `ToastException`
and **zero** events). The failed show has already unwound its own handles by then, so the notifier is
unaffected and a later `Show` is a fresh attempt.

**The Error-level line.** One line per failure, written by `NotifyIconTrace.ToastError` at
`TraceEventType.Error`, event id **3** (`NotifyIconTrace.ToastErrorEventId`), shape:

```text
Toast {operation} failed (code {code}, 0x{code:X8}). {exception type}: {message}
```

with `No exception detail was supplied.` in place of the last two fields when the failure carried no
exception - which is the normal case for the shell's asynchronous failure and for the show-path
failure, both of which report a bare code. The code is rendered in decimal **and** hexadecimal
because a toast failure is almost always an `HRESULT`, and an `HRESULT` is read in hexadecimal.
Two concrete renderings pinned by tests (`ToastEventTests`,
`NotifyIconTraceTests.ToastError_renders_a_negative_hresult_in_decimal_and_hexadecimal`):

```text
Toast Show failed (code -2147467259, 0x80004005). No exception detail was supplied.
Toast Activated failed (code 0, 0x00000000). System.InvalidOperationException: consumer bug in Activated
```

**Visibility, and S02's finding F2.** The line is written at `Error`, so it is visible at the trace
source's **default `Warning` level with no configuration**
(`NotifyIconTraceTests.Source_is_named_and_defaults_to_warning_level` pins that default). That is the
failure half of S02's finding F2: S02 recorded that the documented consumer spelling - attaching a
listener to the same-named `TraceSource` - receives nothing, because the per-step toast lines are
`Verbose` and the source sits at `Warning`. The failure line is deliberately **not** `Verbose`: a
consumer who subscribes the documented source now sees a toast failure, while the ordinary step
traffic stays filtered. The Verbose step lines stay Verbose - the gap is converted into one Error
line per failure, not into Error noise per step. Event ids are now **1** (notification-area
failure), **2** (Verbose traffic, filtered by default), **3** (toast failure), and
`NotifyIconTraceTests` pins that the three are distinct so a listener can filter on the id rather than
parse the message.

**The third writer is a backstop, not a failure report.** A consumer handler that throws is caught on
the COM-callable callback path, which writes `NotifyIconTrace.ToastError(eventName, 0, exception)` -
one Error line naming the **event** it arrived on - and deliberately raises **no** `ToastError` event:
the toast was already delivered and the failure belongs to the consumer's handler, not to the toast
subsystem. The event name is threaded into the subscription rather than hardcoded. Pinned by
`ToastEventTests.A_throwing_consumer_handler_is_traced_at_Error_level_and_raises_no_ToastError` and
`ToastEventTests.A_throwing_consumer_handler_does_not_escape_the_raise` (a handler exception never
crosses back into the callback path). A healthy show writes no Error line at all:
`ToastEventTests.A_healthy_show_writes_no_Error_level_line`.

**The honest limit of the live evidence: a `ToastError` cannot be forced on a healthy machine.**
There is no way to make the shell refuse a well-formed show, and the shell's asynchronous `Failed`
callback does not fire on a healthy machine either - so a live `ToastError` was **not** produced in
this task. What *is* live:

- the **throw half**, forced live in this task through the sample's own refusal path (item 3.1);
- the shape and visibility of the Error line, pinned by the tests named above;
- the **event half**, exercised by the fakes: reproduce in process with
  `FakeToastApi.FailNext(ToastOperation.Show)` plus a listener on `NotifyIconTrace.Source`, and the
  activation path with `FakeToastApi.RaiseActivated`.

#### 3.1 The refusal path, forced live

A registration failure is the documented throw, and it can be forced live by giving the sample an
identity whose shortcut cannot be created. Command (the identity in this run was a 300-character run
of `X`, written `'XXXX...'` below; everything else is verbatim):

```text
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-aumid 'XXXX...(300 X)...' --toast-after 2 --run-seconds 8
```

```text
[sample] toast demonstration: identity='XXXX...' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\XXXX....lnk' register=True severity=Default repeat=off - the shows go through the public ToastNotifier, which registers the identity on its first show and raises the three S03 events; the sample's InternalsVisibleTo grant survives this slice for the fresh-link read-back, the --toast-skip-register control and the Verbose trace attachment, none of which has a public equivalent (S05's consumer proof retires it).
[sample] toast show scheduled: first show after 2s (--toast-after); the toast's launch argument is 'sample-toast-N', so the activation a click delivers names the show it came from.
[sample] toast content: title='Trustsoft.NotifyIcon sample toast' body='Click this banner's body: the sample prints the activation it receives.' severity=Default launch='sample-toast-1'
[sample] toast show #1: asking the shell for identity='XXXX...' launch='sample-toast-1' title='Trustsoft.NotifyIcon sample toast'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: register attempt override='XXXX...'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register attempt aumid='XXXX...' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\XXXX....lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register read-back failed at OpenShellLink hr=0x8007007B
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: register failed at OpenShellLink code=0x8007007B; no toast is shown
[sample] toast show #1: REFUSED operation='OpenShellLink' code=0x8007007B - the shell did not accept a toast for identity='XXXX...'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: dispose liveShows=0 createdShortcut=False
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
[sample] toast post-teardown window: activations=0 dismissals=0 errors=0 after the teardown line (0 is the disposal guarantee)
[sample] toast totals: shows=1, accepted=0, activations=0 (last arguments='(none)'), dismissals=0, failures=0, errors=0, refused=1, libraryTrace=5, registered=False, identity='XXXX...'.
```

What this run shows, and what it does not:

- **The throw half is live.** The registration read-back failed (`OpenShellLink hr=0x8007007B`,
  `ERROR_INVALID_NAME`), the library traced `register failed ... no toast is shown`, `Show` threw
  `ToastException(OpenShellLink, 0x8007007B)`, the sample printed **REFUSED** and counted
  `refused=1, accepted=0` - no toast was shown and the process did not crash.
- **A registration failure raises no `ToastError`.** `errors=0` in the totals line of a run whose
  `Show` demonstrably threw is the split's other half, measured: only a failure reported *after*
  registration goes through the event channel.
- It says **nothing** about the `ToastError` line, because that path was not reached - by design, as
  recorded above.

### 4. The disposal guarantee, on two layers

The milestone's success criterion 3 is "after the notifier is disposed, none of the events fires".
S03 closes it on two independent layers, because either one alone can be raced:

1. **The source is detached.** `ToastShow.Dispose` nulls its three callbacks **before** it unwinds,
   then unsubscribes each of the three and releases the handles. Detaching first is what makes a
   callback the shell had **already dequeued** find nothing to invoke instead of finding a live
   handler.
2. **The notifier's own raise paths refuse.** All four paths (`OnShowActivated`, `OnShowDismissed`,
   `OnShowFailed`, `RaiseError`) return immediately once `_disposed` is set, so the guarantee does
   not depend on the show-side detach alone - a disposed notifier writes no line and raises no event.

What makes each layer observable, all of it pinned by `ToastEventTests`:

| Claim | How it is observed | Test |
| --- | --- | --- |
| the source is detached | the fake reports `false` for `RaiseActivated`/`RaiseDismissed`/`RaiseFailed` after `Dispose`, and the counts stay at the pre-dispose values | `After_dispose_the_source_is_detached_and_nothing_fires` (which also delivers once **before** disposal, so it cannot pass by never having connected) |
| a replayed captured callback is silent | the fake retains the real delegates (`CaptureHandlers`) and re-invokes them exactly as a dequeued callback would; all three reach nulled callbacks and raise nothing | `A_dequeued_callback_that_runs_after_dispose_raises_nothing` |
| the notifier's own raise path refuses | the four internal raise members are called directly after `Dispose`; zero events | `The_notifiers_own_raise_paths_refuse_after_dispose` |
| the guarantee is disposal-scoped, not a blanket teardown | a **live** show still delivers through a retained callback, and only the disposed show goes quiet | `The_disposal_guarantee_is_scoped_to_the_disposed_show` |
| a handler that disposes mid-delivery stops the next queued callback | the first delivery disposes from inside the handler; the replayed second callback raises nothing | `A_handler_that_disposes_the_notifier_stops_a_replayed_callback` |
| nothing is left registered | the live capture's teardown lines: `dispose`, three `Unsubscribe...(token) hr=0x00000000`, `remove shortcut ... removed=True` | the S03 capture, item 5 |
| the post-teardown window is silent | the live capture's `toast post-teardown window: activations=0 dismissals=0 errors=0` after the teardown line | the S03 capture, item 5 |
| the shortcut itself is gone | the live capture's post-dispose read-back fails at `OpenShellLink` with **`0x80070002`** (`ERROR_FILE_NOT_FOUND`) - the same reading T03/T05/S02 recorded | the S03 capture, item 5 |

The `--toast-dispose-after` switch exists so the silence is a **window** rather than an instant: the
run disposes the notifier mid-run and then keeps pumping, so "nothing fired after disposal" is a
measurement over wall time, not an assumption about the code path.

### 5. The live capture, verbatim

Reproduce with (from the repository root; the `export`s only matter because the sandbox used for
these runs strips Windows environment variables - a normal shell already has them):

```text
export APPDATA='C:\Users\Maxim\AppData\Roaming'; export ProgramData='C:\ProgramData'
export LOCALAPPDATA='C:\Users\Maxim\AppData\Local'; export DOTNET_CLI_HOME='C:\Users\Maxim'

dotnet build Trustsoft.NotifyIcon.sln -c Release

# the slice's demo run: two action buttons, a mid-run dispose, a measured silence window
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-buttons --toast-after 2 --toast-dispose-after 8 --run-seconds 20
```

The tray/balloon bootstrap lines the sample prints before the toast block are omitted here (they are
unchanged from T05's and S02's captures); everything from the toast demonstration line to the toast
totals line is verbatim, and this block and the negative control in item 6 are the two runs performed
in this task.

```text
[sample] toast library trace: the library's own source 'Trustsoft.NotifyIcon' was raised to Verbose through the sample's InternalsVisibleTo grant, so its per-step toast lines - including the exact XML handed to LoadXml - print below as [trace] lines.
[sample] toast demonstration: identity='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' register=True severity=Default repeat=off - the shows go through the public ToastNotifier, which registers the identity on its first show and raises the three S03 events; the sample's InternalsVisibleTo grant survives this slice for the fresh-link read-back, the --toast-skip-register control and the Verbose trace attachment, none of which has a public equivalent (S05's consumer proof retires it).
[sample] toast show scheduled: first show after 2s (--toast-after); the toast's launch argument is 'sample-toast-N', so the activation a click delivers names the show it came from.
[sample] toast dispose scheduled: the notifier is disposed after 8s (--toast-dispose-after) while this run keeps pumping, so an activation, dismissal or error arriving after the teardown line is measured rather than assumed.
[sample] toast content: title='Trustsoft.NotifyIcon sample toast' body='Click this banner's body: the sample prints the activation it receives.' severity=Default launch='sample-toast-1' buttons=[sample-button-1,sample-button-2]
[sample] toast show #1: asking the shell for identity='Trustsoft.NotifyIcon.Sample' launch='sample-toast-1' title='Trustsoft.NotifyIcon sample toast'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: register attempt override='(default)'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register attempt aumid='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: register succeeded aumid='Trustsoft.NotifyIcon.Sample'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: registered aumid='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: show title='Trustsoft.NotifyIcon sample toast' severity=Default launch='sample-toast-1' aumid='Trustsoft.NotifyIcon.Sample'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: begin aumid='Trustsoft.NotifyIcon.Sample' title='Trustsoft.NotifyIcon sample toast' launch='sample-toast-1'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x00000001 (RPC_E_CHANGED_MODE 0x80010106 is benign)
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast: CreateToastNotifierWithId('Trustsoft.NotifyIcon.Sample') hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: GetNotifierSetting hr=0x00000000 setting=0
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: LoadXml hr=0x00000000 xml=<toast launch="sample-toast-1"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon sample toast</text><text>Click this banner's body: the sample prints the activation it receives.</text></binding></visual><actions><action content="Button 1" arguments="sample-button-1"/><action content="Button 2" arguments="sample-button-2"/></actions></toast>
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: subscribed activated=0xB909B3ED8CAAF81 dismissed=0x7D3953E533BE64AF failed=0xAAFB866D9C49729A
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: Show hr=0x00000000 (accepted by the shell; delivery is judged out of process)
[sample] toast show #1: the shell accepted it (ToastNotifier.Show returned; S_OK inside it is acceptance, not visibility); delivery is judged out of process with scripts/probe-toast --history 'Trustsoft.NotifyIcon.Sample'
[sample] toast registration read-back (fresh shell link): success=True operation='' code=0x00000000 value='Trustsoft.NotifyIcon.Sample' matchesExpected=True
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: dispose liveShows=1 createdShortcut=True
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: dispose
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeActivated(0xB909B3ED8CAAF81) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeDismissed(0x7D3953E533BE64AF) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeFailed(0xAAFB866D9C49729A) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast identity: remove succeeded shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast notifier: remove shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' removed=True operation='' code=0
[sample] toast teardown: 1 show(s) unsubscribed and released - no activation subscription outlives the process.
[sample] toast unregistration: Dispose removed the shortcut this run registered; read-back after dispose success=False operation='OpenShellLink' code=0x80070002 (a failure at OpenShellLink is the shortcut being gone)
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
[sample] toast post-teardown window: activations=0 dismissals=0 errors=0 after the teardown line (0 is the disposal guarantee)
[sample] toast totals: shows=1, accepted=1, activations=0 (last arguments='(none)'), dismissals=0, failures=0, errors=0, refused=0, libraryTrace=24, registered=True, identity='Trustsoft.NotifyIcon.Sample'.
```

What this capture is evidence for, and what it is not:

- **The exact document the shell received carries both action buttons**: the `LoadXml` line contains
  `<actions><action content="Button 1" arguments="sample-button-1"/><action content="Button 2" arguments="sample-button-2"/></actions>` - the S02-pinned shape of item 1's seventh row, now
  carrying the slice's own arguments, accepted by the shell (`LoadXml hr=0x00000000`,
  `Show hr=0x00000000`).
- **The three subscriptions were established and then released**: `subscribed
  activated=0xB909B3ED8CAAF81 dismissed=0x7D3953E533BE64AF failed=0xAAFB866D9C49729A` at show time,
  and all three `Unsubscribe...hr=0x00000000` on teardown, followed by
  `remove shortcut ... removed=True` and the read-back failing at `OpenShellLink` with `0x80070002`.
- **The post-teardown window is a measured silence**: `activations=0 dismissals=0 errors=0 after the
  teardown line`, taken after the dispose at t+8s while the process kept pumping to t+20s - about
  12 s of window with nothing in it.
- **It does not report a click.** `activations=0 (last arguments='(none)')` and `dismissals=0` in this
  run: nobody touched the banner, so this capture proves the payload plumbing and the teardown, not
  the demo's click clauses - which are item 8's human follow-up, by design.
- `registered=True` with the fresh-link read-back `value='Trustsoft.NotifyIcon.Sample'`
  `matchesExpected=True` is registration-on-first-show (D060) holding on the public path, unchanged
  from S02.
- `libraryTrace=24` and the tray totals' `library trace lines=0` in the *same* capture remain the
  measured shape of F2 - the counts are the *sample's* listener, which reaches the library's own
  source through its internals grant, not a consumer's.

### 6. The negative control, measured on the sample's own path

The guard that S03 did not change the unregistered case (and that the S03 events are not what makes a
show work). Command:

```text
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe \
    --toast --toast-skip-register --toast-after 2 --toast-buttons --run-seconds 12
```

```text
[sample] toast demonstration: identity='Trustsoft.NotifyIcon.Sample' shortcut='C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.Sample.lnk' register=False severity=Default repeat=off - this is the negative control, so no shortcut is created anywhere: the shows go through the library's internal seam (ToastShow directly), which is the only path that can show for an identity that was never registered.
[sample] toast demonstration negative control: no shortcut is created; read-back success=False operation='OpenShellLink' code=0x80070002 value='(null)' carriesTheIdentity=False
[sample] toast show scheduled: first show after 2s (--toast-after); the toast's launch argument is 'sample-toast-N', so the activation a click delivers names the show it came from.
[sample] toast content: title='Trustsoft.NotifyIcon sample toast' body='Click this banner's body: the sample prints the activation it receives.' severity=Default launch='sample-toast-1' buttons=[sample-button-1,sample-button-2]
[sample] toast show #1: asking the shell for identity='Trustsoft.NotifyIcon.Sample' launch='sample-toast-1' title='Trustsoft.NotifyIcon sample toast'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: begin aumid='Trustsoft.NotifyIcon.Sample' title='Trustsoft.NotifyIcon sample toast' launch='sample-toast-1'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: GetNotifierSetting hr=0x00000000 setting=0
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: LoadXml hr=0x00000000 xml=<toast launch="sample-toast-1"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon sample toast</text><text>Click this banner's body: the sample prints the activation it receives.</text></binding></visual><actions><action content="Button 1" arguments="sample-button-1"/><action content="Button 2" arguments="sample-button-2"/></actions></toast>
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: subscribed activated=0x330B054A9CFFAAEF dismissed=0xE016C64ACE681DFE failed=0x1CBDD8219050B7F7
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: Show hr=0x00000000 (accepted by the shell; delivery is judged out of process)
[sample] toast show #1: the shell accepted it (setting=0 raw; S_OK is acceptance, not visibility); delivery is judged out of process with scripts/probe-toast --history 'Trustsoft.NotifyIcon.Sample'
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: dispose
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeActivated(0x330B054A9CFFAAEF) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeDismissed(0xE016C64ACE681DFE) hr=0x00000000
Trustsoft.NotifyIcon Verbose: 2 : [trace] toast show: UnsubscribeFailed(0x1CBDD8219050B7F7) hr=0x00000000
[sample] toast teardown: 1 show(s) unsubscribed and released - no activation subscription outlives the process.
[sample] toast post-teardown window: activations=0 dismissals=0 errors=0 after the teardown line (0 is the disposal guarantee)
[sample] toast totals: shows=1, accepted=1, activations=0 (last arguments='(none)'), dismissals=0, failures=0, errors=0, refused=0, libraryTrace=16, registered=False, identity='Trustsoft.NotifyIcon.Sample'.
```

The control's verdict is the absence of an activation, exactly as in S01 and S02: `registered=False`,
`0` activations and `0` dismissals, and the seam's `setting=0 raw` acceptance line unchanged. The
run also confirms the slice's own choice of surface: the control keeps driving `ToastShow` directly
(no shortcut is created anywhere, and the read-back confirms `carriesTheIdentity=False`), while the
public `ToastNotifier` always registers on its first show - which is why the negative control is the
only path that can show for an identity that was never registered.

### 7. The coexistence sentence, recorded for S05

S05's README must copy this sentence **verbatim** (it is pinned in the generated documentation by
`ToastEventTests.The_two_boundary_sentences_appear_verbatim_in_the_generated_documentation`, so the
two slices cannot drift into two different promises):

> Toasts and balloons are independent: showing a toast never suppresses, replaces or re-routes a balloon tip, and showing a balloon tip never replaces or re-routes a toast.

**No M001 balloon behaviour changed.** S03 touched the toast path only; the single shared file it
changed is `NotifyIconTrace.cs`, and the change is additive (one new member and a third event id,
`3` - the tray's ids `1` and `2` and both tray writers are unchanged). The evidence is the M001
complement of the suite (item 9): `--filter "FullyQualifiedName!~Toast"` ran **416 passed / 0 failed**,
including `TrayIconBalloonTipTests` and `BalloonCallbackTests`. The capture quoted in item 5 also
shows the balloon/tray totals counters all at `0` in a toast-only run, i.e. showing a toast requested
no balloon, opened no menu and produced no notification-area traffic.

### 8. Human follow-up: the click clauses of the demo

**This is a follow-up, not a result. No run in this task proved a click.** The automated captures in
items 5 and 6 prove the payload and the plumbing - that the document carries both buttons, that the
subscriptions are established and released, and that a delivered argument reaches the handler and
classifies. **Only a person can prove the click attribution**, because the platform reports no
per-element information (item 2).

To be performed by a human on a machine with a visible notification area, with the sample running as
`--toast --toast-buttons --toast-after 2 --run-seconds 60`:

1. click **Button 1** - the sample must print `element=button-1` for `sample-button-1`;
2. click **Button 2** - the sample must print `element=button-2` for `sample-button-2`;
3. click the toast **body** - the sample must print `element=body` for this show's
   `sample-toast-N` launch argument;
4. dismiss the toast (its close button, or letting it time out) - the sample must print one dismissal
   with a reason from the three known values;
5. confirm that after the teardown line nothing further is printed - which is the disposal guarantee
   as a person sees it.

Expected reading: **three distinct classified arguments plus one dismissal, and silence afterwards**.
The classifier deliberately answers `unknown` for anything it cannot match against the arguments this
run actually sent, so an `element=unknown` line is a finding, not a rounding error (S03/T04's decision:
per-click attribution is not available to an automated instrument, so the classifier never guesses).

### 9. What S03 deliberately did not deliver

| Not delivered | Why / where it lands |
| --- | --- |
| **Images from an `ImageSource`** - `ToastImage.Reference` is still an already-formed reference string (D059) | S04 turns a WPF `ImageSource` into a `Reference` (the contract is S02 item 6) |
| **The failure taxonomy** - `ToastError` reports a stable operation name and a code, not a classified cause | S04 owns the taxonomy; S03's contribution is the channel plus the pinned operation names (`NotificationFailed` and the seam member names) |
| **The README and the package documentation** | S05; this slice only records the verbatim sentence S05 must carry (item 7) |
| **The multi-TFM consumer proof and the exported-surface re-measurement from the packed artifact** | S05; the nineteen-type surface is pinned against the built assembly by three allow-lists, not against the `.nupkg` |
| **Activation after the app has exited (closed-app relaunch)** | D054: v1 delivers activation only while the application is running, and no COM activator is registered |
| **A live `ToastError`** | it cannot be forced on a healthy machine (item 3); the event is exercised by the fake, and the sample's stderr `toast error:` line plus the library's Error-level line are the two surfaces a live failure would travel |

**The suite, run once in this task**
(`dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --no-build`):

```text
Passed!  - Failed:     0, Passed:   580, Skipped:     0, Total:   580, Duration: 1 m 14 s - Trustsoft.NotifyIcon.Tests.dll (net8.0)
```

- **580 passed / 0 failed**, whole assembly, no filter - so the live probes
  (`ToastApiLiveProbeTests`, `ToastIdentityLiveProbeTests`) ran live inside it. S02's sweep was 552;
  the difference is this slice's tests (`ToastEventTests` contributes 18).
- The toast/pin subset (`--filter "FullyQualifiedName~Toast|FullyQualifiedName~PackagePurity"`):
  **176 passed / 0 failed**. The complement (`--filter "FullyQualifiedName!~Toast"`, i.e. the M001
  classes): **416 passed / 0 failed**. 176 + 416 = 592 counts the 12 `PackagePurity` tests twice,
  which is the 580 total.
- **No M001 suite regressed**: the M001 classes are the 416-test complement and they are all green,
  with the same 416 S02 measured.
- Honest note on the one intermediate failure seen in this task: a single full-suite run (not one of
  the two readings above) failed `TrayIconMenuActivationTests.A_second_right_click_while_the_menu_is_open_opens_nothing_new`
  with `foreground=0x5A064E(Chrome_WidgetWin_1) anchorIsForeground=False` - an **environmental
  foreground-stealing flake, not a regression**: the run's own diagnostic block shows another
  application holding the foreground window. That class then passed **16/16 on three consecutive
  isolated re-runs**, and the full sweep above is green. Nothing in this slice touches the menu path
  (it is M001's); the reading is recorded rather than retried until green.
- The slice's own contract class, `--filter "FullyQualifiedName~ToastEventTests"`: **18 passed /
  0 failed** - the event surface, the failure split, the wording rule and all five disposal tests.

### Files added or changed by S03

- `src/Trustsoft.NotifyIcon/ToastNotifier.cs` - the three public events, the subscription wiring, the
  `RaiseError` path, the four post-dispose refusals, and the `Show` failure split (D061).
- `src/Trustsoft.NotifyIcon/ToastActivatedEventArgs.cs`, `ToastDismissedEventArgs.cs`,
  `ToastDismissalReason.cs`, `ToastErrorEventArgs.cs` - the four public payload types (T01).
- `src/Trustsoft.NotifyIcon/Interop/ToastApi.cs` - the callback read-back (`ReadDismissedReason`
  returns `-1` on an unreadable payload), the event-name threading into the subscription, and the
  Error-level backstop for a handler bug.
- `src/Trustsoft.NotifyIcon/Interop/ToastShow.cs` - `Dispose` nulls the three callbacks before
  unwinding (T03).
- `src/Trustsoft.NotifyIcon/NotifyIconTrace.cs` - `ToastError` and event id 3 (additive; the tray
  ids and writers are unchanged).
- `samples/Trustsoft.NotifyIcon.Sample/App.xaml.cs` - the three event subscriptions, the
  `element=...` classifier, the `toast error:` stderr line, `--toast-buttons` and
  `--toast-dispose-after` with the post-teardown window.
- `tests/Trustsoft.NotifyIcon.Tests/ToastEventTests.cs` - the slice's contract class (18 tests).
- `tests/Trustsoft.NotifyIcon.Tests/Fakes/FakeToastApi.cs` - the opt-in handler capture and replay
  members the disposal and race tests need.
- `tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs`, `TrayIconExceptionTests.cs` - the three
  widened surface allow-lists.
- `docs/TOAST-MEASUREMENT.md` - this section.
