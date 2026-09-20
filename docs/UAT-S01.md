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

Result: ______

### 2. The icon appears with no window

Without doing anything else, look at the notification area (and its "show hidden icons" flyout).

Expected: a small icon is present — a blue disc at first, then a red square with a white band, then
a green triangle — and **no application window has appeared anywhere on the desktop**.

Result: ______

### 3. No taskbar button and no Alt-Tab entry

Click the taskbar once, then hold <kbd>Alt</kbd> and press <kbd>Tab</kbd> repeatedly.

Expected: the sample appears **neither** as a taskbar button **nor** in the Alt-Tab list, even though
its icon is in the notification area. (This is the `WS_EX_TOOLWINDOW` premise of D011; the automated
tests can only assert that style bit on the host window, not what the shell does with it.)

Result: ______

### 4. The tooltip shows the tooltip text

Hover the mouse pointer over the sample's icon and hold it still for about a second.

Expected: the shell shows a tooltip reading
`Trustsoft.NotifyIcon sample - the icon changes every second`.
(This is the live proof of the `NIF_SHOWTIP` flag; there is no automated equivalent.)

Result: ______

### 5. The icon changes about once per second

Watch the icon in the notification area for ten to fifteen seconds without touching anything.

Expected: the image changes visibly roughly once per second, cycling through the three frames
(blue disc → red square with a white band → green triangle → …) and never disappearing or turning
blank between changes. The terminal stays free of `[sample] TrayError` lines throughout.

Result: ______

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

Result: ______

## Recording the result

Copy the six `Result:` values into the slice summary together with the date and the Windows build
number. A failed check is a failed check: report it, name the step, and attach the terminal output.
The sample writes runtime failures to `stderr` as `[sample] TrayError operation=...` and to the
`Trustsoft.NotifyIcon.Sample` trace source, so a failure usually has a second, machine-readable
record next to the on-screen symptom.
