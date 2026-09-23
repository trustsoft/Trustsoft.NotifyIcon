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


