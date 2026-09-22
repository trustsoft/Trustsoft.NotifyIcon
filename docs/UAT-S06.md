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
| The library's exported surface is unchanged by this slice | **PASS** | `PackagePurityTests` green (6/6 at this revision); the namespace attributes add no type. Full suite **394 passed / 0 failed** at the closing measurement (the `391` this row carried earlier was an interim reading of an intermediate working tree, superseded by S05's own exit measurement and independently re-derived in the T05 section below) |
| The full suite is green on all three target frameworks, with the S05 baseline accounted for | **PASS** | T05: build **0 warnings, 0 errors** on net8.0/net9.0/net10.0-windows; suite **394 passed / 0 failed / 0 skipped** in 52 s; all 21 test names added since the S04 baseline enumerated by name. See "Cross-framework regression and the evidence pack (T05)" |
| The code-first path still works end to end now that a second construction path exists | **PASS** with one `NOT OBSERVED` | T05: four live runs - register, menu opened at the icon on the library's own open path, shell-accepted balloon, clean dispose (exit code 0, icon gone). The click-driven route into the icon is `NOT OBSERVED` (F1 reproduces in code-first mode too); see the T05 section |
| Bindings in the menu resolve against the component's `DataContext` | **SETTLED - measured, and the acceptance line clarified** | T06: they do not, and the library never promised they would. It never writes a menu's `DataContext`, and a resource-declared menu has no logical parent for the icon's to flow through; the **consumer** sets the menu's own `DataContext` and the bound item resolves against that object graph. Headless `TrayIconMenuDataContextTests` (3/3 green) and live `menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='declared menu data context'` in both run modes. See "Menu data context and bindings (T06)" |

## What this slice does not claim

- **No click reached a declarative icon.** The one failure in this session is the instrument, not the library: see finding F1. What is proven instead is the route the slice plan names as the fallback: the *declared* menu's `Opened`/`Closed` attributes deliver live (Check 3), and the declared balloon handlers were reached by the shell's own balloon callbacks. The markup compiler's validation of every attribute (a build-time fact) stands alongside that.
- **The declarative icon was not clicked by the shell, so the declared click attributes were never delivered.** They are bound at parse time with the same mechanism as the menu's `Opened`/`Closed` - `TrayIconXamlContractTests.Markup_event_attributes_bind_and_fire_for_bubble_and_preview` proves that mechanism headlessly - but a live shell click on this tray stays out of reach (F1).
- **No unit test of resource-scope event wiring.** `XamlReader` binds handler names against the root object only, so a dictionary-rooted element with an event attribute cannot be parsed at all - pinned as a boundary in `TrayIconXamlContractTests`. Resource-scope wiring is BAML's job, validated by the sample build and delivered by the live run (Check 3).
- **No claim that the library pushes a data context into a menu.** It does not do that, and the S06 acceptance line's last clause is clarified below rather than left to be read as such a promise: the library writes `PlacementTarget` and the placement offsets on the menu and nothing else, so a menu's bindings resolve against the menu's own `DataContext`, which the consumer sets. Measured in "Menu data context and bindings (T06)".

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

T06 adds a second headless file, `TrayIconMenuDataContextTests.cs` (3 tests, real popups over
`FakeShellApi`): where a menu's bindings resolve from, and that the library neither writes nor clears
a menu's `DataContext`. See the T06 section at the end of this record.

| Measurement | Result |
|---|---|
| `dotnet build Trustsoft.NotifyIcon.sln -c Release` | succeeded, **0 warnings, 0 errors**, all three target frameworks |
| `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release` | **394 passed, 0 failed, 0 skipped** (S05 ended at **394**, its own exit measurement; the `391`/`384` pairing this row carried earlier was an interim reading and is corrected in the T05 section below. 7 XAML contract tests are S06's own) |
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
| T05 Check 5 (code-first end to end, with a real left click attempted at the icon) | `docs/uat-logs/S06/t05-code-first-e2e.txt` |
| T05 Check 5 control (the identical shape with no click injected at all) | `docs/uat-logs/S06/t05-code-first-e2e-control-noclick.txt` |
| T05 Check 5 repeat (the left click attempted a second time) | `docs/uat-logs/S06/t05-code-first-e2e-repeat.txt` |
| T05 Check 6 (right click attempted at the icon, code-first) | `docs/uat-logs/S06/t05-code-first-click-attempt.txt` |
| T06 Check 2 (declarative run; the declared menu's own `DataContext` and its bound item) | `docs/uat-logs/S06/t06-declarative-menu-data-context.txt` |
| T06 Check 3 (the same mechanism in code-first mode) | `docs/uat-logs/S06/t06-code-first-menu-data-context.txt` |
| T06 suite (full run after the T06 changes) | `docs/uat-logs/S06/t06-suite-full.txt` |

These logs are tracked on purpose even though the repository's `.gitignore` excludes `*.log`; they were added with `git add -f`. Do not remove them as stray logs.

**Probe capabilities added for this slice** (`scripts/probe-live`): `--click-after <seconds>`, which right-clicks the shell-reported icon rectangle, and an `icons-in-notification-area` count reported with the resolved identity. The click capability is the half that does not work against this tray (F1); the count is what makes Check 2 a measurement. T05 added the left-click spelling the balloon route needs, `--left-click-after <seconds>`: it uses the same position oracle, the same pointer nudge and the same synthesised input, with the button changed to the one the sample's balloon demonstration is wired to. It fails identically (Checks 5 and 6), and having both spellings is what shows the refusal is the tray surface rather than one button.

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

## Cross-framework regression and the evidence pack (T05)

**Revision tested:** `milestone/M001` at `3049495` (HEAD of the worktree). The only working-tree change
since that commit is `scripts/probe-live/Program.cs`, which gained a left-click capability this task
needed for one check below; nothing under `src/`, `samples/` or `tests/` was edited by T05, so every
number in this section is measured on the revision the rest of this record describes. Machine and
environment as recorded at the top of this document.

T05's job is the opposite of S05's: S06 changed the sample and one metadata file, so what has to be
shown is that the library's contract and the *existing* construction path came through untouched
while a second path was added. Three independent measurements, then the accounting.

### 1. Build and full suite, all three target frameworks

| # | Command | Result | Evidence |
|---|---|---|---|
| 1 | `dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore` | exit **0**, **0 warnings, 0 errors** - `Trustsoft.NotifyIcon.dll` for net8.0-windows, net9.0-windows and net10.0-windows, plus the sample and the test assembly | `gsd_exec` `0c95dd64-076f-4025-b742-5b31c96f1ba6` |
| 2 | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | exit **0**, **394 passed / 0 failed / 0 skipped**, 52 s | `gsd_exec` `2b41316f-ab0e-4ad2-b4c2-8ca935a57aa7` |
| 3 | `dotnet test ... --filter "FullyQualifiedName~PackagePurityTests"` | exit **0**, **6 passed / 0 failed**, 31 ms | `gsd_exec` `1e189468-780c-47c6-88af-9c1299b0f2a6` |
| 4 | `dotnet test ... --filter "FullyQualifiedName~TrayIconMenuActivationTests|FullyQualifiedName~TrayIconMenuContractTests"` | exit **0**, **21 passed / 0 failed**, 26 s (real WPF popups, in-process) | `gsd_exec` `256a9872-142f-455e-9abb-5f62751d26ab` |
| 5 | the task plan's verify line verbatim, as one invocation: `dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore && dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | exit **0** - build **0 warnings, 0 errors**, then **394 passed / 0 failed / 0 skipped** | `gsd_exec` `59149d79-0191-4867-9b09-a841b77ca261` |
| 6 | `dotnet build scripts/probe-live -c Release` (the instrument T05 extended; not part of the solution, so it cannot reach the shipped package) | exit **0**, **0 warnings, 0 errors** | `gsd_exec` `9dbe3d03-aa71-4409-aa98-86b85053b6f4` |

The suite was run **alone** for rows 2 and 4, per the rule the environment note above records;
neither pass hit the foreground-loss class. The three-TFM claim is not taken from the build console
alone: `Solution_build_outputs_exist_for_all_three_tfms` (row 3's class) fails loudly unless all three
Release output directories exist, so row 2's green run is itself the three-TFM evidence.

### 2. The baseline accounting, by name

The task plan names "the 384 passed and 0 failed baseline recorded at the end of S05". That number is
an **interim reading of an intermediate working tree**, and `docs/UAT-S05.md` deliberately retracts it:
its "Two honest notes about that table" section says an earlier revision "carried an interim `384`
reading" and replaces it with a by-name table. S05's own exit measurement is therefore:

| Point | Tests | Source |
|---|---|---|
| S05 entering baseline (S04 complete, `15694bd`) | **373 passed / 0 failed** | `docs/UAT-S05.md`; `docs/UAT-S04.md` |
| S05 exit measurement (the slice's closing row) | **394 passed / 0 failed / 0 skipped** | `docs/UAT-S05.md`, "Full suite and build" |
| Delta | **+21 test names**, every one listed in that section's table | |
| T05 re-measurement, same revision | **394 passed / 0 failed / 0 skipped**, 52 s | row 2 above |
| T05's own contribution to the delta | **0 test names** - T05 adds no test | this section |

T05 re-derived the delta independently rather than trusting the table: extracting every attributed
test method name from `tests/**/*.cs` at `15694bd` and at HEAD and diffing the two name sets yields
**240 names at the baseline and 261 at HEAD - 21 added, 0 removed**. Per class, the four classes that
changed are `TrayIconRecoveryTests` 0 -> 11, `SliceContractTests` 6 -> 8,
`TrayIconMenuActivationTests` 15 -> 16 and `TrayIconXamlContractTests` 0 -> 7 (11 + 2 + 1 + 7 = 21),
and every other class is identical to the baseline. Of the 21, **7 are S06's own file** and 14 are
S05's, so S06 grows the suite by 7 and leaves the other 387 names where they were. 373 + 21 = 394,
which is the same total the test runner reports, so the accounting closes.

One method detail, recorded because it costs time to rediscover: this repository attributes tests with
`[Fact]`, `[StaFact]`, `[StaTheory]`, `[Theory]` **and a project-defined `[DispatcherFact]`**
(`TrayIconRecoveryTests` uses it for all 11 of its tests). An extractor that matches only xUnit's own
attribute names silently reports zero tests for that class and under-counts the delta by 11 - the
first pass of this accounting did exactly that. The correct pattern is "any attribute whose name ends
in `Fact` or `Theory`".

### 3. The exported surface is unchanged, and the library code with it

| Claim | Evidence at this revision |
|---|---|
| Exactly the seven documented public types (D034) | `PackagePurityTests.Public_surface_is_only_the_documented_types` and `TrayIcon_exposes_the_context_menu_property_without_adding_a_public_type` both green (row 3, 6/6). The assembly-level `XmlnsDefinition`/`XmlnsPrefix` attributes add no exported type - an attribute is metadata, not a type |
| No balloon dependency property (D031) | the only public dependency properties on `TrayIcon` are `IconSourceProperty`, `ToolTipTextProperty`, `VisibleProperty`, `MenuActivationProperty` and the shadowed `ContextMenuProperty`; a balloon stays a method call, and `BalloonTipIcon`/`BalloonTipOptions` are the S04 enums, unchanged |
| No new public type for the menu (D002/D010/D015) | the shadowed `ContextMenuProperty` is asserted to be owned by `TrayIcon`, non-read-only, `null` by default, and resolvable through `DependencyPropertyDescriptor` - the same pin S03 wrote |
| No dependency drift | `Library_csproj_has_no_package_reference`, `Loaded_library_references_only_framework_assemblies` and `Library_targets_three_windows_tfms` green; no assembly reference other than the platform's own |
| The library's own code is untouched by this slice | `git diff --stat 4ccd931..HEAD -- src` reports two files, `Properties/AssemblyInfo.cs` (+22) and `TrayIcon.cs` (-18); the `TrayIcon.cs` change is **S05's** recovery work (`git log --oneline -- src/Trustsoft.NotifyIcon/TrayIcon.cs` ends at S05's `46af54f`/`895d6ad`), and S06's own commits touch `src/` exactly once: `e25498b` adds the 22 metadata lines to `AssemblyInfo.cs` |

So the slice's own contribution to the shipped assembly is two assembly-level attributes. That is the
tightest form the S06 claim can take, and it is asserted rather than reviewed: a widened type, a new
public property or a new package reference all fail row 3's green set.

### 4. The code-first path, re-run end to end (Check 5)

Four live runs against the real notification area, all on the sample built from this revision. The
first is the end-to-end run the task plan asks for (register, a real click attempted at the icon, the
menu opened on the library's own open path, a balloon, clean disposal); the other three exist to
attribute two observations that a single run cannot.

| Run | Command shape | Log |
|---|---|---|
| Check 5 | `probe-live <sample.exe> 50 --left-click-after 10 --menu-after 16 --balloon-after 26 --sample-arg --run-seconds --sample-arg 40` | `docs/uat-logs/S06/t05-code-first-e2e.txt` |
| Check 5 control | the same shape with the click argument removed | `docs/uat-logs/S06/t05-code-first-e2e-control-noclick.txt` |
| Check 5 repeat | the Check 5 shape repeated | `docs/uat-logs/S06/t05-code-first-e2e-repeat.txt` |
| Check 6 | `probe-live <sample.exe> 30 --click-after 12 --sample-arg --run-seconds --sample-arg 18` | `docs/uat-logs/S06/t05-code-first-click-attempt.txt` |

**Check 5, the run itself** (`t05-code-first-e2e.txt`). Code-first mode registered one icon and said
so before anything else (`tray icon registered, rotating 3 frames every 1s`, `context menu assigned
with 2 item(s)`). At t=10 the probe injected a real **left** click at the icon's own shell-reported
rectangle, `(1470,1164)`. At t=16 the sample requested the menu through the library's own open path:

```
sample| [sample] menu opened: popup=0x25800E2 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;97e0a478-...] rect=1446,1045 296x83 dpi=144 scale=1.5 owner=0x0 cursor=1915,1199 bottomLeftDip=964,752
sample| [sample] menu dismissed.
sample| [sample] raw callback hwnd=0x1E00198 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
sample| [sample] raw callback hwnd=0x1E00198 msg=0x0401 event=0x0405 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010405
sample| [sample] balloon preview clicked: event=PreviewBalloonTipClicked phase=tunnel - the main phase must follow unless a handler sets Handled.
sample| [sample] balloon clicked: event=BalloonTipClicked phase=bubble - the shell accepted a click on the balloon.
sample| [sample] tray icon disposed - it must have left the notification area.
sample| [sample] totals: raw callback lines=2, ..., clicks=0, ..., balloon show requests=1 (self=1), balloon clicked deliveries=1, balloon preview deliveries=1, menu opens=1, menu dismissals=1.
[probe] icons-in-notification-area: 1
[probe] sample-exited at t=42s with exit code 0
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x1E00198 uID=1)
```

Read against the two cross-checks: the popup rectangle is the **identical** `1446,1045 296x83
dpi=144 scale=1.5` that Checks 3 and 4 recorded, over the icon at `rect=(1446,1128,1494,1200)`; the
GDI series is `13 -> 15 -> 17` (the code-first rotation) then `29` at the open - the same **+12**
popup step Check 4 measured - and `26` on the reading after disposal; there are 39 consecutive
`icon=present` readings, one icon in the notification area in the run's own final count, and `gone`
after the process disposed and exited with code 0. So register, menu-at-the-icon, an accepted balloon
(`NIN_BALLOONSHOW`), and clean disposal all hold in code-first mode on this revision: the declarative
path did not break the one that was already working.

**The click-driven half is `NOT OBSERVED`, and it is the instrument that fails, not the library.**
Check 5's left click and Check 6's right click were both synthesised at the icon's own shell-reported
rectangle, and both produced `clicks=0` and `raw callback lines=0` in the sample - F1, now reproduced
in code-first mode as well as declaratively. The two buttons fail identically at the same position,
which places the failure at the tray surface (Windows 11's overflow flyout owns the icon) rather than
at the library's decode. A **click-driven balloon** therefore stays `NOT OBSERVED`, exactly as the
earlier F1 record says; what is proven instead is that the shell's balloon callbacks reach a
code-first icon (Check 5) and that the click attributes bind and fire headlessly
(`TrayIconXamlContractTests`, and S02's live record of `clicks=1..3` with the injector that no longer
exists - see the follow-ups).

**`NIN_BALLOONUSERCLICK` (`0x0405`) is a shell behaviour, not the injected click.** Check 5 shows the
balloon-click pair firing live, which the declarative Check 3 explicitly recorded as absent. It is
tempting to credit the left click; the control run rules that out. The control - Check 5's shape with
**no click injected at all** - produced the same `0x0402` then `0x0405` sequence and the same
`balloon clicked deliveries=1`, so the delivery does not depend on the injection. Check 5 repeat, with
the click injected again, produced `0x0402` then `0x0404` twice (`NIN_BALLOONTIMEOUT`) and **no**
`0x0405`. Three runs, three different callback line-ups for the same requested balloon; the accepted
`NIN_BALLOONSHOW` is the one constant. That variance is the shell's, and it is recorded as measured
rather than attributed - the same class of variance the re-verification section already records for
the timeout callback.

**`owner=0x0`: an environment difference, not a regression.** Check 5 and its control both report the
popup's native owner as `0x0`, where the earlier session's identical code-first shape reported the
library's anchor window (`owner=0x2450870`, `owner=0x9802FA`). The library's own documentation names
the mechanism: `TrayMenuAnchorWindow.MakeForeground` is "what makes the popup acquire this window as
its owner", and "the same window, the same placement target and the same offsets with the foreground
call omitted produced an ownerless popup". A refused `SetForegroundWindow` - what a background process
gets while another window holds the foreground - is therefore exactly this reading. The contract is
pinned headlessly and green in this same session: `TrayIconMenuActivationTests` and
`TrayIconMenuContractTests` open real popups and assert `GetWindow(popup, GW_OWNER) == anchor`
(21 passed / 0 failed, row 4). The live diagnostic cannot settle it either way, because the library's
Verbose line that names `foreground=` and `anchor=` is filtered by the trace source's own
`SourceLevels.Warning` switch, which is why `library trace lines=0` appears in every live log here -
both for this new line and for S05's recovery line. What this does **not** show is whether an
ownerless popup is still dismissable by an outside click (S03's T03 record says it is not); that
needs the S03 instrument, and nothing in this task's contract claims it.

### 5. What each piece of evidence proves, and what it does not

| Evidence | Proves | Does not prove |
|---|---|---|
| Build (row 1) | all three TFMs compile from this revision with the declarative resources in the sample, which is the markup compiler's validation of every handler name and signature | any runtime behaviour |
| Full suite (row 2) | 394 tests pass, including the surface pins, the recovery matrix, the S03 popup placement tests and S06's own contracts | nothing about a real shell or a real window; and it is foreground-sensitive when the machine is busy (the environment note above) |
| `TrayIconXamlContractTests` (7 tests) | the declarative property surface resolves from markup, the shadowed `TrayIcon.ContextMenuProperty` is what markup writes, the menu arrives by reference identity, `x:Shared` defaults to shared, markup event attributes bind and fire when the parsed root is a `TrayIcon` subclass, the consumer namespace resolves, a parsed parentless instance disposes cleanly, and the resource-dictionary boundary throws `XamlParseException` | resource-scope event wiring - `XamlReader` binds handler names against the root object only, so that half is **not unit-testable** and is not claimed to be |
| The sample's compiled markup + Check 3 | resource-scope event wiring: the markup compiler validated every attribute at build time, and the declared menu's `Opened`/`Closed` attributes delivered live through the library's own open path | click delivery (F1); anything about a consumer's own assembly, which is what the `tni:` URI in the README is for |
| Check 1 / Check 2 | the declaration registers a working icon, and the declared resources are inert for a code-first run (one icon, by the shell's own count) | that the declarative and code-first paths place the menu differently - Check 3/4 show they do not |
| Check 5 and its control (this section) | the code-first path still registers, opens its menu at the icon, accepts a balloon and disposes cleanly on this revision; the shell's balloon callbacks reach it | a click-driven balloon or menu (F1); any attribution of `0x0405` to the injected click - the control refutes that |
| Check 6 (this section) | a real right click at the icon's own rectangle still does not reach a code-first icon | anything about a click that does arrive |

### 6. Follow-ups recorded rather than closed silently

**What the declarative path cannot express today.**

1. **A declaration nobody looks up registers nothing, silently.** BAML defers instantiation, so an unused declaration is not an error anywhere; that is why this record's checklist requires the `tray icon registered` line before any row is read. There is no diagnostic that reports "a declared icon was never looked up", and none is proposed here.
2. **`x:Shared="False"` must never be used** on the icon: every lookup would mint a second icon. Pinned by `One_resource_key_yields_one_instance`, which asserts the default behaviour - the failure mode is a consumer edit, not a library state.
3. **Resource-scope event attributes are limited to handlers the root object exposes.** A dictionary-rooted element carrying an event attribute cannot be parsed by `XamlReader` at all (pinned as a boundary); BAML is what makes it work, and the handler must be a member of the `ApplicationDefinition`'s class (`App` here).
4. **A declared element of the library's own type cannot use protected hooks.** The sample's no-click self-open path needs `OnTrayClick`, which only a subclass reaches, so the sample declares `local:SampleTrayIcon`. A consumer who declares `tni:TrayIcon` gets the library's public surface only - which is the intended shape, but it means the sample's instrumentation is not itself a consumer-copyable pattern for that one hook.
5. **The local namespace form must not carry `;assembly=`,** and the consumer URI form must be used across assemblies - the `MC3074` trap recorded in Check 3.
6. **Menu data context and bindings on the declarative path are not covered by this slice.** `T06` exists for exactly that (`01-06-PLAN.md`), and the S06 acceptance line about bindings in the menu resolving against the component's `DataContext` is that task's to settle with evidence. **Settled by T06** - see "Menu data context and bindings (T06)" below: the library never writes a menu's `DataContext`, so the consumer sets the menu's own, and the acceptance line now carries that clarification.

**Instrument follow-ups.**

1. **There is no working tray-click instrument in this worktree.** `--click-after` (right click) and the new `--left-click-after` (left click, added by T05) both synthesise real input at the icon's own shell-reported rectangle and neither reaches the tray (F1, reproduced on both buttons). S02/S03 did deliver clicks (`clicks=1..3` recorded in `docs/UAT-S02.md`) with `.gsd/probe-clicks`, a UI-Automation instrument that expanded the `Show Hidden Icons` chevron first; it lived under `.gsd/` (untracked) and is gone from this worktree. A live click-driven check needs that instrument rebuilt - toggle the flyout through UI Automation, then click the element the flyout exposes - not another `mouse_event` at a rectangle the flyout owns.
2. **The library's Verbose diagnostics are invisible live.** `NotifyIconTrace` constructs its `TraceSource` at `SourceLevels.Warning`, so the `TrayIcon menu opened: ... foreground=... anchor=...` line never reaches the sample's attached listener (`library trace lines=0` in every log here). The live instrument therefore cannot explain an ownerless popup, and S05's recovery Verbose line has the same status. Making it readable is a library-side decision about the default switch level (or an API for it), not a sample edit.
3. **The T04 verify line should use a 45 s observation window, not 30 s** - the plan pairs `30` with `--run-seconds 40`, which makes the probe kill the sample and forfeits the clean-dispose claim (recorded in the re-verification section).

**S07 hand-off (in addition to the section below).**

1. **README and packaging:** the declarative run mode, the `tni` URI, and the claim that the declarative path works in a real application now all have evidence behind them (Checks 1, 3, 5); the packaging proof must exclude `scripts/probe-live` and confirm the namespace URI in the package metadata.
2. **CI:** run the suite on a quiet agent or accept the foreground-sensitive `TrayIconMenuActivationTests` class (the environment note above and S03's F5). Two consecutive solo runs in T05 were green at 394/0, so the class is real but not constant.
3. **Suite baseline for later slices:** **394 passed / 0 failed / 0 skipped** at this revision, with 0 tests added by T05. Use that number, not the interim 384.

---

## Menu data context and bindings (T06)

**The claim under test.** The S06 acceptance line (`01-CONTEXT.md`, "Acceptance Criteria") ends with
"bindings in the menu resolve against the component's `DataContext`". Nothing in S06's own plan, tests
or checks covered it, and it stood in tension with the menu contract the library documents and
`TrayIconMenuContractTests` pins - the menu "is the caller's instance", "nothing in this library
inspects, re-parents or mutates the menu's items", and the assigned menu arrives by reference identity.
T06 settles the tension with a measurement, and the acceptance line now carries the clarification
instead of the promise.

**Measured outcome.** The library **never writes a menu's `DataContext`** - it is not one of the values
`OpenMenu` sets (which are `PlacementTarget`, `Placement`, `HorizontalOffset` and `VerticalOffset`) and
nothing clears it in teardown (`TearDownMenu` clears `PlacementTarget` only). The **`TrayIcon`'s own
`DataContext` does not reach the menu**: a `ContextMenu` declared in a resource dictionary has no
logical parent, the library's `ContextMenu` property is its own dependency property rather than
`FrameworkElement.ContextMenu` (so assigning a menu inserts nothing into the icon's logical tree), and
what the library sets as the menu's `PlacementTarget` is its own 1x1 anchor window, whose element tree
carries no data context either. A menu's bindings therefore resolve against the **menu's own
`DataContext`**, and setting it is the consumer's job - `DataContext="{StaticResource ...}"` in markup,
`menu.DataContext = ...` in C# - which is the division of responsibility `TrayIcon.ContextMenu`'s
remarks already document.

### T06 Check 1 - the headless pin

`tests/Trustsoft.NotifyIcon.Tests/TrayIconMenuDataContextTests.cs`, `[StaFact]`, three tests, real
popups opened through the library's own path over `FakeShellApi` (registration, `GetRect` and the
cursor read all go through the scripted seam, so nothing in this file reaches the real notification
area):

| Test | What it pins |
|---|---|
| `The_icons_data_context_does_not_reach_a_menu_declared_in_a_resource_dictionary` | the measurement: with the menu parsed from a `ResourceDictionary` and the icon's `DataContext` set, the open leaves `menu.DataContext` null, the bound item's inherited `DataContext` null and its bound `Header` unresolvable |
| `A_consumer_set_data_context_on_the_menu_is_what_its_bound_items_resolve_against` | the supported mechanism: the consumer's own graph is what the bound item resolves against (same instance, no clone), the icon's `DataContext` does not win over it, and neither the open nor the teardown overwrites or clears it |
| `Assigning_the_menu_re_parents_nothing_and_takes_no_data_context_from_the_icon` | the mechanism's precondition, with no popup at all: the menu keeps no logical parent, never joins the icon's logical tree, keeps its own items - and the assignment applied nothing (no host window, no shell call) |

**Command.**

```
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore \
    --filter "FullyQualifiedName~TrayIconMenuDataContextTests|FullyQualifiedName~TrayIconMenuContractTests|FullyQualifiedName~PackagePurityTests"
```

**Raw evidence** (`gsd_exec` `85bcbbc2-9f5a-4fe3-b71e-3eb32ef121d6`):

```
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 9 s - Trustsoft.NotifyIcon.Tests.dll (net8.0)
```

3 new + the 5 `TrayIconMenuContractTests` + the 6 `PackagePurityTests`: the menu contract this task was
told not to change, and the surface pins (seven documented public types, no balloon dependency
property), are green in the same run as the new file. The new file alone: **3 passed / 0 failed**
(`gsd_exec` `b3915fad-53af-48ca-84a6-1733ec71d923`).

### T06 Check 2 - the live declarative run

**Command** (the task plan's verify line; a 45 s observation window so the sample reaches its own
dispose, per the T04 follow-up above).

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 45 \
    --sample-arg --xaml --sample-arg --run-seconds --sample-arg 40 \
    --sample-arg --open-menu-after --sample-arg 12
```

**Raw evidence** (`docs/uat-logs/S06/t06-declarative-menu-data-context.txt`; `gsd_exec`
`91171a3e-d7b3-4b12-9d59-810ec6c4f4a5`):

```
sample| [sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon.
sample| [sample] tray icon registered from markup (Visible="True" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value).
sample| [sample] context menu taken from markup with 2 item(s); a right click on the icon must open it at the icon.
sample| [sample] --open-menu-after: requesting the menu now, with no click injected.
sample| [sample] menu opened: popup=0x38A05B6 class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;429f9d78-7cca-4b79-8ec9-b9371ba1ebfb] rect=1446,1045 369x83 dpi=144 scale=1.5 owner=0x13D02FA cursor=966,1000 bottomLeftDip=964,752
sample| [sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='declared menu data context'
sample| [sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).
sample| [sample] menu dismissed.
sample| [sample] totals: raw callback lines=0, ..., clicks=0, ..., menu opens=1, menu dismissals=1.
[probe] sample-exited at t=41s with exit code 0
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x2AA00E2 uID=1)
```

**What it says.** The declaration in `App.xaml` now carries the menu's own data context
(`DataContext="{StaticResource TrayMenuData}"`, resolved from the markup-declared
`local:SampleMenuData` resource) and one item bound against it (`Header="{Binding Label}"`); the
`menu data context:` line is printed from the live object graph as the menu opens, so the resolved
text is a reading rather than an inference. Registration, placement at the icon
(`bottomLeftDip=964,752` is the icon's own bottom-left corner in DIP), the declared `Opened`/`Closed`
attributes, one icon in the notification area and a clean dispose (exit code 0, icon gone) all still
hold with a binding in the menu. A binding that had not resolved would print
`item[1].Header=<null>` on this line instead - which is why the line reports the item's act.

**One measured difference from the earlier checks, recorded rather than smoothed over.** The popup is
now `369x83` where S06's Check 3 and Check 4 recorded `296x83`: the second item now renders the
resolved bound text instead of the static "Second item", and a WPF popup's width is driven by its
widest item. The placement anchor - the number the slice's placement claim actually rests on - is
unchanged (`bottomLeftDip=964,752`, left edge `1446`). Nothing about the library moved.

### T06 Check 3 - code-first cross-check of the same mechanism

**Command** - T06 Check 2 with `--sample-arg --xaml` removed.

**Raw evidence** (`docs/uat-logs/S06/t06-code-first-menu-data-context.txt`; `gsd_exec`
`b9eb1b3a-0d6f-4209-b91a-181fd95c1f72`):

```
sample| [sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.
sample| [sample] context menu assigned with 2 item(s); a right click on the icon must open it at the icon.
sample| [sample] menu opened: popup=0x38B05B6 ... rect=1446,1045 377x83 dpi=144 scale=1.5 owner=0x270694 cursor=966,1000 bottomLeftDip=964,752
sample| [sample] menu data context: menu.DataContext=SampleMenuData (set by the consumer) -> item[1].Header='code-first menu data context'
sample| [sample] menu dismissed.
[probe] sample-exited at t=41s with exit code 0
[probe] icons-in-notification-area: 1
[probe] icon-after-exit: gone (...)
```

**What it says.** The two modes reach the same state through the same mechanism, expressed in the two
languages: the declarative menu sets its data context with `DataContext="{StaticResource TrayMenuData}"`
and binds with `Header="{Binding Label}"`; the code-first menu sets `menu.DataContext` and calls
`SetBinding(MenuItem.HeaderProperty, new Binding(nameof(SampleMenuData.Label)))`. Both print the same
line shape with their own resolved value, so the mode is attributable from the line itself. The widths
differ (369 against 377) because the two resolved labels differ in length; the anchor
(`bottomLeftDip=964,752`) is identical, which is what this cross-check is for.

### Suite and surface after T06

| # | Command | Result | Evidence |
|---|---|---|---|
| 1 | `dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore` | exit **0**, **0 warnings, 0 errors** - library on all three TFMs, plus sample and tests | `gsd_exec` `e9d208ce-e11c-442d-a50f-efb2c04e8c97` |
| 2 | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | exit **0**, **397 passed / 0 failed / 0 skipped** (56 s) | `gsd_exec` `992c0b92-8d61-453b-8795-d64f427838f8`; `docs/uat-logs/S06/t06-suite-full.txt` |
| 3 | `PackagePurityTests` | green (6/6 in the filtered run cited in T06 Check 1) - the seven documented public types and the absence of a balloon dependency property are untouched, and T06 changed no library code at all: its changes are the sample, the new test file, and these docs | `gsd_exec` `85bcbbc2-9f5a-4fe3-b71e-3eb32ef121d6` |

394 (S05's exit measurement, independently re-derived by T05) + 3 = 397, and the three added names are
exactly T06's new file, so the baseline accounting closes again.

**Harness note, re-confirmed in this task.** The plan's build line is
`dotnet build Trustsoft.NotifyIcon.sln -c Release`. Inside the `gsd_exec` sandbox the *restore* half of
that command still fails with NuGet's `Value cannot be null. (Parameter 'path1')` for the sample and
test projects even with the `APPDATA`-family variables injected (the library project restores fine);
the evidence above therefore uses `--no-restore` against the assets a normal developer shell already
restored, which changes nothing about what is compiled. This is the harness fact the re-verification
section above records, not a repository fact.

### What T06 does and does not prove

| Evidence | Proves | Does not prove |
|---|---|---|
| `TrayIconMenuDataContextTests` (3 tests) | where a menu's bindings resolve from, that the icon's `DataContext` never reaches the menu, that the library neither writes nor clears a menu's `DataContext` on open or teardown, and that assigning a menu re-parents nothing | resource-scope markup wiring (BAML's job, proven by the sample build and the live run); anything about a real shell |
| T06 Check 2 | a menu declared in `Application.Resources`, with its data context from markup and a bound item, resolves and prints the resolved text live, on the library's own open path, with the declared event attributes still delivering | click delivery (F1); that a consumer's own assembly reaches the same shapes - that is the sample's `local:` namespace difference recorded in Check 3 |
| T06 Check 3 | the code-first menu uses the same mechanism, so the two modes have not diverged | that the two resolved values are equal - they differ by design, and the line names the mode |

---

## Hand-off to S07

- The declarative run command is `-- --xaml`, and it now also takes `--open-menu-after <seconds>`, which opens the *declared* menu with no shell click. The README documents the mode and the namespace URI (`http://schemas.trustsoft.com/notifyicon`, prefix `tni`) is declared in `AssemblyInfo.cs` and should be confirmed alongside the package metadata. The README's snippet is the consumer shape (a consumer's markup lives in another assembly, so it uses that URI); the sample itself cannot, because the element it declares is a type from its own project - and because the local-namespace form must not carry `;assembly=`, which is the `MC3074` trap recorded in Check 3.
- **A menu's data context is the consumer's to set (T06).** The README's XAML snippet should say so next to the `ContextMenu` it shows: a declared menu has no logical parent and the library never writes the menu's `DataContext`, so bindings in the menu resolve against the menu's own data context (`DataContext="{StaticResource ...}"`, or `menu.DataContext = ...` in C#). The declarative and code-first sample menus now both do exactly that and print the resolved text as they open.
- The packaging proof must not include `scripts/probe-live`: it is deliberately absent from `Trustsoft.NotifyIcon.sln` and takes no project reference to the library, so it cannot enter the shipped package or be mistaken for a supported artifact.
- A live click-delivery check for the declarative path is still missing (F1); if S07 wants one, it needs an instrument that can open the Windows 11 overflow flyout, not this one. Nothing in the shipped surface depends on it: the click attributes bind and fire (headless proof) and the same open path is proven live through the declared menu.

## HUMAN OBSERVATION (2026-09-22, project owner, authenticated subjective UAT)

Added 2026-09-22, after the sections above; nothing above is rewritten. The block below records the
project owner's answer from the milestone's authenticated subjective UAT
(`gsd_answer_milestone_subjective_uat`, session-authenticated; verbatim response
**"Accept (Recommended)"**, tested source revision `14ce7da9e43d1c49c1c90d565c5307fd4cd68116`).
The rationale is quoted verbatim in the owner's own words; the machine-measured record above stands
unchanged beside it.

### uat-xaml-ergonomics (criterionId `0b74fd3a-d196-4ad0-9ff6-856ddf378508`)

- Verbatim response: **Accept (Recommended)**
- Owner's rationale (verbatim): «Владелец (разработчик) просмотрел декларативное использование в
  samples/Trustsoft.NotifyIcon.Sample/App.xaml и samples/consumer-proof/: объявление TrayIcon через
  merged ResourceDictionary эргономично.»
- Bears on: the declarative-usage ergonomics this document's live checks exercise (Check 1, T06
  Check 2) - the consumer-developer point of view on the markup surface is now recorded by the person
  it is designed for. No row above is changed; the click-delivery NOT OBSERVED row stands.
