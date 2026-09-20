# Trustsoft.NotifyIcon

A notification-area (system tray) icon for WPF applications that need **no window and no
`System.Windows.Forms`**.

The library talks to the shell through its own P/Invoke declarations: the verified
`NOTIFYICONDATAW` layout, `Shell_NotifyIconW`, and a hidden top-level `HwndSource` host window. The
shipped assembly has **zero package references** — no `System.Windows.Forms`, no
`System.Drawing.Common`, no tray-helper package — and it targets `net8.0-windows`,
`net9.0-windows` and `net10.0-windows`.

## Status

**Early development. Only the core icon lifecycle of the first slice is implemented.**

Working today:

- `TrayIcon`, a `FrameworkElement` that registers an icon (`NIM_ADD` followed by
  `NIM_SETVERSION(NOTIFYICON_VERSION_4)`), shows a tooltip, replaces the displayed image, removes
  the icon and disposes cleanly.
- `TrayIcon.IconSource` accepts any WPF `ImageSource` (bitmap or vector drawing) and converts it to
  an `HICON` with strict GDI handle ownership, so a long-running process does not leak handles.
- `Visible` and the other properties may be set from any thread; the work is marshalled to the
  owning dispatcher.
- Failures are named: `TrayIconException` carries an `Operation` and a `Win32ErrorCode`, and runtime
  failures are raised through the bubbling `TrayError` routed event after one retry.
- Target frameworks: `net8.0-windows`, `net9.0-windows`, `net10.0-windows`. Windows only.

**Planned, not implemented** — nothing below exists yet, so do not code against it:

- click and mouse events on the icon, and a context menu (S02, S04);
- balloon notifications and icon/tooltip sizing for DPI and shell settings (S03);
- explorer-restart recovery and the process-exit fallback (S05);
- XAML usage, `NotifyIcon`-style markup support (S06);
- NuGet packaging, licence metadata and the CI release pipeline (S07).

## Try it

A headless sample application (no window at all) registers a real icon and rotates it once per
second between three generated vector images:

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release
```

Add `-- --run-seconds 20` to let it exit by itself after 20 seconds — useful for watching a graceful
shutdown remove the icon.

Sample behaviour, including what it writes to the console when the shell refuses the registration, is
described in [`docs/UAT-S01.md`](docs/UAT-S01.md).

## Build and test

```text
dotnet build Trustsoft.NotifyIcon.sln -c Release
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows
```

The test suite asserts the struct layout against the Windows SDK header, the exact shell call
sequence, GDI handle counts across repeated icon replacement, the failure policies and the purity of
the public surface (`tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs`).

## Live checks CI cannot perform

GitHub Actions Windows runners have no interactive desktop session, so the checks that need a real
notification area — the icon appearing at all, the alert area showing it, the tooltip on hover — are
a manual checklist: [`docs/UAT-S01.md`](docs/UAT-S01.md). They are **not** automated coverage and are
not reported as such.

## Licence

MIT — see [`LICENSE`](LICENSE).
