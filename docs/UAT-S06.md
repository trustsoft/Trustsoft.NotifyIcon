# UAT-S06 - XAML surface without a visual parent

**Slice:** M001 / S06 (XAML surface without a visual parent)
**Requirements:** R008 (the single class works from C# code and from XAML declared in `Application.Resources`), R009 (the menu and event wiring are expressible in markup)
**Date:** 2026-09-21
**Revision tested:** `milestone/M001` with the S06 work applied (`e25498b` plus the probe additions below)
**Machine:** MINIBOOKX, `Microsoft Windows NT 10.0.26200.0`, single monitor 1920x1200, display scale 150 % (dpi 144)

## Verdict

| Claim | Verdict | Evidence |
|---|---|---|
| A `TrayIcon` declared in `Application.Resources` creates a working icon | **PASS** | Check 1: the declarative run registers, reports its 2-item menu as taken from markup, and disposes cleanly |
| Markup event attributes are validated before the app runs | **PASS** | Check 1: the sample's own markup compilation is the validation - a misspelled handler or a wrong signature fails the build, and the build is clean |
| Markup event attributes deliver at runtime | **NOT OBSERVED** | Check 1: the injected click never reached the icon, so no delivered-click line exists to quote. Recorded below with the measurement rather than claimed |
| The declared resources cost a code-first run nothing (BAML deferral) | **PASS** | Check 2: a code-first run of the same executable ends with exactly one icon in the notification area |
| The declarative path leaves the icon healthy end to end | **PASS** | Check 1: 22 consecutive `icon=present` readings, a flat GDI count, and `icon-after-exit: gone` after a clean dispose |
| The library's exported surface is unchanged by this slice | **PASS** | `PackagePurityTests` green; the namespace attributes add no type. Full suite 391 passed / 0 failed |

## What this slice does not claim

- **No live click delivery.** The one failure in this session is the instrument, not the library: see finding F1. What is proven is that the markup compiler validated every attribute (a build-time fact) and that the declaration produces a real, healthy, well-behaved icon (a live fact).
- **No live menu open in declarative mode.** The menu is proven live in code-first mode by S03's record, and it is proven *declared* here by the 2-item count the app reads back from the declaration. Opening a declared menu live needs a real click at the icon, which is exactly what F1 blocks.
- **No unit test of resource-scope event wiring.** `XamlReader` binds handler names against the root object only, so a dictionary-rooted element with an event attribute cannot be parsed at all - pinned as a boundary in `TrayIconXamlContractTests`. Resource-scope wiring is BAML's job, validated by the sample build and (would-be) delivered by the live run.

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

**Consequence.** The live delivery half of R008/R009 is **NOT OBSERVED** in this record. It is not claimed, and no line is quoted as if a click had been seen. The build-time half - that the markup compiler resolved and type-checked every handler attribute against `App`'s methods - is a real, reproducible fact: the sample does not build without it.

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

---

## Reproducing this

| Run | Log |
|---|---|
| Check 1 (declarative run, with the click attempt) | `docs/uat-logs/S06/check1-declarative-run.log` |
| Check 2 (deferral cross-check, code-first) | `docs/uat-logs/S06/check2-deferral-cross-check.log` |

These logs are tracked on purpose even though the repository's `.gitignore` excludes `*.log`; they were added with `git add -f`. Do not remove them as stray logs.

**Probe capabilities added for this slice** (`scripts/probe-live`): `--click-after <seconds>`, which right-clicks the shell-reported icon rectangle, and an `icons-in-notification-area` count reported with the resolved identity. The click capability is the half that does not work against this tray (F1); the count is what makes Check 2 a measurement.

## Hand-off to S07

- The declarative run command is `-- --xaml`; the README documents it and the namespace URI (`http://schemas.trustsoft.com/notifyicon`, prefix `tni`) is declared in `AssemblyInfo.cs` and should be confirmed alongside the package metadata.
- The packaging proof must not include `scripts/probe-live`: it is deliberately absent from `Trustsoft.NotifyIcon.sln` and takes no project reference to the library, so it cannot enter the shipped package or be mistaken for a supported artifact.
- A live click-delivery check for the declarative path is still missing (F1); if S07 wants one, it needs an instrument that can open the Windows 11 overflow flyout, not this one.
