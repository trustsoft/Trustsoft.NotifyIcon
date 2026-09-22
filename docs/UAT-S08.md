# UAT-S08 - deterministic menu owner and dismissal

**Slice:** M001 / S08 (Deterministic menu owner and dismissal)
**Requirements:** R003 (a right click opens the assigned menu at the icon and an outside click dismisses
it), R007/R014 (owner lifetime: the popup's owner is the library's anchor window, never the shell
registration host, and the menu path leaks no window or GDI object), R015 (property/state changes are
applied on the owning dispatcher)
**Date:** 2026-09-22
**Machine:** MINIBOOKX, `MINGW64_NT-10.0-26200` (Git Bash), .NET SDK `10.0.401`, single monitor
1920x1200 at 150 % scale.
**Revision tested:** `996c9b0` on `milestone/M001` (S08/T01, T02 and T03 committed) plus one
uncommitted T02 follow-up edit to `tests/Trustsoft.NotifyIcon.Tests/TrayIconMenuContractTests.cs`
(`Win32.GetWindow(popup, GW_OWNER)` -> `Win32.GetWindowOwner(popup)`, same value).
**Document status:** **final, consolidated by S08/T04.** Every row names the instrument behind it. A
hermetic test is never presented as a live user-path observation, and a live run is never presented as
a contract.
**Retry note.** The first T04 attempt measured these runs and wrote the platform notes, then aborted on
a tool it did not have (the plan asked it to edit the requirement records). The retry re-ran the exit
measurement, the builds, the filtered classes and the live sample in the same hostile session; both
attempts' exec ids are cited, and the requirement-record editing is now an explicit hand-off (S08-23).

## The instruments, named

- **In-repo hermetic tests** (the shipped contract). They open the menu through the product's own path -
  a registered `TrayIcon` receives the shell's version-4 `WM_CONTEXTMENU` callback by same-thread
  `SendMessage` - and drive a **real** WPF popup, a **real** outside click and **real** window handles.
  Only the shell seam is a fake. The refusal of the foreground claim is scripted on that seam
  (`FakeShellApi.SetForegroundWindowResult = false`), the repair's re-claim is a real call. Classes:
  `TrayIconMenuOwnerDeterminismTests`, `TrayMenuOwnerMechanismProbeTests`, `TrayMenuDismissalTests`,
  `TrayIconMenuCloseNotificationTests`.
- **The live reference sample** (`Trustsoft.NotifyIcon.Sample`). Its own `Opened`/`Closed` handlers, OS
  window readings (`EnumThreadWindows`, `GetWindowRect`, `GetWindow(GW_OWNER)`, `GetDpiForWindow`) and
  totals line are an independent instrument around the library. It has no access to the library's
  internal trace source (S03 finding), and its `owner=` reading is taken **before** the library's
  post-open repair.
- **The full-suite run** (`dotnet test ... --no-build`). The milestone's exit measurement.
- **A scratch session-foreground probe** (`.gsd/exec/foreground-session-probe.ps1`, not product code).
  It reads the desktop's foreground window before this process creates a window of its own, then asks
  `SetForegroundWindow` for a window it just created. This is what makes "hostile session" a
  measurement in this document rather than an assumption.
- **Human follow-ups.** Named where they apply; never counted as passes.

## Environment

This session is the **hostile shape by measurement**: an agent shell that never received the last input
event, with a foreign process holding the foreground. Scratch probe, gsd_exec
`e8604f6e-9ae8-4446-8ad4-6c58ecf6732e`:

```
probe pid=10032
foregroundBeforeAnyWindowOfOurs=0x2260A4E class=CASCADIA_HOSTING_WINDOW_CLASS pid=14372 process=WindowsTerminal
foregroundBeforeHeldByThisProcess=False
windowCreated=0x1D3056C
setForegroundWindow=False
foregroundAfterClaim=0x2260A4E class=CASCADIA_HOSTING_WINDOW_CLASS pid=14372 process=WindowsTerminal
windowIsForegroundAfterClaim=False
```

`setForegroundWindow=False` with the terminal (`0x2260A4E`) holding the foreground is the shape the S06
diagnostics recorded (`setForegroundWindow=False ... owner=0x0 foregroundBeforeOpen=0x0`), and the
handle is the same foreign foreground window the T01 measurement recorded
(`docs/REMEDIATION-S08-MEASUREMENT.md`, `foregroundBeforeOpen=0x2260A4E`). The live sample run in this
session independently reads the popup ownerless on its own open line
(`owner=0x0`, gsd_exec `9ea72e4d`; re-measured in the retry session, `c827750b`, raw output below).

The retry session measured the same foreground state before it ran anything else - `probe pid=668`,
`foregroundBeforeHeldByThisProcess=False`, `setForegroundWindow=False`, `foregroundAfterClaim=0x2260A4E
... process=WindowsTerminal` (gsd_exec `90e93238-8b21-47c3-927f-38d30cb00d37`, the same exec as the two
suite runs below).

**One session quirk, recorded because it cost time.** In this agent shell the `dotnet` CLI inherits a
stripped environment (no `APPDATA`/`LOCALAPPDATA`/`ProgramData`/`TMP`) and reuses long-lived MSBuild and
Roslyn compiler servers that were started from such a shell; inside those processes NuGet's folder
resolution throws `ArgumentNullException (path1)`, which surfaces as `error NETSDK1060` on every project
whose restore graph is re-evaluated (here the sample's WPF temp project) and as a failing
`dotnet restore`. It is not repository state: a trivial project in `%TEMP%` outside the worktree failed
identically (gsd_exec `715f29ac`). Exporting the shell folders and keeping the CLI off the reused
servers fixes it (gsd_exec `7e7b0000`, `90e93238`); the recipe is in `docs/TEST-ENVIRONMENT.md`,
"How to run".

**What this environment does and does not mean.** It reproduces the state the five S06 failures came
from, so the suite result below is the exit measurement the slice asked for. It is **not** a normal
interactive desktop: no human is at the keyboard, no shell click can be delivered to this process's tray
icon (the Windows 11 overflow flyout owns it), and a normal-session run was not available in this unit.
The normal-session leg therefore rests on the hermetic "claim granted" tests and on the pre-S08
interactive-session total, exactly as recorded in the rows below.

## The exit measurement

| Run | Command | Result | Session | Evidence |
| --- | --- | --- | --- | --- |
| before (S06 UAT, revision `c57446a`) | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore` | **`Failed: 5, Passed: 399, Skipped: 0, Total: 404`**, exit 1, 55 s; reproduced in 56 s | agent shell, foreground refused | gsd_uat_exec `cb18d05d-24ff-4015-8373-e84ba41e6d4d`, `7e1dda82-6312-4f98-8d15-54871eb12319` |
| after, run 1 (S08/T04, revision `996c9b0`) | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 79 s | agent shell, foreground refused (measured above) | gsd_exec `9a9e274d-b545-4f4a-a37e-480af5227424` |
| after, run 2 (same revision, immediately after) | same | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 79 s | same session | gsd_exec `1fed151a-2d61-4bb9-821f-502e27c38dc1` |
| after, retry run 1 (T04 retry, same revision plus the uncommitted doc/test edits) | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 1 m 16 s | agent shell, foreground refused (re-measured above) | gsd_exec `90e93238-8b21-47c3-927f-38d30cb00d37` |
| after, retry run 2 (immediately after) | same | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 1 m 15 s | same session | gsd_exec `90e93238-8b21-47c3-927f-38d30cb00d37` |

The same command that produced `Failed: 5, Passed: 399, Total: 404` twice now produces a green run twice,
in the same environment shape. 416 = the 404 baseline + 4 probe tests (T01, reshaped by T02) + 4
determinism tests (T02) + 4 close-notification tests (T03). **0 skipped**: no conditional guard was
introduced anywhere. Every class that held one of the five failures was also run filtered at this
revision and is green (see "Filtered runs" below).

## Claims, one row per claim

| # | Claim | Verdict | Instrument | Evidence |
|---|---|---|---|---|
| S08-1 | The refused foreground claim is scriptable, and the probe's V1 control reproduces the S06 failure state: WPF builds the popup ownerless and an outside click leaves the menu open | **PASS** | hermetic test + raw measurement dump | `TrayMenuOwnerMechanismProbeTests.The_refused_claim_is_really_refused_and_wpf_builds_the_popup_ownerless`; V1 row of `docs/REMEDIATION-S08-MEASUREMENT.md` (exec `2b1d1298`) |
| S08-2 | The owner write alone (V2) repairs the value but not the dismissal, so it was rejected with its measured numbers | **PASS (measured rejection)** | raw measurement dump | V2 row, exec `2b1d1298`: `ownerAfter=anchor`, `isOpenAfter=True`, popup still on screen |
| S08-3 | The shipped mechanism (V3: explicit owner + attach-thread foreground sequence, `SwitchToThisWindow` as recorded last resort) makes the anchor foreground and dismisses the menu, leaving no window | **PASS** | raw dump (real `ShellApi` over the real popup) + hermetic tests | V3 row, exec `2b1d1298`; `TrayMenuOwnerMechanismProbeTests.The_delivered_open_reclaims_the_foreground_because_the_plain_claim_was_refused` |
| S08-4 | An in-library low-level mouse hook (V4) was not applied, because V3 dismissed; it is recorded as considered and rejected for a library that promises no window | **PASS (recorded decision)** | measurement + decision D044 | V4 row of the same file; `docs/REMEDIATION-S08-MEASUREMENT.md`, "An in-library low-level mouse hook (V4) is not acceptable" |
| S08-5 | With the foreground claim refused, the library's own open path yields `GW_OWNER == anchor`, never the registration host | **PASS** | hermetic product-path test | `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_the_popup_is_owned_by_the_anchor_and_never_by_the_registration_host` (gsd_exec `58d00a30`, 4/4) |
| S08-6 | In that same refused state an injected outside click dismisses the menu and no popup window remains | **PASS** | hermetic product-path test | `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_an_outside_click_dismisses_the_menu_and_leaves_no_popup_window` |
| S08-7 | The click-driven path is unchanged: exactly one granted claim, `repaired=False`, WPF's own construction already owned the popup, and the pinned rectangle/offset round trip | **PASS** | hermetic product-path test | `TrayIconMenuOwnerDeterminismTests.The_click_driven_path_claims_once_needs_no_repair_and_is_placed_unchanged` |
| S08-8 | The no-foreground edge takes the recorded last resort (`SwitchToThisWindow`) and still repairs and dismisses | **PASS** | hermetic product-path test | `TrayIconMenuOwnerDeterminismTests.With_no_foreground_window_the_recorded_last_resort_repairs_the_owner_and_dismisses_the_menu` |
| S08-9 | The raw ownerless construction (hand-built popup, no placement target) is still not dismissed: the repair does not reach every popup in the process | **PASS** | in-repo real-popup test | `TrayMenuDismissalTests.An_ownerless_popup_with_no_placement_target_is_not_dismissed_by_an_outside_click`; class 6/6 (gsd_exec `8e6b31c2`) |
| S08-10 | The popup is resolved from the menu's own presentation source, never a size/class heuristic over the process's windows | **PASS** | hermetic test + measurement | `TrayMenuOwnerMechanismProbeTests.The_popup_is_resolved_from_the_menu_presentation_source_not_a_size_heuristic`; `presentationSource == heuristic` on every measured run |
| S08-11 | A disposal-driven close delivers `ContextMenu.Closed` to the consumer's own handler exactly once, before `Dispose` returns, with no anchor or popup window left | **PASS** | in-repo real-popup test | `TrayIconMenuCloseNotificationTests.Disposing_the_icon_delivers_the_open_menus_Closed_exactly_once_before_it_returns`; class 4/4 (gsd_exec `a2d546ad`) |
| S08-12 | The same delivery holds in the hermetic hostile state (claim refused, popup really built ownerless) | **PASS** | in-repo test with scripted seam | `...With_the_foreground_claim_refused_the_disposal_driven_close_still_notifies_and_leaves_no_window` (gsd_exec `a2d546ad`) |
| S08-13 | Disposal with nothing open delivers nothing **and does not pump** the dispatcher | **PASS** | in-repo negative control | `...Disposing_an_icon_whose_menu_is_already_closed_delivers_nothing_and_does_not_pump_the_queue` (gsd_exec `a2d546ad`) |
| S08-14 | The close trace line carries `closeNotificationDelivered=`, so a support log distinguishes a notifying close from a silent one | **PASS** | in-repo test on the library's internal trace source | `...The_close_trace_line_records_that_the_consumer_notification_was_delivered` (gsd_exec `a2d546ad`) |
| S08-15 | The live reference sample, in a run that never clicks outside and whose menu is still open at shutdown, prints its dismissed line and reports `menu dismissals=1` | **PASS** | live sample run | gsd_exec `9ea72e4d` (T04 run), `52a02e73` (T03 run) and `c827750b` (T04 retry run), raw output below |
| S08-16 | The pre-fix control: the identical run at the same revision minus the change reported `menu opens=1, menu dismissals=0` and printed no dismissed line | **PASS (control)** | live sample run before the change | gsd_exec `efb4a322`, raw output below |
| S08-17 | This session's foreground state is the hostile one that produced the five failures | **PASS** | scratch session-foreground probe | gsd_exec `e8604f6e`, re-measured in the retry session, `90e93238`, raw output above |
| S08-18 | The full suite is green at this revision in that session, twice, with nothing skipped | **PASS** | full-suite run | gsd_exec `9a9e274d`, `1fed151a` (416/416/0); re-run twice in the retry session, `90e93238` (416/416/0) |
| S08-19 | The five previously failing tests still exist with the same names and now pass | **PASS** | full suite + per-class runs at this revision | `TrayMenuDismissalTests` 6/6 (`8e6b31c2`); `TrayIconMenuActivationTests` 16/16 (`6908467f`, holds two of the five); `SliceContractTests` 8/8 (`29ed48cc`, holds one); `TrayIconMenuContractTests` (holds one) inside the 416/416 runs |
| S08-20 | The build is clean on the solution and on each of the three library target frameworks | **PASS** | build | gsd_exec `513ec620` (solution, 0 warnings / 0 errors), `5055e5d3` (net8.0/net9.0/net10.0-windows, exit 0 each); re-run in the retry session, `7e7b0000` (solution + all three TFMs, 0 warnings / 0 errors, exit 0 each) |
| S08-21 | `docs/TEST-ENVIRONMENT.md` no longer accepts the five failures as an environment dependency and explicitly rejects the skip-with-reason guard | **PASS** | document | `docs/TEST-ENVIRONMENT.md` (rewritten by this task: status RETIRED, rejected guard section, before/after totals, what still depends on the OS) |
| S08-22 | F1, F4 and F5 carry their replacement dispositions in `docs/UAT-S03.md`, cross-referencing D030/D042/D043 against D044/D045 | **PASS** | document | `docs/UAT-S03.md` - status blocks on F1 (CLOSED, S08/T03/D045), F4 (SUPERSEDED, S08/T02/D044), F5 (TIGHTENED TO EQUALITY, S08/T02/D044), plus the check-7, 10f and "What was NOT observed" pointers |
| S08-23 | The M001 requirement records that state the dismissal and the owner clauses (R003, R007/R014) name the test or run carrying each verdict | **DEFERRED - deliberate hand-off with a named owner** | this document and `docs/UAT-S03.md` carry the verdicts; the database wording is updated outside this task | R003 (the outside-click dismissal clause) and the R007/R014 owner language are **not** edited from this task: **no `gsd_requirement_*` tool was called here, by design.** The verdict behind each clause is already carried by S08-5/S08-6/S08-9/S08-10 (owner, dismissal) and by the F1/F4/F5 status lines of `docs/UAT-S03.md` (S08-22), so nothing in this slice depends on the wording being edited. The named owner of the update is the **operator or the M001 milestone validation**; leaving the records untouched is not a gap in this unit's evidence. |
| S08-24 | The suite is green in a **normal** session too | **NOT OBSERVED in this unit** | hermetic equivalent + inherited total | This unit only ever had the agent shell. Carried instead by the hermetic "claim granted" path (`The_click_driven_path_claims_once_needs_no_repair_and_is_placed_unchanged`, `TrayIconMenuActivationTests` 16/16) and by the pre-S08 interactive-session total (404/0/0, M001 validation round 1). Not re-measured here. |

## Raw output - T04 (the hostile-session proof)

### The full suite, twice

```
Test run for C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   416, Skipped:     0, Total:   416, Duration: 1 m 16 s - Trustsoft.NotifyIcon.Tests.dll (net8.0)
```

Identical output for run 2 (gsd_exec `9a9e274d`, `1fed151a`).

### Builds

```
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore        # gsd_exec 513ec620: Build succeeded, 0 Warning(s), 0 Error(s)
dotnet build src/Trustsoft.NotifyIcon/... -f net8.0-windows          # gsd_exec 5055e5d3: exit=0
dotnet build src/Trustsoft.NotifyIcon/... -f net9.0-windows          # gsd_exec 5055e5d3: exit=0
dotnet build src/Trustsoft.NotifyIcon/... -f net10.0-windows         # gsd_exec 5055e5d3: exit=0
```

### Filtered runs at this revision

```
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayMenuDismissalTests"
  -> Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 7 s            (gsd_exec 8e6b31c2)
dotnet test ... --filter "FullyQualifiedName~TrayIconMenuOwnerDeterminismTests"
  -> Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 7 s            (gsd_exec 58d00a30)
dotnet test ... --filter "FullyQualifiedName~TrayMenuOwnerMechanismProbeTests"
  -> Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 7 s            (gsd_exec 4dc1c4b5)
dotnet test ... --filter "FullyQualifiedName~TrayIconMenuCloseNotificationTests"
  -> Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 7 s            (gsd_exec a2d546ad)
dotnet test ... --filter "FullyQualifiedName~TrayIconMenuActivationTests"
  -> Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16, Duration: 20 s         (gsd_exec 6908467f)
dotnet test ... --filter "FullyQualifiedName~SliceContractTests"
  -> Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 2 s             (gsd_exec 29ed48cc)
```

Re-run in the retry session in one exec (gsd_exec `c827750b-598c-49a7-aa2c-2bd97bc44dd2`):

```
dotnet test ... --filter "FullyQualifiedName~TrayMenuDismissalTests"              -> 6/6, exit 0
dotnet test ... --filter "FullyQualifiedName~TrayIconMenuOwnerDeterminismTests"    -> 4/4, exit 0
dotnet test ... --filter "FullyQualifiedName~TrayMenuOwnerMechanismProbeTests"     -> 4/4, exit 0
dotnet test ... --filter "FullyQualifiedName~TrayIconMenuCloseNotificationTests"   -> 4/4, exit 0
```

### Live sample run in this refused-foreground session (T04)

```
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release --no-build -- --run-seconds 12 --open-menu-after 8
```

Raw stdout (gsd_exec `9ea72e4d`, exit 0):

```
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
[sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
[sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
[sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
[sample] tray icon registered, rotating 3 frames every 1s.
[sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
[sample] menu self-open requested: the assigned menu will open once after 8s and be closed again 6s later (--open-menu-after). No shell click is injected for this.
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x2430B86 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;f9ff93e4-...] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=0,0 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

- `owner=0x0` is the sample's own OS reading taken inside the consumer's `Opened` handler, **before** the
  library's post-open repair runs (this is the sample's known reading-order limit, unchanged here). It
  is the hostile WPF construction, and the library's own post-open reading is the authoritative one
  (S08-5/S08-6).
- `[sample] menu dismissed.` appears **before** `[sample] tray icon disposed`, i.e. the notification
  arrived before `Dispose` returned, and the run reports `menu dismissals=1` for a menu nobody clicked
  outside - the F1 case, live, in the refused-foreground session.

The retry session reproduced the same run in the same session shape (gsd_exec `c827750b`, exit 0):

```
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x47C09B4 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;023338ff-...] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=417,0 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

## Raw output - T03 (the disposal-driven close, kept from that task)

### RUN A, disposal-driven close (the F1 case)

```
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release --no-build -- --run-seconds 12 --open-menu-after 8
```

`--run-seconds 12` with `--open-menu-after 8` is deliberately shorter than the sample's own 6-second
self-close hold (which would fire at t=14 s), so the menu is **still open** when the run's shutdown path
disposes the icon: this is the disposal-driven close F1 measured, not the consumer-driven hold close.

**Before the change** (exec `efb4a322-eede-44e6-a263-2a1ea37146d2`, raw stdout):

```
[sample] menu self-open requested: the assigned menu will open once after 8s and be closed again 6s later (--open-menu-after). No shell click is injected for this.
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x3D0540 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;ed86ba24-...] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=35,786 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=0.
```

> **No `[sample] menu dismissed.` line at all**, and `menu opens=1, menu dismissals=0` - the recorded F1
> gap reproduced in this session, with the popup built ownerless (`owner=0x0`).

**After the change** (exec `52a02e73-064f-477d-9f60-4d162476db15`, RUN A, raw stdout):

```
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x446025E class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;4662cedb-...] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=648,977 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

### RUN B, the plan's prescribed command

```
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release --no-build -- --run-seconds 25 --open-menu-after 8
```

Raw stdout (exec `52a02e73`, RUN B) - the sample's own hold close fires first here, as designed, and the
totals read `menu opens=1, menu dismissals=1`:

```
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x8802EC class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;3b0264ec-...] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=648,977 bottomLeftDip=964,752
[sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

> **Why RUN A is the sharper evidence.** With `--run-seconds 25` the sample closes its menu itself at
> t=14 s, and that consumer-driven close already delivered `Closed` before T03 (S06's logs record the
> same `menu opens=1, menu dismissals=1`). RUN A is the disposal-driven case F1 is about, and it is the
> one that moved from `1/0` to `1/1`.

## Dispositions this slice supersedes

| Recorded before S08 | Replacement |
| --- | --- |
| **D030** - the disposal-driven close destroys the anchor synchronously but does not deliver `ContextMenu.Closed` to a consumer (S03 finding F1) | **D045**: a bounded dispatcher wait (500 ms, 5 ms poll at `DispatcherPriority.Normal`) lets WPF finish its own popup destroy before the anchor dies, so the consumer's `Closed` runs exactly once before `Dispose` returns. S08-11..S08-16. |
| **D042** - document the F4 limitation for v1 and leave the open path unchanged (option (c); options (a) and (b) not implemented) | **D044**: the library writes the popup's owner itself and re-claims the foreground, so the no-click open is anchor-owned and dismissable. S08-2..S08-8. |
| **D043** - the popup's `GW_OWNER` is asserted only as "the anchor or absent, never the registration host"; nothing in the library sets or repairs the owner | **D044** (the direction D043 itself allowed): the value is the library's own write, so the product-path tests assert equality. The bounded form survives only for the hand-built `TrayMenuScenario` popup. S08-5, S08-9, S08-10. |

The supersession is recorded in the decision register as **D046** - "what S08 does to the three
dispositions its repair supersedes: D042's document-only F4 disposition, D043's bounded owner assertion
(finding F5) and D030's F1 close-notification gap" - which names D044 and D045 as the replacements and
the evidence behind them. It was saved from this task's first attempt; the retry read the register, found
D046 already covering all three dispositions, and appended nothing, so there is no duplicate row.
`docs/UAT-S03.md` carries a status line on each finding naming the test class or run behind it (S08-22);
`docs/TEST-ENVIRONMENT.md` records that the five-failure environment dependency is retired and that the
skip-with-reason guard is rejected (S08-21). The M001 requirement records themselves (R003's
validation/notes for the dismissal clause, the R007/R014 owner language) are a **deliberate hand-off**,
not an unrecorded gap: the verdict behind each clause is carried by the rows above and by the F1/F4/F5
status lines, the database wording is updated by its named owner - the operator or the M001 milestone
validation - and **no `gsd_requirement_*` tool was called from this task** (S08-23).

## What S08 does not claim

- **No live user-path observation anywhere.** Every dismissal, owner and close claim above is an in-repo
  test that injects the shell's version-4 `WM_CONTEXTMENU` callback into the real host window, or a live
  sample run driven by the sample's own `--open-menu-after` switch. A human right-clicking a real tray
  icon was not performed and is not claimed.
- **A click the OS never delivers to this process is not evidence of dismissal.** This session's tray
  icon belongs to the Windows 11 overflow flyout, so a synthesised click at the icon's own shell-reported
  rectangle reaches nothing (S06 finding F1, unchanged). No instrument here observes a real outside click
  a user made; the outside clicks the tests inject are real input events into a real popup, and that
  distinction is kept everywhere.
- **The requirement records are not edited here.** R003 and the R007/R014 owner language live in the GSD
  database, not in this repository, and this task called no `gsd_requirement_*` tool. The verdicts behind
  them are the rows above; the wording is updated separately by the operator or by the M001 milestone
  validation (S08-23).
- **No normal-session measurement.** The slice's Done-when asks for green in both a normal session and
  the hostile one. The hostile one is measured (S08-17, S08-18); the normal one was not available to this
  unit and is carried by the hermetic claim-granted tests plus the pre-S08 interactive-session total
  (S08-24). It is not presented as re-measured.
- **The keyboard Escape path, the live Display Scale matrix (100/125/150/175/200 %) and the two-monitor
  mixed-scale case remain the human follow-ups S03 recorded** (`docs/UAT-S03.md`, "What was NOT
  observed"). S08 changed the owner and close mechanics, not that surface, and this document does not
  claim them.
- **The close pump's timeout branch is not measured live.** No way exists to make WPF withhold `Closed`;
  the 500 ms bound is stated and traced (`closeNotificationDelivered=False`), not demonstrated.
- **The `owner=` reading on the sample's open line is pre-repair.** It reads `0x0` in this session and
  must not be read as the library's final value; the library's own internal readings
  (`MenuOwnerBeforeRepair`/`After`/`Repaired`) are authoritative (S08-5).
- **The old totals are historical.** `Failed: 5, Passed: 399, Total: 404` is S06's measurement at
  `c57446a`, recorded here as the before-leg only because it is the exact reading this slice exists to
  turn green; it is not a reading at this revision.

## Reproduce

```
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayMenuDismissalTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconMenuOwnerDeterminismTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayMenuOwnerMechanismProbeTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconMenuCloseNotificationTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconMenuActivationTests"
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~SliceContractTests"
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release --no-build -- --run-seconds 12 --open-menu-after 8
```

In this worktree `dotnet` must see a Windows temp directory (git-bash exports `TEMP=/tmp`, which breaks
NuGet's temp path with `NETSDK1060`): use `cmd //c`, or export `TEMP`/`TMP` to
`C:\Users\<user>\AppData\Local\Temp` first. The raw variant measurements behind the mechanism are in
`docs/REMEDIATION-S08-MEASUREMENT.md`.

The agent shell needs one more thing: export the shell folders *and* keep the CLI off the reused build
servers (`USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `ProgramData`, `ALLUSERSPROFILE`, `TEMP`, `TMP`, then
`--disable-build-servers -p:UseSharedCompilation=false`). Without them the CLI reuses an MSBuild or
Roslyn compiler server started from a stripped environment, and NuGet's folder resolution throws
`ArgumentNullException (path1)` - `error NETSDK1060` on any project whose restore graph is re-evaluated.
A trivial project outside the worktree failed identically, so it is the session, not this repository.
The evidence rows in `docs/UAT-S08.md` were produced with that recipe.
