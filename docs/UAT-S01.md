# UAT-S01 — Live notification-area checks for the core tray icon

These checks are **manual** and they are the only evidence for the part of S01 that GitHub Actions
Windows runners cannot produce. Per D009 those runners have no interactive desktop session, so the
behaviour that genuinely depends on a live shell — an icon that appears and stays, an alert area
that shows it, a tooltip that follows the mouse — is demonstrated here rather than claimed as
automated coverage.

**Do not report these checks as automated coverage, and do not report a check as passed that you did
not personally observe on screen.** The automated suite covers the call sequence, the struct layout,
the handle ownership and the failure policies; it cannot see the notification area.

Scope: S01 only (the core icon lifecycle). **Explorer-restart recovery is deliberately NOT part of
this checklist** — it belongs to S05, together with the process-exit fallback.

## How the 2026-09-20 results below were obtained

The run of this checklist was performed by an automated agent with **no human eye and no ability to
view a screenshot**, so "observed" below means *measured by a read-only instrument on the same
desktop session*, never "looked at". Every instrument and its output is named in the `Result:` line:

- **UIAutomation** (`System.Windows.Automation`) — the notification area's accessibility tree. A
  registered tray icon appears there as a `SystemTray.NormalButton` whose accessible name is the
  tooltip text the shell stored.
- **Window topology** (`EnumWindows` + `GetWindowThreadProcessId` + `GetWindowLongPtrW`) — the
  sample process's top-level windows, their visibility and extended styles.
- **Pixel sampling** (`CopyFromScreen` of the icon tile) — dominant-channel classification and a
  SHA-1 of the tile bytes, once per second, which is how the frame rotation was measured.
- **`GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS)`** — the GDI object count, calibrated in
  the same run with `CreateCompatibleDC`/`DeleteDC` (10 DCs ⇒ exactly `+10`, then back to `0`).

Anything that could not be established this way is marked **NOT OBSERVED** with the reason, not
`PASS`.

Environment: **Windows 11 Pro, build 26200** (`10.0.26200.9457`), x64, interactive session, .NET 8
SDK. Date: **2026-09-20**.

## Prerequisites

- Windows 10 or 11 with an interactive desktop session and the notification area (the taskbar's
  "hidden icons" flyout is fine; expand it first so the sample's icon is visible).
- A .NET 8 SDK (the sample targets `net8.0-windows`).
- A terminal opened in the repository root.

## Checklist

### 1. Run the sample

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release
```

Expected: the terminal prints

```text
[sample] tray icon registered, rotating 3 frames every 1s.
[sample] no window is shown - check the notification area, not the taskbar.
```

and the process stays in the foreground producing no exception. If it instead prints
`[sample] startup failure: operation=... win32Error=...`, that is a real failure of the registration
path — record the exact line and stop; do not continue with the remaining steps.

Result: **PASS** (2026-09-20). Four runs, all with the expected output and exit code 0:
`--run-seconds 75`, `--run-seconds 150` (19:13:24–19:15:58), `--run-seconds 120`
(19:18:09–19:20:13, the run used for the tooltip attempt in check 4) and `--run-seconds 50` all
printed the two expected lines plus
`[sample] will shut down by itself after Ns (graceful close check).`, no `[sample] startup failure:`
line, no `[sample] TrayError` line, and each one ended with
`[sample] tray icon disposed - it must have left the notification area.` and exit code 0.

### 2. The icon appears with no window

Without doing anything else, look at the notification area (and its "show hidden icons" flyout).

Expected: a small icon is present — a blue disc at first, then a red square with a white band, then
a green triangle — and **no application window has appeared anywhere on the desktop**.

Result: **PASS for presence and for "no window"; the three shapes themselves NOT OBSERVED**
(2026-09-20). The icon is registered in the collapsed tray's overflow: with the "Show Hidden Icons"
flyout opened, UIAutomation returned a `SystemTray.NormalButton` named exactly
`Trustsoft.NotifyIcon sample - the icon changes every second` at `1368,1042 60x60`, on-screen
(`IsOffscreen=false`). The tile's pixel sample cycled through a blue-dominant frame (486 coloured
pixels), a red-dominant frame with 144 white pixels (the white band) and a green-dominant frame
(289), i.e. the three frames' colours — that is what "blue → red with a white band → green" looks
like numerically, but the *shapes* (disc, square, triangle) were not checked, because no human eye
and no image viewer were available to this run. No window appeared: all 7 top-level windows of the
sample process were invisible (`VISIBLE_TOPLEVEL_WINDOWS=0`).

### 3. No taskbar button and no Alt-Tab entry

Click the taskbar once, then hold <kbd>Alt</kbd> and press <kbd>Tab</kbd> repeatedly.

Expected: the sample appears **neither** as a taskbar button **nor** in the Alt-Tab list, even though
its icon is in the notification area. (This is the `WS_EX_TOOLWINDOW` premise of D011; the automated
tests can only assert that style bit on the host window, not what the shell does with it.)

Result: **PASS** (2026-09-20), measured rather than key-pressed. The taskbar's own accessibility tree
was enumerated while the icon was registered: the button list (`Taskbar.TaskListButtonAutomationPeer`
with an `Appid:` per button) held 10 buttons — Start/Search, File Explorer, Yandex, Chrome, Rider,
VS Code, Visual Studio, Bondly, Notepad3, Terminal — and **none** for the sample
(`TASKBAR_BUTTONS_FOR_SAMPLE=0`). The host window measured `exstyle=0x00000180`
(`WS_EX_TOOLWINDOW` set, `WS_EX_WINDOWEDGE` added by WPF, `WS_EX_APPWINDOW` clear), and of the
process's 7 top-level windows **0** were Alt-Tab-eligible (visible and not `WS_EX_TOOLWINDOW`).
The Alt-Tab list itself was not opened with the keyboard; its eligibility condition was measured
instead.

### 4. The tooltip shows the tooltip text

Hover the mouse pointer over the sample's icon and hold it still for about a second.

Expected: the shell shows a tooltip reading
`Trustsoft.NotifyIcon sample - the icon changes every second`.
(This is the live proof of the `NIF_SHOWTIP` flag; there is no automated equivalent.)

Result: **NOT OBSERVED** (2026-09-20) — with the tooltip text itself confirmed to be **in the shell's
hands**. Two programmatic hover attempts were made on the live icon while the flyout was open: the
cursor was placed on the tile centre with `SetCursorPos`, and the second attempt additionally
injected genuine mouse input (`mouse_event` moves) and then polled for 4 seconds. Both attempts
found **no visible window** and **no UIA `ToolTip` element** carrying the text; all 19
`tooltips_class32` top-level windows on the desktop were hidden with empty captions. The rendered
tooltip bubble therefore cannot be reported as passed by this run: recording a hover bubble as PASS
would require actually seeing it. What *was* verified is the input side of the feature: the string
`Trustsoft.NotifyIcon sample - the icon changes every second` is what the shell stores for the icon
and exposes as its accessible name, so `NIF_SHOWTIP`/`szTip` demonstrably reached the shell — it is
the *rendering* of the bubble that remains unobserved.

### 5. The icon changes about once per second

Watch the icon in the notification area for ten to fifteen seconds without touching anything.

Expected: the image changes visibly roughly once per second, cycling through the three frames
(blue disc → red square with a white band → green triangle → …) and never disappearing or turning
blank between changes. The terminal stays free of `[sample] TrayError` lines throughout.

Result: **PASS** (2026-09-20). 14 consecutive samples of the icon's 60x60 tile, one second apart
(19:13:56–19:14:09), each classified by dominant colour and hashed:

```text
SAMPLE  0 blue=486 green=0   red=0   white=0   sha1=6AF619C89374
SAMPLE  1 blue=0   green=0   red=432 white=144 sha1=30CB71611CD7
SAMPLE  2 blue=0   green=289 red=0   white=0   sha1=C93BFAAB8266
SAMPLE  3 … repeating exactly (6AF6 / 30CB / C93B → …)
```

A strict three-frame cycle with a period of 3 s, the same SHA-1 recurring for the same frame across
five cycles, every sample carrying coloured pixels (nothing blank or missing), and no
`[sample] TrayError` line in the terminal during any run.

### 6. The icon disappears on a normal exit

Stop the sample the normal way: press <kbd>Ctrl</kbd>+<kbd>C</kbd> in the terminal, or start it with
a self-imposed lifetime and let it finish on its own:

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --run-seconds 20
```

Expected: the terminal prints `[sample] tray icon disposed - it must have left the notification area.`
and the sample's icon **is gone** from the notification area as soon as the process has exited. A
leftover icon that vanishes only when the mouse hovers over it means the `NIM_DELETE` on disposal
did not reach the shell — record that as a failure.

Result: **PASS** (2026-09-20). The `--run-seconds 75` run printed
`[sample] tray icon disposed - it must have left the notification area.` and exited with code 0.
The overflow flyout was left **open** across the exit as the control: immediately after the process
ended the UIAutomation scan found the icon element gone, with the flyout still showing its other
hidden icons (12 tray items, including TrafficMonitor, Sticky Password and Bluetooth — so the
flyout was genuinely populated and open, not collapsed). After dismissing the flyout (Escape) and
reopening it fresh, the scan **still** found 0 elements matching the tooltip name — which rules out
the stale-leftover case that clears itself on hover. `NIM_DELETE` reached the shell.

## Two open risks from the research document, double-checked

Both were flagged as "assumed rather than verified"; here is the measurement for each.

1. **`GetAncestor(hwnd, GA_ROOT) == hwnd` (T05).** Re-run and quoted:
   `dotnet test … --filter "FullyQualifiedName~HostWindowTests"` →
   **`Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`** (377 ms), the class that contains
   `Host_is_a_top_level_window` (the `GA_ROOT` proof), `Host_is_not_visible` and
   `Host_has_toolwindow_extended_style_and_not_appwindow`.
2. **`WS_EX_TOOLWINDOW` preservation by `HwndSource`.** The T05 narrative recorded a measured
   `GWL_EXSTYLE = 0x180`; this checklist reproduced it **on the live sample process**:
   `hwnd=0xC3073A class='HwndWrapper[Trustsoft.NotifyIcon.Sample;…]'
   title='Trustsoft.NotifyIcon.TrayMessageWindow' visible=False exstyle=0x00000180 TOOLWINDOW=True
   APPWINDOW=False`. The style bit *is* preserved (WPF adds `WS_EX_WINDOWEDGE`), so the "no taskbar
   button" claim has both its premise and its live observation: 0 taskbar buttons and 0
   Alt-Tab-eligible windows in check 3.

## GDI evidence, as measurements (R007)

Everything below is a measured number, not an adjective. The instrument was calibrated in the same
run: **10 × `CreateCompatibleDC` ⇒ `+10`, 10 × `DeleteDC` ⇒ `0`**.

| Scenario | Source kind | Replacements | GDI before | GDI after | Delta | Delta after full GC |
| --- | --- | --- | --- | --- | --- | --- |
| Live sample, PID 10936, run 2 | frozen vector `DrawingImage` | ~57 (1/s) | 123 @ 19:14:23 | 235 @ 19:15:20 | **+112** | not measured (live) |
| Probe, public `TrayIcon` | frozen `BitmapSource` | 50 | 5 | 5 | **0** | 0 |
| Probe, public `TrayIcon` | frozen vector `DrawingImage` | 50 | 7 | 109 | **+102** (2.04/replacement) | +10 |
| Probe, public `TrayIcon` | frozen vector `DrawingImage` | 200 | 17 | 413 | **+396** (1.98/replacement) | +4 |
| Isolation probe (WPF only) | `new RenderTargetBitmap(16,16,…)` | 50 | — | — | **+101** | +29 |

The probe drove the **public** `TrayIcon` API (no seam, no fake) on one STA thread with a pumping
dispatcher, one replacement per dispatcher turn, exactly like the sample's once-a-second rotation;
its output is recorded in `.gsd/exec/9571ea58-f346-42a4-bf16-9f4e3abd0940.stdout`.

**What this confirms:** the library's own `HICON`/`HBITMAP` ownership is exact. Fifty replacements
with a **bitmap** source move the process GDI count by **0**, matching the suite's
`No_gdi_leak_across_50_icon_replacement_cycles` (`Passed! 1 test`, assertion `after - before <= 2`,
identity counters: 52 icons created, 52 destroyed) and `Real_shell_conversion_leaves_the_GDI_count_flat`
(20 real conversions, `Assert.InRange(after - before, 0, 2)`), with the seam release counts
`DeletedObjects` = **2** on the success path (`Valid_Bgra32_source_produces_a_nonzero_icon_and_releases_both_bitmaps`),
**100** across 50 conversions, **2** on the `CreateIconIndirect` failure path, **1** on the
mask-failure path, **0** when nothing was allocated (colour-bitmap failure, unsupported source, null
input). The suite reports `Passed! 0 failed, 113 passed, 0 skipped` for the whole assembly and
`HiconFactoryTests` 11/11.

**What it does not substantiate (finding F1).** The slice demo's claim is that *replacing the icon
repeatedly leaves the GDI handle count flat*, and the sample app demonstrates it with **vector**
frames. For a vector `DrawingImage` the conversion goes through
`HiconFactory.Rasterize` → a **new `RenderTargetBitmap` per replacement**, and that costs **~2 GDI
objects per replacement**, returned only when the GC finalizes them: 50 replacements ⇒ `+102`, 200
⇒ `+396`, while the live sample climbed `123 → 235` in 57 seconds with **no gen0 collection during
the window** (probe: `gen0Before == gen0After` in both runs). After a forced full GC the residue was
small and **not proportional to the replacement count** (`+10` after 50, `+4` after 200), which is
what distinguishes this from a runaway leak — but between collections the count is *not* flat, and
at one replacement per second an application can accumulate hundreds of GDI objects before a
collection. `README.md` said the conversion "does not leak handles", which is true for bitmaps and
over-broad for drawings; see the finding recorded with this task and the README correction that
accompanies it.

## Recording the result

Copy the six `Result:` values into the slice summary together with the date and the Windows build
number. A failed check is a failed check: report it, name the step, and attach the terminal output.
The sample writes runtime failures to `stderr` as `[sample] TrayError operation=...` and to the
`Trustsoft.NotifyIcon.Sample` trace source, so a failure usually has a second, machine-readable
record next to the on-screen symptom.

Recorded for S01/T10 on 2026-09-20 (Windows 11 Pro build 26200): checks 1, 2, 3, 5, 6 = **PASS**
(machine-measured; the frame *shapes* in check 2 were not visually verified), check 4 = **NOT
OBSERVED** (no visible tooltip bubble could be detected; the tooltip text is confirmed to be
registered with the shell), plus finding **F1** in the GDI table above.
