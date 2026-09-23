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
| Session | agent shell (no interactive foreground); `RoInitialize(RO_INIT_SINGLETHREADED)` on the already-MTA thread returns `0x80010106` (`RPC_E_CHANGED_MODE`) |
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
live shell cannot be asked to do: the exact measured acquisition and teardown sequence, the payload
string handed to `LoadXml`, the notifier bound to the caller's identity, the subscribe-before-show
and unsubscribe-before-release ordering, the failure-as-data unwinding at every step (each of the
11 steps is scripted to fail, and each unwind releases exactly the handles acquired and removes
exactly the subscriptions registered), the idempotent disposal, and the payload's escaping and
omission rules.

