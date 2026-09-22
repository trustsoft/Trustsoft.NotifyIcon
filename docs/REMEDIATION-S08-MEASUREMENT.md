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

## Shipped: the mechanism in the library's own open path (S08/T02)

T02 moved V3 out of this instrument and into `TrayIcon.OpenMenu`, so the table above is the evidence
behind the implementation rather than a description of a test-local repair. What the library does now,
after `menu.IsOpen = true` and only for the popup its own open resolved from
`PresentationSource.FromVisual(menu)`:

1. read the popup's `GW_OWNER`, write this anchor's handle when it differs, and read it back;
   nothing is written when WPF already resolved the anchor, which is the ordinary click-driven case;
2. re-claim the foreground through the attach-thread sequence (`AttachThreadInput` to the foreground
   window's thread, `BringWindowToTop`, `SetForegroundWindow`, detach) when the anchor is not already
   the foreground window, with `SwitchToThisWindow` as the recorded last resort when no window holds
   the foreground at all. `TrayMenuAnchorWindow.ReclaimForeground` is the whole of it; it is a no-op
   on the ordinary path.

The open trace line now carries `popup=0x… ownerBefore=0x… ownerAfter=0x…` beside the existing
`anchor=` and `foreground=`, and the same readings are exposed internally as `MenuPopupHandle`,
`MenuOwnerBeforeRepair`, `MenuOwnerAfterRepair`, `MenuOwnerRepaired` and `MenuAnchorIsForeground`.

**The delivered path measured, with the claim refused through the seam** (exec
`16713c30-e0a4-4508-aa7b-ac3fe63da97b`, 2026-09-22; raw line below taken from
the class named at the end of this section):

```
host=0x… anchorBeforeClick=0x… libraryPopup=0x… presentationSource=0x… windows=[0x…]
ownerBefore=0x0 ownerAfter=<anchor> ownerMeasured=<anchor> repaired=True anchorForeground=True
foregroundAfterRepair=<anchor> claims=[False, True] claimObserved=True
anchorRect=(100,216,1x1) popupRect=(100,216,193x50) offset=(66.667,144)
isOpenBefore=True isOpenAfter=False windowsAfterClick=[]
```

Four facts follow from it, and each is asserted rather than assumed:

| Fact | Reading | Where it is asserted |
| --- | --- | --- |
| WPF still builds the popup ownerless under the refusal | `ownerBefore=0x0` | `TrayMenuOwnerMechanismProbeTests.The_refused_claim_is_really_refused_and_wpf_builds_the_popup_ownerless` |
| The library's own write is what the owner becomes | `ownerAfter == ownerMeasured == anchor`, `repaired=True` | `...The_delivered_open_writes_the_anchor_into_the_owner_slot_and_reads_it_back` |
| The refused claim is re-claimed and the anchor ends up foreground | `claims=[False, True]`, `anchorForeground=True` | `...The_delivered_open_reclaims_the_foreground_because_the_plain_claim_was_refused` |
| The menu is dismissed and nothing is left behind | `isOpenAfter=False`, `windowsAfterClick=[]` | `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_an_outside_click_dismisses_the_menu_and_leaves_no_popup_window` |

The click-driven path is unchanged, measured rather than assumed: with the claim granted the seam sees
exactly one claim, WPF's own construction already resolved the anchor (`ownerBefore == anchor`,
`repaired=False`), and the placement round trip and popup rectangle are the ones the placement tests
pin (`offset=(66.667,144)`, `popupRect=(100,216,193x50)`) -
`TrayIconMenuOwnerDeterminismTests.The_click_driven_path_claims_once_needs_no_repair_and_is_placed_unchanged`.

**One honest difference from the V2 row above.** In T01's instrument the owner write was applied to a
popup that had already been shown (600 ms after the open) and the outside click still left the menu
open, which is why the mechanism carries the activation half. In the delivered path the write happens
inside the open, while the popup window is being created, and the re-claim follows it; the delivered
measurement therefore proves the outcome and does not re-measure "owner write alone". The V2 row stays
as recorded evidence for why both halves are shipped.

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
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~TrayIconMenuOwnerDeterminismTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~TrayMenuDismissalTests"
```

The probe class now asserts the delivered mechanism's internals
(`The_refused_claim_is_really_refused_and_wpf_builds_the_popup_ownerless`,
`The_delivered_open_writes_the_anchor_into_the_owner_slot_and_reads_it_back`,
`The_delivered_open_reclaims_the_foreground_because_the_plain_claim_was_refused`,
`The_popup_is_resolved_from_the_menu_presentation_source_not_a_size_heuristic`); the delivered
contract is asserted by `TrayIconMenuOwnerDeterminismTests`. T01's raw dump was produced by a
throwaway variant harness that is no longer part of the shipped class, because the repairs it applied
by hand now live in the library.

The T01 variant table's own probe assertions no longer exist as written, because the repairs that
class applied by hand now live in the library; the raw dump above is its record. At the T01 revision,
exec `cd13d936-d211-4af5-8357-430e9ab89c89` ran the probe class (4 passed) and `TrayMenuDismissalTests`
(6 passed), and the full suite was `Failed: 0, Passed: 408, Skipped: 0`
(exec `8005c66f-6aab-437b-8ae6-45b8ddc62174`; 404 baseline tests plus the 4 probe tests).

One implementation note that the raw lines depend on: `FakeShellApi`'s window-manager members perform
the real call when a test does not script them, and a scripted `SetForegroundWindowResult` is consumed
by the claim the library makes before the popup exists - the one WPF's construction depends on. That is
what keeps the hostile state reproducible while leaving the repair's re-claim a measured real call
rather than a second script.
