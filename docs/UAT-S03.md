# UAT-S03 — Live context-menu placement checks for the notification-area icon

These checks are **manual** and they are the only evidence for the part of S03 that a GitHub Actions
Windows runner cannot produce. Those runners have no interactive desktop session, so no shell ever
delivers a right-click callback, no WPF popup is ever placed on a real monitor, and no monitor has a
Display Scale to be wrong about.

**Do not report these checks as automated coverage, and do not report a check as passed that was not
measured.** The automated suite injects the shell's callback message into the real host window and
therefore exercises the whole menu path - the decoder, the routed events, `OnTrayClick`, the shell's
icon rectangle, `TrayIconPlacement`, the anchor window, a real WPF popup and a real injected outside
click - but it does so against a scripted icon rectangle and a scripted or fixed DPI, and it never
sees the notification area. What it cannot prove is that a real process reads its own monitor's real
Display Scale. Scope: S03 only (menu activation, placement, dismissal, anchor lifetime).

Two sessions wrote into this document: **T05** measured the menu on an executable that was still
SYSTEM_AWARE, and **T06** declared PerMonitorV2, added the sample's own DPI readings and a no-click
menu switch, and re-ran the live checks on that build. Rows measured in both sessions carry both
dates; the T05 captures quote the open line as it was before T06 added the `cursor=` and
`bottomLeftDip=` fields, and that difference is stated wherever the older quote appears.

## How the results below were obtained

Every `Result:` line is a **measurement from a named instrument**, not a look at the screen: the
agent that ran this checklist has no human eye and cannot view a screenshot, so "observed" means
*recorded by the instrument named in the line*. Anything that could not be established this way is
marked **NOT OBSERVED** with its reason, never inferred into a pass.

- **The sample's console (`Trustsoft.NotifyIcon.Sample`)** - the in-process half. Since S03/T05 the
  sample owns a real two-item `ContextMenu`, assigned to the icon's `ContextMenu` property at
  startup, and writes `[sample] menu opened: popup=0x... class=... rect=L,T WxH dpi=... scale=...
  owner=0x... cursor=X,Y bottomLeftDip=x,y` on the menu's `Opened` event and `[sample] menu
  dismissed.` on `Closed`. The numbers in that line are read from the OS inside the sample
  (`EnumThreadWindows` + `GetWindowRect` + `GetWindow(GW_OWNER)` + `GetDpiForWindow`), not from the
  library, which is what makes them an independent reading of the same popup the injector measures
  from outside. **T06 added** the two trailing fields (`cursor=`, read with `GetCursorPos`, and
  `bottomLeftDip=`, the popup's lower-left corner in the DIP space the placement offsets live in) and
  the startup readings below; the `rect=`/`dpi=`/`scale=`/`owner=` fields are unchanged.
- **The click injector (`probe-clicks`)** - real input and the out-of-process half. It finds the icon
  through UI Automation, injects a real right click, and for the `menu` scenario prints the icon
  rectangle, the window under the cursor, the popup window it finds by enumerating the sample
  process's visible windows (`EnumWindows` + `GetWindowThreadProcessId` + `IsWindowVisible` +
  `GetWindowRect`), its owner, the comparison between the popup and the icon rectangles, and then
  injects an outside click and reports whether any visible popup survived it. **T06 added one
  scenario, `menu-self`**: the same measurement without any click, with the sample opening its own
  menu through the library's click path (`--open-menu-after`), which is what separated the two
  conditions behind finding F4. **The instrument was extended, not replaced**: the existing `single`,
  `double`, `right`, `middle` and `cancel-*` scenarios and their output shape are unchanged, so S02's
  recorded captures stay reproducible.
- **The PerMonitorV2 manifest (`samples/Trustsoft.NotifyIcon.Sample/app.manifest`)** - the
  executable-level declaration. It is not an instrument by itself; what makes it measurable is the
  sample's own startup line (`GetProcessDpiAwareness` + `GetAwarenessFromDpiAwarenessContext` +
  `AreDpiAwarenessContextsEqual` against `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`) and the
  out-of-process check in `.gsd/measure-dpi-awareness.ps1`, which reads the first of those on a
  freshly started process. A missing or stale manifest therefore shows up twice, as `value=1` and as
  `isPerMonitorV2=False`, with a warning line beside it.
- **The DPI/awareness instrument** (`.gsd/measure-dpi-awareness.ps1`, a scratch script, not product
  code) - `SetProcessDPIAware` + `GetSystemMetrics` + `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` for the
  session's own numbers, and `GetProcessDpiAwareness` on a freshly started sample process.
- **The outside-click injector** (a scratch PowerShell helper written for T06 and not kept as a
  repository instrument: `SetCursorPos` + `mouse_event`) - it exists only because finding F4 needed a
  click injected into a run the injector itself was not driving. It is named here so the F4 reading
  has a provenance.
- **The in-repo fixture suites** - named for every row this single-monitor session could not run
  live: `TrayIconPlacementTests` (the pure calculator at 100/125/150/175/200 %, negative coordinates,
  every screen edge, taskbar-reserved edges and mixed-scale pairs), `TrayIconMenuActivationTests`
  (the delivered path at a scripted 144 DPI, including the documented cursor/work-area/DPI fallbacks,
  the `MenuActivation = None` opt-out and the no-second-open rule) and
  `TrayIconMenuOwnerLifetimeTests` (50 open/dismiss cycles, GDI deltas, anchor lifetime).

Environment: **Windows 11 Pro, build 26200** (`Microsoft Windows NT 10.0.26200.0`), x64, .NET SDK
**10.0.401**, target `net8.0-windows`, interactive session. Display configuration of this session,
measured before the manifest (`.gsd/exec/ed8c5e8f-e2a7-4eb4-a6b7-5c18451a1061.stdout`) and after it
(`.gsd/exec/cc60db0e-f760-4bbc-b866-f85305590ffe.stdout`):

```text
monitors=1
primaryPhysical=1920x1200
virtualPhysical=1920x1200
dpiForSystemForAnAwareProcess=144
primaryMonitorEffectiveDpi=144 (hr=0x00000000) scale=1.5
sampleGetProcessDpiAwareness=0x00000000 value=1 (0=unaware, 1=system-aware, 2=per-monitor-aware)   [T05: before the manifest]
sampleGetProcessDpiAwareness=0x00000000 value=2 (0=unaware, 1=system-aware, 2=per-monitor-aware)   [T06: with it]
```

The same configuration as the sample itself reports it after T06 (every T06 capture, for example
`.gsd/exec/23b81994-4b4b-404f-be00-f9fdd14f383d.stdout`):

```text
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
[sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
[sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
[sample] tray monitor: primary monitor device=\\.\DISPLAY1 dpi=144 scale=1.5 - ...
```

**One monitor, Display Scale 150 %** (144 DPI effective, `DpiValue=0` = the recommended value in
`HKCU\Control Panel\Desktop\PerMonitorSettings`), work area 1920x1128 because the taskbar occupies
the bottom 72 physical pixels. The scale matrix and the two-monitor rows below are therefore
**NOT OBSERVED** in this session, and the fixture suites named with them are what carries those
claims.

## Prerequisites and how to run it yourself

- Windows 10 or 11 with an interactive desktop session and a notification area. The icon is usually
  in the tray's overflow flyout; the injector expands it first (it invokes the `Show Hidden Icons`
  chevron through UI Automation).
- A .NET 8 (or later) SDK, and a terminal in the repository root.
- **Rebuild before reading anything.** PerMonitorV2 lives in the manifest and is baked into the
  executable, so a stale `samples/.../bin/Release/...` binary cannot show it: the sample's own
  startup line reports `value=1`/`isPerMonitorV2=False` and prints a warning when that happens.
- Build once, then run one scenario per process (the raw-callback stream is only interpretable if a
  run contains exactly one interaction):
  ```text
  dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
  dotnet build .gsd/probe-clicks -c Release --no-restore
  dotnet run --project .gsd/probe-clicks -c Release --no-build -- menu 25        # click-driven
  dotnet run --project .gsd/probe-clicks -c Release --no-build -- menu-self 30   # no click at all
  ```
  The `menu` check is green when the capture contains `[sample] menu opened:` with a `rect=`, a
  `dpi=` and a non-zero `owner=`, a `CLICK menu-placement` line whose `atScreenCorner=False`, a
  non-empty `CLICK outside-click`, and then both `CLICK menu-after-outside-click | no visible popup
  window of the sample process remains` and `[sample] menu dismissed.`. The run's totals line must
  read `menu opens=1, menu dismissals=1`.
  **The scenarios print the comparison themselves** (`iconRect`, `menuTopLeft`, `delta`,
  `popupContainsIconPoint`, `atScreenCorner`), which is what turns the placement row into a number
  pair instead of a judgement.
  A menu that does not open is a **failed** check: the capture then contains
  `CLICK menu-window | NOT FOUND ...` and the sample's stderr, and it must be recorded as such.
- The sample can also open its own menu with no click at all:
  `Trustsoft.NotifyIcon.Sample.exe --open-menu-after 2 --run-seconds 12` (or `--open-menu-after=2`;
  a bare `--open-menu-after` means the default 5 s delay). It requests the menu through the library's
  click path after the delay, holds it for 6 s and closes it again. **Any argument the sample does
  not understand, or a malformed delay, is rejected on stderr with exit code 2** rather than ignored,
  so a capture whose switch was a typo cannot be mistaken for a capture that proved the opposite.
- For the exit behaviour, `-- right 25` (no outside click) shows the menu open and then the icon's
  graceful shutdown; see finding F1 for what that run does and does not show.

## Checklist

### 1. A right click opens the context menu with no window present

Result: **PASS** (2026-09-20, measured twice: T05 `.gsd/exec/9eaf36e3-9b0b-43bc-b538-547f2bc7d265.stdout`
on the pre-manifest build, T06 `.gsd/exec/23b81994-4b4b-404f-be00-f9fdd14f383d.stdout` on the
PerMonitorV2 build, scenario `menu`). Both captures agree line for line on the placement. The sample
is windowless (its own startup line says so) and the injector found the icon at
`rect=1224,1042 60x60` in the overflow flyout, clicked its centre `1254,1072`, and
`WindowFromPoint(1254,1072)` was `class='TopLevelWindowForOverflowXamlIsland' title='System tray
overflow window.' pid=5544` - the click landed on the shell's tray surface. The shell then delivered
the same three callbacks S02 recorded for a right click, and the library opened the menu:

```text
[sample] raw callback hwnd=0x4490994 msg=0x0401 event=0x0204 ...    WM_RBUTTONDOWN
[sample] raw callback hwnd=0x4490994 msg=0x0401 event=0x0205 ...    WM_RBUTTONUP
[sample] raw callback hwnd=0x4490994 msg=0x0401 event=0x007B ...    WM_CONTEXTMENU
[sample] click type=TrayRightClick button=Right count=1 anchor=1254,1071
[sample] menu opened: popup=0x5150620 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;99bc0e6c-...] rect=1225,1021 296x83 dpi=144 scale=1.5 owner=0x2D50728 cursor=1254,1072 bottomLeftDip=816.667,736
```

(T06 capture; the T05 line was identical except that it ended at `owner=0x10A0548`.)

The popup's **owner is `0x2D50728`, the library's anchor window** - not `0x0` (the ownerless popup
T03 pinned as undismissable) and not the shell registration host. The out-of-process instrument
measured the same window independently: `CLICK menu-window | popup=0x5150620
class='HwndWrapper[...]' rect=1225,1021 296x83 owner=0x2D50728`.

### 2. The menu's rectangle is at the icon, not in the screen corner

Result: **PASS** (2026-09-20, T06 capture named above; identical numbers in the T05 capture). The
injector printed the icon rectangle UI Automation reported and the popup rectangle it found, and the
two agree that the popup's lower-left corner sits at the icon:

```text
CLICK menu-placement | iconRect=1254,1072 menuTopLeft=1225,1021 delta=(-29,-51) popupContainsIconPoint=True atScreenCorner=False
```

Read as numbers: the popup spans `x 1225..1521`, `y 1021..1104`; the icon's own 60x60 button spans
`x 1224..1284`, `y 1042..1102`. The popup's **left edge is 1 px right of the icon's left edge** and
the popup's **bottom edge is 2 px below the icon's bottom edge** - i.e. the menu's lower-left corner
is at the icon's lower-left corner and the menu grows upward, which is the notification-area
convention `TrayIconPlacement` documents (the tray sits at the bottom of the screen, so the menu is
placed above the icon). It is not in the screen corner (`atScreenCorner=False`), and the delta from
the clicked point is `(-29,-51)` px, i.e. the popup is adjacent to the icon rather than anywhere
else. The library resolved the same monitor the popup landed on at **`dpi=144`, `scale=1.5`**, which
matches the session's Display Scale of 150 % measured independently above, and the popup's own
`bottomLeftDip=816.667,736` (T06) is the same point expressed in the DIP space the offsets live in.

### 3. An outside click dismisses the menu

Result: **PASS** (2026-09-20, T06 capture named above; identical in the T05 capture). The injector
injected right+left at `925,721` - outside the popup, far from the anchor - and
`WindowFromPoint(925,721)` was `class='CASCADIA_HOSTING_WINDOW_CLASS' title='gsd' pid=22652`, i.e. an
unrelated window that could not have dismissed the popup by itself. Immediately afterwards:

```text
CLICK outside-click | injected right+left at 925,721 - outside the popup and away from the icon
CLICK menu-after-outside-click | no visible popup window of the sample process remains
[sample] menu dismissed.
[sample] totals: raw callback lines=3, ..., clicks=1, cancelled by a Preview handler=0, menu opens=1, menu dismissals=1.
```

Two instruments agree: the out-of-process window enumeration finds no visible popup of the sample
process any more, and the sample's own `Closed` handler fired exactly once. Menu opens=1 and
dismissals=1 also rules out the two `Opened`/`Closed` pairs a reopened menu would produce.

### 4. The Display Scale matrix (100/125/150/175/200 %) with the menu reopened at each value

Result: **NOT OBSERVED at the live level - the fixture suite carries this row.** This session has one
monitor at 150 % (measured above), so only the 150 % column was measured live and the other four
columns were never run. The evidence for the whole matrix is
`tests/Trustsoft.NotifyIcon.Tests/TrayIconPlacementTests.cs`: 82 headless fixtures covering 100, 125,
150, 175 and 200 %, negative-origin monitors (left of and above the primary), the tray rectangle at
every screen edge, taskbar-reserved work areas and mixed-scale pairs, asserting
`offset == clamped physical point / (dpi/96)` exactly. To run the live matrix a human changes
Settings > System > Display > Scale to each value (signing out or restarting the shell where Windows
requires it), repeats the `menu` scenario, and records the `dpi=`/`scale=` reading in the sample's
line plus the `menu-placement` comparison for each value. **Do not fill these rows in from the
fixtures and present them as live observations.**

### 5. The two-monitor mixed-scale case

Result: **NOT OBSERVED - this session has one monitor** (`monitors=1`, `primaryPhysical=1920x1200`,
`virtualScreen=0,0 1920x1200`). The evidence for the claim is the fixture suite again (mixed-scale
pairs in `TrayIconPlacementTests`) plus the scripted-DPI tests in `TrayIconMenuActivationTests`, which
drive the delivered menu path at a scripted 144 DPI and assert that the anchor window really sits at
the physical point the offset names. The live mixed-scale demonstration (right click on a monitor at
one scale, move the icon to a monitor at another, reopen) belongs to a reviewer with two monitors.

### 6. The application manifest declares PerMonitorV2 and is in effect

Result: **PASS (2026-09-20, T06)** - and it was measured as *not* satisfied before T06, which is what
the row is for. Three readings, two of them outside the process:

```text
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
                                                     ^ the sample's own line, every T06 capture
sampleGetProcessDpiAwareness=0x00000000 value=2        ^ .gsd/exec/cc60db0e-f760-4bbc-b866-f85305590ffe.stdout, out of process
sampleGetProcessDpiAwareness=0x00000000 value=1        ^ .gsd/exec/ed8c5e8f-e2a7-4eb4-a6b7-5c18451a1061.stdout, the T05 build, before the manifest
```

`value=2` is per-monitor aware; the `isPerMonitorV2=True` comparison is what distinguishes V2 from
V1, because `GetProcessDpiAwareness` reports `2` for both. **Before this task the same reading was
`value=1`** (DPI_AWARENESS_SYSTEM_AWARE), and the row was recorded as NOT OBSERVED for that reason:
the placement arithmetic that T05 measured at 150 % was the *monitor's* effective DPI as the library
reads it (`GetDpiForMonitor`, awareness-independent by design, D026), which is the correct input
regardless of the process's awareness, but a system-aware process is not a per-monitor consumer.
`samples/Trustsoft.NotifyIcon.Sample/app.manifest` now declares `dpiAwareness` =
`PerMonitorV2` and `longPathAware` under the SMI/2016 namespace with `supportedOS` for Windows 10/11,
`<ApplicationManifest>` wires it into the sample's csproj, and the sample prints a warning beside the
reading when it is not in effect - which is the diagnosable form of the stale-binary failure mode.

### 7. The menu is torn down on a graceful exit

Result: **PASS for the library's contract, NOT OBSERVED as a sample `Closed` line - see finding F1.**
**Superseded by S08/T03 (D045), revision `996c9b0`: a disposal-driven close now delivers the consumer's
`Closed` and the sample prints `[sample] menu dismissed.` for it - see F1's status line and
`docs/UAT-S08.md`.**
In-repo, `TrayIconMenuActivationTests.Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent`
proves synchronously, after `Dispose`: the menu is closed, `OpenContextMenu` is null, the anchor
handle is `IntPtr.Zero`, `Win32.IsWindow(anchor)` is false, the popup is gone after a pump and a
second `Dispose` is a no-op. Live (`-- right 25`, no outside click, `.gsd/exec/b6f031c6-4bea-442b-b286-1c413f3c9963.stdout`
and the T06 `menu` capture) the run exits with code 0, empty stderr, `menu opens=1` and **`menu
dismissals=0`**: the sample's own `Closed` line did not arrive for the disposal-driven close. The
library's teardown is synchronous (the in-repo test asserts it), so this is a notification-order
observation about WPF and the sample's instrumentation, recorded rather than repaired (finding F1).

The **consumer-driven** close does deliver it, measured in T06's no-click scenario: the sample sets
`IsOpen = false` on its own menu, prints `[sample] menu dismissed.` and the injector's out-of-process
enumeration confirms the window is gone (`CLICK menu-after-self-close | no visible popup window of
the sample process remains ...`, `.gsd/exec/d2d200b1-3e40-43e5-8e34-33595604af9e.stdout`).

### 8. Which surface the icon was on during the run

Result: **the overflow flyout** (2026-09-20, all captures). The injector's `flyout` line shows it
invoked the `Show Hidden Icons` chevron, and `WindowFromPoint` at the click point returned
`TopLevelWindowForOverflowXamlIsland` / `System tray overflow window.` owned by Explorer (pid 5544).
S02's checklist had to record exactly this distinction, so it is recorded here too: **whether a
right click on the icon's taskbar copy (not the flyout) produces the identical sequence and
placement was not measured.**

### 9. The menu opens with no click injected at all (T06)

Result: **PASS** (2026-09-20, `.gsd/exec/d2d200b1-3e40-43e5-8e34-33595604af9e.stdout`, scenario
`menu-self`). The instrument expanded the flyout (so the icon is displayed, as it is for a real
click), put the cursor on the icon **without clicking**, and the sample opened its own menu through
the library's click path (`--open-menu-after 8`). Everything the click-driven rows measured is
reproduced: the same popup rectangle, the same DPI, and a real owner.

```text
CLICK icon | rect=1224,1042 60x60 clickAt=1254,1072 offscreen=False
CLICK hit-test | cursor=1254,1072 ... foreground=0x8001C class='TopLevelWindowForOverflowXamlIsland'
CLICK click | none injected - the sample opens its own menu (--open-menu-after 8)
CLICK menu-window | popup=0x3800972 class='HwndWrapper[...]' rect=1225,1021 296x83 owner=0x6020650
CLICK menu-placement | iconRect=1254,1072 menuTopLeft=1225,1021 delta=(-29,-51) popupContainsIconPoint=True atScreenCorner=False
CLICK menu-after-self-close | no visible popup window of the sample process remains once the sample closed its own menu (was 1225,1021 296x83)
[sample] menu opened: popup=0x3800972 class=HwndWrapper[...] rect=1225,1021 296x83 dpi=144 scale=1.5 owner=0x6020650 cursor=1254,1072 bottomLeftDip=816.667,736
[sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
[sample] menu dismissed.
```

This row is **not** a substitute for check 1: it proves that the placement, the anchor ownership and
the teardown are the same when no click is involved, and it proves that a failure to open is visible
in the console (`[sample] --open-menu-after: requesting the menu now, with no click injected.` with no
`[sample] menu opened:` after it is a failure, not a missing run). It does not prove the shell
delivers a callback - only check 1 does that. Finding F4 is the other half of this row: with the icon
**hidden** (flyout closed) the same no-click self-open produces an ownerless popup.

### 10. Negative checks (T06)

| # | Negative case | Result |
|---|---------------|--------|
| 10a | An argument the sample does not understand | **PASS** (2026-09-20, `.gsd/exec/0bd8f470-bc48-4b0d-8943-c1d6cb3d0996.stdout` + its `.stderr`): `--bogus` prints `[sample] unknown argument '--bogus' - this sample does not ignore arguments it does not understand.` and the usage line on **stderr** and exits **2**; `--open-menu-after=soon` is rejected the same way; `--run-seconds=3 --cancel-preview=right` still starts and exits 0. No icon is created on the rejected paths, so a typo cannot leave a tray icon behind. |
| 10b | A right click with cancellation - events only, no menu | **PASS, live** (2026-09-20, `.gsd/exec/5143a2e7-cb58-4ed2-a615-9ee54e79e0a4.stdout`, scenario `cancel-right`): the injector's real right click on the icon produced `cancelled by a Preview handler=1, menu opens=0, menu dismissals=0` and **no** `[sample] menu opened:` line, i.e. a Preview handler setting `Handled` suppresses the menu exactly as documented. |
| 10c | `MenuActivation = None` | **PASS in-repo only** (`TrayIconMenuActivationTests.MenuActivation_None_opens_nothing_and_still_delivers_both_right_click_events`). 10b is the same shape live - no menu, no menu line - but through the Preview-cancellation opt-out rather than `MenuActivation`; the live `MenuActivation = None` variant was not run (the sample has no switch for it) and is therefore **NOT OBSERVED**. |
| 10d | A second right click while the menu is already open | **PASS in-repo only** (`TrayIconMenuActivationTests.A_second_right_click_while_the_menu_is_open_opens_nothing_new`). Not demonstrated live: no instrument in this session can inject a second click while the menu is open without first dismissing it. **NOT OBSERVED** live. |
| 10e | An empty `ContextMenu` item collection | **Design, recorded live**: WPF suppresses a menu with no items, so an empty menu would demonstrate nothing about placement. The sample therefore carries two real items and says so on every run: `[sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.` - present in every capture above, which is the cheap non-empty assertion. |
| 10f | A hidden icon's menu (the same no-click open with the flyout closed) | **FAIL of the dismissal contract - finding F4**, recorded with raw captures rather than repaired. **F4 is superseded by S08/T02 (D044), revision `996c9b0`:** a menu opened with no preceding click is now owned by the anchor window and dismissed by an outside click - see F4's status line and `docs/UAT-S08.md`. |

## Findings

Measurements that contradict something written down or expected. None of them was repaired with a
late change inside these tasks: each is recorded with the evidence needed to act on it.

### F1 — a disposal-driven close does not deliver `ContextMenu.Closed` to the consumer

> **Status — CLOSED by S08/T03, decision D045, 2026-09-22, revision `996c9b0`.** The measurement below
> stands as the S03 record; the gap is repaired inside the library. After `IsOpen = false` the close path
> now waits, bounded at 500 ms, for WPF's own deferred popup destroy before the anchor window is
> destroyed, so the consumer's `Closed` handler runs exactly once before `Dispose` returns. Carried by
> `TrayIconMenuCloseNotificationTests` (4/4, including the refused-foreground variant and the no-pump
> negative), the live sample run that moved `menu opens=1, menu dismissals=0` to `1/1` (gsd_exec
> `52a02e73`, reproduced at `9ea72e4d`), and the hostile-session full-suite run at 416/416 (gsd_exec
> `9a9e274d`). D030's recorded gap is superseded by D045. Raw evidence: `docs/UAT-S08.md`.

Measured twice with the `right` scenario (menu opened, nothing clicked outside, then the sample's
`--run-seconds` shutdown path disposes the icon): `menu opens=1, menu dismissals=0`, with no
`[sample] menu dismissed.` line at all, and the second run had a 300 ms dispatcher frame pump
(including posted messages) between the disposal and the totals line
(`.gsd/exec/b6f031c6-4bea-442b-b286-1c413f3c9963.stdout`). The library's own teardown is *not* the
problem and is separately proven: after `Dispose` returns, the menu is closed, the anchor window is
destroyed and a second disposal is a no-op (`Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent`,
green in the suite). What the measurement says is narrower and worth knowing: **a consumer that
subscribes to its own menu's `Closed` event does not receive it for a disposal-driven close** (the
OS-driven dismissal path - an outside click - does deliver it, check 3). A consumer that keeps state
tied to its menu's `Closed` event should not rely on it at shutdown. Reported rather than repaired,
for the same reason S02's F1 was reported: the fix would be a change to the close sequence
(`TearDownMenu` runs synchronously right after `IsOpen = false`, destroying the anchor while the
popup is still tearing down), and it needs its own measurement and its own decision.

### F2 — two icons' menus cannot both be showing at once

Measured while writing the S03 boundary contracts (`TrayIconMenuContractTests`, the simultaneous
phase; raw failure output in `.gsd/exec/c66a9418-b3ff-4e50-b4ec-32252ff71de5.stdout`): with one
icon's menu open, opening a second `TrayIcon`'s menu **left both menus closed** - `firstOpen=True,
secondOpen=False` in one measurement and both false in another, although each icon's own open is
synchronous and successful in isolation. WPF keeps one popup active at a time, and the newly
made-foreground anchor window is an interaction the previously open menu reacts to. The library's
documented rule ("one menu instance per icon") is unaffected; what this adds is that two *different*
menus on two icons are not simultaneously visible either - which is why the contract test asserts
only the invariant the library owes (neither instance ever holds the other instance's menu or anchor
window) and records the outcome instead of pinning it.

### F3 — the pump control reports 1 private-range message per run, where every S02 run reported 0

All S03 runs report `pump-observed private-range messages=1` in the sample's totals line whenever a
menu is opened through the library's own click path, where every one of S02's eight recorded runs
reported `0` (S02 finding F3, whose conclusion was that the tray callback is delivered synchronously
and is invisible to a pump-level observer). The raw hook in the same runs saw exactly the same
**three** callback lines S02 measured for a right click (`0x0204`, `0x0205`, `0x007B`), so the extra
message the pump saw is **not** the tray callback. T06 adds one data point: in the no-click self-open
run the pump counted 1 as well (`.gsd/exec/d2d200b1-3e40-43e5-8e34-33595604af9e.stdout`) and in the
Preview-cancellation run it counted 0 (`.gsd/exec/5143a2e7-cb58-4ed2-a615-9ee54e79e0a4.stdout`),
i.e. it tracks whether a menu opened rather than whether a click was delivered. Nothing in this slice
depends on the count, and the difference is still not attributed here - it is recorded because a
control instrument that changes its reading between slices is exactly the kind of thing that later
reads as a mystery.

### F4 — a menu opened for a **hidden** icon has no owner and is not dismissed by an outside click

> **Status — SUPERSEDED by S08/T02, decision D044, 2026-09-22, revision `996c9b0`.** D042's
> document-only v1 disposition is replaced: the library now writes the popup's owner itself and
> re-claims the foreground, so a menu opened with no preceding click is owned by the anchor window and
> an outside click dismisses it, in the hostile state as well. Carried by
> `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_the_popup_is_owned_by_the_anchor_and_never_by_the_registration_host`
> and `...With_the_foreground_claim_refused_an_outside_click_dismisses_the_menu_and_leaves_no_popup_window`
> (4/4), `TrayMenuOwnerMechanismProbeTests` (4/4; the V1/V2/V3 measurement with its raw lines is in
> `docs/REMEDIATION-S08-MEASUREMENT.md`), and the live sample run in this refused-foreground session
> that reports `menu dismissals=1` (gsd_exec `9ea72e4d`). The hand-built ownerless construction this
> finding pinned is still ownerless and still not dismissed
> (`TrayMenuDismissalTests.An_ownerless_popup_with_no_placement_target_is_not_dismissed_by_an_outside_click`),
> because it is WPF's own construction and not what the library repairs. Raw evidence: `docs/UAT-S08.md`.

Measured in T06 while exercising the new no-click switch, and it is a live reproduction of the exact
construction T03 pinned as a negative in-repo. Two configurations, one variable - whether the icon is
displayed:

**Icon displayed (flyout expanded by the instrument, no click injected)** -
`.gsd/exec/d2d200b1-3e40-43e5-8e34-33595604af9e.stdout`:

```text
CLICK menu-window | popup=0x3800972 ... rect=1225,1021 296x83 owner=0x6020650
[sample] menu opened: ... rect=1225,1021 296x83 dpi=144 scale=1.5 owner=0x6020650 ...
```

**Icon hidden (flyout closed, no click injected)** - `.gsd/exec/cc60db0e-f760-4bbc-b866-f85305590ffe.stdout`
and `-3f9da3f0-...`, `-86f63acc-...` (four runs, same result):

```text
[sample] menu opened: popup=0x7E90A3C ... rect=1225,1021 296x83 dpi=144 scale=1.5 owner=0x0 cursor=1009,1168 ...
[sample] menu opened: popup=0x35600BE ... rect=1350,1045 296x83 dpi=144 scale=1.5 owner=0x0 cursor=1036,1153 ...
```

`owner=0x0` is the ownerless popup of T03's negative case, and it behaves like one: with the menu in
that state an outside click at `400,300` (injected by the scratch helper named above) did **not**
dismiss it - the only dismissal was the sample's own close
(`.gsd/exec/86f63acc-c2c7-4952-94ec-0cf73f3a75c2.stdout`):

```text
outside click injected at 400,300
[sample] menu opened: popup=0x1C502A0 ... owner=0x0 ...
[sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
[sample] menu dismissed.
```

**What was not separated.** Two conditions changed together between the two configurations, and this
session did not isolate them: (i) the shell's rectangle for a hidden icon is not the rectangle of a
displayed icon (in the hidden runs the popup is clamped to the bottom edge of the work area,
`y 1045..1128` against the work area's `1128`), and (ii) the no-click configuration has no user input
preceding the open, and the library's own code records that the anchor *without* its
`SetForegroundWindow` call produces an ownerless popup. Pointing the other way: in the displayed-icon
run the instrument had just reported the shell's overflow window as the foreground window
(`foreground=0x8001C class='TopLevelWindowForOverflowXamlIsland'`), i.e. the sample was not
foreground there either, and the owner was still non-zero - which argues for (i) and against (ii),
without settling it. UI Automation became unavailable for the tray during the session (two attempts
returned `<Shell_TrayWnd not found>` before recovering), so no run could hold both a click-free open
and a displayed icon under two different foreground states.

**Impact, as measured.** The consumer path is a right click, and a user can only click an icon that
is displayed - so the ownerless configuration is not reachable by a user click: every click-driven run
in this document produced `owner=` non-zero and a dismissable popup. What the finding does say is that
the dismissal contract depends on the shell returning the rectangle of a *displayed* icon, and that
the library accepts the shell's rectangle for a hidden icon without recording anything about it. **A
follow-up should either (a) verify after the open that the popup's owner is the anchor and log a
warning when it is not, or (b) treat a rectangle the clamp had to move as a reason to re-resolve the
placement, or (c) simply document that a menu opened for a hidden icon may not dismiss on an outside
click.** None of the three belongs in T06, which is the manifest and demonstration task, and each
needs its own measurement.

The `--open-menu-after` switch that exposed this is a **sample instrument, not product surface**: it
calls the library's documented `OnTrayClick` hook from a derived `TrayIcon`, which is the only way to
reach the menu path without a shell click. Check 9 records what it proves; this finding records the
one configuration in which it produces something the click path does not.

### F5 — the popup's `GW_OWNER` is a WPF-internal outcome, not a value the library can promise

> **Status — TIGHTENED TO EQUALITY by S08/T02, decision D044, 2026-09-22, revision `996c9b0`.** The
> bound below ("the anchor or absent") was the right contract while the value was WPF's. It is the
> library's own write now - the direction D043 itself allowed - so every product-path test asserts
> `GW_OWNER == anchor` and never the registration host, with the dismissal clauses left unconditional.
> Carried by `TrayIconMenuOwnerDeterminismTests` (4/4), `TrayMenuOwnerMechanismProbeTests` (4/4) and
> `TrayIconMenuActivationTests` (16/16). The "or absent" bound survives only where the popup is
> hand-built by the shared `TrayMenuScenario` harness, on which the library's repair deliberately never
> runs. D043 is superseded by D044. Raw evidence: `docs/UAT-S08.md`.

Found in T06 while re-running the **automated** suite for this task's verification gate, and it is
the explanation of the one intermittent assertion S03's menu proof carried: both
`TrayMenuDismissalTests.A_menu_anchored_to_the_anchor_window_is_owned_by_it_and_dismissed_by_an_outside_click`
and the `SliceContractTests` boundary asserted that the popup's owner *was* the anchor window. In
full-suite runs that assertion failed four times out of five, and because T06 had just added the
foreground readings to the harness, the failure printed the whole measurement beside it
(`.gsd/exec/b24c7c21-8a0a-4c45-84b2-e516af5be111.stdout`):

```text
strategy=AnchorWindow host=0x36F0548 anchor=0x3C80740 foregroundMade=True setForegroundWindow=True rootLaidOut=True
  windowsAfterOpen=[0x6C047A(?)] popup=0x6C047A class=HwndWrapper[testhost;...] rect=(450,450,643,500) owner=0x0
  foregroundBeforeOpen=0x3C80740 foregroundAtMeasure=0x3C80740 activeWindowAtMeasure=0x3C80740
  isOpenBefore=True isOpenAfter=False windowsAfterClick=[] foregroundAfterClick=0x1ED0334
```

Read as facts: the anchor was the desktop's **foreground window** *and* the test thread's **active
window**, immediately before the open and again after it (`foregroundBeforeOpen`,
`foregroundAtMeasure`, `activeWindowAtMeasure` all name `anchor=0x3C80740`), its 1x1 visual reported a
valid layout (`rootLaidOut=True`), the foreground claim was granted (`setForegroundWindow=True`) —
and WPF still built the popup with **no owner** (`owner=0x0`), then dismissed it correctly on the
outside click (`isOpenAfter=False`, no popup window left, which is why the dismissal assertions kept
passing while the owner assertion failed). The same class run on its own, and the probe evidence
from T03, produced `owner=` the anchor. A filtered run of only the four menu test classes reproduced
the failure (2 of 32 tests, `.gsd/exec/5b111e1c-f149-44bf-ac96-619c0830c12e.stdout`), so it is a
property of a loaded process, not of another collection running beside this one.

WPF's own code says why the value cannot be pinned. In `Popup.BuildWindow`
(`dotnet/wpf`, `PresentationFramework/.../Primitives/Popup.cs`) the popup window is created with
`param.ParentWindow = parent` — its owner — **only** when both hold: `parent` is non-zero, i.e. the
placement target resolved to an `HwndSource` (`PopupSecurityHelper.GetPresentationSource`), and
`ConnectedToForegroundWindow(parent)` is true, i.e. `GetForegroundWindow()` equals that window or one
of its `GetParent` ancestors *at that instant*. The library cannot observe or control that instant,
and this measurement shows the two inputs it *can* influence — the foreground claim and the laid-out
placement target — were both in the required state while the owner still came out empty.

**Impact, as measured.** None on the consumer-visible contract: every failing run dismissed on the
outside click, the live click-driven runs (checks 1-3) all reported a non-zero owner, and the
`owner=0x0` construction F4 records is still the one that does *not* dismiss. What changes is what
the suite may claim: the boundary now asserts *the anchor or nothing, never the registration host*
and keeps the dismissal clauses unconditional, with this finding as the reason written beside the
assertion. **A follow-up that wants a guaranteed owner has to create one after the open** (find the
popup window and set its owner explicitly — the library does not, and T03's `MakeForeground` design
is what it does instead), or accept the owner as a recorded diagnostic; both are F4's follow-up
options (a) and (c) applied to the automated instrument.

**Closure, as verified.** The two assertions were narrowed to the clause above, the harness kept the
three foreground readings that made this diagnosable, and the suite went green on that build: two
consecutive full runs of **322 passed / 0 failed / 0 skipped**, and the slice's own verification
command (`dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore`, the suite, then
`probe-clicks right 25`) succeeded end to end in `.gsd/exec/5f53bd88-01a4-47d2-869f-7c2c573f73d7.stdout`
(clean build on all three TFMs, 0 warnings, exit code 0). The live path is unchanged on the same
build: the click-driven `menu` scenario still opens an anchor-owned popup at the icon and dismisses
it on an outside click — `owner=0x6FC0A10`, `menu opens=1, menu dismissals=1`,
`.gsd/exec/1093e99f-a885-40c1-8255-69a0e7f3be41.stdout` — which is why the finding is recorded as a
limit on what the *automated* instrument may claim rather than as a product defect.

## What was NOT observed

- **Any Display Scale other than 150 %** (check 4), and **any second monitor** (check 5). Both rows
  are marked NOT OBSERVED above with the fixture suites named as the evidence instead.
- **A Display Scale change while the process is running.** No `WM_DPICHANGED` handling for the
  zero-sized, never-visible, tool-window anchor was implemented or claimed by this slice, and the
  behaviour of the popup and the placement under a live per-monitor scale change is unmeasured. This
  is an explicit follow-up, not a gap that the fixture tests cover.
- **The menu's position after the icon moves between monitors with different scales.** Only one
  monitor exists here; the placement is computed per open from the shell's icon rectangle, but the
  reopen-elsewhere case was never run.
- **Whether focus visibly moves away from the foreground application when the menu opens.** The
  anchor is made foreground by design (measured in T03/T04 as load-bearing for the popup's owner),
  and the user-visible cost of that - the other application losing focus - was not measured. It stays
  on the slice's follow-up list. F4 is adjacent to it but is not the same question: it measures the
  popup's *owner*, not what the user sees happen to their focused window.
- **A click on a menu item.** The sample's menu carries two real items and its first item writes a
  line when chosen, but no click on an item was injected in these runs, so the item-invocation path
  was not observed live (it is the consumer's own WPF code, not the library's).
- **A guaranteed popup owner** (finding F5): the popup's `GW_OWNER` is decided inside WPF's
  `Popup.BuildWindow` from its own placement-target resolution and the foreground window at that
  instant. Measured as the anchor in a quiet process and as `0x0` under full-suite load with the
  anchor foreground, laid out and active — with the dismissal still correct either way. The suite
  therefore asserts "the anchor or nothing, never the registration host" instead of equality.
  **S08/T02 (D044) changes this:** the library now writes the owner itself and reads it back, so every
  product-path test asserts equality (see F5's status line); the bounded form survives only for the
  hand-built `TrayMenuScenario` popup, which the library's repair never touches.
- **Keyboard dismissal (Escape) and the taskbar (non-flyout) surface.** Neither was injected; the
  earlier S02 caveat about the taskbar copy therefore still stands for the menu path (check 8).
- **The live `MenuActivation = None` and second-click-while-open variants** (10c, 10d): in-repo only,
  with the reasons given in the table.
- **The library's Verbose menu trace lines.** They remain unreachable from a consumer assembly in
  .NET 8 (S02 finding F2), which is why every number in this document comes from OS-level instruments
  around the library rather than from the library's own diagnostics - and why F4's causal question
  could not be answered from the library's own `foreground=` reading.

## Recording the result

Copy the checklist results into the slice summary together with the date, the Windows build number and
the session's display configuration, and carry the NOT OBSERVED entries over verbatim. A failed check
is a failed check: name the step, attach the capture, and do not restate it as a pass. The sample
writes runtime failures to stderr as `[sample] TrayError operation=...`; all runs here exited with
code 0 and empty stderr.

Recorded for S03/T05 on 2026-09-20 and extended for S03/T06 on 2026-09-20 (Windows 11 Pro build
26200, .NET SDK 10.0.401, x64, one monitor 1920x1200 physical at Display Scale 150 %, icon in the
notification-area overflow flyout; the T06 captures were taken on the PerMonitorV2 build and the T05
ones on the SYSTEM_AWARE build it replaced):

- checks **1-3 = PASS** (machine-measured by the sample's OS readings and the injector's
  out-of-process window measurements, which agree on the popup handle, its rectangle, its owner and
  its monitor's DPI, in both sessions);
- check **6 = PASS** in T06 (`value=2`, `isPerMonitorV2=True`, confirmed out of process), recorded as
  NOT OBSERVED in T05 with the `value=1` reading that made it fail then;
- check **7 = PASS for the library's contract / NOT OBSERVED as a live `Closed` line** (finding F1),
  with the consumer-driven close measured live in T06 (check 9);
- check **8 = recorded** (overflow flyout);
- check **9 = PASS** (the menu opens and closes with no click injected, same popup geometry, real
  owner, window gone afterwards);
- check **10 = mixed**: 10a and 10b PASS live, 10b's shape standing in for 10c, 10d 10e recorded
  in-repo or by design, 10f a FAIL of the dismissal contract in a configuration a user click cannot
  reach (finding F4);
- checks **4 and 5 = NOT OBSERVED** with the fixture suites named as the evidence;
- findings **F1** (a disposal-driven close does not deliver `Closed` to the consumer), **F2** (two
  icons' menus cannot both be shown at once), **F3** (the pump control reads 1 private-range message
  where every S02 run read 0, and 0 when the click was cancelled) and **F4** (a menu opened for a
  hidden icon is ownerless and not dismissed by an outside click), and **F5** (the popup's owner is a
  WPF-internal outcome - the anchor in a quiet process, `0x0` under full-suite load with the anchor
  foreground and the dismissal still correct - which is why the suite's boundary assertion reads
  "the anchor or absent, never the registration host").

**S08 status, added 2026-09-22 at revision `996c9b0` (the S03 record above is unchanged):** F1 is
**CLOSED** by S08/T03 (D045) - a disposal-driven close now delivers the consumer's `Closed` exactly
once before `Dispose` returns; F4 is **SUPERSEDED** by S08/T02 (D044) - D042's document-only v1
disposition is replaced by the owner repair, which makes the no-click open anchor-owned and dismissable;
F5 is **TIGHTENED TO EQUALITY** by S08/T02 (D044) - the `GW_OWNER` is the library's own write on every
product path. F2 and F3 stand unchanged. The per-finding status lines above name the test class or run
carrying each verdict; `docs/UAT-S08.md` holds the raw evidence and `docs/TEST-ENVIRONMENT.md` records
that the five-failure environment dependency is retired.
