# UAT-S02 — Live click-event checks for the notification-area icon

These checks are **manual** and they are the only evidence for the part of S02 that a GitHub Actions
Windows runner cannot produce. Those runners have no interactive desktop session, so no shell ever
delivers a notification-area callback on them, and the behaviour that depends on a live shell — a
click on the icon becoming a typed routed event, and a Preview handler suppressing it — is
demonstrated here rather than claimed as automated coverage.

**Do not report these checks as automated coverage, and do not report a check as passed that was
not measured.** The automated suite covers the decoding rules, the eight routed events, the
Preview-to-main suppression in the raiser, the payload shape and the boundary contracts; it injects
the callback message itself and therefore proves what the library does with a well-formed callback.
It cannot prove what the shell sends, and it never sees the icon. Scope: S02 only.

## How the 2026-09-20 results below were obtained

Every `Result:` line below is a **measurement from a named instrument**, not a look at the screen:
the agent that ran this checklist has no human eye and cannot view a screenshot, so "observed" means
*recorded by the instrument named in the line*. Anything that could not be established this way is
marked **NOT OBSERVED** with its reason, never inferred into a pass.

- **The sample's console (`Trustsoft.NotifyIcon.Sample`)** — the decoded half of the evidence. Its
  click handlers write one `[sample] click ...` line per delivered routed event and one
  `[sample] click cancelled by Preview handler: ...` line per cancellation. Started with
  `--run-seconds` and, for the cancellation checks, `--cancel-preview <click type>`.
- **The raw callback stream (read-only `HwndSource` hook)** — the shell-level half. The sample
  enumerates the top-level windows of its own thread and adds a hook to each one WPF still knows as
  an `HwndSource`, which reaches the library's hidden host window without naming a single library
  internal; each message in the private range `WM_USER..WM_APP-1` is printed as
  `[sample] raw callback hwnd=... msg=... event=... iconId=... wParam=... lParam=...`. The hook never
  consumes a message and never changes one. It flushes per line, so the last line of a run survives
  it.
- **The pump control, calibrated in the same run** — the sample also counts the private-range
  messages the WPF message pump retrieves (`ComponentDispatcher.ThreadFilterMessage`) and reports the
  count in its totals line without printing them. Its calibration is
  `.gsd/exec/5b0da9e1-3cbe-4df9-9a40-a6a0f93ba08a.stdout`: a message **posted** to an `HwndSource` is
  seen by the pump *and* by a hook, while the same message **sent** from a foreign thread is seen by
  the hook only (`SENT WM_USER+1 seen by pump=False hook=True`). That is what makes
  `raw callback lines=6, pump-observed private-range messages=0` an interpretable measurement rather
  than two coincidences: the shell's tray callback is delivered synchronously and is never retrieved
  from the queue.
- **The click injector (`probe-clicks`)** — real input, not a fabricated message:
  `SetCursorPos` to the icon's UI Automation bounding-rectangle centre followed by
  `mouse_event` down/up pairs (left, left twice inside the double-click time, right, middle). The
  injector prints the icon rectangle, the coordinates actually injected, and what
  `WindowFromPoint` found at those coordinates, so a scenario whose click missed can be told apart
  from one the shell ignored.
- **The trace-channel probe (`probe-trace`)**, `.gsd/exec/9e1e101f-1975-438f-b857-9741b0b6d252.stdout`
  — the measurement behind finding F2: a `TraceSource` created with the library's channel name gets
  its own listener list, and an `<App.config><system.diagnostics>` listener entry is not applied by
  .NET 8.
- **The library's channel itself**: because of F2, the lines the library writes at Verbose about the
  callbacks it does not map (mouse motion, key selection) are **not observable by a consumer** and
  therefore do not appear in these captures. Nothing below relies on them.

Environment: **Windows 11 Pro, build 26200**, x64, interactive session, .NET SDK **10.0.401**,
target `net8.0-windows`. Date: **2026-09-20**. All runs in this document were one scenario per
process, of which the eight captures are `.gsd/exec/clicks2-<scenario>.txt`.

## Prerequisites and how to run it yourself

- Windows 10 or 11 with an interactive desktop session and the notification area. The icon is
  usually in the tray's overflow flyout; expand it first (the injector does this by invoking the
  `Show Hidden Icons` chevron).
- A .NET 8 (or later) SDK, and a terminal in the repository root.
- `dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --run-seconds 60` prints
  the active modes at startup, then one `[sample] click ...` line per click you make on the icon and
  one `[sample] raw callback event=0x....` line per callback the shell sends. The line
  `[sample] raw callback trace: hooked N HwndSource(s) owned by this thread` must print `N >= 1`; a
  `0` would mean the host window did not exist yet and the raw stream is not being watched.
- Add `-- --cancel-preview left|double|right|middle` to cancel one click type: that Preview handler
  sets `Handled`, prints
  `[sample] click cancelled by Preview handler: previewType=... (no [sample] click line for this click)`,
  and the matching `[sample] click` line must **not** appear. The startup line names the effective
  mode, so a typo cannot make the run prove the wrong thing.

## Checklist

Each result names the run, what the injector did, and the **exact ordered** lines the sample
printed, raw callback lines interleaved with the decoded ones exactly as they arrived.

### 1. A single left click raises one left-click event

Result: **PASS** (2026-09-20, `.gsd/exec/clicks2-single.txt`). The injector found the icon at
`1320,1042 60x60` and clicked its centre `1350,1072`;
`WindowFromPoint(1350,1072)` was
`class='TopLevelWindowForOverflowXamlIsland' title='System tray overflow window.' pid=5544`
(Explorer) — the click landed on the shell's tray surface, not on a stale rectangle. The sample
printed, in this order:

```text
[sample] raw callback hwnd=0x2960992 msg=0x0401 event=0x0200 ...      (pointer motion over the icon)
[sample] raw callback hwnd=0x2960992 msg=0x0401 event=0x0201 ...      WM_LBUTTONDOWN
[sample] raw callback hwnd=0x2960992 msg=0x0401 event=0x0202 ...      WM_LBUTTONUP
[sample] click type=TrayLeftClick button=Left count=1 anchor=1349,1071
[sample] raw callback hwnd=0x2960992 msg=0x0401 event=0x0400 ...      NIN_SELECT
[sample] totals: raw callback lines=5, pump-observed private-range messages=0, library trace lines=0, clicks=1, cancelled by a Preview handler=0.
```

### 2. A left double click raises a double-click event

Result: **PASS for the double-click event; see finding F1 for what else a double click delivers**
(2026-09-20, `.gsd/exec/clicks2-double.txt`). Two down/up pairs 60 ms apart, both inside the shell's
double-click time, produced:

```text
event=0x0201      WM_LBUTTONDOWN
event=0x0202      WM_LBUTTONUP
[sample] click type=TrayLeftClick button=Left count=1 anchor=1349,1071
event=0x0400      NIN_SELECT
event=0x0203      WM_LBUTTONDBLCLK
[sample] click type=TrayLeftDoubleClick button=Left count=2 anchor=1349,1071
event=0x0202      WM_LBUTTONUP
[sample] click type=TrayLeftClick button=Left count=1 anchor=1349,1071
event=0x0400      NIN_SELECT
[sample] totals: raw callback lines=6, pump-observed private-range messages=0, library trace lines=0, clicks=3, cancelled by a Preview handler=0.
```

### 3. A right click raises exactly one right-click event

Result: **PASS** (2026-09-20, `.gsd/exec/clicks2-right.txt`):

```text
event=0x0204      WM_RBUTTONDOWN
event=0x0205      WM_RBUTTONUP
event=0x007B      WM_CONTEXTMENU
[sample] click type=TrayRightClick button=Right count=1 anchor=1349,1071
[sample] totals: raw callback lines=3, pump-observed private-range messages=0, library trace lines=0, clicks=1, cancelled by a Preview handler=0.
```

Exactly one event, because `WM_RBUTTONUP` (0x0205) is deliberately unmapped — and 0x0205 *is* sent,
which contradicts the rationale currently written next to that decision (finding F4). The
*decision* is validated by this measurement; only its stated reason is wrong.

### 4. A middle click raises a middle-click event

Result: **PASS** (2026-09-20, `.gsd/exec/clicks2-middle.txt`):

```text
event=0x0207      WM_MBUTTONDOWN
event=0x0208      WM_MBUTTONUP
[sample] click type=TrayMiddleClick button=Middle count=1 anchor=1349,1071
event=0x0200 × 3  pointer motion, all unmapped
[sample] totals: raw callback lines=5, pump-observed private-range messages=0, library trace lines=0, clicks=1, cancelled by a Preview handler=0.
```

### 5. A Preview handler that sets `Handled` prevents the action

Result: **PASS for all four click types** (2026-09-20: `.gsd/exec/clicks2-cancel-left.txt`,
`clicks2-cancel-double.txt`, `clicks2-cancel-middle.txt`, `clicks2-cancel-right.txt`). Each run
started the sample with `--cancel-preview <type>` and injected that type's click. In every case the
raw callback lines are **identical to the uncancelled run** — the shell delivered the click and the
library decoded it — while the main `[sample] click` line is absent and `clicks=0`:

| Cancelled type | Raw sequence (msg 0x0401) | Decoded output | Totals |
| --- | --- | --- | --- |
| `left` | `0x0201`, `0x0202`, `0x0400` | `click cancelled by Preview handler: previewType=PreviewTrayLeftClick button=Left count=1` | `clicks=0, cancelled=1` |
| `double` | `0x0201`, `0x0202`, `0x0400`, `0x0203`, `0x0202`, `0x0400` | two `TrayLeftClick` lines pass through; `PreviewTrayLeftDoubleClick button=Left count=2` is cancelled | `clicks=2, cancelled=1` |
| `right` | `0x0204`, `0x0205`, `0x007B` | `click cancelled by Preview handler: previewType=PreviewTrayRightClick button=Right count=1` | `clicks=0, cancelled=1` |
| `middle` | `0x0207`, `0x0208` | `click cancelled by Preview handler: previewType=PreviewTrayMiddleClick button=Middle count=1` | `clicks=0, cancelled=1` |

The `double` row is the sharpest evidence that the cancellation is precise rather than a blanket
suppression: of the three events a double click delivers, the one the switch named was suppressed
and the other two were delivered normally.

Every run also confirms the two read-only instruments agree with each other: the decoded click line
names `anchor=1349,1071`, and the raw line for the same callback carries
`wParam=0x00000000042F0545`, i.e. `GET_X_LPARAM` = `0x0545` = 1349 and `GET_Y_LPARAM` = `0x042F` =
1071. The anchor the library exposes is the anchor the shell sent.

## The raw `NOTIFYICON_VERSION_4` sequence per interaction (what the shell actually sent)

This is the section the slice's open question was about: Microsoft documents the *encoding* of the
version-4 callback (`LOWORD(lParam)` = event code, `HIWORD(lParam)` = icon id, `wParam` = anchor)
but not the *sequence* per interaction. Measured on 2026-09-20, with the icon in the tray's overflow
flyout, every click injected as real input:

| Interaction | Event codes in order (`msg = WM_USER+1 = 0x0401`) | Decoded as |
| --- | --- | --- |
| Pointer motion over the icon | `0x0200` (repeated, 2–37 per run) | nothing (unmapped) |
| Single left click | `0x0201`, `0x0202`, `0x0400` | `TrayLeftClick` (from `0x0202`) |
| Double left click | `0x0201`, `0x0202`, `0x0400`, `0x0203`, `0x0202`, `0x0400` | `TrayLeftClick`, `TrayLeftDoubleClick`, `TrayLeftClick` |
| Right click | `0x0204`, `0x0205`, `0x007B` | `TrayRightClick` (from `0x007B`) |
| Middle click | `0x0207`, `0x0208` | `TrayMiddleClick` (from `0x0208`) |

Codes: `0x0200` `WM_MOUSEMOVE`, `0x0201` `WM_LBUTTONDOWN`, `0x0202` `WM_LBUTTONUP`, `0x0203`
`WM_LBUTTONDBLCLK`, `0x0204` `WM_RBUTTONDOWN`, `0x0205` `WM_RBUTTONUP`, `0x0207` `WM_MBUTTONDOWN`,
`0x0208` `WM_MBUTTONUP`, `0x007B` `WM_CONTEXTMENU`, `0x0400` `NIN_SELECT` (`WM_USER + 0`).

**Question 1 — does a right click deliver only `WM_CONTEXTMENU`? No.** It delivers
`WM_RBUTTONDOWN`, then `WM_RBUTTONUP`, then `WM_CONTEXTMENU` — three callbacks for one right click,
of which only the last is mapped. The library's right-click decision stands (only one
`TrayRightClick` is raised), but the sentence in `ShellConstants` explaining it ("`WM_RBUTTONUP` is
not sent under v4") is contradicted by this measurement and should be corrected, as finding F4.

**Question 2 — does a double click end with a trailing `WM_LBUTTONUP`? Yes**, and the consequence
is finding F1: one double click produces **three** routed events, not one.

**Question 3 — does `NIN_SELECT` arrive alongside `WM_LBUTTONUP`? Yes.** `0x0400` (`NIN_SELECT`)
follows every left-button release — after the single click's `0x0202` and after both releases of the
double click — and it is *not* sent for the right or middle clicks. Because `NIN_SELECT` is not one
of the four mapped codes, it adds no event and would have been invisible without the raw stream: it
is the single clearest reason this section needed an instrument of its own.

## Findings

These are measurements that contradict something currently written down. None of them was fixed by
a late change inside this task: each is recorded with the evidence needed to act on it.

### F1 — a double click delivers three click events (single, double, single)

Measured in `.gsd/exec/clicks2-double.txt` and reproducible: the shell sends `WM_LBUTTONUP` for the
first press, `WM_LBUTTONDBLCLK` for the second press and `WM_LBUTTONUP` again for the second
release, and the decoder maps each code independently by design. A consumer that handles
`TrayLeftClick` therefore sees one event at the start of a double click and another one after it.

This is reported rather than repaired: suppression would have to be *stateful* (a single click may
only be suppressed once it is known that no double click follows, which is a timing policy), it would
need its own window-timing test, and the decoder's statelessness is exactly what makes the encoding
provable. The honest outcome at the end of this slice is a documented follow-up, not an unverified
behaviour change. It should be decided before S03 builds menu activation on top of these events,
because `MenuActivation.LeftClick`-style behaviour would otherwise open a menu after every double
click.

### F2 — the library's trace channel cannot be reached from a consumer assembly

Measured twice. In the live runs, the sample attached a Verbose listener to the channel named
`Trustsoft.NotifyIcon` exactly as `NotifyIconTrace`'s remarks describe, and the totals line reports
`library trace lines=0` in **all eight** runs — while the same runs prove the library was writing
those lines, because dozens of callbacks arrived that its decoder does not map (`0x0200`, `0x0201`,
`0x0400`) and `OnHostMessage` writes exactly one Verbose line only for those. The probe
(`.gsd/exec/9e1e101f-1975-438f-b857-9741b0b6d252.stdout`) shows why, for both documented
subscription paths:

```text
[probe] library listeners = 1, switch level = Warning      (the library's own source, unchanged)
[probe] consumer listeners = 1                             (its own DefaultTraceListener only)
[probe] consumer saw 0 line(s):
```

In .NET 8 a `TraceSource` created with an existing source's name does **not** share that source's
listeners or switch (verified: `same listener collection = False`, `same switch = False`), and
`<system.diagnostics>` in `App.config` is copied to the output as `<assembly>.dll.config` but not
applied. The library's own remarks promise the opposite — "a consumer can subscribe the usual way …
and can raise the level to see more when diagnosing" — so the Verbose click stream, and the Error
lines the windowless-failure policy writes, are unreachable by a consumer today. That weakens D008's
promise to exactly the extent that a windowless host has no other diagnostic channel. A decision is
needed (expose the source as public surface, or mirror to `Trace`); it is not a change this task may
make silently.

### F3 — the callback is delivered synchronously, and a pump-level observer never sees it

The pump control reports `pump-observed private-range messages=0` in all eight runs while the hook
reports 2–40 lines from the same run; the calibration run
`.gsd/exec/5b0da9e1-3cbe-4df9-9a40-a6a0f93ba08a.stdout` establishes that the pump does see private
messages that are genuinely *posted* (`POSTED WM_USER+2 seen by pump=True hook=True`) and does not
see ones that are *sent* (`SENT WM_USER+1 seen by pump=False hook=True`). Therefore the tray
callback is delivered synchronously. The decoder's remarks currently say the shell "posts every
notification-area event for our icon to the host window"; the encoding description is unaffected,
but that word should be corrected, because it is precisely the kind of detail a reader would use to
decide how to observe the stream.

### F4 — the stated reason for leaving `WM_RBUTTONUP` unmapped is wrong (the decision is not)

`ShellConstants.WM_RBUTTONUP`'s remarks say the code "is not sent under `NOTIFYICON_VERSION_4`".
Measured in `.gsd/exec/clicks2-right.txt` and `clicks2-cancel-right.txt`, it *is* sent: every right
click delivered `0x0204`, `0x0205`, then `0x007B`. The decision to leave it unmapped is what keeps a
right click to exactly one `TrayRightClickEvent`, so this is a documentation correction with a
warning attached, not a behaviour report: `TrayEventDecoderTests.Unmapped_event_codes_decode_to_nothing`
calls the `WM_RBUTTONUP` row "load-bearing" for the case where the shell sent both codes, and this
measurement shows that case is not hypothetical - it is every right click. If someone "fixed" the
comment by mapping the code, every right click would raise two right-click events. The correction
belongs to the slice that owns `ShellConstants`.

## What was NOT observed

- **The icon in the taskbar itself (not the overflow flyout).** Every click in this document was
  injected into the overflow flyout, because that is where this session keeps the icon: the injector
  had to invoke the `Show Hidden Icons` chevron first, and `WindowFromPoint` at the click point
  returned the flyout (`TopLevelWindowForOverflowXamlIsland`). Whether a click on the *taskbar* copy
  of the same icon produces the identical sequence was not measured. Nothing in the library depends
  on which surface delivered the callback, but "same sequence in the taskbar" is an assumption here,
  not a measurement.
- **Keyboard selection (`NIN_KEYSELECT`).** No keyboard interaction was injected, so no key-select
  notification was observed and the `NIN_SELECT | NINF_KEY` code is not covered by this checklist.
- **Balloon and popup notifications (`NIN_BALLOON*`, `NIN_POPUPOPEN/CLOSE`).** Out of scope for S02
  (they belong to S04), and none was raised.
- **Actual menu placement after a right click.** S02 only reports the click; nothing opens a menu
  yet, and the decoded right-click anchor is **informational only** — Microsoft documents `wParam` as
  valid for `NIN_POPUPOPEN`, `NIN_SELECT`, `NIN_KEYSELECT` and the mouse messages between
  `WM_MOUSEFIRST` and `WM_MOUSELAST`, and `WM_CONTEXTMENU` (0x007B) is outside that set. This
  session's shell did fill it in (`wParam=0x00000000042F0545` = the click point, matching the raw
  line for the same callback), but the official status stays undefined: **S03 must take menu
  placement from `Shell_NotifyIconGetRect`, never from `TrayIconClickEventArgs.ScreenAnchor`.**
- **The per-line rendering of the trace channel.** F2 means no consumer-side capture of the
  library's Verbose lines exists; the raw stream in this document comes from the window hook, which
  observes the same callbacks one layer lower (before any decoding).

## Recording the result

Copy the five checklist results into the slice summary together with the date and the Windows build
number. A failed check is a failed check: name the step, attach the capture, and do not restate it as
a pass. The sample writes runtime failures to `stderr` as `[sample] TrayError operation=...` (none
occurred in any run here; all eight runs exited with code 0 and empty `stderr`), so a failure usually
has a machine-readable record next to the on-screen symptom.

Recorded for S02/T05 on 2026-09-20 (Windows 11 Pro build 26200, .NET SDK 10.0.401, x64): checks 1–5
= **PASS** (all machine-measured by injected real input and by the sample's own console, which
carries the raw callback lines and the decoded click lines in one ordered stream), with the
taskbar-surface and keyboard paths marked **NOT OBSERVED** above, plus findings **F1** (a double
click delivers three events — reported, not repaired), **F2** (the library's trace channel is
unreachable from a consumer assembly), **F3** (the callback is delivered synchronously, so a
pump-level observer cannot see it) and **F4** (the stated reason for leaving `WM_RBUTTONUP` unmapped
is contradicted by measurement).
