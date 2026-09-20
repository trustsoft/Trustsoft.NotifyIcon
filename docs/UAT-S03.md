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

## How the 2026-09-20 results below were obtained

Every `Result:` line is a **measurement from a named instrument**, not a look at the screen: the
agent that ran this checklist has no human eye and cannot view a screenshot, so "observed" means
*recorded by the instrument named in the line*. Anything that could not be established this way is
marked **NOT OBSERVED** with its reason, never inferred into a pass.

- **The sample's console (`Trustsoft.NotifyIcon.Sample`)** - the in-process half. Since S03/T05 the
  sample owns a real two-item `ContextMenu`, assigned to the icon's `ContextMenu` property at
  startup, and writes `[sample] menu opened: popup=0x... class=... rect=L,T WxH dpi=... scale=...
  owner=0x...` on the menu's `Opened` event and `[sample] menu dismissed.` on `Closed`. The numbers
  in that line are read from the OS inside the sample (`EnumThreadWindows` + `GetWindowRect` +
  `GetWindow(GW_OWNER)` + `GetDpiForWindow`), not from the library, which is what makes them an
  independent reading of the same popup the injector measures from outside.
- **The click injector (`probe-clicks`)** - real input and the out-of-process half. It finds the icon
  through UI Automation, injects a real right click, and for the new `menu` scenario prints the icon
  rectangle, the window under the cursor, the popup window it finds by enumerating the sample
  process's visible windows (`EnumWindows` + `GetWindowThreadProcessId` + `IsWindowVisible` +
  `GetWindowRect`), its owner, the comparison between the popup and the icon rectangles, and then
  injects an outside click and reports whether any visible popup survived it. **The instrument was
  extended, not replaced**: the existing `single`, `double`, `right`, `middle` and `cancel-*`
  scenarios and their output shape are unchanged, so S02's recorded captures stay reproducible.
- **The DPI/awareness instrument** (`.gsd/measure-dpi-awareness.ps1`, a scratch script, not product
  code) - `SetProcessDPIAware` + `GetSystemMetrics` + `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` for the
  session's own numbers, and `GetProcessDpiAwareness` on a freshly started sample process.
- **The in-repo fixture suites** - named for every row this single-monitor session could not run
  live: `TrayIconPlacementTests` (the pure calculator at 100/125/150/175/200 %, negative coordinates,
  every screen edge, taskbar-reserved edges and mixed-scale pairs), `TrayIconMenuActivationTests`
  (the delivered path at a scripted 144 DPI, including the documented cursor/work-area/DPI
  fallbacks) and `TrayIconMenuOwnerLifetimeTests` (50 open/dismiss cycles, GDI deltas, anchor
  lifetime).

Environment: **Windows 11 Pro, build 26200** (`Microsoft Windows NT 10.0.26200.0`), x64, .NET SDK
**10.0.401**, target `net8.0-windows`, interactive session with the icon in the notification-area
**overflow flyout**. Display configuration of this session, measured
(`.gsd/exec/ed8c5e8f-e2a7-4eb4-a6b7-5c18451a1061.stdout`):

```text
monitors=1
primaryPhysical=1920x1200
virtualPhysical=1920x1200
dpiForSystemForAnAwareProcess=144
primaryMonitorEffectiveDpi=144 (hr=0x00000000) scale=1.5
sampleGetProcessDpiAwareness=0x00000000 value=1 (0=unaware, 1=system-aware, 2=per-monitor-aware)
```

**One monitor, Display Scale 150 %** (144 DPI effective, `DpiValue=0` = the recommended value in
`HKCU\Control Panel\Desktop\PerMonitorSettings`). The scale matrix and the two-monitor rows below are
therefore **NOT OBSERVED** in this session, and the fixture suites named with them are what carries
those claims.

## Prerequisites and how to run it yourself

- Windows 10 or 11 with an interactive desktop session and a notification area. The icon is usually
  in the tray's overflow flyout; the injector expands it first (it invokes the `Show Hidden Icons`
  chevron through UI Automation).
- A .NET 8 (or later) SDK, and a terminal in the repository root.
- Build once, then run one scenario per process (the raw-callback stream is only interpretable if a
  run contains exactly one interaction):
  ```text
  dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
  dotnet build .gsd/probe-clicks -c Release --no-restore
  dotnet run --project .gsd/probe-clicks -c Release --no-build -- menu 25
  ```
  The check is green when the capture contains `[sample] menu opened:` with a `rect=`, a `dpi=` and
  an `owner=`, a `CLICK menu-placement` line whose `atScreenCorner=False`, a non-empty
  `description`-free `CLICK outside-click`, and then both `CLICK menu-after-outside-click | no
  visible popup window of the sample process remains` and `[sample] menu dismissed.`. The run's
  totals line must read `menu opens=1, menu dismissals=1`.
  **The `menu` scenario prints the comparison itself** (`iconRect`, `menuTopLeft`, `delta`,
  `popupContainsIconPoint`, `atScreenCorner`), which is what turns the placement row into a number
  pair instead of a judgement.
  A menu that does not open is a **failed** check: the capture then contains
  `CLICK menu-window | NOT FOUND ...` and the sample's stderr, and it must be recorded as such.
- For the exit behaviour, `-- right 25` (no outside click) shows the menu open and then the icon's
  graceful shutdown; see finding F1 for what that run does and does not show.

## Checklist

### 1. A right click opens the context menu with no window present

Result: **PASS** (2026-09-20, `.gsd/exec/9eaf36e3-9b0b-43bc-b538-547f2bc7d265.stdout`, scenario
`menu`). The sample is windowless (its own startup line says so) and the injector found the icon at
`rect=1224,1042 60x60` in the overflow flyout, clicked its centre `1254,1072`, and
`WindowFromPoint(1254,1072)` was `class='TopLevelWindowForOverflowXamlIsland' title='System tray
overflow window.' pid=5544` - the click landed on the shell's tray surface. The shell then delivered
the same three callbacks S02 recorded for a right click, and the library opened the menu:

```text
[sample] raw callback hwnd=0x4580BB2 msg=0x0401 event=0x0204 ...    WM_RBUTTONDOWN
[sample] raw callback hwnd=0x4580BB2 msg=0x0401 event=0x0205 ...    WM_RBUTTONUP
[sample] raw callback hwnd=0x4580BB2 msg=0x0401 event=0x007B ...    WM_CONTEXTMENU
[sample] click type=TrayRightClick button=Right count=1 anchor=1254,1071
[sample] menu opened: popup=0x4AB0A10 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;d760ca39-...] rect=1225,1021 296x83 dpi=144 scale=1.5 owner=0x10A0548
```

The popup's **owner is `0x10A0548`, the library's anchor window** - not `0x0` (the ownerless popup
T03 pinned as undismissable) and not the shell registration host. The out-of-process instrument
measured the same window independently: `CLICK menu-window | popup=0x4AB0A10 class='HwndWrapper[...]'
rect=1225,1021 296x83 owner=0x10A0548`.

### 2. The menu's rectangle is at the icon, not in the screen corner

Result: **PASS** (2026-09-20, same capture). The injector printed the icon rectangle UI Automation
reported and the popup rectangle it found, and the two agree that the popup's lower-left corner sits
at the icon:

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
matches the session's Display Scale of 150 % measured independently above.

### 3. An outside click dismisses the menu

Result: **PASS** (2026-09-20, same capture). The injector injected right+left at `925,721` - outside
the popup, far from the anchor - and `WindowFromPoint(925,721)` was
`class='CASCADIA_HOSTING_WINDOW_CLASS' title='gsd' pid=22652`, i.e. an unrelated window that could not
have dismissed the popup by itself. Immediately afterwards:

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
`virtualPhysical=1920x1200`). The evidence for the claim is the fixture suite again (mixed-scale
pairs in `TrayIconPlacementTests`) plus the scripted-DPI tests in `TrayIconMenuActivationTests`, which
drive the delivered menu path at a scripted 144 DPI and assert that the anchor window really sits at
the physical point the offset names. The live mixed-scale demonstration (right click on a monitor at
one scale, move the icon to a monitor at another, reopen) belongs to the slice's live demonstration
task (T06) and to a reviewer with two monitors.

### 6. The application manifest declares PerMonitorV2 and is in effect

Result: **NOT OBSERVED, and measured to be *not* satisfied by the current build.** Running
`GetProcessDpiAwareness` against a freshly started sample process reports
`0x00000000 value=1` = **SYSTEM_AWARE**, not per-monitor
(`.gsd/exec/ed8c5e8f-e2a7-4eb4-a6b7-5c18451a1061.stdout`, named command in the capture). The
PerMonitorV2 declaration is manifest-only and belongs to the sample/manifest task; until it lands,
the placement arithmetic this checklist measured at 150 % is the *monitor's* effective DPI as the
library reads it (`GetDpiForMonitor`), which is the correct input regardless of the process's own
awareness, but a process that is only system-aware is not a per-monitor consumer. The sample's
manifest, its own startup awareness line, and this row are the deliverables of the manifest task (T06).

### 7. The menu is torn down on a graceful exit

Result: **PASS for the library's contract, NOT OBSERVED as a sample `Closed` line - see finding F1.**
In-repo, `TrayIconMenuActivationTests.Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent`
proves synchronously, after `Dispose`: the menu is closed, `OpenContextMenu` is null, the anchor
handle is `IntPtr.Zero`, `Win32.IsWindow(anchor)` is false, the popup is gone after a pump and a
second `Dispose` is a no-op. Live (`-- right 25`, no outside click, `.gsd/exec/b6f031c6-4bea-442b-b286-1c413f3c9963.stdout`
and `.gsd/exec/9eaf36e3-9b0b-43bc-b538-547f2bc7d265.stdout`) the run exits with code 0, empty stderr,
`menu opens=1` and **`menu dismissals=0`**: the sample's own `Closed` line did not arrive for the
disposal-driven close. The library's teardown is synchronous (the in-repo test asserts it), so this
is a notification-order observation about WPF and the sample's instrumentation, recorded rather than
repaired (finding F1).

### 8. Which surface the icon was on during the run

Result: **the overflow flyout** (2026-09-20, all captures). The injector's `flyout` line shows it
invoked the `Show Hidden Icons` chevron, and `WindowFromPoint` at the click point returned
`TopLevelWindowForOverflowXamlIsland` / `System tray overflow window.` owned by Explorer (pid 5544).
S02's checklist had to record exactly this distinction, so it is recorded here too: **whether a
right click on the icon's taskbar copy (not the flyout) produces the identical sequence and
placement was not measured.**

## Findings

Measurements that contradict something written down or expected. None of them was repaired with a
late change inside this task: each is recorded with the evidence needed to act on it.

### F1 — a disposal-driven close does not deliver `ContextMenu.Closed` to the consumer

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

All three S03 runs report `pump-observed private-range messages=1` in the sample's totals line,
where every one of S02's eight recorded runs reported `0` (S02 finding F3, whose conclusion was that
the tray callback is delivered synchronously and is invisible to a pump-level observer). The raw hook
in the same runs saw exactly the same **three** callback lines S02 measured for a right click
(`0x0204`, `0x0205`, `0x007B`), so the extra message the pump saw is **not** the tray callback.
Nothing in this slice depends on the count, and the difference is not attributed here - it is
recorded because a control instrument that changes its reading between slices is exactly the kind of
thing that later reads as a mystery.

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
  on the slice's follow-up list.
- **The PerMonitorV2 manifest and the sample's own awareness line** (check 6). Measured:
  `GetProcessDpiAwareness = 1` (SYSTEM_AWARE) for the current build.
- **A click on a menu item.** The sample's menu carries two real items and its first item writes a
  line when chosen, but no click on an item was injected in these runs, so the item-invocation path
  was not observed live (it is the consumer's own WPF code, not the library's).
- **Keyboard dismissal (Escape) and the taskbar (non-flyout) surface.** Neither was injected; the
  earlier S02 caveat about the taskbar copy therefore still stands for the menu path (check 8).
- **The library's Verbose menu trace lines.** They remain unreachable from a consumer assembly in
  .NET 8 (S02 finding F2), which is why every number in this document comes from OS-level
  instruments around the library rather than from the library's own diagnostics.

## Recording the result

Copy the checklist results into the slice summary together with the date, the Windows build number and
the session's display configuration, and carry the NOT OBSERVED entries over verbatim. A failed check
is a failed check: name the step, attach the capture, and do not restate it as a pass. The sample
writes runtime failures to stderr as `[sample] TrayError operation=...`; all runs here exited with
code 0 and empty stderr.

Recorded for S03/T05 on 2026-09-20 (Windows 11 Pro build 26200, .NET SDK 10.0.401, x64, one monitor
1920x1200 physical at Display Scale 150 %, icon in the notification-area overflow flyout): checks
1-3 = **PASS** (machine-measured by the sample's OS readings and the injector's out-of-process window
measurements, which agree on the popup handle, its rectangle, its owner and its monitor's DPI), check
7 = **PASS for the library's contract / NOT OBSERVED as a live `Closed` line** (finding F1), check 8 =
recorded (overflow flyout), checks 4-6 = **NOT OBSERVED** with the fixture suites named as the
evidence and T06 named as the remaining live work, plus findings **F1** (a disposal-driven close does
not deliver `Closed` to the consumer), **F2** (two icons' menus cannot both be shown at once) and
**F3** (the pump control reads 1 private-range message where every S02 run read 0).
