# UAT-S07 - packaging and the consumer proof

**Slice:** M001 / S07 (Packaging and consumer proof)
**Requirements:** R010 (package metadata, English README, licence and XML documentation, installable and usable from a consumer project without additional setup), R011 (no runtime dependency group beyond WPF; no WinForms, no H.NotifyIcon, no WinRT contracts), R012 (built and run on `net8.0-windows`, `net9.0-windows` and `net10.0-windows`)
**Date:** 2026-09-21
**Revision tested:** the `milestone/M001` worktree with the S07/T01, T02 and T03 work applied; the raw logs below carry the exact timestamps and the pack's own sha256.
**Machine:** MINIBOOKX, `MINGW64_NT-10.0-26200` (Git Bash), .NET SDK `10.0.401`, single monitor 1920x1200 at 150 % scale.
**Document status:** **complete.** Assembled per task as the slice ran, then consolidated by T05, which added the environment block, the task plan's verify line as a single invocation, the three milestone-level cross-checks and the R010/R011/R012 verdicts. The reopen fix (section 6) replaced this document's earlier, unmeasured exit-code claim with the contract measured on the shipped script. Every command quoted in this document was run as written; the raw logs and their producers are under `docs/uat-logs/S07/`. **T05 closeout:** the whole pack was then re-measured independently at revision `55a651f` — the plan's verify line, the inspector's six exit-code cases, the three milestone-level cross-checks with their live `--xaml` run and the consumer proof end-to-end — and every claim held (see the last row of the table below and section 3 for what the artifact's sha256 did under the re-run). No file under `src/`, `samples/`, `tests/` or `scripts/` has changed since `7b59f23`, which is the revision the T05 logs record, so those measurements still describe this source.

## Verdict so far

| Claim | Verdict | Evidence |
|---|---|---|
| `dotnet pack -c Release` writes the package into `artifacts/` with the R010 metadata | **PASS** | T01: `docs/uat-logs/S07/t01-package-inspection.txt` — nupkg `Trustsoft.NotifyIcon.1.0.0.nupkg` (325,181 bytes, 12 entries), nuspec `id=Trustsoft.NotifyIcon`, `version=1.0.0`, `authors=Trustsoft`, `<license type="expression">MIT`, `<readme>README.md</readme>`, tags including `wpf` |
| The shipped package contains the three frameworks' assemblies **and** their XML documentation | **PASS** | T02: `lib/net8.0-windows7.0/`, `lib/net9.0-windows7.0/`, `lib/net10.0-windows7.0/`, each with `Trustsoft.NotifyIcon.dll` and `Trustsoft.NotifyIcon.xml`, asserted per framework |
| The nuspec declares no runtime dependency beyond WPF (R011) | **PASS** | T02: zero `<dependency>` entries across the three target-framework groups, and `Microsoft.WindowsDesktop.App.WPF` is the only framework reference. D038 records why the SDK's empty `<group>` elements stay (removing them trips NU5128) and why the assertion is "zero `<dependency>` entries", not "no `<dependencies>` element" |
| Nothing from the sample, the tests or `scripts/probe-live` is packaged | **PASS** | T02: the forbidden-pattern assertion (no entry matching `Sample`, `Tests`, `testhost`, `probe-live`, `consumer-proof`) and the allowed-set assertion (every entry is one of nuspec, README, LICENSE, `lib/<tfm>/assembly+xml`, package metadata) both hold |
| The package carries the README and the licence it advertises | **PASS** | T02: `README.md` and `LICENSE` are package entries, and the nuspec `<readme>` names an entry that exists |
| The inspection is an executable proof, not a reading exercise | **PASS** | T02: `scripts/verify-package.sh` prints one `PASS`/`FAIL` line per assertion and exits non-zero quoting the offender. Three deliberately broken copies produced three non-zero exits with the offending entry quoted — `docs/uat-logs/S07/t02-negative-controls.txt` |
| The inspection is part of the repository's documented verification path | **PASS** | `README.md`, "Repository notes → Build, test and pack": the pack and `bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg` with the explanation of what it asserts. T04 ran that line verbatim after the restructure (`docs/uat-logs/S07/t04-readme-verification.txt`) |
| The library's own build declares the metadata and the non-shipping projects cannot be packed | **PASS** | T01: `PackagePurityTests` 11/11, plus three negative controls that break one invariant each and are restored byte-for-byte (`docs/uat-logs/S07/t01-negative-controls.txt`) |
| A fresh consumer project installs the package from the local feed and shows a working icon on each of the three frameworks (R010, R012) | **PASS** | T03: `docs/uat-logs/S07/t03-consumer-proof.txt` — one build, one surface assertion and one probed live run per framework, each `18/18` presence samples, a `gdi` series that rises only while the menu's popup is created and then never grows again, `sample-exit 0`, `icon-after-exit: gone`. That the package came from `artifacts/` and from nowhere else is proved by two controls: the same restore with a fresh global-packages folder succeeds with the feed present and fails with `NU1101 ... in source(s): artifacts-local-feed` with the feed emptied |
| The consumer sees exactly the documented public surface, checked from its own assembly | **PASS** | T03: `samples/consumer-proof/App.xaml.cs` carries its own copy of the seven documented type names and reports `7 exported type(s)` with a PASS on every framework; it also asserts the package's assembly references carry no WinForms, no System.Drawing and no other tray implementation |
| A shell click at the icon reaches a consumer application | **FAIL, unchanged from S06/F1** | T03 section 8 of the log: the probe injected a right click into the icon's own rectangle (this run's coordinates: `1326,1164`; they follow the tray slot, so the log is the record of where this run's icon sat) and the consumer reported `clicks=0`, `menu opens=0`. The instrument limit `docs/UAT-S06.md` recorded is therefore not a property of the sample; the consumer proof reaches its menu through the documented `OnTrayClick` hook instead, and says so |
| The README documents install, code-first use, declarative use, windowless shutdown, the interaction model, the measured declarative traps and the deliberate exclusions | **PASS** | T04: `README.md` restructured consumer-first, with every quoted command run as written — `docs/uat-logs/S07/t04-readme-verification.txt` (9 commands, 0 failures; `README.md` sha256 `5310d269…` identical before and after the run, the log having been regenerated at the current revision when `ff32421` edited the README's prose afterwards; see *Every quoted command, run as written*). The facts a consumer copies are guarded by `PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface`, with a positive control and five negative controls in `docs/uat-logs/S07/t04-readme-guard-controls.txt` |
| The task plan's whole verify line passes as one invocation on this revision | **PASS** | T05: `docs/uat-logs/S07/t05-plan-verify.txt` — pack exit 0, inspector `VERDICT all 15 assertions hold`, suite **403 passed / 0 failed / 0 skipped** |
| The shipped public surface is still the seven documented types | **PASS** | T05: `PackagePurityTests` **12/12**, `Public_surface_is_only_the_documented_types` among them — `docs/uat-logs/S07/t05-milestone-claims.txt` section 1 |
| The package contains nothing from the sample, the tests, the probe or the consumer proof | **PASS** | T05: the inspector's forbidden-entry assertion and its allowed-set assertion, both green on the artifact this run packed — same log, section 2 |
| The declarative path the README documents is the path the sample runs | **PASS** | T05: identical namespace URI and prefix on both sides, the README's attribute set a subset of the sample's declared set (8 of 17), `TrayIconXamlContractTests` 7/7, and the live `--xaml` run printing `declaration mode: XAML` / `registered from markup` — same log, section 3 |
| The suite's growth across S07 is accounted for by name, not assumed | **PASS** | T05: **+9 test names, 0 removed** (267 → 276 by method name), 6 in `PackagePurityTests` and 3 in `TrayIconMenuDataContextTests`; 394 (S06's closing count) + 9 = the 403 this run reports — `docs/uat-logs/S07/t05-suite-accounting.txt` |
| The artifact's sha256 identifies the pack run, and no claim here rests on it | **PASS** | T05: two consecutive packs are byte-identical and content-identical (`dad165f3…`, 328,642 bytes, 12 entries); the older hash in the same log is the artifact T04's pack left at the same fixed path — `docs/uat-logs/S07/t05-pack-reproducibility.txt` |
| The inspector's exit codes in this document are measured, not asserted | **PASS** | T05: six invocations of the shipped script — `0` on the real package (`VERDICT all 15 assertions hold`), `1` with the path quoted for an absent path, a directory and a non-zip file, and `2` for exactly the two usage-error cases (no argument, `unzip` absent). The driver states each expected code before running and fails on a mismatch, and it asserts the script's own header still says `2 = usage error` — `docs/uat-logs/S07/t05-usage-boundary.txt` |
| The pack's claims hold when a second party re-measures them at the closing revision | **PASS** | T05 closeout at `55a651f`: the plan's verify line re-run as one invocation (pack exit 0; inspector `VERDICT all 15 assertions hold`; suite **403 passed / 0 failed / 0 skipped**, 58 s), `PackagePurityTests` **12/12**, `TrayIconXamlContractTests` **7/7**, the three milestone-level cross-checks re-run green including the live `--xaml` run (`declaration mode: XAML`, `registered from markup`, 3 matched lines), the inspector's six exit-code cases at `0`/`1`/`2` exactly as section 6 records, and the consumer proof re-run end-to-end (three frameworks, `18/18` presence samples, `gdi` rising once with the menu popup and then flat, `sample-exit 0`, `icon-after-exit: gone`, surface assertion `PASS`, click attempt `injected=1 / delivered=0`). The artifact's sha256 moved again within this one revision while every package entry stayed byte-identical — section 3 |

## What this slice does not claim (so far)

- **The package was never pushed to nuget.org, and this document does not claim it was.** Producing and installing the package is the proof this slice offers; publishing is an outward-facing action nobody has authorised. Every install in this document is from the local `artifacts/` folder feed (T03: `dotnet nuget list source` shows it as the only registered source, and the two controls in section 3 of that log show nothing else can supply the package).
- **One machine, one shell build.** All evidence here comes from one Windows machine and one Git Bash environment (`.NET SDK 10.0.401`). Nothing about a second machine, a release pipeline or a signing step has been exercised. The consumer proof adds the sharper version of the same limit: the icon it showed was on this machine's single interactive desktop session, so "the package works on a consumer machine" is evidenced for exactly one machine.
- **The consumer proof is code-first.** It exercises the README's first example — no window, the icon registered from C#, a menu assigned, click handlers, a balloon through `ShowBalloonTip`, clean disposal. The declarative path from a consumer's own assembly is covered elsewhere (the library's `TrayIconXamlContractTests` parses the namespace; `docs/UAT-S06.md` measures the declarative run in the in-repo sample), and T04 is what puts both in front of a reader.
- **No shell interaction was delivered in the consumer application either.** Every consumer run reached its menu through the documented `OnTrayClick` hook, and the injected click reproduced S06's F1 (`docs/uat-logs/S07/t03-consumer-proof.txt`, section 8). The balloon in those runs is a `ShowBalloonTip` request observed from the client side; whether the shell drew it is not claimed here.
- **`scripts/verify-package.sh` checks structure, not semantics.** It asserts that the XML documentation file is *present* beside each framework's assembly; whether the documentation is complete or in English is asserted by `PackagePurityTests` on the source side, not by this script.
- **The inspection reads the package, not the published page.** Whether nuget.org would render the metadata (tags, description length, licence expression) is not verified; only that the nuspec and the entries the SDK produces from this project carry it.
- **The nuspec is never dumped as evidence.** By design: each assertion prints the single value it is about, so a green log names what was checked instead of inviting the reader to re-scan XML.

---

## The consumer proof (T03)

### The requirement this answers

R010's real content is "installable and usable from a consumer project without additional setup", and R012 says the package works on all three target frameworks. Neither can be shown from inside this solution, where the library is a project reference and the repository's `Directory.Build.props` supplies every setting. So T03 added `samples/consumer-proof/`: a small windowless WPF application whose only reference is

```xml
<PackageReference Include="Trustsoft.NotifyIcon" Version="1.0.0" />
```

restored from the local folder feed `artifacts/` that `dotnet pack` writes to. It is the shape a package user has, and it is deliberately not part of this repository's build.

### Why it is outside the solution, and how that is enforced

Three mechanisms, because a project that inherits anything from this repository would stop being a consumer:

- **Absent from `Trustsoft.NotifyIcon.sln`.** `dotnet pack Trustsoft.NotifyIcon.sln` cannot see it, and the solution gives it no project reference. `PackagePurityTests.Non_shipping_projects_are_not_packable_and_the_instruments_stay_out_of_the_solution` asserts the absence, and `scripts/verify-package.sh` fails the pack inspection on any nupkg entry matching `consumer-proof`.
- **Its own empty `Directory.Build.props`.** MSBuild stops its upward walk at the first one it finds, so the repository root's `Nullable`/`LangVersion`/`GenerateDocumentationFile`/`TreatWarningsAsErrors` never reach this project. Everything it needs (`Nullable`, `ImplicitUsings`, `LangVersion`) is written in its own csproj, where a consumer would write it.
- **Its own `nuget.config`.** `<clear />` drops every inherited source, so only `artifacts-local-feed` remains — printed in section 2 of the log, which is `dotnet nuget list source` output rather than a claim.

### The commands and what they printed

**Raw evidence:** `docs/uat-logs/S07/t03-consumer-proof.txt` (415 lines), produced by `bash docs/uat-logs/S07/t03-consumer-proof.sh`.

```
dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
dotnet restore samples/consumer-proof/ConsumerProof.csproj
for tfm in net8.0-windows net9.0-windows net10.0-windows; do dotnet build samples/consumer-proof -c Release -f $tfm; done
samples/consumer-proof/bin/Release/<tfm>/ConsumerProof.exe --surface-only
dotnet run --project scripts/probe-live -c Release --no-build -- <consumerExe> 24 --run-seconds 18 --self-open-menu-after 6 --show-balloon-after 10
```

The per-framework columns, extracted from the three probed runs (section 7 of the log):

```
framework        present(s)  gdi first/max sample-exit  teardown
net8.0-windows   18          13/25         0            gone
net9.0-windows   18          11/25         0            gone
net10.0-windows  18          11/25         0            gone
```

Each row also carried `icons-in-notification-area: 1`, `surface assertion: PASS` and `consumer evidence: 1 menu open(s), 1 dismissal(s), 1 balloon request(s), 0 shell click(s) delivered`. The `gdi` series is printed per run in the log's own verdict block instead of being summarised into one fixed pair of numbers, because the opening samples differ between runs — the first sample can fall while WPF is still realising resources. This run's three series were `13>25`, `11>13>19>25` and `11>13>19>25`, each reaching `25` by `t=8s` and holding it for the last 11 or 12 of its 18 samples; the rise is the menu's own WPF popup window, created when the assigned menu opens at 6 s. The count does not fall back after the dismissal, because WPF keeps the popup's resources, and no sample after the plateau is higher than the one before it — no growth per balloon request, per menu dismissal or on disposal, which is the property the README's GDI paragraph reports for a fixed frozen source.

`dotnet list package` (section 4) resolves `Trustsoft.NotifyIcon 1.0.0` for all three frameworks, and the consumer's own startup lines name the package the run actually loaded:

```
[consumer] package assembly: ...\samples\consumer-proof\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.dll
[consumer] package identity: Trustsoft.NotifyIcon assemblyVersion=1.0.0.0 informationalVersion=1.0.0+e4f9f84ba78453e6a81ae8d5953b6805841a087c
[consumer] this build targets: .NETCoreApp,Version=v8.0
[consumer] public surface: 7 exported type(s): Trustsoft.NotifyIcon.BalloonTipIcon, Trustsoft.NotifyIcon.BalloonTipOptions, Trustsoft.NotifyIcon.TrayErrorEventArgs, Trustsoft.NotifyIcon.TrayIcon, Trustsoft.NotifyIcon.TrayIconClickEventArgs, Trustsoft.NotifyIcon.TrayIconException, Trustsoft.NotifyIcon.TrayMenuActivation
[consumer] PASS the package surfaces exactly the 7 documented public types and no others, checked from this consumer assembly rather than from the library's test project.
[consumer] package assembly references: PresentationCore, PresentationFramework, System.Collections, System.Diagnostics.TraceSource, System.Runtime, System.Runtime.InteropServices, System.Threading, System.Xaml, WindowsBase
[consumer] PASS the package's assembly references no WinForms, no System.Drawing and no other tray implementation.
```

### Where the installed package came from: two controls

"Installable from the package" is easy to assert and easy to be wrong about — a warm global-packages folder or an inherited nuget.org source would make a restore succeed for the wrong reason. Both controls therefore start from a **fresh** `NUGET_PACKAGES` folder (section 3 of the log):

```
control A: local feed present  -> Restored ... (in 316 ms; this run)    exit 0
control B: local feed emptied  -> error NU1101: Unable to find package Trustsoft.NotifyIcon.
                                  No packages exist with this id in source(s): artifacts-local-feed
                                                                        exit 1
```

Control A says the package was installed from `artifacts/` and from nowhere else; control B says no other source — not a machine-wide folder, not nuget.org, not the project reference every other project in this repository uses — can supply it. The restore duration in parentheses is the one that run printed and is not a claim about performance; the log, not this document, is where the exact value lives. The held nupkg is moved back afterwards, and the log shows it back in `artifacts/` before the builds run.

### The interaction path, and the click that still does not arrive

A consumer cannot pass the sample's `--open-menu-after` switch: that belongs to the sample. So the proof carries the same instrument the sample carries — a subclass that calls the documented protected `OnTrayClick` — and the run opens its assigned menu at 6 s with no shell click. Every menu line in the log is therefore attributable to the library acting on the consumer's own menu instance, and the observation is `1 menu open(s), 1 dismissal(s)` per framework.

The stronger path, the physical click, was attempted again here with `--click-after 8` and **no** self-open switch, so anything the consumer logged could only have come from the injected click (section 8):

```
[probe] click injected: right click at (1326,1164) - the icon's own rectangle ...
[consumer] totals: clicks=0, preview deliveries=0, menu opens=0, menu dismissals=0, ..., balloon show requests=1
click columns: injected=1 click(s); delivered to the consumer=0; menu opens=0
```

That is S06's F1 reproduced from the consumer side: the injection reaches the shell's icon rectangle, the shell does not deliver the callback to this application. It is recorded as a **non**-result. Nothing in this slice claims a shell click was tested, and the menu evidence above comes from the hook, not from the click.

### The consumer-side surface assertion

The claim that matters most to a package user is "these are the types I get, and nothing else". `PackagePurityTests.Public_surface_is_only_the_documented_types` asserts it from inside the library's own test project; the consumer proof asserts it from an assembly that only ever installed a nupkg, with its own copy of the seven documented names in the consumer's source. A widened surface fails there even if nobody updated the library's test — which is what makes it a second opinion rather than a restatement. It reaches the assertion through `--surface-only`, a mode that prints the surface and exits without creating an icon, so the check is runnable on its own (`exit 0` on all three frameworks).

### The instrument's own failure paths

An instrument that silently ignored what it did not understand would make a run whose switch was misspelled look like a run that proved the opposite. Section 9 of the log therefore measures each rejection rather than assuming it, on the built consumer executable:

```
--nonsense                       -> exit 2, "unknown argument '--nonsense' - this application does not ignore arguments it does not understand."
--surface-only --run-seconds 5   -> exit 2, "'--surface-only' ... cannot be combined with a run switch"
--run-seconds=-3                 -> exit 2, "'--run-seconds' needs a non-negative number of seconds, got '-3'."
--show-balloon-after             -> exit 2, "'--show-balloon-after' needs a number of seconds"
```

No icon is created on any of those paths, which is why the rejections are checked before any shell state exists. The other two exit codes the consumer owns are `1` (the shell refused the registration: the consumer prints `operation=... win32Error=...` and exits instead of dying in an invisible unhandled exception — code path present, not exercised, because it needs a session with no notification area) and `3` (the package surface was not the documented one).

### Reproducing this section

```
bash docs/uat-logs/S07/t03-consumer-proof.sh    # ~2 min: pack, restore controls, 3 builds, 3 probed runs, 1 click attempt, 4 usage errors -> t03-consumer-proof.txt
bash docs/uat-logs/S07/t03-plan-verify.sh      # ~2 min: the plan's verify command, the purity filter, the full suite, the package inspection -> t03-plan-verify.txt
```

The script repairs the shell environment first (an agent shell can arrive without the Windows known-folder variables, and with that environment the SDK fails inside NuGet's restore-graph evaluation with `Value cannot be null. (Parameter 'path1')`), deletes the previous nupkg so the log names the artifact it produced, builds the probe instrument it is about to run, and prints the raw output of every step. Both producers build the binaries their `--no-build` runs need, because a re-materialized worktree has none (`bin/` and `obj/` are gitignored): measured on this script's first run in such a worktree, where all three probe runs failed to start the instrument and the log read `0` presence samples on every framework — the absence of the instrument masquerading as the absence of the icon, which is why the build now appears in the log verbatim. `artifacts/` and every `*.nupkg` are gitignored, so the log and its producer are the durable evidence, not the package file.

The same re-materialized worktree produced a second, quieter trap in the test command: `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore` exits `0` and prints **nothing at all** while the test project has never been built there, because the test target never runs — a silent zero-count green that reads exactly like a pass. Measured this attempt: two such invocations produced zero bytes of output and exit `0`; the same command reported `Passed! - Failed: 0, Passed: 402` once the test project had been built (`dotnet build tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release`), and `dotnet vstest …Trustsoft.NotifyIcon.Tests.dll --TestCaseFilter:"FullyQualifiedName~PackagePurityTests"` reported `11` passed independently of the SDK's test subcommand — `11` and `402` are this revision's counts, and both grew by one with T04's README guard (the class to 12, the suite to 403). Build before running tests here, and treat an empty `dotnet test` log as “nothing ran”, not as success.

---

## The README as the install and usage document (T04)

### The requirement this answers

R010 names an English README as part of the published package, and `PackageReadmeFile` makes this file the first thing a package consumer reads. Before T04 it was an early-development status note that predated the package existing: it opened with `**Early development. ... NuGet packaging and the release pipeline are not (S07).**` and then listed, under the heading *"Planned, not implemented — nothing below exists yet, so do not code against it"*, a series of features that were implemented, each with its own UAT record. A consumer following that document would have concluded that the package they had just installed did not exist and that the features they were holding were not delivered.

### What changed

The file was rebuilt around the order a consumer's questions actually arrive, with the repository-facing material moved below a horizontal rule rather than mixed into the usage text:

| Section | What it answers |
|---|---|
| Title paragraph | what the library is, what it does not depend on, which frameworks it targets |
| **Install** | the `PackageReference` line, the version and the fact that it is **not on nuget.org**; what else travels in the package (XML documentation, README, licence) |
| **Quick start — code first** | the whole windowless shape in one block: construct on the UI thread, subscribe, assign the caller's own menu, set an `ImageSource`, register with `Visible`, `ShowBalloonTip` |
| **Declarative usage** | the `http://schemas.trustsoft.com/notifyicon` namespace (prefix `tni`) and a self-consistent `Application.Resources` declaration, plus the three facts that bite: build-time handler validation, BAML's deferred construction, `x:Shared` |
| **Windowless shutdown** | `ShutdownMode="OnExplicitShutdown"`, no `StartupUri`, `Dispose` on `Exit`/`SessionEnding`, why no process-exit fallback exists, explorer-restart re-registration, and `TrayIconException` as the named failure of a refused registration |
| **Interaction model** | the four routed click pairs with their `Preview` twins, `MenuActivation`, the menu being the caller's own instance and **never written by the library**, and the balloon being a method call rather than a dependency property |
| **Declarative traps the slices measured** | `MC3074` from an assembly-qualified local `clr-namespace`, and resource-scope event attributes being compiled-XAML only |
| **What this package deliberately is not** | no WinForms / no `System.Drawing.Common` / no `H.NotifyIcon` / no WinRT contracts package; balloons only in v1 with toasts in M002 (D005); no balloon dependency properties (D031); no DPI-driven icon resizing yet; `NOTIFYICON_VERSION_4` only; no `RepositoryUrl`/`PackageProjectUrl` |
| *(measured cost)* | the GDI paragraph, with the bitmap/frozen-vector numbers quoted from the S01 table and the remaining per-distinct-image cost of a fresh vector source |
| **Repository notes** | build, test, pack, `scripts/verify-package.sh`, the sample, `scripts/probe-live`, the CI-less live checklist and the `docs/UAT-S0N.md` index |
| **Licence** | MIT |

The stale `## Status` section was **removed, not re-worded**: its accurate material (what each slice delivered, with its limitations) is now carried by the interaction-model, trap and exclusion sections, each next to the evidence path that measured it, and every limitation is attributed to the document that measured it rather than paraphrased into reassurance. The public surface is named as the seven documented types, which is the list `samples/consumer-proof/` asserts from a consumer assembly — so the README, the library's test and the consumer's test now name the same set.

### Every quoted command, run as written

**Raw evidence:** `docs/uat-logs/S07/t04-readme-verification.txt` (197 lines), produced by `bash docs/uat-logs/S07/t04-readme-verification.sh`. The producer executes each command through `bash -c` so the line in the log is the line in the document, prints `README.md`'s sha256 before and after, and exits with the number of failures:

```
README.md:   sha256 5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526

command                                                                             exit  duration
-------------------------------------------------------------------------------------------------
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release                124*  12s
  (the README documents this invocation as running until the session ends; the harness stopped
   it at 12s, and its output shows the icon registered before that - 0 leftover processes)
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --run-seconds 20   0  25s
dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 20  0  25s
dotnet build Trustsoft.NotifyIcon.sln -c Release                                    0   4s
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows  0  64s
dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release         0   2s
bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg               0   2s
dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release -f net8.0-windows       0   2s
dotnet run --project scripts/probe-live -c Release -- samples/.../Trustsoft.NotifyIcon.Sample.exe 12 --run-seconds 8  0  13s

SUMMARY  9 command(s), 0 failure(s)
README.md sha256 before: 5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526
README.md sha256 after:  5310d269b1296f61ea10d6af7bab605a34de5ac306bede82c95df42addb2a526
```

**The log was regenerated at the corrected revision, and that is the point of the hash pair.** The first run was bound to `e87308ab…`; `ff32421` then added two bullets to *What this package deliberately is not* and one sentence to the repository notes, and since `README.md` is a package entry the header hash — not the run — became the stale part. Re-running the producer at `5310d269…` reproduced the same nine commands, the same nine exits and the same `403` test count, which is what makes the regeneration a check rather than a ritual: had the prose edit broken a quoted command or the guard, the re-run would have shown it. A fresh pack at this revision ships that exact file — `unzip -p artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg README.md` `cmp`s equal to `README.md` at sha256 `5310d269…` — which is what the inspector's README assertion checks by name and what `PackageReadmeFile` resolves to for a consumer.

`*` the only non-zero exit is the documented-watchdog case, and the script fails the run if that command stops for any other reason. Two independent checks came out of the same log: `dotnet test` reported `Passed! - Failed: 0, Passed: 403` — 402 at the T03 revision plus T04's README guard, so the README's build-then-test advice in the repository notes describes a real suite rather than an empty silent pass — and the probed sample run reported `icons-in-notification-area: 1`, `observed-present: yes`, `gdi=13,15,17,17,17,17,17,17` across its eight samples, `sample-exit 0` and `icon-after-exit: gone (hr=0x80004005)` — the same columns every other slice's live evidence uses. The `verify-package.sh` line in the log is the 15-assertion PASS block quoted in the T02 section, re-run against the package this README revision was packed into.

The lines the README tells a consumer to trust were therefore not read for sense: each was executed, and the log records the exit code for each. The command set is exactly the one the document quotes - the developer-facing commands, the two live sample invocations (code-first and declarative, with their documented `--run-seconds` value) and the probe invocation - so nothing in the README is a command nobody ran.

### The guard the restructure left behind, and its five negative controls

Prose cannot be unit-tested, but the facts a consumer copies out of the README can be. `PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface` reads the file `PackageReadmeFile` names and asserts:

- the install line, **built from the library project's own `PackageId` and `Version`**, so a version bump that forgets the document fails instead of publishing a README that tells a consumer to install a version that does not exist;
- the consumer markup namespace `http://schemas.trustsoft.com/notifyicon` and the `xmlns:tni=` declaration that goes with it;
- the five consumer sections (`## Install`, `## Quick start`, `## Declarative usage`, `## Windowless shutdown`, `## Interaction model`) as structural headings rather than prose;
- that `## Repository notes` sits *below* the install line, which is the T04 requirement that repository instructions stay out of a package user's way;
- that all seven documented public type names appear, which is what makes the word *documented* in `Public_surface_is_only_the_documented_types` refer to the readme the package ships rather than to a list that exists only in the test suite.

A guard that has never been seen to fail is not a guard, so the same five facts were each broken in the real README, on a copy-restore cycle that proves the file is byte-identical afterwards.

**Raw evidence:** `docs/uat-logs/S07/t04-readme-guard-controls.txt` (125 lines), produced by `bash docs/uat-logs/S07/t04-readme-guard-controls.sh`:

```
positive control - the README as it stands                          PASS  guard passes (exit 0)
control 1 - install line advertises version 9.9.9                  PASS  guard fails, names "must show the install line a consumer copies"
control 2 - the namespace URI is removed everywhere it appears     PASS  guard fails, names "must document the consumer markup namespace"
control 3 - "## Windowless shutdown" is dropped                     PASS  guard fails, names "must carry a '## Windowless shutdown' section"
control 4 - "## Repository notes" moves above the install line       PASS  guard fails, names "repository-facing content must sit below the consumer content"
control 5 - "BalloonTipOptions" is no longer named                  PASS  guard fails, names "does not name [BalloonTipOptions]"
every control                                                      PASS  README restored byte-for-byte (5310d269…)

SUMMARY  0 control failure(s)
```

Control 2 failed on this script's first run and produced the useful correction in it: mutating one occurrence of the URI left the guard legitimately satisfied by the other two (the URI is documented three times), so the control now rewrites all of them, and the comment in the script records the measured reason. That is the kind of thing a control is for - it caught a control that proved less than its label claimed, before this document could quote it.

### What this section does not claim

- **The README's code samples are not compiled by this task.** There is no literate-testing harness in this repository; the C# example and the XAML snippet are the shape the consumer proof and the in-repo sample already exercise and measure (`samples/consumer-proof/App.xaml.cs`, `docs/UAT-S06.md`), and the two facts that a reader could get wrong from the snippet - the namespace URI and the caller-owned menu `DataContext` - are each pinned by a test (`TrayIconXamlContractTests`, `TrayIconMenuDataContextTests`). A future slice could add a compiled example; this one does not have one, and does not claim it.
- **The English requirement is asserted on the source side, not by a language detector.** `PackagePurityTests` pins the documentation flags and the metadata text, and the README guard pins the strings a consumer copies; nothing here runs a spellchecker or a language classifier over the prose. "English README" is met by the document being written in English (reviewable) and by the XML documentation being English, which is asserted.
- **The links are checked by hand, once.** Every relative link in the README resolves to a file that exists in this repository, and the one anchor (`docs/UAT-S01.md#gdi-evidence-as-measurements-r007`) names a heading that exists (`## GDI evidence, as measurements (R007)`). No link checker runs in CI or in the test suite, so a future edit can break a link without failing anything.
- **The measured numbers in the README are quoted, not re-measured here.** The GDI figures come from the S01 table (`docs/UAT-S01.md#gdi-evidence-as-measurements-r007`), the placement/DPI behaviour from `docs/UAT-S03.md`, the balloon behaviour from `docs/UAT-S04.md` and the teardown/recovery behaviour from `docs/UAT-S05.md`; T04 moved them in front of a consumer and attributed them, and did not repeat the measurements.
- **The guard covers the copied facts, not the prose.** A rewrite that keeps the install line, the namespace, the five headings, the section order and the seven type names still passes, even if every sentence around them became false. What the guard buys is that the *things a consumer types* cannot drift silently; the accuracy of the surrounding claims is carried by the evidence paths beside them and by review. The README's own example code is still not compiled (see above).

### Reproducing this section

```
bash docs/uat-logs/S07/t04-readme-verification.sh    # ~2.5 min: 9 command(s), the two live runs, the probe -> t04-readme-verification.txt
bash docs/uat-logs/S07/t04-readme-guard-controls.sh  # ~15 s: the guard, then five single-fact mutations on restored copies -> t04-readme-guard-controls.txt
```

The producer repairs the shell environment first, for the same measured reason as the T01-T03 scripts (an agent shell can arrive without the Windows known-folder variables and make the SDK fail inside NuGet's restore-graph evaluation). It needs a live desktop session for the two sample runs and the probe; without one the sample reports the refused registration and the probe reports `observed-present: no`, which is why those two lines are quoted here rather than summarised.

---

## Pack inspection as an executable proof (T02)

### The requirement this answers

"The nuspec has no runtime dependency group" is the claim R011 ultimately rests on, and it is exactly the kind of claim that gets asserted from memory: a hand inspection today does not stop a metadata change tomorrow from adding a dependency group silently. The inspection therefore had to be runnable, had to print one line per invariant, and had to fail non-zero naming the offending entry.

### Why a shell script rather than a C# tool

The inspection is zip reading plus a handful of string comparisons, and `unzip` is already what anyone reaches for to look inside a nupkg. A shell script adds no project, no target framework and no restore to the verification path, and — measurably important in this repository — it never invokes the SDK. An instrumented shell here arrives without the Windows profile variables and makes `dotnet build` fail inside NuGet's restore-graph evaluation (`Value cannot be null. (Parameter 'path1')`, measured; see `docs/uat-logs/S07/t01-package-inspection.sh`); `scripts/verify-package.sh` keeps working in that environment. The source-side half of the same claims stays where it was, in `tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs`; this script is the artifact side.

The expected identity is read from `src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj`, not hardcoded, so the check cannot agree with a stale version number — control 3 below is what proves it fails when the package and the project disagree.

### The command and its raw output

```
dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
```

**Raw evidence** (`docs/uat-logs/S07/t02-verify-package.txt`, produced by `docs/uat-logs/S07/t02-verify-package.sh`):

```
package: artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
        sha256 ffd813f0900e5d15704900daaeb637a944ea203b71fa68a40f739bfc17524191
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
verify-package.sh exit=0
```

The `<dependency>` line above is the one that carries R011. Under D038 it is asserted as **zero `<dependency>` entries in any target-framework group** rather than as an absent `<dependencies>` element, because the SDK writes one *empty* `<group targetFramework="..."/>` per lib folder (`net8.0-windows7.0`, `net9.0-windows7.0`, `net10.0-windows7.0`) and deleting that element makes the pack fail with `NU5128` — measured in T01, and the reason `SuppressDependenciesWhenPacking` is not set. The framework-reference assertion is what pins "beyond WPF": WPF is the only framework the package requires.

The sha256 quoted in each log identifies the artifact that log inspected, and is not stable across packs: a nupkg carries entry timestamps and the SDK writes the repository's commit into the nuspec's `<repository>` element, so repacking the same sources produces a different hash — and, for the same reason, a slightly different byte size (the T01 log inspected a 325,181-byte artifact, the T03 log the 325,600-byte one the pack printed in section 1 of that log, separate packs of the same sources and the same `informationalVersion`). The identity assertion, not the hash or the size, is what pins the package to the sources.

### The three negative controls

**Raw evidence** (`docs/uat-logs/S07/t02-negative-controls.txt`, produced by `docs/uat-logs/S07/t02-negative-controls.sh`). Each control copies the real package, breaks exactly one invariant in the copy, and runs the real script against it:

```
## control 1 - a <dependency> entry inside the net8.0-windows group (R011 / D038)
$ bash scripts/verify-package.sh /tmp/.../broken-dependency.nupkg
(exit=1)

  FAIL  the nuspec declares 1 <dependency> entry/entries; offenders:
          <dependency id="H.NotifyIcon" version="9.9.9" />
VERDICT  1 of 15 assertions failed

## control 2 - an entry from the sample under lib/
$ bash scripts/verify-package.sh /tmp/.../broken-sample-entry.nupkg
(exit=1)

  FAIL  the package contains 1 entry/entries it must not; offenders:
          lib/net8.0-windows7.0/Trustsoft.NotifyIcon.Sample.dll
  FAIL  the package contains 1 entry/entries outside the allowed set; offenders:
          lib/net8.0-windows7.0/Trustsoft.NotifyIcon.Sample.dll
VERDICT  2 of 15 assertions failed

## control 3 - the nuspec version disagrees with the library project (1.0.0 -> 9.9.9)
$ bash scripts/verify-package.sh /tmp/.../broken-version.nupkg
(exit=1)

  FAIL  the nuspec version is '9.9.9' but the library project declares '1.0.0'
VERDICT  1 of 15 assertions failed

## the real package is untouched
  before: 1b6f4d13bef1b68ebf5be3de964fe98dd71e533ce72e22ba31d5d99939d473e2
  after:  1b6f4d13bef1b68ebf5be3de964fe98dd71e533ce72e22ba31d5d99939d473e2
  PASS  the real package is byte-for-byte unchanged (325181 bytes)
```

Control 1 is the R011 failure mode itself: a dependency added by a later metadata change. Control 2 catches the "publish the demo" accident from the other side — both the forbidden-pattern assertion and the allowed-set assertion name the entry, which is the double coverage the plan asked for. Control 3 is the control that proves the identity check reads the project rather than trusting the package: it names both values. The sha256 comparison at the end proves the controls ran on copies and left the real package byte-identical.

### What the controls do and do not prove

- They prove the script **exits non-zero and names the offending entry** for a dependency group, a forbidden packaged entry and an identity mismatch. A verifier that has never been seen to fail is not a verifier; this one has been seen to fail three times, on purpose, on copies.
- They do **not** prove the SDK would still produce a correct package after the mutations — they are not a pack-time test. They test the inspector, and only the inspector.
- They do **not** cover every assertion: the README/licence presence, the XML-documentation presence and the framework-reference assertion have no dedicated control. The XML-documentation and metadata assertions are covered from the source side by the T01 controls (`docs/uat-logs/S07/t01-negative-controls.txt`, three named guard failures).
- The script's exit codes, **measured on the shipped script rather than read off it** (`docs/uat-logs/S07/t05-usage-boundary.txt`, produced by `docs/uat-logs/S07/t05-usage-boundary.sh`): `0` when all 15 assertions hold; `1` when a package argument is absent, a directory or not a zip — the offending path is quoted and the usage line is *not* printed; `2` only for a usage error, which means no package argument at all, or `unzip` absent from `PATH`. A caller can therefore distinguish "the package is wrong" (1) from "you invoked the inspector wrongly" (2), and the script's own header states the same contract — the driver asserts that too, so the document and the script cannot drift apart silently again.

### Reproducing this section

```
bash docs/uat-logs/S07/t02-verify-package.sh        # packs, then inspects -> t02-verify-package.txt
bash docs/uat-logs/S07/t02-negative-controls.sh     # three broken copies    -> t02-negative-controls.txt
```

Both scripts write their log under `docs/uat-logs/S07/` and print it. The `artifacts/` directory and every `*.nupkg` are gitignored, so the logs and their producers are the durable evidence, not the package file.

---

## Package metadata and the non-packable projects (T01)

Recorded here because the milestone's acceptance leans on it; the raw logs are `docs/uat-logs/S07/t01-package-inspection.txt` and `docs/uat-logs/S07/t01-negative-controls.txt`.

- The package metadata lives in `src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj` (the one packable project), not in `Directory.Build.props`: repository-wide values would leak `PackageId Trustsoft.NotifyIcon` onto the sample and the tests. `RepositoryUrl` and `PackageProjectUrl` are deliberately absent — this repository has no remote and no project page, and a fabricated link cannot be corrected after publication. `PackagePurityTests` asserts their absence so that adding one later is a deliberate edit.
- Version `1.0.0`, stable, declared once; what would move it is D037 (major for breaking surface, minor for additive API, patch for internal/documentation/packaging-only change).
- The sample, the test project and `scripts/probe-live` declare `IsPackable=false`; the probe additionally cannot enter the package by construction (absent from `Trustsoft.NotifyIcon.sln`, no project reference to the library), which is what keeps its presence oracle independent of the code it checks.
- `PackagePurityTests` 11/11 at this revision, with three negative controls that break one invariant each and are restored byte-for-byte (`cmp`).

## Follow-ups this slice already carries

- **S06 click-delivery instrument limit (F1), re-measured in the consumer application.** A shell click injected at the icon was never observed to arrive in the sample (`docs/UAT-S06.md`), and T03 reproduces the same non-delivery from a consumer process (section 8 of `docs/uat-logs/S07/t03-consumer-proof.txt`). Both the library and its consumer therefore reach the menu through the documented `OnTrayClick` hook in every automated run; a real end-user click is still unmeasured, which is why no run claims one.
- **Foreground-sensitive popup tests.** At the T01 measurement the full suite reported 398 passed / 4 failed, all four being the popup tests that `docs/UAT-S06.md` documents as environment-dependent (`popupOwner=0x0`, `foreground=0x0()`, `setForegroundWindow=False`). At the T03 revision the same suite reported **402 passed / 0 failed** on the same machine (`docs/uat-logs/S07/t03-plan-verify.txt`), so the four are confirmed as an environment property — they pass when the desktop conditions they need are present — and no code change addresses them here. The class remains environment-dependent rather than fixed.
- **Metadata change vs. published package.** Nothing in this repository can retract a version once published; the inspection script is the guard in front of that, not a substitute for a release process.
- **README staleness, partly guarded by T04.** The README's `## Status` section used to say NuGet packaging was not delivered, and the list under it contradicted its own heading (items it labelled "planned, not implemented" were implemented, each with its own UAT record). T04 removed that section entirely rather than re-wording it: the README now leads with install, usage, shutdown, the interaction model, the measured traps and the deliberate exclusions, and the repository-facing material sits below a horizontal rule. Five of the facts a consumer copies are now guarded (`PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface`, five negative controls in `docs/uat-logs/S07/t04-readme-guard-controls.txt`); the prose around them is not, and a sentence can still go stale without failing anything.

---

## The evidence pack, the requirement verdicts and the cross-checks (T05)

**Revision tested:** `7b59f23` (worktree HEAD). T05 edited no library, sample or test file; what it added is
`docs/uat-logs/S07/t05-*.{sh,txt}` and this section.

### The environment, recorded rather than described

Raw: `docs/uat-logs/S07/t05-environment.txt`.

| | |
|---|---|
| Machine | MinibookX |
| Windows | `Microsoft Windows [Version 10.0.26200.9457]` |
| Shell | `MINGW64_NT-10.0-26200 3.6.10-710e5275.x86_64` (Git Bash) |
| Active SDK | `10.0.401`; installed: 6.0.428, 7.0.410, 8.0.425, 9.0.318, 10.0.303, 10.0.401 |
| WindowsDesktop runtimes | 6.0.36, 7.0.20, **8.0.31**, **9.0.20**, 10.0.11, **10.0.12** — the three the package targets are installed and were what the consumer runs executed against |
| Display | one monitor 1920x1200 at 150 % scale (the context of every live run in this slice) |

### The raw logs this section is built from

| Log | Producer | Contents |
|---|---|---|
| `docs/uat-logs/S07/t05-plan-verify.txt` | `t05-plan-verify.sh` | the task plan's verify line as one invocation |
| `docs/uat-logs/S07/t05-milestone-claims.txt` | `t05-milestone-claims.sh` | the three milestone-level cross-checks |
| `docs/uat-logs/S07/t05-pack-reproducibility.txt` | `t05-pack-reproducibility.sh` | the artifact's identity across two consecutive packs |
| `docs/uat-logs/S07/t05-suite-accounting.txt` | inline commands | the suite's growth across S07, by test name |
| `docs/uat-logs/S07/t05-environment.txt` | inline commands | the environment table above |

### 1. The task plan's verify line, run as one invocation

```text
dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore
```

| Command | Exit | Output |
|---|---|---|
| pack | **0** | `Successfully created package '…\artifacts\Trustsoft.NotifyIcon.1.0.0.nupkg'`, sha256 `9731972d…`, 328,650 bytes, 12 entries |
| inspection | **0** | `VERDICT  all 15 assertions hold` — 15 `PASS` lines, no `FAIL` |
| suite | **0** | `Passed!  - Failed: 0, Passed: 403, Skipped: 0, Total: 403, Duration: 57 s` (the log regenerated for the reopen fix; the first run of the same line recorded 58 s on the same 403 tests) |

**The 403 is accounted for, not asserted.** The extractor that counts public test methods by name finds
**267 names at S06's closing revision `3049495` and 276 here: +9 added, 0 removed**, which is exactly
the runner's 394 → 403. The nine are named in `docs/uat-logs/S07/t05-suite-accounting.txt`: six in
`PackagePurityTests` (the T01 metadata/purity/documentation pins and T04's README pin) and three in
`TrayIconMenuDataContextTests` (T06's menu-data-context pins). Because the extractor counts methods and
the runner counts test cases, the equality is also the cross-check that no new `Theory` cases were
smuggled in beside the methods.

### 2. The three milestone-level cross-checks

| Claim | How it was made mechanical | Result |
|---|---|---|
| The shipped surface is still the seven documented types | `PackagePurityTests` filtered run (`Public_surface_is_only_the_documented_types`, plus the context-menu-property pin that asserts the shadowed property adds no type) | **12/12 green** |
| The package carries nothing from the sample, the tests, the probe or the consumer proof | the inspector's forbidden-pattern assertion (patterns `Sample Tests testhost probe-live consumer-proof`) **and** its allowed-set assertion (every entry must be nuspec, README, LICENSE, `lib/<tfm>/assembly+xml` or package metadata) | both **PASS** |
| The declarative path the README documents is the path the sample runs | four comparisons, in the log's section 3: the namespace URI the README writes equals the one `AssemblyInfo.cs` declares with `XmlnsDefinition`, and the prefix equals its `XmlnsPrefix`; every attribute the README's `tni:TrayIcon` snippet writes is one the sample's declared icon element writes; the library type the README names is the one `TrayIconXamlContractTests` parses through that namespace (7/7) and the sample element derives from it (`SampleTrayIcon : TrayIcon`); and the documented live invocation prints `declaration mode: XAML` and `registered from markup` | all **PASS** |

One deliberate difference is recorded rather than smoothed over: the README snippet uses `tni:` because a
consumer's `ApplicationDefinition` lives in another assembly, while the sample must write
`local:SampleTrayIcon` — an assembly-qualified `clr-namespace` for a type in the definition's own project
fails the markup compiler with MC3074, which is the trap the README documents in its own words. The
attribute set, not the namespace spelling, is what the subset check compares.

### 3. What the artifact's hash does and does not identify

`t05-plan-verify.txt` prints the artifact's sha256 before and after the pack it runs. In the run recorded
here — the regenerated run on the revision this document was corrected against — the two readings are
**equal** (`9731972d…`, 328,650 bytes, 12 entries, at the fixed path `artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg`):
the path already held a pack of this same revision, so the repack reproduced it byte for byte. That
is a consequence of what the path held, not a determinism claim, and this log was regenerated for this
revision, so its pair no longer reads the way the earlier run's did. The determinism claim is the one
`t05-pack-reproducibility.txt` measures directly, and it stands on its own:

- two consecutive packs of one unchanged tree are **byte-identical and content-identical** (`dad165f3…`,
  328,642 bytes, the same 12 entries with the same per-entry sha256);
- the value at that fixed path is **not** stable across eras, and that log's comparison section lists what
  earlier logs saw there: `e87308ab…` (the README as T02/T04 shipped it), `e8f551a2…` (T04's own pack,
  `docs/uat-logs/S07/t04-readme-verification.txt`), `ffd813f0…` (T01/T02-era — also the smaller build: the
  T01 and T02 logs record that artifact at 325,181 and 325,169 bytes against T05's 328,642) and `dad165f3…`
  (T05's). `README.md` is a package entry, so a README rebuild changes the bytes for a real reason rather
  than a nondeterministic one. The T04 log named in that list was regenerated afterwards, when `ff32421`
  edited the README again: it now records `5310d269…` (the README at the corrected revision) and
  `f57397ba…` (the pack of that revision), while `e8f551a2…` is the value its earlier run measured;
- the sentence in `t05-pack-reproducibility.txt` about a "T04-vs-T05 hash difference in
  t05-plan-verify.txt" describes the plan-verify log **as it stood when that log was produced**. The
  plan-verify log was regenerated afterwards on the corrected revision, which is why its own pair now reads
  equal; the reproducibility log was not re-run, and every value quoted above is the one it measured.

**Re-measured at the closing revision `55a651f`.** The T05 closeout re-ran this section's experiment and
learned the sharper version of the same lesson. Two consecutive packs with no build in between were again
byte-identical (`f5c14c4a…`, 329,315 bytes, 12 entries; `README.md` in that artifact hashes to the
`5310d269…` the T04 log records, which is the README this revision ships). Later packs of the same clean tree
produced different bytes again (`7aeca7c9…`, then `0ec9358b…` after an explicit `dotnet build -t:Rebuild`)
— and unzipping that `7aeca7c9…` artifact and the `0ec9358b…` one and hashing every entry shows **all 12
entries byte-identical**.
What moves is the zip's stored entry timestamps, which follow the build outputs. So content-identity is the
property that holds across a rebuild, byte-identity holds only for packs taken with no intervening build,
and the hash identifies the pack run in every case. Nothing in R010, R011 or R012 rests on either.

So a nupkg's sha256 identifies the pack run that produced it, not a value to compare across logs. Nothing
in R010, R011 or R012 depends on byte-equality: the inspector's identity assertion reads the id and the
version back out of the project file, and its content assertions are hash-independent.

### 4. Requirement verdicts

#### R010 — published as `Trustsoft.NotifyIcon` with English XML documentation, an English README, a LICENSE and package metadata, installable and usable without additional setup

**PASS**, with one part evidenced by review rather than by test.

| Part of the requirement | Evidence |
|---|---|
| Package metadata a consumer sees | nuspec `id=Trustsoft.NotifyIcon`, `version=1.0.0`, `authors=Trustsoft`, `<license type="expression">MIT`, `<readme>README.md</readme>`, tags including `wpf` (T01 inspection, re-checked in `t05-plan-verify.txt`); pinned source-side by `Library_csproj_declares_the_package_metadata_a_consumer_sees` |
| English README shipped | the package carries `README.md` and the nuspec `<readme>` names that existing entry — both asserted lines of the T05 inspection; the README's structure and facts are pinned by `Readme_documents_the_install_line_usage_and_the_shipped_surface` |
| LICENSE file | `LICENSE` asserted present at the package root |
| XML documentation | `Trustsoft.NotifyIcon.xml` asserted beside each framework's assembly (one line per TFM); source side pinned by `Release_build_emits_xml_documentation_beside_every_target_framework_assembly` (`GenerateDocumentationFile` true in `Directory.Build.props` and a real `T:Trustsoft.NotifyIcon.TrayIcon` entry in the file) |
| Installable and usable from a consumer project without additional setup | T03: one `PackageReference`, one restore from the local feed, one build and one probed live run **per framework**, `18/18` presence samples, a `gdi` series flat at its plateau of `25` once the menu's popup exists, `icon-after-exit: gone` — `docs/uat-logs/S07/t03-consumer-proof.txt` |

**Not evidenced:** the package was never pushed to nuget.org, and how nuget.org would render the metadata
(description length, tag formatting) is unverified. The English-ness of the XML documentation text is
asserted only in the same proxy sense as the description (an ASCII check, D010); the doc comments being
English is review, and this document says so rather than implying a test.

#### R011 — no runtime dependencies beyond the base class library and WPF; no WinForms, no H.NotifyIcon, no WinRT contracts

**PASS.**

| Part of the requirement | Evidence |
|---|---|
| No runtime dependency group beyond WPF | `PASS  no <dependency> entry appears in any of the 3 target-framework group(s) under <dependencies>` and `PASS  the only framework reference in the nuspec is Microsoft.WindowsDesktop.App.WPF` |
| No WinForms, no H.NotifyIcon, no WinRT contracts | three independent checks: `Loaded_library_references_only_framework_assemblies` (the loaded assembly's own reference table), `Library_csproj_has_no_package_reference` and `No_project_declares_a_package_reference_outside_the_test_framework_and_the_packed_library` (a sweep of every csproj/props file), plus the inspector's allowed-set assertion, which would reject a third-party assembly riding along under `lib/`. The consumer proof asserts the same thing from a consumer's side (its own copy reports the package's references carry no WinForms and no other tray implementation) |

**Caveat, recorded because the wording would otherwise read as a technicality:** the SDK writes empty
`<group>` elements into `<dependencies>`, so the nuspec has the element with zero `<dependency>` children
(D038 explains why removing them trips NU5128). The claim is exactly `zero <dependency> entries`, which is
what both the inspector and the tests assert. Test-only references (xUnit, `Microsoft.NET.Test.Sdk`) exist
in the test project and are shown by the same assertions not to be in the package.

#### R012 — builds and runs on `net8.0-windows`, `net9.0-windows` and `net10.0-windows`

**PASS.**

| Part of the requirement | Evidence |
|---|---|
| Builds on all three | T03's three `dotnet build -f <tfm>` runs (one per framework, none standing in for the rest); pinned by `Library_targets_three_windows_tfms` and `Solution_build_outputs_exist_for_all_three_tfms` |
| Runs on all three | T03's three live probed consumer runs, one per framework; and the shipped package carries `lib/net8.0-windows7.0/`, `lib/net9.0-windows7.0/`, `lib/net10.0-windows7.0/`, each with the assembly and its XML documentation |

**Not evidenced:** all three were exercised on one machine with the three WindowsDesktop runtimes installed
(8.0.31, 9.0.20, 10.0.12); no second machine, no CI matrix, no earlier Windows build was tried.

### 5. Follow-ups the milestone leaves open

These are open, not closed, and each names the document or test that carries the detail:

1. **Click delivery was never observed through a real shell click (S06/F1).** Every automated run in this
   slice — sample and consumer alike — reached its menu through the documented `OnTrayClick` hook, and the
   injected click reproduced the same non-delivery from the consumer process (`docs/UAT-S06.md`; section 8
   of `docs/uat-logs/S07/t03-consumer-proof.txt`). A real end-user click remains unmeasured.
2. **The S03 popup tests are foreground-sensitive and environment-dependent.** T01's measurement of the
   full suite saw 398 passed / 4 failed with `foreground=0x0()` and `setForegroundWindow=False`; the same
   suite passed 402/0 and then 403/0 on the same machine, and the T03 rerun saw one run report
   `Failed: 1, Passed: 402` between two 403/0 runs — that failing test's name was not recorded by the
   producer of the day, so `docs/uat-logs/S07/t03-plan-verify.sh` now prints every failing test name it
   is given. The four are not fixed, they are environment-
   dependent (`docs/UAT-S06.md`, `docs/uat-logs/S07/t01-*`).
3. **The GDI cost of handing over a fresh vector image per change.** Every icon change converts a source
   to a new HICON; the measured steady-state count is quoted in the README's "One measured cost" section
   and measured in `docs/UAT-S01.md` (the consumer runs above re-confirm a `gdi` series flat at its
   plateau after the menu popup's rise).
4. **Consumer setup discoveries.** An agent shell can arrive without the Windows known-folder variables
   (the SDK then fails inside NuGet's restore-graph evaluation with `Value cannot be null. (Parameter
   'path1')`), and `dotnet test` in a never-built worktree prints nothing and exits 0. Both are written
   into the README's repository notes, because they cost time to rediscover.
5. **The artifact path holds one file at a time.** `artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg` is
   overwritten by every pack, and `artifacts/` is gitignored; the logs carry the hashes, so a reader can
   see which pack a claim was measured against (section 3 above).
6. **Publication is not attempted.** Nothing in this milestone authorises pushing to nuget.org, and D037's
   version policy is the thing that would have to be consulted first.

### 6. The reopen finding this revision corrects

An earlier revision of this document claimed in three places that `scripts/verify-package.sh` exits 2 —
"usage error" — when its package argument is absent or unreadable. The shipped script does not do that:
exit 2 is reachable only when there is no package argument at all or when `unzip` is absent from `PATH`,
while an absent, directory or non-zip argument exits **1** with the offending path quoted. The claim had
been written from the script's header rather than read off a run, which is the failure class this slice
exists to remove — and the slice's own T02 record had already measured the correct contract
(`.gsd/phases/01-core-tray-icon-for-wpf-without-winforms/S07-T02-SUMMARY.md`), so the document contradicted
its own evidence.

The correction has three parts, and each is on disk rather than in prose:

- the three occurrences (the T02 section's exit-code paragraph, the Failure Modes row and the
  Negative Tests row) state the measured contract — exit 0 = all 15 assertions hold, exit 1 = a bad package
  argument with the path quoted, exit 2 = usage error only;
- `docs/uat-logs/S07/t05-usage-boundary.sh` was added: it states each expected exit code **before** it runs
  the shipped script, fails on a mismatch, asserts that exit 2 occurs in exactly the two usage-error cases
  and in no package-argument case, and asserts that the script's own header still says `2 = usage error`, so
  document and script cannot drift apart silently again. Its six cases are recorded in
  `docs/uat-logs/S07/t05-usage-boundary.txt`, which now carries the Negative Tests row's evidence pointer in
  place of the usage block that covered only the no-argument case;
- the plan's verify line was re-run afterwards (`t05-plan-verify.txt`, regenerated on this revision), so the
  evidence in this section belongs to the corrected revision and not to the one that carried the wrong claim.

### 7. Reproducing this section

```text
bash docs/uat-logs/S07/t05-plan-verify.sh          # -> t05-plan-verify.txt
bash docs/uat-logs/S07/t05-milestone-claims.sh     # -> t05-milestone-claims.txt
bash docs/uat-logs/S07/t05-pack-reproducibility.sh # -> t05-pack-reproducibility.txt
bash docs/uat-logs/S07/t05-usage-boundary.sh       # -> t05-usage-boundary.txt (six exit-code cases)
```

`t05-environment.txt` and `t05-suite-accounting.txt` record the environment commands and the
`git grep`-based name extraction respectively; both are transcriptions of commands run as written.

## Failure Modes

This unit's product is a document plus four shell scripts, so its failure surface is subprocesses and
files, not runtime branches. Each path below was either exercised or deliberately left to the harness,
and this table says which.

| Dependency | Failure path | Handling |
|---|---|---|
| `dotnet pack` / `dotnet build` / `dotnet test` (4–70 s subprocesses) | non-zero exit on a broken build, a failing test, or a shell missing the Windows known-folder variables (`Value cannot be null. (Parameter 'path1')` — measured twice in this slice, in T03 and in T05's first reproducibility probe) | every script captures the exit code (`pack_exit`, `verify_exit`, `suite_exit`, `declarative_exit`), prints it, and exits 1 with `FAILURES ABOVE`. The environment-repair block at the top of each script is the measured fix for the profile-variable failure |
| the packaged artifact (a file at a fixed path) | absent, unreadable, or stale | `verify-package.sh` exits **1** and quotes the offending path when its argument is absent, a directory or not a zip — measured, `docs/uat-logs/S07/t05-usage-boundary.txt`; exit **2** is reserved for a usage error (no argument at all, or `unzip` absent), so a bad package path is never confusable with a mistyped invocation; a stale file is made visible by printing the hash *before* the pack in section 0 of the plan-verify log |
| `unzip` (the inspector's only external tool) | not installed | the script checks for it and exits 2 with a message before running an assertion |
| a check whose extraction matches nothing | a pipeline reports the producer's exit code, so a run that printed nothing can read as a pass | handled explicitly: the attribute extraction asserts both sides are non-empty before the subset check, and the live declarative run writes to a file and asserts the run's exit code **and** at least three matched lines (`declarative run exit=0 matched lines=3`) |
| a hung subprocess | no script imposes a timeout | **not handled by the scripts.** The harness (`gsd_exec`, 600 s) bounds it, and a hang leaves no log file rather than a false pass — recorded here instead of pretended. The longest measured command is the ~58 s suite |

## Load Profile

No runtime load dimension. The unit is one document and four one-shot scripts over a 328 KB artifact:
the costs are a ~4 s pack, a sub-second inspection, a ~58 s suite of 403 tests and an 8 s live sample run,
all CPU-bound and linear in artifact size and test count. Nothing is pooled, cached, rate-limited or
shared — no network, no concurrency, no server, no queue — so there is no resource to protect and no
breakpoint at 10x; the only saturated thing would be a single core's wall time. The one amplifying factor
worth naming is that each script re-runs the suite (or a filtered subset) rather than reusing a previous
result, so a repeated run costs the same again; `gsd_exec_search` is the cheaper path for a repeat.

## Negative Tests

The controls this slice carries. T05 adds no test; it points at the controls that exist, and it hardened
the one instrument of its own that could have passed silently.

| Negative surface | Control | Evidence |
|---|---|---|
| A `<dependency>` entry appears in a target-framework group (R011's own failure mode) | `scripts/verify-package.sh` on a deliberately mutated copy exits 1 and quotes the offending entry | `docs/uat-logs/S07/t02-negative-controls.txt`, control 1 |
| A sample, test or probe entry gets packed | the same script, control 2 — caught from both sides, by the forbidden-pattern assertion and by the allowed-set assertion | same file, control 2 |
| The nuspec version drifts from the version the project declares | the same script, control 3, which names both values (`9.9.9` vs `1.0.0`) rather than only reporting a mismatch | same file, control 3 |
| The metadata, the `IsPackable=false` flags, the package-reference purity, the XML documentation or the README pin breaks | three source-side guard controls (one invariant each, restored byte-for-byte with `cmp`) plus five negative controls for the README pin | `docs/uat-logs/S07/t01-negative-controls.txt` and `docs/uat-logs/S07/t04-readme-guard-controls.txt`, produced by the scripts beside them |
| The instrument itself could pass vacuously (an extractor that finds nothing, a live run whose grep matches nothing) | the hardening T05 added: non-empty extraction on both sides, and a live run whose exit code **and** matched-line count are both asserted | `docs/uat-logs/S07/t05-milestone-claims.txt` |
| An absent, directory or non-zip package argument | `verify-package.sh` exits **1** and quotes the offending path — *not* the usage-error exit 2; exit 2 is reached only by the no-argument case and the `unzip`-absent case | `docs/uat-logs/S07/t05-usage-boundary.txt`, produced by `docs/uat-logs/S07/t05-usage-boundary.sh`: `an absent package path=1`, `a directory as the package argument=1`, `a file that exists but is not a zip=1`, `no package argument at all (a usage error)=2`, `unzip absent from PATH (the other usage-error path)=2`. The row replaces an earlier claim that a bad argument exits 2 — the claim the reopen finding caught |

Suite-side negative assertions, all green at 12/12 in `PackagePurityTests`:
`Library_csproj_has_no_package_reference`, `Loaded_library_references_only_framework_assemblies`,
`No_project_declares_a_package_reference_outside_the_test_framework_and_the_packed_library`,
`Non_shipping_projects_are_not_packable_and_the_instruments_stay_out_of_the_solution`,
`Public_surface_is_only_the_documented_types`.
