# S08 remediation measurement: the menu popup's owner and its dismissal

Scope: M001/S08/T01. This document records the measurement that decides which owner mechanism the
library ships. It is the evidence behind decision D044 and the probe class
`tests/Trustsoft.NotifyIcon.Tests/TrayMenuOwnerMechanismProbeTests.cs`.

## Why this measurement exists

The five failing tests listed in `docs/TEST-ENVIRONMENT.md` and the S03 findings F4 and F5 are one
mechanism: WPF's `Popup.BuildWindow` decides the popup's owner from the foreground relationship at the
instant the popup window is created, so a session in which Windows refuses this process the foreground
produces an ownerless popup (`owner=0x0`) that an outside click does not dismiss. Until this task the
refusal was an ambient property of the session, which is why the same binary was green in one session
and `Failed: 5, Passed: 399` in another.

T01 makes the refusal scriptable and measures the repair variants against real Windows.

## The hermetic hostile state

The refusal is injected through the shell seam, not by hoping the session is hostile:

- `IShellApi` gained `GetForegroundWindow`, `SetForegroundWindow`, `GetWindowOwner`,
  `SetWindowOwner` and `GetWindowThreadProcessId`; `ShellApi` is their only `[DllImport]` home
  (`src/Trustsoft.NotifyIcon/Interop/ShellApi.cs`).
- `TrayMenuAnchorWindow.MakeForeground` now claims the foreground through the seam
  (`_shell.SetForegroundWindow(handle)`), so the library's own claim is the scriptable one.
- A `FakeShellApi` with `SetForegroundWindowResult = false` is the refusal; the probe also scripts
  `ForegroundWindowOverride` to a foreign handle, which is the shape the hostile session shows
  (`Shell_TrayWnd` or a terminal holding the foreground).

The probe opens the menu through the product's own path: a registered `TrayIcon` with an assigned
`ContextMenu` receives a version-4 `WM_CONTEXTMENU` callback by a same-thread `SendMessage` (the
S02/T04 injection precedent), on scripted geometry (icon rect `100,200,116,216`, work area
`1920x1080`, DPI 144, cursor `640,480`). The popup and the anchor are real windows; only the shell
answers are scripted. V2 and V3 apply their repair through a real `ShellApi` over the real popup
handle, so the new seam members are exercised against Windows rather than against a fake.

## Raw measurements

One line per variant. The four variants are V1 (control), V2 (explicit owner), V3 (V2 plus the
attach-thread foreground sequence) and V4 (V2 plus an in-library mouse hook, applied only if V2 and V3
both fail to dismiss).

Instrument: a diagnostic dump run of the probe harness, exec
`2b1d1298-f25c-499d-95f9-fa414525c859` (2026-09-22, worktree `milestone/M001`). The harness ships as
`TrayMenuOwnerMechanismProbeTests` (whose assertions pin the same facts); the dump method itself was a
throwaway instrument and is not part of the shipped class. Raw stdout:

```
mechanism=Shipped host=0x4501AE anchor=0x4C056C anchorBeforeClick=0x4C056C popup=0x3EC0A46(heuristic) heuristic=0x3EC0A46 presentationSource=0x3EC0A46 setForegroundWindow=False claimCallObserved=True ownerBefore=0x0 ownerAfter=0x0 foregroundBeforeOpen=0x2260A4E foregroundAfterOpen=0x2260A4E activeWindowAfterOpen=0x0 foregroundAfterRepair=0x2260A4E isOpenBefore=True isOpenAfter=True windowsAfterClick=[0x3EC0A46] foregroundAfterClick=0x2260A4E
mechanism=ExplicitOwner host=0x4601AE anchor=0x4D056C anchorBeforeClick=0x4D056C popup=0x3ED0A46(heuristic) heuristic=0x3ED0A46 presentationSource=0x3ED0A46 setForegroundWindow=False claimCallObserved=True ownerBefore=0x0 ownerAfter=0x4D056C foregroundBeforeOpen=0x2260A4E foregroundAfterOpen=0x2260A4E activeWindowAfterOpen=0x0 foregroundAfterRepair=0x2260A4E isOpenBefore=True isOpenAfter=True windowsAfterClick=[0x3ED0A46] foregroundAfterClick=0x2260A4E
mechanism=AttachThreadForeground host=0x4701AE anchor=0x0 anchorBeforeClick=0x4E056C popup=0x3EE0A46(heuristic) heuristic=0x3EE0A46 presentationSource=0x3EE0A46 setForegroundWindow=False claimCallObserved=True ownerBefore=0x0 ownerAfter=0x4E056C foregroundBeforeOpen=0x2260A4E foregroundAfterOpen=0x2260A4E activeWindowAfterOpen=0x0 foregroundAfterRepair=0x4E056C isOpenBefore=True isOpenAfter=False windowsAfterClick=[] foregroundAfterClick=0x2260A4E
```

Field meanings: `anchor` is the anchor handle read after the click (zero means the dismissal tore it
down), `anchorBeforeClick` is the same handle read before the click, `popup` is the resolved popup
window (`heuristic` = the single popup-sized window of this process; `presentationSource` =
`PresentationSource.FromVisual(menu)`), `ownerBefore`/`ownerAfter` are the popup's `GW_OWNER` before
and after the repair, `foregroundAfterRepair` is the desktop foreground window after the repair,
`isOpenAfter` and `windowsAfterClick` are the dismissal result.

| Variant | setForegroundWindow | ownerBefore | ownerAfter | foregroundAfterRepair | isOpenAfter | windowsAfterClick | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V1 Shipped (control) | `False` | `0x0` | `0x0` | `0x2260A4E` (foreign) | `True` | `[0x3EC0A46]` | Ownerless popup, outside click does **not** dismiss |
| V2 ExplicitOwner | `False` | `0x0` | anchor (`0x4D056C`) | `0x2260A4E` (foreign) | `True` | `[0x3ED0A46]` | Owner repaired, outside click still does **not** dismiss |
| V3 AttachThreadForeground | `False` | `0x0` | anchor (`0x4E056C`) | anchor (`0x4E056C`) | `False` | `[]` | Owner repaired **and** menu dismissed, anchor torn down |
| V4 MouseHook | not applied | - | - | - | - | - | Not required: V3 dismissed, so the "if and only if both V2 and V3 fail" condition was never met |

The three measured variants reproduced identically on every run made during this task (three manual
dump repeats, the cited exec runs, and the shipped probe assertions): V1 always `owner=0x0` and
`isOpenAfter=True`; V2 always `owner=anchor` and `isOpenAfter=True`; V3 always `owner=anchor`,
`foregroundAfterRepair=anchor`, `isOpenAfter=False`, `windowsAfterClick=[]`.

Two further measured facts recorded on every run:

- `claimCallObserved=True` on all variants: the library really makes its foreground claim through the
  seam, so the scripted refusal is the value the library reads.
- `presentationSource == heuristic` on all runs: `PresentationSource.FromVisual(menu)` on the open
  menu resolves the same window the size discrimination finds, so the shipped repair can name the
  popup without a size or class heuristic over the process's windows.

## What the measurement decided

**The shipped mechanism is V3: the explicit owner plus the attach-thread foreground sequence.**

- The owner write alone (V2) repairs the value but not the behaviour: the measured popup is owned by
  the anchor (`ownerAfter == anchor`) and the menu an outside click leaves open anyway
  (`isOpenAfter=True`, popup still on screen). V2 is therefore rejected as insufficient, with those
  numbers.
- V3 adds the attach-thread sequence (`AttachThreadInput` to the foreground window's thread,
  `BringWindowToTop`, `SetForegroundWindow`, detach), which made the anchor the foreground window
  (`foregroundAfterRepair == anchor`) and produced the dismissal (`isOpenAfter=False`,
  `windowsAfterClick=[]`, anchor destroyed). `SwitchToThisWindow` is the recorded last resort when
  there is no foreground window to attach to; this session measured `foregroundBeforeOpen=0x2260A4E`,
  so it was not taken.
- V1 is the control: it reproduces the exact state that failed five tests (`owner=0x0`, menu stays
  open) from a script, which is what turns the environment dependency into a contract.

**An in-library low-level mouse hook (V4) is not acceptable for this library and was not needed.**
A `WH_MOUSE_LL` hook would install process-global input interception into a library whose entire
public promise is a notification-area icon and no window; it would run on a consumer's input path, it
would need correct removal on every close path including a crashed teardown, and it would be a far
larger behavioural surface than the two calls V3 needs. It is recorded here as considered and
rejected, and the requirement to apply it was never met because V3 dismissed.

## What is not claimed

- The measurement is hermetic for the *refusal* (scripted through the seam) and real for the *dismissal*
  (a real injected click against a real popup routed by the OS). It is not a live user observation: no
  keyboard Escape path, no live notification-area click and no Display Scale matrix was exercised here.
- V4 was not measured. Its row records the measured values that made it unnecessary, not a result.
- The absolute window handles change every run; only their relationships (`ownerAfter == anchor`,
  `foregroundAfterRepair == anchor`, `windowsAfterClick == []`) are asserted.

## Reproduce

```
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~TrayMenuOwnerMechanismProbeTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~TrayMenuDismissalTests"
```

The probe class asserts the facts above (`V1_...`, `V2_the_explicit_owner_alone_is_measurably_insufficient`,
`V3_the_chosen_mechanism_owns_the_popup_and_dismisses_it`,
`The_popup_is_resolvable_from_the_menu_presentation_source`). At the final revision, exec
`cd13d936-d211-4af5-8357-430e9ab89c89` ran the probe class (4 passed) and `TrayMenuDismissalTests`
(6 passed); the same dismissal class passed 6/6 immediately after the seam change (exec
`aac87ef5-2a9b-44bd-acb3-0687a52f65a6`). The full suite is green at this revision:
`Failed: 0, Passed: 408, Skipped: 0` (exec `8005c66f-6aab-437b-8ae6-45b8ddc62174`; 404 baseline tests
plus the 4 probe tests).

One implementation note that the raw lines depend on: `FakeShellApi`'s five window-manager members
perform the real call when a test does not script them, so answering the foreground claim with a bare
`true` cannot silently change the delivered behaviour. The probe scripts `SetForegroundWindowResult =
false`, which is the only difference between its open and the delivered one.
