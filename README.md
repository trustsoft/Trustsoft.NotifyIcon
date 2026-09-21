# Trustsoft.NotifyIcon

A notification-area (system tray) icon for WPF applications that need **no window and no
`System.Windows.Forms`**.

The library talks to the shell through its own P/Invoke declarations: the verified
`NOTIFYICONDATAW` layout, `Shell_NotifyIconW`, and a hidden top-level `HwndSource` host window. The
shipped assembly has **zero package references** — no `System.Windows.Forms`, no
`System.Drawing.Common`, no tray-helper package — and it targets `net8.0-windows`,
`net9.0-windows` and `net10.0-windows`.

## Status

**Early development. The core tray-icon surface (S01-S05) and the declarative XAML path (S06) are
implemented; NuGet packaging and the release pipeline are not (S07).**

Working today:

- `TrayIcon`, a `FrameworkElement` that registers an icon (`NIM_ADD` followed by
  `NIM_SETVERSION(NOTIFYICON_VERSION_4)`), shows a tooltip, replaces the displayed image, removes
  the icon and disposes cleanly.
- Click and mouse events on the icon: four click types, each with a cancellable `Preview` twin.
- `TrayIcon.ContextMenu`, the consumer's own menu, opened at the icon by a right click and dismissed
  by an outside click — from a process that owns no visible window, because the popup is owned by a
  1×1 activating anchor window placed at the icon. Placement reads the shell's own icon rectangle
  and the monitor's effective DPI, so the physical-to-offset scale factor is applied exactly once.
- `TrayIcon.ShowBalloonTip(title, text, icon, options)` shows a legacy `Shell_NotifyIcon` balloon
  with a chosen severity (`BalloonTipIcon`) and behaviour switches (`BalloonTipOptions`: `NoSound`,
  `RespectQuietTime`, `Realtime`), and clicking the balloon raises the cancellable
  `PreviewBalloonTipClicked`/`BalloonTipClicked` routed pair. Overlong text is truncated to the
  shell's field capacities, never rejected. See
  [the live balloon checks](docs/UAT-S04.md).
- `TrayIcon.IconSource` accepts any WPF `ImageSource` (bitmap or vector drawing) and converts it to
  an `HICON` with strict GDI handle ownership. **Repeated replacement is flat for a bitmap source**
  (measured: 0 GDI objects across 50 replacements) **and for a frozen vector source**: a frozen
  drawing is rasterized once per pixel size and its pixels are then reused, so the sample's
  once-a-second rotation of three frames leaves the process GDI count unchanged (measured from
  outside the process: 17 objects at t=6s and still 17 at t=60s; before this change the same run
  climbed 123 → 235 in 57s). A source that is still mutable is read afresh on every replacement, so
  an edit to it is never masked by a cached earlier picture. See
  [the GDI measurements](docs/UAT-S01.md#gdi-evidence-as-measurements-r007).
- `Visible` and the other properties may be set from any thread; the work is marshalled to the
  owning dispatcher.
- Failures are named: `TrayIconException` carries an `Operation` and a `Win32ErrorCode`, and runtime
  failures are raised through the bubbling `TrayError` routed event after one retry.
- Explorer-restart recovery: after Explorer restarts the icon re-registers itself with no application
  action, because the `TaskbarCreated` broadcast re-issues `NIM_ADD` plus `NIM_SETVERSION(4)` with the
  handle the icon already owns. Teardown needs no code at all — the registration is bound to the host
  window, so a process that dies without `Dispose` leaves nothing behind. Both are measured live in
  [`docs/UAT-S05.md`](docs/UAT-S05.md).
- Declarative use: the icon, its image, its menu and its event handlers can be declared in markup and
  resolved from `Application.Resources`, using the `http://schemas.trustsoft.com/notifyicon` namespace
  (prefix `tni`) this assembly declares. Run the sample with `-- --xaml --run-seconds 20` to see the
  same behaviour reached without a line of property or event wiring in C#; the boundary — event
  attributes in a resource are wired by the compiled XAML, not by a runtime parse — is pinned in
  `tests/Trustsoft.NotifyIcon.Tests/TrayIconXamlContractTests.cs`.
- Target frameworks: `net8.0-windows`, `net9.0-windows`, `net10.0-windows`. Windows only.

**Known limitation ([F1](docs/UAT-S01.md#gdi-evidence-as-measurements-r007), measured 2026-09-20):**
a *different*, previously unseen vector source on every replacement — a new `DrawingImage` per change
rather than a rotation between a fixed set — still builds one WPF rasterizer per source, costing
about two GDI objects per distinct image until the GC finalizes it (measured: +101 across 50 fresh
rasterizers; before the fix that cost was paid once per *replacement* instead, which is what the
first live run caught: 123 → 235 objects in 57 seconds). The cost is proportional to the number of
distinct images an application hands over, not to the number of replacements. No WPF API removes it:
`RenderTargetBitmap` is single-use — a second `Render` throws and `Clear()` does not lift it — and
`WriteableBitmap` has no `Render(Visual)` member at all.

**Planned, not implemented** — nothing below exists yet, so do not code against it:

- click and mouse events on the icon are implemented (S02); the `ContextMenu` a right click opens
  at the icon is implemented (S03) — assign one and it opens, or set `MenuActivation="None"` to keep
  the right click a pure event; balloon notifications are implemented (S04) — `ShowBalloonTip` and
  the balloon click routed pair exist, recorded in
  [`docs/UAT-S04.md`](docs/UAT-S04.md);
- icon and tooltip sizing for DPI and shell settings — explicitly **not delivered in S03**: the menu
  placement reads the monitor's DPI, but the `HICON` is still rasterized at a fixed 16 px, so a
  display at a scale above 100 % gets a correctly placed menu with a scaled-up icon. That is the
  named follow-up the milestone roadmap carries, not a silently dropped clause;
- explorer-restart recovery and undisposed termination are implemented (S05) — the live evidence is
  [`docs/UAT-S05.md`](docs/UAT-S05.md). There is deliberately **no** process-exit fallback: a forced
  kill runs no managed code, so a finalizer or `ProcessExit` handler could not run in the case it
  would have to cover, and the host-window mechanism is the guarantee instead;
- declarative XAML usage is implemented (S06) — see the bullet above. What is **not** delivered is
  `NotifyIcon`-style markup *convenience*: there are no balloon dependency properties (a balloon stays
  a method call by decision), and the declarative path uses the library's `TrayIcon` rather than a
  sample-specific subclass, because an `ApplicationDefinition` cannot resolve a type from its own
  project (measured: the markup compiler reports `MC3074`);
- NuGet packaging, licence metadata and the CI release pipeline (S07).

## Try it

A headless sample application (no window at all) registers a real icon and rotates it once per
second between three generated vector images:

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release
```

Add `-- --run-seconds 20` to let it exit by itself after 20 seconds — useful for watching a graceful
shutdown remove the icon. Add `-- --xaml` to run the same application from the declaration in
`App.xaml` instead of the C# construction path; both modes print the same lines, which is what makes
a diff between two captures meaningful.

Sample behaviour, including what it writes to the console when the shell refuses the registration, is
described in [`docs/UAT-S01.md`](docs/UAT-S01.md); the balloon demonstration and its switches are
recorded in [`docs/UAT-S04.md`](docs/UAT-S04.md).

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
a manual checklist: [`docs/UAT-S01.md`](docs/UAT-S01.md), extended per slice since: click delivery
([`docs/UAT-S02.md`](docs/UAT-S02.md)), menu placement ([`docs/UAT-S03.md`](docs/UAT-S03.md)),
balloon notifications ([`docs/UAT-S04.md`](docs/UAT-S04.md)) and continuity/teardown
([`docs/UAT-S05.md`](docs/UAT-S05.md)). They are **not** automated coverage and
are not reported as such.

## Licence

MIT — see [`LICENSE`](LICENSE).
