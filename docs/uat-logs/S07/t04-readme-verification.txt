S07/T04 - every command the README quotes, run as written
date:        2026-09-21 16:26:18
machine:     MinibookX
shell:       5.3.15(2)-release
os:          MINGW64_NT-10.0-26200 3.6.10-710e5275.x86_64
dotnet:      10.0.401
README.md:   sha256 e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820
environment repair applied: APPDATA=C:\Users\Maxim\AppData\Roaming LOCALAPPDATA=C:\Users\Maxim\AppData\Local

----------------------------------------------------------------
README command (documented as never exiting by itself, stopped by this harness at 12s): dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release
----------------------------------------------------------------
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
[sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
[sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
[sample] tray monitor: primary monitor device=\\.\DISPLAY1 dpi=144 scale=1.5 - the notification area lives on the primary monitor's taskbar; the popup's own dpi= reading in the menu-open line is the authoritative per-open value.
[sample] preview cancellation mode: off - every click reaches its main handler.
[sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=off - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both.
[sample] library trace listener attached to source 'Trustsoft.NotifyIcon', level Verbose.
[sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
[sample] tray icon registered, rotating 3 frames every 1s.
[sample] no window is shown - check the notification area, not the taskbar.
[sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
[sample] raw callback trace: hooked 1 HwndSource(s) owned by this thread, watching 0x0400..0x7FFF; the pump counter is armed as the control.
=> exit=124 duration=12s
=> the watchdog stopped it at the bound, as documented; the lines above are its output
=> leftover sample processes after cleanup: 0

----------------------------------------------------------------
README command: dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --run-seconds 20
----------------------------------------------------------------
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
[sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
[sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
[sample] tray monitor: primary monitor device=\\.\DISPLAY1 dpi=144 scale=1.5 - the notification area lives on the primary monitor's taskbar; the popup's own dpi= reading in the menu-open line is the authoritative per-open value.
[sample] preview cancellation mode: off - every click reaches its main handler.
[sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=off - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both.
[sample] library trace listener attached to source 'Trustsoft.NotifyIcon', level Verbose.
[sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
[sample] tray icon registered, rotating 3 frames every 1s.
[sample] no window is shown - check the notification area, not the taskbar.
[sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
[sample] raw callback trace: hooked 1 HwndSource(s) owned by this thread, watching 0x0400..0x7FFF; the pump counter is armed as the control.
[sample] will shut down by itself after 20s (graceful close check).
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
=> exit=0 duration=24s

----------------------------------------------------------------
README command: dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 20
----------------------------------------------------------------
[sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
[sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
[sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
[sample] tray monitor: primary monitor device=\\.\DISPLAY1 dpi=144 scale=1.5 - the notification area lives on the primary monitor's taskbar; the popup's own dpi= reading in the menu-open line is the authoritative per-open value.
[sample] preview cancellation mode: off - every click reaches its main handler.
[sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=off - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both.
[sample] library trace listener attached to source 'Trustsoft.NotifyIcon', level Verbose.
[sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon.
[sample] tray icon registered from markup (Visible="True" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value).
[sample] no window is shown - check the notification area, not the taskbar.
[sample] context menu taken from markup with 2 item(s); a right click on the icon must open it at the icon.
[sample] raw callback trace: hooked 1 HwndSource(s) owned by this thread, watching 0x0400..0x7FFF; the pump counter is armed as the control.
[sample] rotation disabled in declaration mode: the markup's image is what the icon shows.
[sample] will shut down by itself after 20s (graceful close check).
[sample] tray icon disposed - it must have left the notification area.
[sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
=> exit=0 duration=25s

----------------------------------------------------------------
README command: dotnet build Trustsoft.NotifyIcon.sln -c Release
----------------------------------------------------------------
  Determining projects to restore...
  All projects are up-to-date for restore.
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net9.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net10.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon.Tests -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Tests.dll
  Trustsoft.NotifyIcon.Sample -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\samples\Trustsoft.NotifyIcon.Sample\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Sample.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.69
=> exit=0 duration=4s

----------------------------------------------------------------
README command: dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows
----------------------------------------------------------------
  Determining projects to restore...
  All projects are up-to-date for restore.
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon.Tests -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Tests.dll
Test run for C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\tests\Trustsoft.NotifyIcon.Tests\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   403, Skipped:     0, Total:   403, Duration: 58 s - Trustsoft.NotifyIcon.Tests.dll (net8.0)
=> exit=0 duration=63s

----------------------------------------------------------------
README command: dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
----------------------------------------------------------------
  Determining projects to restore...
  All projects are up-to-date for restore.
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net10.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net9.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
  Successfully created package 'C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\..\..\artifacts\Trustsoft.NotifyIcon.1.0.0.nupkg'.
=> exit=0 duration=4s

----------------------------------------------------------------
README command: bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
----------------------------------------------------------------
package: artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
        sha256 e8f551a2d09f8496bde3021c30701db9c86240d0826736ffeced653418b2b580
        expected identity from src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj: id='Trustsoft.NotifyIcon' version='1.0.0'
  PASS  the library project declares the expected identity id='Trustsoft.NotifyIcon' version='1.0.0'
-- identity (the nuspec)
  PASS  the package carries a root-level nuspec: Trustsoft.NotifyIcon.nuspec
  PASS  the nuspec id is 'Trustsoft.NotifyIcon'
  PASS  the nuspec version is '1.0.0'
-- dependency groups (R011)
  PASS  no <dependency> entry appears in any of the 3 target-framework group(s) under <dependencies> (the SDK's empty groups, D038)
  PASS  the only framework reference in the nuspec is Microsoft.WindowsDesktop.App.WPF
-- package content
  PASS  the package carries exactly the three target-framework lib folders: net10.0-windows7.0 net8.0-windows7.0 net9.0-windows7.0
  PASS  lib/net8.0-windows7.0/ carries Trustsoft.NotifyIcon.dll and its XML documentation Trustsoft.NotifyIcon.xml
  PASS  lib/net9.0-windows7.0/ carries Trustsoft.NotifyIcon.dll and its XML documentation Trustsoft.NotifyIcon.xml
  PASS  lib/net10.0-windows7.0/ carries Trustsoft.NotifyIcon.dll and its XML documentation Trustsoft.NotifyIcon.xml
  PASS  the package carries README.md at its root
  PASS  the nuspec <readme> names 'README.md' and that entry is in the package
  PASS  the package carries the licence file LICENSE at its root
  PASS  no entry belongs to the sample, the tests, the probe or the consumer proof (patterns: Sample Tests testhost probe-live consumer-proof)
  PASS  every package entry is one the package is allowed to contain (nuspec, README, LICENSE, lib/<tfm>/assembly+xml, package metadata)


VERDICT  all 15 assertions hold
=> exit=0 duration=2s

----------------------------------------------------------------
README command: dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release -f net8.0-windows
----------------------------------------------------------------
  Determining projects to restore...
  All projects are up-to-date for restore.
  Trustsoft.NotifyIcon -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\src\Trustsoft.NotifyIcon\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon.Sample -> C:\Users\Maxim\YandexDisk\Projects AI\Trustsoft.NotifyIcon\.gsd-worktrees\M001\samples\Trustsoft.NotifyIcon.Sample\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Sample.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.91
=> exit=0 duration=3s

----------------------------------------------------------------
README command: dotnet run --project scripts/probe-live -c Release -- samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 12 --run-seconds 8
----------------------------------------------------------------
[probe] probe-live start 2026-09-21 16:28:39; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX
[probe] sample exe: samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe
[probe] observe: 12s; kill-after: no; click-after: no; left-click-after: no; balloon-after: no; menu-after: no; sample args: --run-seconds 8
[probe] launched pid=9872
sample| [sample] process DPI awareness: GetProcessDpiAwareness=0x00000000 value=2 (DPI_AWARENESS_PER_MONITOR_AWARE); threadContextAwareness=2 (DPI_AWARENESS_PER_MONITOR_AWARE) isPerMonitorV2=True.
sample| [sample] display configuration: monitors=1 virtualScreen=0,0 1920x1200 physical, primaryPhysical=1920x1200
sample| [sample] monitor 0: device=\\.\DISPLAY1 primary=True rect=0,0 1920x1200 work=0,0 1920x1128 dpi=144 scale=1.5 hr=0x00000000
sample| [sample] tray monitor: primary monitor device=\\.\DISPLAY1 dpi=144 scale=1.5 - the notification area lives on the primary monitor's taskbar; the popup's own dpi= reading in the menu-open line is the authoritative per-open value.
sample| [sample] preview cancellation mode: off - every click reaches its main handler.
sample| [sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=off - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both.
sample| [sample] library trace listener attached to source 'Trustsoft.NotifyIcon', level Verbose.
sample| [sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
sample| [sample] tray icon registered, rotating 3 frames every 1s.
sample| [sample] no window is shown - check the notification area, not the taskbar.
sample| [sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
sample| [sample] raw callback trace: hooked 1 HwndSource(s) owned by this thread, watching 0x0400..0x7FFF; the pump counter is armed as the control.
sample| [sample] will shut down by itself after 8s (graceful close check).
[probe] t=1s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=13
[probe] t=2s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=15
[probe] t=3s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=4s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=5s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=6s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=7s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=8s pid=9872 icon=present rect=(1494,1128,1542,1200) gdi=17
sample| [sample] tray icon disposed - it must have left the notification area.
sample| [sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
[probe] sample-exited at t=9s with exit code 0
[probe] identity: hwnd=0x41C00DA uID=1 title="(no title)" (Trustsoft.NotifyIcon.TrayMessageWindow is the expected title)
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] observed-present: yes
[probe] sample-alive-at-end: False; exit-code: 0
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x41C00DA uID=1)
=> exit=0 duration=12s

================================================================
SUMMARY  9 command(s), 0 failure(s)
README.md sha256 before: e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820
README.md sha256 after:  e87308ab2af00b805def26e8fa2cae1c6570fff2215a974de6a08bf2cc366820
the README was not edited while its commands ran (the log belongs to this revision)
