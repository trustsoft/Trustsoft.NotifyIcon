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

The checklist was run **twice on 2026-09-20**: once before the rasterization change that finding F1
produced, and once after it (the second run is the one quoted in the `Result:` lines below, with the
first run's numbers named where they differ). Both runs were performed by an automated agent with
**no human eye and no ability to view a screenshot**, so "observed" below means *measured by a
read-only instrument on the same desktop session*, never "looked at". Every instrument and its
output is named in the `Result:` line:

- **UIAutomation** (`System.Windows.Automation`) — the notification area's accessibility tree. A
  registered tray icon appears there as a `SystemTray.NormalButton` whose accessible name is the
  tooltip text the shell stored.
- **Window topology** (`EnumWindows` + `GetWindowThreadProcessId` + `GetWindowLongPtrW`) — the
  sample process's top-level windows, their visibility and extended styles.
- **Pixel sampling** (`BitBlt` of the icon tile into a 32bpp top-down DIB section, read back with
  `GetDIBits`) — dominant-channel classification and a SHA-1 of the tile bytes, once per second,
  which is how the frame rotation was measured. No `System.Drawing` anywhere: it is not part of the
  shared framework this project targets.
- **`GetGuiResources`** — the GDI object count, calibrated in the same run with
  `CreateCompatibleDC`/`DeleteDC` (10 DCs ⇒ exactly `+10`, then back to `0`). The first run measured
  it in-process; the second run measured the **sample's own process** from the outside
  (`GetGuiResources(process.Handle, GR_GDIOBJECTS)`, once a second for 60 seconds), which is the
  stricter form: it can see a rasterizer the conversion leaves behind for the GC to collect.

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

Result: **PASS** (2026-09-20, second run; the first run measured the same). Second run, measured by
`.gsd/probe-live`: the sample was started as
`Trustsoft.NotifyIcon.Sample.exe --run-seconds 75` (pid 9492) and printed
`[sample] tray icon registered, rotating 3 frames every 1s.`,
`[sample] no window is shown - check the notification area, not the taskbar.`,
`[sample] will shut down by itself after 75s (graceful close check).` and, on the way out,
`[sample] tray icon disposed - it must have left the notification area.` — with **empty stderr**
and **exit code 0**, and no `[sample] startup failure:` or `[sample] TrayError` line anywhere. The
first run made four invocations (`--run-seconds 75/150/120/50`, 19:13–19:20) with those same lines,
the same empty stderr and the same exit code.

### 2. The icon appears with no window

Without doing anything else, look at the notification area (and its "show hidden icons" flyout).

Expected: a small icon is present — a blue disc at first, then a red square with a white band, then
a green triangle — and **no application window has appeared anywhere on the desktop**.

Result: **PASS for presence and for "no window"; the three shapes themselves NOT OBSERVED**
(2026-09-20, second run; the first run measured the same two things). The icon lives in the
collapsed tray's overflow: after the probe invoked the `Show Hidden Icons` chevron by name, and for
58 consecutive one-second samples, UIAutomation returned a `SystemTray.NormalButton` named exactly
`Trustsoft.NotifyIcon sample - the icon changes every second` at `1368,1042 60x60`, on-screen. The
tile's sampled pixels carry the three frames' colours (check 5 has the numbers); the *shapes*
(disc, square, triangle) were not checked, because no human eye and no image viewer were available
to this run. No window appeared: **0 of the sample's 7 top-level windows were visible** —
`TabletPenServiceHelperClass/WISPTIS`, two `MediaContextNotificationWindow`-class WPF wrappers, the
`Trustsoft.NotifyIcon.TrayMessageWindow` host, one more unnamed WPF wrapper and two `IME` windows,
all `visible=False`.

### 3. No taskbar button and no Alt-Tab entry

Click the taskbar once, then hold <kbd>Alt</kbd> and press <kbd>Tab</kbd> repeatedly.

Expected: the sample appears **neither** as a taskbar button **nor** in the Alt-Tab list, even though
its icon is in the notification area. (This is the `WS_EX_TOOLWINDOW` premise of D011; the automated
tests can only assert that style bit on the host window, not what the shell does with it.)

Result: **PASS** (2026-09-20, second run) — measured rather than key-pressed; the first run measured
the same condition with 10 buttons. The taskbar's own accessibility tree was enumerated while the
icon was registered: 19 buttons (`Start`, `Search`, and `Appid:`-carrying entries for File Explorer,
Yandex, Chrome, Rider, VS Code, Visual Studio, Bondly, Notepad3, Terminal, the tray chevron and the
clock area) and **none for the sample**. The host window measured `exstyle=0x00000180`
(`WS_EX_TOOLWINDOW` set, `WS_EX_WINDOWEDGE` added by WPF, `WS_EX_APPWINDOW` clear); it is the only
one of the process's 7 top-level windows with `WS_EX_TOOLWINDOW`, and **0** of them were visible —
so **0** were Alt-Tab-eligible (visible and not `WS_EX_TOOLWINDOW`). The Alt-Tab list itself was not
opened with the keyboard; its eligibility condition was measured instead.

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
the *rendering* of the bubble that remains unobserved. The second run did not re-attempt the hover
(no mouse input was injected), but it re-confirmed the input half from the other direction: the icon
was **located by that exact name** in the accessibility tree for 58 consecutive samples, which is the
shell publishing the tooltip string the library stored.

### 5. The icon changes about once per second

Watch the icon in the notification area for ten to fifteen seconds without touching anything.

Expected: the image changes visibly roughly once per second, cycling through the three frames
(blue disc → red square with a white band → green triangle → …) and never disappearing or turning
blank between changes. The terminal stays free of `[sample] TrayError` lines throughout.

Result: **PASS** (2026-09-20, second run; the first run measured the same cycle at 19:13:56–19:14:09
with the hashes `6AF619C89374` / `30CB71611CD7` / `C93BFAAB8266`). 58 consecutive samples of the
icon's 60x60 tile, one second apart, each classified by dominant colour and hashed:

```text
SAMPLE t=3s  401B62EB3182 blue=3024 green=0   red=384 white=192
SAMPLE t=4s  0EE5CE7DB7C4 blue=3229 green=361 red=0   white=0
SAMPLE t=5s  83F608CE0722 blue=3600 green=0   red=0   white=0
SAMPLE t=6s  401B62EB3182 … repeating exactly (401B / 0EE5 / 83F6 → …)
```

Exactly **three distinct SHA-1 values** across all 58 samples, distributed **20 / 19 / 19**, in a
strict three-second cycle (24+ consecutive cycles with no deviation), every sample carrying pixels
belonging to its own frame and nothing blank; the 192 white pixels are the white band of the red
frame. No `[sample] TrayError` line in the terminal during any run.

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

Result: **PASS** (2026-09-20, second run). The `--run-seconds 75` run (pid 9492) printed
`[sample] tray icon disposed - it must have left the notification area.` and exited with code 0.
Because the icon had been located and pixel-sampled for the preceding 58 seconds, this is not a
"never found in the first place" result: 1.5 s after the process ended the chevron was invoked again
and the UIAutomation scan found **0** elements matching the tooltip name, re-checked 1.2 s later
with the same result — which rules out the stale-leftover case that clears itself on hover. The
first run additionally left the flyout **open** across the exit as its control and got the same
answer. `NIM_DELETE` reached the shell.

## Two open risks from the research document, double-checked

Both were flagged as "assumed rather than verified"; here is the measurement for each.

1. **`GetAncestor(hwnd, GA_ROOT) == hwnd` (T05).** Re-run and quoted:
   `dotnet test … --filter "FullyQualifiedName~HostWindowTests"` →
   **`Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`** (474 ms, re-run after the
   conversion change), the class that contains `Host_is_a_top_level_window` (the `GA_ROOT` proof),
   `Host_is_not_visible` and `Host_has_toolwindow_extended_style_and_not_appwindow`.
2. **`WS_EX_TOOLWINDOW` preservation by `HwndSource`.** The T05 narrative recorded a measured
   `GWL_EXSTYLE = 0x180`; both live runs reproduced it **on the live sample process**. Second run:
   `hwnd=0x31047C class='HwndWrapper[Trustsoft.NotifyIcon.Sample;…]'
   title='Trustsoft.NotifyIcon.TrayMessageWindow' visible=False exstyle=0x00000180 toolwindow=True
   appwindow=False`; first run: `hwnd=0xC3073A … exstyle=0x00000180 TOOLWINDOW=True
   APPWINDOW=False`. The style bit *is* preserved (WPF adds `WS_EX_WINDOWEDGE`), so the "no taskbar
   button" claim has both its premise and its live observation: no taskbar button for the sample and
   0 Alt-Tab-eligible windows in check 3.

## GDI evidence, as measurements (R007)

Everything below is a measured number, not an adjective. The instrument was calibrated in the same
run: **10 × `CreateCompatibleDC` ⇒ `+10`, 10 × `DeleteDC` ⇒ `0`**.

### After the rasterization change (the numbers the demo now stands on)

| Scenario | Source kind | Replacements | GDI before | GDI after | Delta |
| --- | --- | --- | --- | --- | --- |
| Live sample, pid 9492, 60 one-second samples | frozen vector `DrawingImage`, 3 frames rotating | 57 (1/s) | 17 @ t=6s | 17 @ t=60s | **0** (every one of the 60 samples was 17) |
| Suite: `Repeated_replacement_of_frozen_vector_frames_leaves_the_GDI_count_flat` | frozen vector `DrawingImage`, 3 frames | 60 | measured | measured | **0** observed (assertion is `<= 2` growth) |
| Suite: `No_gdi_leak_across_50_icon_replacement_cycles` | frozen `BitmapSource` | 50 | 5 | 5 | **0** |
| Suite: `Real_shell_conversion_leaves_the_GDI_count_flat` | frozen `BitmapSource` | 20 real conversions | — | — | within `0..2` |
| Probe, public `TrayIcon` (first run) | frozen `BitmapSource` | 50 | 5 | 5 | **0** |

### Before it, for the same live scenario (the first run's measurement, F1)

| Scenario | Source kind | Replacements | GDI before | GDI after | Delta | Delta after full GC |
| --- | --- | --- | --- | --- | --- | --- |
| Live sample, PID 10936, run 2 | frozen vector `DrawingImage` | ~57 (1/s) | 123 @ 19:14:23 | 235 @ 19:15:20 | **+112** | not measured (live) |
| Probe, public `TrayIcon` | frozen vector `DrawingImage` | 50 | 7 | 109 | **+102** (2.04/replacement) | +10 |
| Probe, public `TrayIcon` | frozen vector `DrawingImage` | 200 | 17 | 413 | **+396** (1.98/replacement) | +4 |
| Isolation probe (WPF only) | `new RenderTargetBitmap(16,16,…)` | 50 | — | — | **+101** (2.02 each) | +29 |

The live series was measured on the **sample's own process handle** from outside the process
(`GetGuiResources(process.Handle, GR_GDIOBJECTS)`, once a second for 60 seconds) by
`.gsd/probe-live`. The first run's probe drove the **public** `TrayIcon` API (no seam, no fake) on
one STA thread with a pumping dispatcher, one replacement per dispatcher turn, exactly like the
sample's once-a-second rotation; its output is recorded in
`.gsd/exec/9571ea58-f346-42a4-bf16-9f4e3abd0940.stdout`.

**What this confirms:** the library's own `HICON`/`HBITMAP` ownership is exact, and after the
memoization so is the drawing path for repeated replacement. Fifty replacements with a **bitmap**
source move the process GDI count by **0**, matching the suite's
`No_gdi_leak_across_50_icon_replacement_cycles` (`Passed! 1 test`, assertion `after - before <= 2`,
identity counters: 52 icons created, 52 destroyed) and `Real_shell_conversion_leaves_the_GDI_count_flat`
(20 real conversions, `Assert.InRange(after - before, 0, 2)`), with the seam release counts
`DeletedObjects` = **2** on the success path (`Valid_Bgra32_source_produces_a_nonzero_icon_and_releases_both_bitmaps`),
**100** across 50 conversions, **2** on the `CreateIconIndirect` failure path, **1** on the
mask-failure path, **0** when nothing was allocated (colour-bitmap failure, unsupported source, null
input). The suite reports `Passed! 0 failed, 116 passed, 0 skipped` for the whole assembly and
`HiconFactoryTests` 14/14.

**Finding F1: what it was, and what was done about it.** The first run's measurement stands as
history: with a fresh rasterizer per replacement, a drawing source was **not** flat — and for the
slice demo, which rotates **vector** frames, that made the demo's own claim false. It was therefore
reported as a blocker rather than as a pass, and `README.md` was corrected to say so. The cause was
that every replacement built a new `RenderTargetBitmap`, each holding about two GDI objects until
the GC finalizes it, and **WPF leaves no reusable rasterizer**: `RenderTargetBitmap` is single-use
(a second `Render` throws `ArgumentException: The Image passed to the ImageVisualManager cannot be
frozen`, and `Clear()` does not lift it — measured on .NET 8), and `WriteableBitmap` has no
`Render(Visual)` member at all (measured). The repair is therefore to stop rasterizing: the
conversion memoizes the pixels of a **frozen** source per pixel size
(`HiconFactory.GetRasterizedPixels`), which is sound because a frozen `Freezable` cannot change; a
source that is still mutable is rasterized afresh on every call, so an edit to it can never be
masked by a cached earlier picture. The live table above is the result: **0** growth across 60
one-second replacements of the three frozen frames, against **+112** for the same scenario before.

**What is still not flat (residual).** Handing over a *different* previously unseen vector source on
every replacement — a new `DrawingImage` per change rather than a rotation between a fixed set —
still builds one rasterizer per source, costing the measured ~2 GDI objects per distinct image until
the GC finalizes it. That cost is proportional to the number of distinct images an application hands
over, not to the number of replacements, and it is the `+101 / 50` row above. No WPF API removes it,
so it is documented in `README.md` rather than presented as solved.

## Recording the result

Copy the six `Result:` values into the slice summary together with the date and the Windows build
number. A failed check is a failed check: report it, name the step, and attach the terminal output.
The sample writes runtime failures to `stderr` as `[sample] TrayError operation=...` and to the
`Trustsoft.NotifyIcon.Sample` trace source, so a failure usually has a second, machine-readable
record next to the on-screen symptom.

Recorded for S01/T10 on 2026-09-20 (Windows 11 Pro build 26200, .NET 8 SDK, x64): checks 1, 2, 3,
5, 6 = **PASS** (machine-measured; the frame *shapes* in check 2 were not visually verified), check
4 = **NOT OBSERVED** (no visible tooltip bubble could be detected; the tooltip text is confirmed to
be registered with the shell and the icon is located by it), plus finding **F1** in the GDI section
above — found in the first run, repaired by the rasterization memoization, with its residual case
named rather than hidden.
