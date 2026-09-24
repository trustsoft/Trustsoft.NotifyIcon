# Trustsoft.NotifyIcon

A notification-area (system tray) icon for WPF applications that need **no window** and **no
`System.Windows.Forms`** anywhere in the dependency graph.

The library talks to the shell through its own P/Invoke declarations — the verified
`NOTIFYICONDATAW` layout, `Shell_NotifyIconW`, and a hidden top-level `HwndSource` host window — so
the shipped assembly carries **zero package references** and the package declares **no runtime
dependency beyond WPF**. It targets `net8.0-windows`, `net9.0-windows` and `net10.0-windows`.
**Windows only**: the notification area is a Windows shell feature, and the package declares
`net*-windows` for that reason rather than by accident.

## Install

```xml
<PackageReference Include="Trustsoft.NotifyIcon" Version="1.0.0-preview.2" />
```

`1.0.0-preview.2` is the **tray-only pre-release**: it carries everything in this document, and the
toast subsystem is not in it yet. The number is a SemVer pre-release of the v1 surface (D010, D037),
so a plain `dotnet add package Trustsoft.NotifyIcon` will not select it — ask for it by version, or
pass `--prerelease`. The final `1.0.0` is reserved for the release that carries the tray icon and the
toasts in the same package (D072).

`1.0.0-preview.2` supersedes `1.0.0-preview.1`, and the only defect it corrects is in this document:
the earlier pre-release shipped while this repository had no remote and said so, and the repository
now has one, which made that sentence false for the package you are holding. No code, no test and no
guard changed; the earlier number keeps its own tag and its own artifact rather than being reissued
with different content under the same number (D074).

It is **not published to any package feed** — the source lives in a git remote and is pushed there,
which is not the same act as publishing a package, and no NuGet feed has ever received one. The
package is produced by `dotnet pack` into this repository's `artifacts/` folder, and installing it
into a fresh windowless WPF project from that folder feed is what the packaging evidence measures
(`docs/UAT-S07.md`).

The package carries, beside each framework's assembly, three things a consumer should not have to
fetch separately: the **XML documentation** file (`Trustsoft.NotifyIcon.xml`), this **README** and
the **MIT licence** text. Installing it requires no additional setup: no WinForms, no
`System.Drawing.Common`, no tray-helper package and no WinRT contracts package (R011).

## Quick start — code first

A windowless application is the case this library exists for. This is the whole shape — construct
the icon on the UI thread, subscribe to what you care about, assign your own menu, give it an image
(any `ImageSource`: bitmap or vector), and register it by setting `Visible`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Trustsoft.NotifyIcon;

// On the UI thread (the thread that owns the Dispatcher), e.g. in Application.OnStartup.
var icon = new TrayIcon { ToolTipText = "My application" };

icon.TrayLeftClick += (_, e) => Console.WriteLine($"left click at {e.ScreenAnchor} (count={e.ClickCount})");
icon.TrayRightClick += (_, e) => Console.WriteLine($"right click at {e.ScreenAnchor}");
icon.TrayError += (_, e) => Console.Error.WriteLine($"{e.Operation}: win32 {e.Win32ErrorCode}");

// The menu is your own instance. The library opens it at the icon and never clones it,
// so its DataContext is yours to set (a ContextMenu has no logical parent of its own).
var menu = new ContextMenu { DataContext = myViewModel };
menu.Items.Add(new MenuItem { Header = "Open" });
menu.Items.Add(new MenuItem { Header = new Binding("Status") });
icon.ContextMenu = menu;

// Any ImageSource. A frozen DrawingImage needs no image file and no decoder.
var image = new DrawingImage(new GeometryDrawing(
    new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0xA0)), null, new EllipseGeometry(new Point(8, 8), 7, 7)));
image.Freeze();
icon.IconSource = image;

icon.Visible = true;   // this is what registers the icon with the shell
icon.ShowBalloonTip("Ready", "My application is running.", BalloonTipIcon.Info);
```

The whole public surface is **twenty types**. Seven of them are the tray subsystem: `TrayIcon`,
`TrayIconException`, `TrayErrorEventArgs`, `TrayIconClickEventArgs`, `TrayMenuActivation` and the two
balloon enums `BalloonTipIcon` and `BalloonTipOptions`. The other thirteen are the toast subsystem
documented under [Toast notifications](#toast-notifications): the entry point `ToastNotifier`, its
failure type `ToastException`, the content model `ToastContent` with `ToastButton`, `ToastImage` and
the three vocabularies `ToastSeverity`, `ToastSound` and `ToastImagePlacement`, the three
event/argument types `ToastActivatedEventArgs`, `ToastDismissedEventArgs` and `ToastErrorEventArgs`
together with the `ToastDismissalReason` vocabulary, and the outcome vocabulary
`ToastNotificationSetting`. That list is asserted **four times over** — from inside the library
(`tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs`), from a project that only ever installed
the nupkg (`samples/consumer-proof/`), from the package inspector's artifact rule that reads the
exported surface out of the packed XML documentation, and by the documentation guards that require
every public member of all twenty types to be documented in the shipped XML file — so a widened,
undocumented or renamed surface fails on all of them.

## Declarative usage

The same element declared in markup comes from the library's own XML namespace,
`http://schemas.trustsoft.com/notifyicon` (prefix `tni`), which the assembly declares with
`XmlnsDefinition`/`XmlnsPrefix` so markup never has to name the CLR namespace and assembly by hand:

```xml
<Application x:Class="MyApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:MyApp"
             xmlns:tni="http://schemas.trustsoft.com/notifyicon"
             ShutdownMode="OnExplicitShutdown">
  <Application.Resources>
    <!-- Declared before the icon: StaticResource resolves in document order. -->
    <DrawingImage x:Key="TrayImage">
      <DrawingImage.Drawing>
        <GeometryDrawing Brush="#FF3060A0">
          <EllipseGeometry Center="8,8" RadiusX="7" RadiusY="7" />
        </GeometryDrawing>
      </DrawingImage.Drawing>
    </DrawingImage>

    <!-- Your own data object, for the menu item that binds below. -->
    <local:MyMenuData x:Key="TrayMenuData" Status="ready" />

    <ContextMenu x:Key="TrayMenu"
                 DataContext="{StaticResource TrayMenuData}"
                 Opened="OnMenuOpened"
                 Closed="OnMenuClosed">
      <MenuItem Header="Open" Click="OnOpen" />
      <MenuItem Header="{Binding Status}" />
    </ContextMenu>

    <tni:TrayIcon x:Key="TrayIcon"
                  Visible="True"
                  ToolTipText="My application"
                  MenuActivation="RightClick"
                  IconSource="{StaticResource TrayImage}"
                  ContextMenu="{StaticResource TrayMenu}"
                  TrayRightClick="OnTrayClick"
                  TrayError="OnTrayError" />
  </Application.Resources>
</Application>
```

Three things worth knowing before you copy it:

- **Event attributes are validated at build time.** The markup compiler resolves every handler name
  and signature when it compiles the `ApplicationDefinition`, so a misspelled handler is a build
  error rather than a silently dead event.
- **The declaration is inert until looked up.** BAML defers creation of a resource until the first
  lookup, so declaring the icon does not register anything by itself; a code-first application that
  never reads `Application.Current.Resources["TrayIcon"]` never instantiates one (cross-checked live
  in `docs/UAT-S06.md` rather than assumed).
- **`x:Shared` stays at its default.** One key, one instance, one icon; `x:Shared="False"` would mint
  a new icon on every lookup.

The in-repo sample runs both paths and prints the same lines either way, which is what makes a diff
between two captures meaningful:

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 20
```

## Windowless shutdown

WPF ends an application when its last window closes, so an application that owns **no** window must
say so explicitly — otherwise there is nothing to keep the message loop alive:

```xml
<Application ShutdownMode="OnExplicitShutdown">   <!-- and no StartupUri -->
```

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Dispose on the way out: this sends NIM_DELETE and destroys the icon's handle.
    SessionEnding += (_, _) => _trayIcon?.Dispose();   // logoff / shutdown
    Exit += (_, _) => _trayIcon?.Dispose();            // Shutdown() or the end of Run()
}
```

What that buys, and what it does not:

- **`Dispose()` removes the icon** through `NIM_DELETE` and destroys the `HICON`. Calling it twice
  is safe.
- **A process that dies without `Dispose` still leaves nothing behind.** The registration is bound
  to the library's host window, so a hard kill takes the icon with it — there is deliberately no
  process-exit fallback, because a forced kill runs no managed code (measured, `docs/UAT-S05.md`).
- **Explorer restart needs no application code.** The library listens for the shell's
  `TaskbarCreated` broadcast and re-issues `NIM_ADD` with the handle the icon already owns (measured,
  `docs/UAT-S05.md`).
- **A refused registration is named, not swallowed.** With no interactive session the shell rejects
  the icon and the library throws `TrayIconException`, which carries `Operation` and
  `Win32ErrorCode`. Catch it at startup: a `WinExe` that does not would die in an invisible unhandled
  exception, which is exactly how "the icon never appeared" turns into a mystery. Runtime failures on
  the live icon are reported through the bubbling `TrayError` routed event after one retry.

## Interaction model

What the type name does not tell you, in one place:

- **A right click opens the assigned `ContextMenu` at the icon.** That is the default
  (`MenuActivation="RightClick"`, the value `TrayMenuActivation.RightClick`); set
  `MenuActivation="None"` to keep the right click a pure event and open the menu yourself. The menu
  opens from a process that owns no visible window: it is a real WPF popup owned by a 1×1 activating
  anchor window placed at the icon, using the shell's own icon rectangle and the monitor's effective
  DPI (`docs/UAT-S03.md`).
- **The four click types are routed events with `Preview` counterparts**: `TrayLeftClick`,
  `TrayLeftDoubleClick`, `TrayRightClick`, `TrayMiddleClick`, and `PreviewTrayLeftClick`,
  `PreviewTrayLeftDoubleClick`, `PreviewTrayRightClick`, `PreviewTrayMiddleClick`. A `Preview`
  handler that sets `Handled` cancels the matching main event — and on a right click also stops the
  menu from opening, so a consumer keeps full control (`docs/UAT-S02.md`).
- **The menu is your instance and is never cloned.** The library sets the popup's `PlacementTarget`
  to its own anchor window, so the icon's `DataContext` never reaches the menu and **the library
  never writes a menu's `DataContext`**: if your menu items bind, set the menu's `DataContext`
  yourself (this is the S06 clarification, `docs/UAT-S06.md`).
- **A balloon is a method call, not a property.** `ShowBalloonTip(title, text, icon, options)` with
  `BalloonTipIcon` (`None`, `Info`, `Warning`, `Error`) and `BalloonTipOptions`
  (`NoSound`, `RespectQuietTime`, `Realtime`) — there are no balloon dependency properties by
  decision (D031). It requires a registered icon and non-empty text, and it truncates overlong text
  to the shell's field capacities rather than rejecting it. Clicking the balloon raises the
  cancellable `PreviewBalloonTipClicked`/`BalloonTipClicked` routed pair (`docs/UAT-S04.md`).
- **Property setters are marshalled.** `Visible`, `IconSource`, `ToolTipText`, `ContextMenu` and
  `MenuActivation` may be set from any thread; the shell work is moved to the dispatcher that owns
  the host window.

## Toast notifications

The package ships a real Windows toast subsystem beside the tray icon: the same assembly, the same
zero-dependency posture, no WinRT contracts package and no separate registration tool. Install it as
the [Install](#install) section shows — the toast subsystem needs nothing beyond the package itself —
then show a toast, which is a `ToastContent` object shown through a `ToastNotifier`:

```csharp
using Trustsoft.NotifyIcon;

// On the UI thread. The identity is registered on the first Show; the id may be overridden
// until that first show, and a null id derives it from the entry assembly's simple name.
using var toasts = new ToastNotifier { AppUserModelId = "MyVendor.MyApp" };

toasts.Activated += (_, e) => Console.WriteLine($"activated: {e.Arguments}");
toasts.Dismissed += (_, e) => Console.WriteLine($"dismissed: {e.Reason}");
toasts.ToastError += (_, e) => Console.Error.WriteLine($"{e.Operation}: 0x{e.ErrorCode:X8}");

var content = new ToastContent { Title = "Backup finished", Body = "12 files archived." };
content.Buttons.Add(new ToastButton { Text = "Open folder", Arguments = "open" });
content.Buttons.Add(new ToastButton { Text = "Dismiss", Arguments = "dismiss" });

toasts.Show(content);
```

Three things the sample does not show, each of which a consumer has to know:

- **The identity lifecycle.** The first `Show` registers the AppUserModelID by writing a Start-menu
  shortcut that carries it — the shell delivers a toast only to a registered identity — reads the
  value back through a fresh link, and only then shows. `AppUserModelId` is settable **until that
  first show**, and a `null` id derives the default from the entry assembly's simple name; a later
  set throws `InvalidOperationException` rather than silently misrouting. `Dispose` removes every
  live show and then the shortcut it created, so the next launch starts from an unregistered
  identity; a removal failure is traced and ignored rather than thrown from a process on its way out.
- **What an activation means.** Toasts and balloons are independent: showing a toast never
  suppresses, replaces or re-routes a balloon tip, and showing a balloon tip never replaces or
  re-routes a toast. Activated reports that the toast's launch or button argument arrived; it is
  not a report that the user clicked the body. The activation is delivered to the running
  application only — a toast clicked after the application has exited is out of scope for v1 (D054)
  — and its `Arguments` is the pressed button's `ToastButton.Arguments`, or the `ToastContent.Launch`
  string for a body click.
- **The image temp-file lifetime.** A `ToastImage.Source` is persisted per show as a PNG at
  `Path.GetTempPath()/Trustsoft.NotifyIcon/toast-<guid:N>.png`, handed to the shell as an absolute
  `file:///` reference, and deleted in the same show's unwind path. A process killed before teardown
  can therefore leave one orphan file, and an Action Center entry whose file is already gone renders
  without its image. A pre-formed `ToastImage.Reference` is never a library-owned file and is never
  deleted.

### The toast failure surface

- **A registration failure throws `ToastException`.** If the identity cannot be established, the
  first `Show` throws rather than dropping the toast silently, because an unpackaged process has no
  other way to learn that nothing will be delivered. The exception carries `Operation` (a stable
  string, or the name of the failing call) and `ErrorCode`; catch it at startup.
- **A runtime delivery failure is not fatal.** Once the identity is registered, a show Windows could
  not deliver is reported through `ToastError` plus exactly one Error-level line on the
  `Trustsoft.NotifyIcon` `TraceSource` — visible at its default `Warning` level — and `Show` returns
  normally. The `ErrorCode` is the raw `HRESULT` (for example `WPN_E_NOTIFICATION_DISABLED`).
- **`ToastNotifier.NotificationSetting` is an outcome, not a failure.** It reports the platform's
  notification setting read for the identity during the last show: `null` means "not read" (no show
  has reached the setting step, or the show failed earlier) and never "Enabled"; a non-`Enabled`
  value is a routine OS state that is never raised on `ToastError` and never turns a `Show` into a
  failure. Report the setting to explain yourself, and let the shell's own `Failed` callback be the
  failure.

## Declarative traps the slices measured

Two traps that cost time when they are hit, both recorded with their measurements in
`docs/UAT-S06.md`:

- **A type from your own project must be declared with an unqualified `clr-namespace`.** Inside an
  `ApplicationDefinition`, `xmlns:local="clr-namespace:MyApp"` resolves, while
  `clr-namespace:MyApp;assembly=MyApp` fails the markup compiler with **MC3074**, because the
  assembly being compiled does not exist yet. The assembly qualifier is required only when the type
  comes from another assembly — and for this library you should not need it at all: use the
  `http://schemas.trustsoft.com/notifyicon` namespace instead.
- **Event attributes in a resource dictionary are compiled-XAML only.** `XamlReader` binds handler
  names against the parsed root object, so a dictionary-rooted element carrying an event attribute
  cannot be parsed at runtime at all; it is the BAML the compiler produces that wires it. Pinned as a
  boundary in `tests/Trustsoft.NotifyIcon.Tests/TrayIconXamlContractTests.cs`.

## What this package deliberately is not

- **No `System.Windows.Forms`, no `System.Drawing.Common`, no `H.NotifyIcon`, no WinRT contracts
  package** (R011). The shell conversation is the library's own P/Invoke.
- **Balloons and toasts are two separate subsystems, both shipped in v1.** The package carries the
  toast subsystem (see [Toast notifications](#toast-notifications)) in the same assembly and the same
  zero-dependency posture as the tray icon. Balloons use the legacy `Shell_NotifyIcon` mechanism and
  toasts use the WinRT notification stack with its own registration contract (D005); neither switches
  to the other, and showing one never suppresses, replaces or re-routes the other.
- **No balloon dependency properties** (D031) and no `NotifyIcon`-style markup convenience layer.
- **No DPI-driven icon resizing yet.** Menu placement reads the monitor's effective DPI, but the
  `HICON` is still rasterized at a fixed 16 px, so a display above 100 % scale gets a correctly
  placed menu with a scaled-up icon. This is a named follow-up in the milestone roadmap
  (`docs/UAT-S03.md`), not a silently dropped clause.
- **No consumer-visible *verbose* trace log in v1.** The library's diagnostics go through
  `System.Diagnostics.TraceSource` on the `Trustsoft.NotifyIcon` source, which defaults to
  `Warning`; a default-configured application that only references the package receives no *step*
  trace lines on net8 (measured: zero lines received, `docs/UAT-S02.md`), and in-repo tests hear them
  only because they attach listeners in-process. Failures are visible at the default level: a toast
  delivery failure writes exactly one Error-level line, and the consumer-reachable failure signals
  are the `TrayError` routed event and the `ToastError` event, each carrying the operation constant
  and the error code. Raise the source to `Verbose` to see the per-step traffic (the notification
  setting, image precedence, dispose accounting).
- **Display-scale and balloon-OS-behaviour evidence in v1 is fixture-first, by recorded decision.**
  Menu placement is proven by 82 headless fixtures (100/125/150/175/200 %, negative-origin and
  mixed-DPI monitor pairs) plus one live session at 150 % (`docs/UAT-S03.md`); live observation at
  the remaining scale settings and on two monitors with different scale factors is **deferred to
  the follow-up milestone**, not claimed. Likewise, balloon quiet-time suppression and
  realtime-discard semantics are pinned at the shell seam and accepted live (the shell confirms
  each configuration), while their OS-side behaviour rests on the documented shell contract until
  the deferred live pass.
- **No second shell protocol.** The library registers with `NOTIFYICON_VERSION_4` and no other.
- **No `RepositoryUrl` or `PackageProjectUrl` in the metadata.** The source remote is a git host
  rather than a package feed, and no public project page exists; a link in package metadata cannot be
  corrected after publication, so the omission is deliberate and asserted by `PackagePurityTests`.

### One measured cost, so it does not surprise you

`IconSource` accepts any `ImageSource` and converts it to an `HICON` with strict GDI ownership.
Replacing the image repeatedly is flat for a bitmap source (measured: 0 GDI objects across 50
replacements) and for a **frozen** vector source, which is rasterized once per pixel size and reuses
those pixels (measured from outside the process: 17 GDI objects at t=6 s and still 17 at t=60 s,
where before that change the same run climbed 123 → 235 in 57 s). A source that is still **mutable**
is read afresh on every replacement, so an edit to it is never masked by a cached picture. The cost
that remains is the one you choose: handing over a **different, previously unseen vector source** on
every change builds one WPF rasterizer per distinct image, about two GDI objects each until the GC
finalizes it (measured: +101 across 50 fresh rasterizers). The cost is proportional to the number of
distinct images you hand over, not to the number of replacements, and no WPF API removes it
(`RenderTargetBitmap` is single-use and `WriteableBitmap` has no `Render(Visual)`).
[The measurements](docs/UAT-S01.md#gdi-evidence-as-measurements-r007).

---

## Repository notes

Everything below is about building and checking this repository, not about using the package.

### Build, test and pack

```text
dotnet build Trustsoft.NotifyIcon.sln -c Release
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows
dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
```

The last two lines are the packaging path: the pack writes the nupkg into `artifacts/`, and
`scripts/verify-package.sh` inspects the packed file and asserts its invariants one at a time — the
nuspec identity against the version this project declares, no `<dependency>` entry in any
target-framework group, the three framework folders each carrying the assembly and its XML
documentation, the README and the licence at the package root, and no entry from the sample, the
tests, the probe or the consumer proof. It prints one `PASS`/`FAIL` line per assertion, exits
non-zero and quotes the offending entry when one is broken, and needs only `unzip`: it never calls
the SDK, so the artifact check still runs where `dotnet build` does not. Raw output, and the same
script failing on three deliberately broken packages, is in [`docs/UAT-S07.md`](docs/UAT-S07.md).

Two traps measured in a freshly re-materialized worktree, both in `docs/UAT-S07.md`: `dotnet test`
prints **nothing at all** and exits `0` while the test project has never been built there (build it
first, and treat an empty test log as "nothing ran", not as success), and an agent shell can arrive
without the Windows known-folder variables, which makes the SDK fail inside NuGet's restore-graph
evaluation with `Value cannot be null. (Parameter 'path1')`. `scripts/verify-package.sh` is
unaffected by the second.

The test suite asserts the struct layout against the Windows SDK header, the exact shell call
sequence, GDI handle counts across repeated icon replacement, the failure policies, the purity of
the public surface and the packaging metadata
(`tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs`). Five menu/popup tests in four classes
are **desktop-foreground-sensitive** and can fail when Windows denies the foreground to the test
process — [`docs/TEST-ENVIRONMENT.md`](docs/TEST-ENVIRONMENT.md) documents the mechanism, the
exactly-affected tests and the acceptance decision before you distrust a red run.

### The headless sample and the live probe

A sample application (no window at all) registers a real icon and rotates it once per second between
three generated vector images:

```text
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release
```

Add `-- --run-seconds 20` to let it exit by itself after 20 seconds — useful for watching a graceful
shutdown remove the icon. Add `-- --xaml` to run the same application from the declaration in
`App.xaml` instead of the C# construction path; both modes print the same lines, which is what makes
a diff between two captures meaningful. Add `-- --open-menu-after 5` to have the menu open and close
itself five seconds in without any shell click, in either mode; the declarative path's live proof is
[`docs/UAT-S06.md`](docs/UAT-S06.md), where that switch is what shows the *declared* menu opening at
the icon.

`scripts/probe-live` is the instrument those checks are watched with: it starts the target, finds
the icon in the notification area from outside the process, samples presence and the process's GDI
count once a second, and reports a verdict after the process exits. It exits non-zero when the icon
was never observed present, so an absence cannot be read as a pass. Its P/Invoke declarations,
structure layout and identifier are its own, so a bug in the library cannot make the probe agree with
it.

```text
dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release -f net8.0-windows
dotnet run --project scripts/probe-live -c Release -- samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 12 --run-seconds 8
```

### Live checks CI cannot perform

GitHub Actions Windows runners have no interactive desktop session, so the checks that need a real
notification area — the icon appearing at all, the alert area showing it, the tooltip on hover, the
menu at the icon, a balloon, click delivery, teardown — are a manual checklist, one document per
slice: [`docs/UAT-S01.md`](docs/UAT-S01.md) (icon, GDI, DPI),
[`docs/UAT-S02.md`](docs/UAT-S02.md) (click delivery),
[`docs/UAT-S03.md`](docs/UAT-S03.md) (menu placement),
[`docs/UAT-S04.md`](docs/UAT-S04.md) (balloons),
[`docs/UAT-S05.md`](docs/UAT-S05.md) (explorer restart, teardown),
[`docs/UAT-S06.md`](docs/UAT-S06.md) (declarative path) and
[`docs/UAT-S07.md`](docs/UAT-S07.md) (packaging and the consumer proof). They are **not** automated
coverage and are not reported as such.

Raw per-run logs and their producers live under `docs/uat-logs/<slice>/` for the slices that kept
them (S05 onward), so a claim in a checklist document can be traced back to the command that
produced it.

## Licence

MIT — see [`LICENSE`](LICENSE).
