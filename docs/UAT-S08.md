# UAT-S08 - deterministic menu owner and dismissal

**Slice:** M001 / S08 (Deterministic menu owner and dismissal)
**Requirements:** R003 (a right click opens the assigned menu at the icon and an outside click dismisses
it), R007/R014 (owner lifetime: the popup's owner is the library's anchor window, never the shell
registration host, and the menu path leaks no window or GDI object), R015 (property/state changes are
applied on the owning dispatcher)
**Date:** 2026-09-22
**Machine:** MINIBOOKX, `MINGW64_NT-10.0-26200` (Git Bash), .NET SDK `10.0.401`, single monitor
1920x1200 at 150 % scale.
**Revision tested:** the `M001` worktree with S08/T01 and T02 applied and T03's change on top of them.
**Document status:** **in progress.** This file currently carries T03's evidence (the disposal-driven
close notification, D030 / S03 finding F1). T04 assembles the consolidated document - the hostile-session
suite run, the rewrite of `docs/TEST-ENVIRONMENT.md`, the superseded-decision dispositions (F1, F4, F5)
and the consolidated "what is not claimed" section - on top of it.

## Environment note that matters for the readings below

This session **refuses this process the foreground**. The reference sample's own menu-open line reads
`owner=0x0` on every run recorded here, which is exactly the S06-measured hostile shape
(`setForegroundWindow=False`, popup built ownerless). That is the state D044's owner repair exists for
(D044 repairs the value and re-claims the activation relationship), and it is why the T03 assertions
below do not depend on the session granting the foreground: the close-notification contract is written
for both states, and the refused state is scripted on the seam (T03-3).

## T03 - a disposal-driven close delivers `ContextMenu.Closed` (closes D030 / S03 F1)

**Task goal (as planned).** After `menu.IsOpen = false`, wait (bounded) for WPF's own popup destroy -
which is what raises `Closed` - before the anchor window (the popup's owner) is destroyed, deliver the
notification exactly once, leave no anchor or popup window behind, and show the same result in the
reference sample.

**What changed.** `TrayIcon.CloseMenu` no longer tears the anchor down immediately after
`IsOpen = false`. It pumps the owning dispatcher through a `DispatcherFrame`, bounded at **500 ms**
(`MaxMenuCloseNotificationWaitMilliseconds`, polled every 5 ms at `DispatcherPriority.Normal`), until
both (a) the library's own `Closed` handler has run (`_menuCloseNotificationDelivered`) and (b) the popup
window the open resolved from the menu's own presentation source is no longer a window. Only then does
`TearDownMenu` destroy the anchor, exactly as before. An instance with no open menu returns through the
idempotent teardown without touching the queue, and the wait never runs twice for one disposal
(`_disposed` gates `DisposeCore`, and the teardown clears the state the pump reads).

### Claims and verdicts

| # | Claim | Verdict | Instrument | Evidence |
|---|---|---|---|---|
| T03-1 | A menu closed by disposing the icon delivers `ContextMenu.Closed` to the consumer's own handler **exactly once, before `Dispose` returns** | **PASS** | in-repo test (hermetic, real popup) | `TrayIconMenuCloseNotificationTests.Disposing_the_icon_delivers_the_open_menus_Closed_exactly_once_before_it_returns` — the handler count is read immediately after `Dispose` returns (no intervening pump), asserted `1` and `Assert.Same(menu, deliveries[0])`; a second `Dispose`, and a 400 ms pump afterwards, deliver nothing further |
| T03-2 | The same disposal leaves **no anchor window and no popup window** behind | **PASS** | in-repo test (real windows) | same test — `IsWindow(anchor) == false`, `IsWindow(popup) == false` and `TrayMenuScenario.FindPopupWindows()` empty, all read with no pump after `Dispose`; plus `menu.IsOpen == false`, `trayIcon.OpenContextMenu == null`, `trayIcon.MenuAnchorHandle == 0`, `menu.PlacementTarget == null` |
| T03-3 | The close notification is delivered in the **hermetic hostile state** (foreground claim refused, popup built ownerless) | **PASS** | in-repo test (scripted seam) | `With_the_foreground_claim_refused_the_disposal_driven_close_still_notifies_and_leaves_no_window` — asserts the seam refused the pre-popup claim (`SetForegroundWindowResults[0] == false`), that WPF really built the popup ownerless (`MenuOwnerBeforeRepair == 0`), and then that the disposal delivers one `Closed` and leaves no window |
| T03-4 | Disposal with **nothing open** delivers nothing **and does not pump** the dispatcher | **PASS** | in-repo test (negative control) | `Disposing_an_icon_whose_menu_is_already_closed_delivers_nothing_and_does_not_pump_the_queue` — a `DispatcherPriority.Background` work item posted immediately before `Dispose` has **not** run when `Dispose` returns, and does run when the queue is pumped afterwards (the control that the post was real) |
| T03-5 | The **close trace line** carries the delivery, so a support log can tell a notifying close from a silent one | **PASS** | in-repo test (listener on the library's internal source) | `The_close_trace_line_records_that_the_consumer_notification_was_delivered` — exactly one `TrayIcon menu closed; anchor 0x…` line, containing `closeNotificationDelivered=True` |
| T03-6 | The reference sample, in a run that **never clicks outside** and whose menu is still open at shutdown, prints its dismissed line and reports `menu dismissals=1` | **PASS** | live sample run (disposal-driven variant) | exec `52a02e73-064f-477d-9f60-4d162476db15`, RUN A below: `[sample] menu dismissed.` appears **after** the menu-open lines and **before** `[sample] tray icon disposed`, totals `menu opens=1, menu dismissals=1` |
| T03-7 | The **pre-fix control**: the identical command at the same revision minus the change reported `menu opens=1, menu dismissals=0` and printed no dismissed line | **PASS (control)** | live sample run before the change | exec `efb4a322-eede-44e6-a263-2a1ea37146d2`, RUN A (before) below — the finding this task closes, reproduced in this session |
| T03-8 | The plan's prescribed sample command prints its dismissed line and reports `menu dismissals=1` | **PASS** | live sample run | exec `52a02e73-…`  RUN B below — `--run-seconds 25 --open-menu-after 8`, `[sample] menu dismissed.` and `menu opens=1, menu dismissals=1` |
| T03-9 | The existing disposal contract (synchronous, complete, idempotent) is unchanged, and no delivered menu behaviour regressed | **PASS** | in-repo tests | `TrayIconMenuActivationTests` 16/16 (incl. `Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent`), `TrayIconLifecycleTests` 15/15, `TrayIconMenuOwnerDeterminismTests` 4/4, `TrayMenuDismissalTests` 6/6, `TrayIconMenuOwnerLifetimeTests` 3/3 |
| T03-10 | The whole suite is green at this revision | **PASS** | full suite run | exec `53dbbaa8-de9a-4a8a-b1d2-337cfc2be9d2` — **416 passed / 0 failed / 0 skipped** (412 at T02 + the 4 new tests) |

### Raw output — RUN A, disposal-driven close (the F1 case)

Command, verbatim:

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
[sample] menu opened: popup=0x3D0540 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;ed86ba24-fb52-4731-bdd9-8371eae10ef5] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=35,786 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=0.
```

> **No `[sample] menu dismissed.` line at all**, and `menu opens=1, menu dismissals=0` — the recorded F1
> gap reproduced in this session, with the popup built ownerless (`owner=0x0`, the hostile foreground
> state).

**After the change** (exec `52a02e73-064f-477d-9f60-4d162476db15`, RUN A, raw stdout):

```
[sample] menu self-open requested: the assigned menu will open once after 8s and be closed again 6s later (--open-menu-after). No shell click is injected for this.
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x446025E class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;4662cedb-e684-4229-be77-955b8ce0b760] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=648,977 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

> The dismissed line is printed **during** the disposal — `[sample] tray icon disposed` is written after
> `TrayIcon.Dispose()` returns, so the notification arrived before `Dispose` returned, which is T03-1
> and T03-2 measured on the consumer-visible path. The sample's `DrainDispatcher` and its instrumentation
> were left exactly as they were: they are the instrument that measured the gap.

### Raw output — RUN B, the plan's prescribed command

```
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release --no-build -- --run-seconds 25 --open-menu-after 8
```

Raw stdout (exec `52a02e73-…`, RUN B) — the sample's own hold close fires first here, as designed:

```
[sample] menu self-open requested: the assigned menu will open once after 8s and be closed again 6s later (--open-menu-after). No shell click is injected for this.
[sample] --open-menu-after: requesting the menu now, with no click injected.
[sample] menu opened: popup=0x8802EC class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;3b0264ec-ad3a-4324-95f3-07ccdab9b183] rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x0 cursor=648,977 bottomLeftDip=964,752
[sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
[sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
[sample] menu dismissed.
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
```

(Exact raw text of the totals line: `pump-observed private-range messages=0`.)

> **Why RUN A is the sharper evidence.** With `--run-seconds 25` the sample closes its menu itself at
> t=14 s (`--open-menu-after` hold), and that consumer-driven close already delivered `Closed` before
> T03 (S06's logs record the same `menu opens=1, menu dismissals=1`). RUN A is the disposal-driven case
> F1 is about, and it is the one that moved from `1/0` to `1/1`.

### Commands run for T03

```
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore                             # exec 76057a9f, exit 0, 0 warnings, 0 errors
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconMenuCloseNotificationTests"   # exec 069c56ad, 4/4
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconLifecycleTests"               # exec 07c96032, 15/15
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build                                                                  # exec 53dbbaa8, 416/416
```

### Instruments, named per claim

- **T03-1, T03-2, T03-4**: hermetic in-repo tests that open the menu through the product's own path (a
  registered `TrayIcon` receives the shell's version-4 `WM_CONTEXTMENU` by same-thread `SendMessage`)
  and drive the real WPF popup. They observe the real consumer-visible event and the real window handles;
  only the shell seam is a fake.
- **T03-3**: the same instrument with the foreground claim scripted refused on the seam.
- **T03-5**: a `TextWriterTraceListener` on the library's internal `NotifyIconTrace.Source`, raised to
  `Verbose` for the duration. This is a *test*, not a sample run, and deliberately so: the sample can
  only attach to a trace source it constructs by name, which is not the internal instance the library
  writes to (that is the S03 finding about the documented channel, unchanged here). `library trace
  lines=0` in the sample's totals is the same fact, not an absence of library tracing.
- **T03-6, T03-7, T03-8**: live sample runs, whose readings come from the sample's own instruments
  (`Opened`/`Closed` handlers, popup enumeration, the totals line) rather than from the library's word.

## What T03 does not claim

- **The pump's timeout path is not measured live.** The 500 ms bound is stated and exercised only as a
  bound, not by a menu whose `Closed` never arrives (no way to produce that on a live desktop: WPF either
  raises it or the popup never opened). What is claimed is the shape the bound guarantees — the wait
  always returns, the teardown still runs and traces, and a timed-out close is visible in the close line
  as `closeNotificationDelivered=False`.
- **No claim about the whole hostile desktop beyond this session.** This session already refuses the
  foreground (every sample open reads `owner=0x0`), and T03-3 scripts the refusal hermetically; the
  full-suite run in a session that denies the foreground remains T04's measurement.
- **No claim about an outside click here.** Dismissal is S08/T02's contract (`TrayMenuDismissalTests`,
  `TrayIconMenuOwnerDeterminismTests`); T03 changes only the close path and re-runs those classes as a
  regression check.
- **The Escape/keyboard route and the live Display Scale matrix** stay the human follow-ups S03 recorded;
  nothing in T03 touches them.
