# UAT-S06 - XAML surface without a visual parent

**Slice:** M001 / S06 (XAML surface without a visual parent)
**Requirements:** R008 (the single class works from C# code and from XAML declared in `Application.Resources`), R009 (the menu and event wiring are expressible in markup)
**Date:** 2026-09-21
**Revision tested:** `milestone/M001` with the S06 work applied (`32d8eee` plus the declaration fix and the new check below)
**Machine:** MINIBOOKX, `Microsoft Windows NT 10.0.26200.0`, single monitor 1920x1200, display scale 150 % (dpi 144)

## Verdict

| Claim | Verdict | Evidence |
|---|---|---|
| A `TrayIcon` declared in `Application.Resources` creates a working icon | **PASS** | Check 1: the declarative run registers, reports its 2-item menu as taken from markup, and disposes cleanly |
| Markup event attributes are validated before the app runs | **PASS** | Check 1: the sample's own markup compilation is the validation - a misspelled handler or a wrong signature fails the build, and the build is clean |
| Markup event attributes deliver at runtime | **PASS** | Check 3: the declared menu's own `Opened`/`Closed` attributes fired (`menu opened:` then `menu dismissed.`), and the declared balloon pair was reached by the shell's own `NIN_BALLOONSHOW`/`NIN_BALLOONTIMEOUT`. This is the fallback route the slice plan names for exactly this instrument limit - not a click |
| A menu declared in markup opens at the icon, on the library's own open path | **PASS** | Check 3: `menu opened: popup=0x3740806 ... rect=1446,1045 296x83 dpi=144 scale=1.5` against the icon's own `rect=(1446,1128,1494,1200)` - the popup sits directly above the icon, at the right DPI. Check 4 produces the identical rectangle in code-first mode |
| A click injected at the icon reaches the icon | **NOT OBSERVED** | Check 1: the injected click never arrived. Recorded below with the measurement (F1) rather than claimed |
| The declared resources cost a code-first run nothing (BAML deferral) | **PASS** | Check 2: a code-first run of the same executable ends with exactly one icon in the notification area |
| The declarative path leaves the icon healthy end to end | **PASS** | Check 1: 22 consecutive `icon=present` readings, a flat GDI count, and `icon-after-exit: gone` after a clean dispose |
| The library's exported surface is unchanged by this slice | **PASS** | `PackagePurityTests` green; the namespace attributes add no type. Full suite 391 passed / 0 failed |

## What this slice does not claim

- **No click reached a declarative icon.** The one failure in this session is the instrument, not the library: see finding F1. What is proven instead is the route the slice plan names as the fallback: the *declared* menu's `Opened`/`Closed` attributes deliver live (Check 3), and the declared balloon handlers were reached by the shell's own balloon callbacks. The markup compiler's validation of every attribute (a build-time fact) stands alongside that.
- **The declarative icon was not clicked by the shell, so the declared click attributes were never delivered.** They are bound at parse time with the same mechanism as the menu's `Opened`/`Closed` - `TrayIconXamlContractTests.Markup_event_attributes_bind_and_fire_for_bubble_and_preview` proves that mechanism headlessly - but a live shell click on this tray stays out of reach (F1).
- **No unit test of resource-scope event wiring.** `XamlReader` binds handler names against the root object only, so a dictionary-rooted element with an event attribute cannot be parsed at all - pinned as a boundary in `TrayIconXamlContractTests`. Resource-scope wiring is BAML's job, validated by the sample build and delivered by the live run (Check 3).

---

## Check 1 - the declarative run

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 30 \
    --click-after 12 \
    --sample-arg --xaml --sample-arg --run-seconds --sample-arg 22
```

**Raw evidence** (`docs/uat-logs/S06/check1-declarative-run.log`):

```
sample| [sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon.
sample| [sample] tray icon registered from markup (Visible="True" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value).
sample| [sample] context menu taken from markup with 2 item(s); a right click on the icon must open it at the icon.
[probe] click injected: right click at (1470,1164) - the icon's own rectangle, so the shell's callback and the menu it opens are the real ones
sample| [sample] tray icon disposed - it must have left the notification area.
sample| [sample] totals: raw callback lines=0, pump-observed private-range messages=0, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=0 (self=0), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=0, menu dismissals=0.
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] observed-present: yes
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x690138 uID=1)
```

**What it says.**

1. **The declaration is what registered the icon.** `Visible="True"` lives in `App.xaml`, so the registration happened inside the resource lookup - the one call the declarative branch makes. Nothing in `App.xaml.cs` assigned a property or subscribed to an event in this run, which is what R008 asks for.
2. **The menu arrived from markup**, with the item count read back from the declaration (`2 item(s)`), rather than from `CreateTrayMenu()`.
3. **The icon behaved for the whole run:** 22 consecutive `icon=present` readings at `rect=(1446,1128,1494,1200)`, one icon in the notification area, GDI flat at 13 (no rotation in this mode, so the series has no churn at all), and `gone` once the process disposed and exited.
4. **The click did not arrive.** F1 below.

**Finding F1 (instrument limit, recorded rather than worked around).** The probe injected a real right click at the centre of the icon's own shell-reported rectangle, twice, with a pointer move across the icon before the buttons on the second attempt, and the sample recorded `clicks=0` with `raw callback lines=0`. Two distinct instrument problems surfaced and were fixed before that conclusion:

- a hand-declared `INPUT` union made `SendInput` return 0 of 2 events (the signature of a rejected record size); replaced with the layout-free `mouse_event`, which does synthesise the events;
- the first working click still delivered nothing, and a pointer nudge across the icon changed nothing.

So the click is synthesised and the shell reports the icon at that rectangle, yet the tray delivers no callback. The likely cause is that on Windows 11 an icon whose position the shell reports can still be non-hit-testable at that position (the overflow flyout owns it), which is consistent with S03's recorded experience that "the injector cannot always click (the icon may be in a flyout it cannot reach)" - which is why that slice added the no-click `--open-menu-after` path in the first place. Making this check pass would need the overflow flyout opened and its contents driven, which is a different instrument from this one and is not built here.

**Consequence.** Click delivery into this tray stays **NOT OBSERVED** and is not claimed. The delivery half of R009 was closed instead through the route the slice plan names as the fallback for exactly this limit: the declared menu's own `Opened`/`Closed` attributes and the declared balloon handlers, both reached live in Check 3. The build-time half - that the markup compiler resolved and type-checked every handler attribute against `App`'s methods - is a real, reproducible fact: the sample does not build without it.

---

## Check 2 - the deferral cross-check

`App.xaml` declares the resources unconditionally, on the claim that BAML defers instantiation until the first lookup, so a code-first run that never looks them up cannot end up with a second icon. The check is the icon count, measured by the shell rather than by the app.

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 18 \
    --sample-arg --run-seconds --sample-arg 12
```

**Raw evidence** (`docs/uat-logs/S06/check2-deferral-cross-check.log`):

```
sample| [sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] observed-present: yes
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x700180 uID=1)
```

**What it says.** Code-first mode, one icon. The claim holds where it matters - a consumer that never uses the declarative path pays nothing for the declaration being there, which is why the resources can be declared unconditionally instead of behind a lookup guard.

---

## Check 3 - the declared menu opens at the icon, and the declared attributes deliver

Check 1 could not close the delivery half of R009, because it needed a shell click. This check closes it the way the slice plan allows: the menu is declared in markup with `Opened="OnMenuOpened"` and `Closed="OnMenuClosed"`, so making that menu open exercises two markup-declared event attributes at once, and the menu's placement is the library's own production open path.

**What had to change first.** The declaration in `App.xaml` named the library's `tni:TrayIcon`, and the sample reported `--open-menu-after: unavailable in declaration mode`, because the no-click open path needs the protected `OnTrayClick` hook that only a subclass can reach. The plan's T02 promoted `SampleTrayIcon` for precisely this, and the delivered markup could not use it on the belief that a local type is unresolvable from an `ApplicationDefinition`. That belief was wrong in its specific form: **MC3074 comes from the assembly qualifier, not from the local type.** `xmlns:local="clr-namespace:Trustsoft.NotifyIcon.Sample"` - no `;assembly=` - resolves `SampleTrayIcon` and builds clean. With that, the declared element *is* the sample's own subclass, every attribute on it is still the library's dependency properties and routed events, and the self-open instrument works in this mode too. The sample now assigns `_sampleTrayIcon` from the lookup as a guarded cast, so a declaration of the plain library type still reports "unavailable" rather than throwing.

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 45 \
    --sample-arg --xaml --sample-arg --run-seconds --sample-arg 40 \
    --sample-arg --open-menu-after --sample-arg 12 \
    --sample-arg --show-balloon-after --sample-arg 20
```

**Raw evidence** (`docs/uat-logs/S06/check3-declarative-menu-and-balloon.log`):

```
sample| [sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon.
sample| [sample] tray icon registered from markup (Visible="True" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value).
sample| [sample] context menu taken from markup with 2 item(s); a right click on the icon must open it at the icon.
[probe] t=12s pid=18660 icon=present rect=(1446,1128,1494,1200) gdi=13
sample| [sample] --open-menu-after: requesting the menu now, with no click injected.
sample| [sample] menu opened: popup=0x3740806 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;d0f26eb7-21fa-44fd-bf12-2b4152c69cb9] rect=1446,1045 296x83 dpi=144 scale=1.5 owner=0x1F20858 cursor=1467,23 bottomLeftDip=964,752
[probe] t=13s pid=18660 icon=present rect=(1446,1128,1494,1200) gdi=25
sample| [sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
sample| [sample] menu dismissed.
sample| [sample] --show-balloon-after: showing a balloon now, with no click injected.
sample| [sample] raw callback hwnd=0x3F60598 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
sample| [sample] raw callback hwnd=0x3F60598 msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404
sample| [sample] totals: raw callback lines=2, pump-observed private-range messages=1, library trace lines=0, clicks=0, cancelled by a Preview handler=0, balloon show requests=1 (self=1), balloon clicked deliveries=0, balloon preview deliveries=0, menu opens=1, menu dismissals=1.
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] observed-present: yes
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x3F60598 uID=1)
```

**What it says.**

1. **The declared menu opened, at the icon, at the right DPI.** The icon is at `rect=(1446,1128,1494,1200)`; the popup landed at `1446,1045 296x83`, whose bottom edge is exactly the icon's top edge, with `dpi=144 scale=1.5` measured per open. The path is the library's own: menu activation policy, the assigned menu, the shell's icon rectangle, `TrayIconPlacement`, the anchor window and a real WPF popup. The only step it bypasses is the shell's callback and its decode - which is what F1 blocks.
2. **Two markup-declared event attributes delivered at runtime.** `menu opened:` is `OnMenuOpened`, reached only through the `Opened="OnMenuOpened"` attribute; `menu dismissed.` is `OnMenuClosed` through `Closed`. Neither handler is subscribed in C# in this mode - the subscription block is skipped whenever the instance came from markup - so the only route to either line is the declaration.
3. **The declared balloon handlers were reachable too.** The shell's own lifecycle callbacks for the declared icon arrived: `event=0x0402` (`NIN_BALLOONSHOW`, the acceptance the slice's demonstration is about) and `event=0x0404` (`NIN_BALLOONTIMEOUT`, the shell retiring it). No `NIN_BALLOONUSERCLICK` arrived in this run, so no public balloon event was raised - stated as measured rather than inferred.
4. **The icon stayed healthy across all of it.** 40 consecutive `icon=present` readings, `icons-in-notification-area: 1`, and `icon-after-exit: gone` after the sample disposed and exited. The GDI series steps once from 13 to 25 when the popup is created and stays flat afterwards; Check 4 shows the same +12 step in code-first mode, so the step is the popup's cost and not a declarative leak.
5. **Menu counting agrees with the reading.** `menu opens=1, menu dismissals=1` in the sample's own totals - one open, one close, matching the two handler lines above.

---

## Check 4 - code-first cross-check of the same run shape

The declarative run's two loudest numbers are its menu rectangle and its GDI step. Both are only meaningful against the same run in code-first mode, which S03 already established as the working reference.

**Command** - Check 3's command with `--sample-arg --xaml` removed.

**Raw evidence** (`docs/uat-logs/S06/check4-code-first-cross-check.log`):

```
sample| [sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
sample| [sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
sample| [sample] menu opened: popup=0x420224 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;7f5e51b4-ae14-4aa5-9ace-0542e0a6e93c] rect=1446,1045 296x83 dpi=144 scale=1.5 owner=0x2450870 cursor=1544,100 bottomLeftDip=964,752
sample| [sample] raw callback hwnd=0x100198 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located)
[probe] observed-present: yes
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x100198 uID=1)
```

**What it says.** The declared menu and the assigned menu open at the **identical** rectangle (`1446,1045 296x83 dpi=144 scale=1.5`) even though the two runs had the cursor at different places when they opened, which is the point: placement comes from the icon the shell reports, not from the cursor. The GDI series is `13 -> 15 -> 17` (the code-first rotation) and then `29` for the popup - a +12 step, exactly the declarative run's `13 -> 25`. Both modes end with one icon in the notification area and `gone` after exit. One icon, same placement, same handle cost: the declaration reaches the same states.

---

## Verification summary - headless proof and the suite

`tests/Trustsoft.NotifyIcon.Tests/TrayIconXamlContractTests.cs`, `[StaFact]`, zero shell calls:

| Test | Claim it pins |
|---|---|
| `Declarative_property_markup_creates_a_configured_instance_without_registering` | property markup resolves (string, bool, enum, `StaticResource` image), and `Visible="False"` leaves the instance inert: no host window, no registration |
| `Markup_resolves_the_library_owned_context_menu_property_and_the_menu_by_identity` | markup writes the shadowed `TrayIcon.ContextMenuProperty` (via `DependencyPropertyDescriptor`), the menu is reference-identical, and the inherited `FrameworkElement.ContextMenu` is null |
| `One_resource_key_yields_one_instance` | `x:Shared` keeps its default: one key, one instance |
| `Markup_event_attributes_bind_and_fire_for_bubble_and_preview` | with a `TrayIcon` subclass as the parsed root, `TrayLeftClick` and `PreviewTrayLeftClick` attributes bind and both handlers run with real payloads |
| `Markup_event_attributes_in_a_resource_dictionary_are_a_pinned_boundary` | a dictionary-rooted element with an event attribute throws `XamlParseException` - the boundary, pinned so the gap is not mistaken for an oversight |
| `The_consumer_namespace_is_declared_and_markup_resolves_through_it` | the assembly declares `XmlnsDefinition`/`XmlnsPrefix` for `http://schemas.trustsoft.com/notifyicon` (prefix `tni`) **and** a real parse through that URI succeeds |
| `A_parsed_parentless_instance_disposes_cleanly` | a parse-created parentless instance disposes cleanly, and disposal is idempotent |

| Measurement | Result |
|---|---|
| `dotnet build Trustsoft.NotifyIcon.sln -c Release` | succeeded, **0 warnings, 0 errors**, all three target frameworks |
| `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release` | **391 passed, 0 failed, 0 skipped** (S05 ended at 384; 7 XAML contract tests added) |
| `PackagePurityTests` | green, untouched - the namespace attributes added no exported type, and there is still no balloon dependency property (D031) |
| Library changes in this slice | `Properties/AssemblyInfo.cs` only (two metadata attributes). The library code itself is untouched, as the slice research predicted |
| Sample changes in this slice | `App.xaml` (the declaration, now of `local:SampleTrayIcon`), `App.xaml.cs` (the `--xaml` branch, the self-open hook's availability condition, and the console lines), plus `SampleTrayIcon` promoted to a public top-level type in `App.xaml.cs` |

**Environment note on the suite (measured, recorded rather than smoothed over).** The suite is not
fully deterministic on a machine that is being used while it runs. Three consecutive full runs during
this session produced **391 passed / 0 failed**, then **388 / 3**, then **389 / 2**, and the failing
set differed each time: the S03 real-popup tests (`TrayIconMenuActivationTests`) fail when another
process takes the foreground while the WPF popup is open, because WPF closes an open `ContextMenu`
when it loses activation. The failure diagnostic names the cause directly -
`foreground=0xB10624(CASCADIA_HOSTING_WINDOW_CLASS)` with `anchorIsForeground=False` - and a quiet
re-run is green. The last full run before this record was written was green (391 / 0). Nothing about
the library changed between those runs; the machine did. S07's CI proof should either run on a quiet
agent or harden the `OpenMenu` helper with a bounded single retry on the
`anchorIsForeground=False` signature, which is a test-environment fix and not a library change.

---

## Reproducing this

| Run | Log |
|---|---|
| Check 1 (declarative run, with the click attempt) | `docs/uat-logs/S06/check1-declarative-run.log` |
| Check 2 (deferral cross-check, code-first) | `docs/uat-logs/S06/check2-deferral-cross-check.log` |
| Check 3 (declarative run; declared menu self-opens, declared attributes deliver) | `docs/uat-logs/S06/check3-declarative-menu-and-balloon.log` |
| Check 4 (the same run shape in code-first mode, for the menu rectangle and the GDI step) | `docs/uat-logs/S06/check4-code-first-cross-check.log` |

These logs are tracked on purpose even though the repository's `.gitignore` excludes `*.log`; they were added with `git add -f`. Do not remove them as stray logs.

**Probe capabilities added for this slice** (`scripts/probe-live`): `--click-after <seconds>`, which right-clicks the shell-reported icon rectangle, and an `icons-in-notification-area` count reported with the resolved identity. The click capability is the half that does not work against this tray (F1); the count is what makes Check 2 a measurement.

## Re-verification by the auto-mode unit (T04)

Every live check in this record was re-run on the same machine (`MINIBOOKX`, `Microsoft Windows NT 10.0.26200.0`, single 1920x1200 monitor at 150 %) and on the revision that contains the work this record describes (`milestone/M001`, `2f0620c`, whose history includes the `f55c1be` declaration fix). Nothing under `src/`, `samples/`, `tests/` or `scripts/` was edited between the two passes; only `docs/` grew. This section is the reproducibility evidence: the same claims, re-measured.

**Prerequisite for running any of it inside the auto-mode sandbox (measured, recorded because it costs an hour to rediscover).** The `gsd_exec` sandbox hands its shell a stripped environment - `APPDATA`, `LOCALAPPDATA`, `PROGRAMFILES`, `ProgramFiles(x86)` and `ProgramData` are absent and `TEMP` is the MSYS path `/tmp`. NuGet's `XPlatMachineWideSetting` constructor then fails with `Value cannot be null. (Parameter 'path1')` from `NuGetEnvironment.CalculateFolderPath`, before a single project is evaluated; `dotnet build`, `dotnet restore`, `dotnet test` and even the assets-file read inside the build all fail this way. Injecting the variables fixes it, and does not change what is built:

```
env APPDATA='C:\Users\Maxim\AppData\Roaming' LOCALAPPDATA='C:\Users\Maxim\AppData\Local' \
    PROGRAMFILES='C:\Program Files' 'ProgramFiles(x86)=C:\Program Files (x86)' ProgramData='C:\ProgramData' \
    TEMP='C:\Users\Maxim\AppData\Local\Temp' TMP='C:\Users\Maxim\AppData\Local\Temp' \
    <the command as written above>
```

This is a harness fact, not a repository fact. A normal developer shell needs nothing of it.

| Re-checked claim | Re-verification log | Result on the second pass |
|---|---|---|
| Check 1 shape - declarative run with a real click injected at the icon's own rectangle | `docs/uat-logs/S06/reverify-check1-declarative-click-attempt.txt` | `[probe] click injected: right click at (1470,1164)`, then `clicks=0, ... raw callback lines=0`. **F1 reproduces**, so the NOT OBSERVED row above still stands on this session too. One icon, 23 readings, `sample-exited at t=23s with exit code 0`, `icon-after-exit: gone` |
| Check 2 shape - the deferral cross-check, code-first, declared resources inert | `docs/uat-logs/S06/reverify-check2-deferral-cross-check.txt` | `[probe] icons-in-notification-area: 1` - a consumer that never looks the declaration up still ends with exactly one icon; `sample-exited ... exit code 0`; `gone` |
| Check 3, exactly as the task plan's verify command writes it (30 s observation window) | `docs/uat-logs/S06/reverify-check3-declarative-menu-and-balloon.txt` | registered from markup, then `menu opened: ... rect=1446,1045 296x83 dpi=144 scale=1.5` over the icon at `rect=(1446,1128,1494,1200)`; shell callbacks `event=0x0402` and `0x0404`; `icons-in-notification-area: 1`. The window ends before the sample's own 40 s shutdown, so this shape is killed by the probe (`sample-alive-at-end: True`) and **cannot carry the clean-dispose claim** |
| Check 3 shape with this record's 45 s window | `docs/uat-logs/S06/reverify-check3b-declarative-graceful-dispose.txt` | same rectangle, same two markup-declared handler lines (`menu opened:` / `menu dismissed.`), `menu opens=1, menu dismissals=1`, 40 consecutive `icon=present` readings, GDI flat at 13 then 25, `sample-exited at t=41s with exit code 0`, `icon-after-exit: gone` |
| Check 4 shape - the same run in code-first mode | `docs/uat-logs/S06/reverify-check4-code-first-cross-check.txt` | `menu opened: ... rect=1446,1045 296x83 dpi=144 scale=1.5` - the identical rectangle; GDI `13 -> 15 -> 17 -> 29` (a +12 popup step, against the declarative `13 -> 25`); one icon; exit code 0; `gone` |
| `dotnet build Trustsoft.NotifyIcon.sln -c Release -t:Rebuild` | console (quoted below) | succeeded, **0 warnings, 0 errors**: the library on all three TFMs, plus sample and tests |
| `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release` | `docs/uat-logs/S06/reverify-suite-full.txt` | **394 passed, 0 failed, 0 skipped** (52 s), first attempt - the foreground-loss flakiness described in the environment note above did not appear in this pass |

The build console, whole and unfiltered:

```
  Trustsoft.NotifyIcon -> ...\src\Trustsoft.NotifyIcon\bin\Release\net9.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> ...\src\Trustsoft.NotifyIcon\bin\Release\net10.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon -> ...\src\Trustsoft.NotifyIcon\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
  Trustsoft.NotifyIcon.Sample -> ...\samples\Trustsoft.NotifyIcon.Sample\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Sample.dll
  Trustsoft.NotifyIcon.Tests -> ...\tests\Trustsoft.NotifyIcon.Tests\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Tests.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

The declarative run, in this pass (Check 3b, 45 s window):

```
sample| [sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon.
sample| [sample] tray icon registered from markup (Visible="True" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value).
sample| [sample] context menu taken from markup with 2 item(s); a right click on the icon must open it at the icon.
sample| [sample] menu opened: popup=0x1C700E2 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;...] rect=1446,1045 296x83 dpi=144 scale=1.5 owner=0x2F605B6 cursor=1055,1147 bottomLeftDip=964,752
sample| [sample] menu dismissed.
sample| [sample] raw callback hwnd=0x14B01A8 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
sample| [sample] tray icon disposed - it must have left the notification area.
sample| [sample] totals: raw callback lines=2, ..., balloon show requests=1 (self=1), ..., menu opens=1, menu dismissals=1.
[probe] icons-in-notification-area: 1 (...)
[probe] observed-present: yes
[probe] sample-alive-at-end: False; exit-code: 0
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x14B01A8 uID=1)
```

**Two measured differences between the passes, recorded rather than smoothed over.**

1. The shell raised `NIN_BALLOONTIMEOUT` (`0x0404`) **twice** in the second declarative pass (once in the first) and twice in this pass's code-first cross-check. That count is the shell's; the accepted `NIN_BALLOONSHOW` (`0x0402`) is one per requested balloon in every pass. Nothing in the library counts or de-duplicates the timeout callback, so the variance is not a library behaviour and is stated as measured.
2. The task plan's verify command pairs a **30 s observation window with a 40 s sample run**, so the probe hard-kills the sample (`taskkill /f ... -> exit 0`) instead of observing its own shutdown. Both halves are therefore recorded here: the 30 s shape proves registration, markup delivery and the accepted balloon, and the 45 s shape carries the clean-dispose claim. A later edit to that verify line should use `45` as the observation window.

The re-verification logs use the `.txt` suffix because the repository's `.gitignore` excludes `*.log` (which is why the four original logs needed `git add -f`); `.txt` needs no force-add. They are evidence, not stray output.

---

## Hand-off to S07

- The declarative run command is `-- --xaml`, and it now also takes `--open-menu-after <seconds>`, which opens the *declared* menu with no shell click. The README documents the mode and the namespace URI (`http://schemas.trustsoft.com/notifyicon`, prefix `tni`) is declared in `AssemblyInfo.cs` and should be confirmed alongside the package metadata. The README's snippet is the consumer shape (a consumer's markup lives in another assembly, so it uses that URI); the sample itself cannot, because the element it declares is a type from its own project - and because the local-namespace form must not carry `;assembly=`, which is the `MC3074` trap recorded in Check 3.
- The packaging proof must not include `scripts/probe-live`: it is deliberately absent from `Trustsoft.NotifyIcon.sln` and takes no project reference to the library, so it cannot enter the shipped package or be mistaken for a supported artifact.
- A live click-delivery check for the declarative path is still missing (F1); if S07 wants one, it needs an instrument that can open the Windows 11 overflow flyout, not this one. Nothing in the shipped surface depends on it: the click attributes bind and fire (headless proof) and the same open path is proven live through the declared menu.
