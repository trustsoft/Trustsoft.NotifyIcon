# UAT-S07 - packaging and the consumer proof

**Slice:** M001 / S07 (Packaging and consumer proof)
**Requirements:** R010 (package metadata, English README, licence and XML documentation, installable and usable from a consumer project without additional setup), R011 (no runtime dependency group beyond WPF; no WinForms, no H.NotifyIcon, no WinRT contracts), R012 (built and run on `net8.0-windows`, `net9.0-windows` and `net10.0-windows`)
**Date:** 2026-09-21
**Revision tested:** the `milestone/M001` worktree with the S07/T01, T02 and T03 work applied; the raw logs below carry the exact timestamps and the pack's own sha256.
**Machine:** MINIBOOKX, `MINGW64_NT-10.0-26200` (Git Bash), .NET SDK `10.0.401`, single monitor 1920x1200 at 150 % scale.
**Document status:** assembled per task as the slice runs. T05 consolidates it into the slice's evidence pack; the rows marked *pending* are the ones later tasks will add.

## Verdict so far

| Claim | Verdict | Evidence |
|---|---|---|
| `dotnet pack -c Release` writes the package into `artifacts/` with the R010 metadata | **PASS** | T01: `docs/uat-logs/S07/t01-package-inspection.txt` — nupkg `Trustsoft.NotifyIcon.1.0.0.nupkg` (325,181 bytes, 12 entries), nuspec `id=Trustsoft.NotifyIcon`, `version=1.0.0`, `authors=Trustsoft`, `<license type="expression">MIT`, `<readme>README.md</readme>`, tags including `wpf` |
| The shipped package contains the three frameworks' assemblies **and** their XML documentation | **PASS** | T02: `lib/net8.0-windows7.0/`, `lib/net9.0-windows7.0/`, `lib/net10.0-windows7.0/`, each with `Trustsoft.NotifyIcon.dll` and `Trustsoft.NotifyIcon.xml`, asserted per framework |
| The nuspec declares no runtime dependency beyond WPF (R011) | **PASS** | T02: zero `<dependency>` entries across the three target-framework groups, and `Microsoft.WindowsDesktop.App.WPF` is the only framework reference. D038 records why the SDK's empty `<group>` elements stay (removing them trips NU5128) and why the assertion is "zero `<dependency>` entries", not "no `<dependencies>` element" |
| Nothing from the sample, the tests or `scripts/probe-live` is packaged | **PASS** | T02: the forbidden-pattern assertion (no entry matching `Sample`, `Tests`, `testhost`, `probe-live`, `consumer-proof`) and the allowed-set assertion (every entry is one of nuspec, README, LICENSE, `lib/<tfm>/assembly+xml`, package metadata) both hold |
| The package carries the README and the licence it advertises | **PASS** | T02: `README.md` and `LICENSE` are package entries, and the nuspec `<readme>` names an entry that exists |
| The inspection is an executable proof, not a reading exercise | **PASS** | T02: `scripts/verify-package.sh` prints one `PASS`/`FAIL` line per assertion and exits non-zero quoting the offender. Three deliberately broken copies produced three non-zero exits with the offending entry quoted — `docs/uat-logs/S07/t02-negative-controls.txt` |
| The inspection is part of the repository's documented verification path | **PASS** | `README.md`, "Build and test": the pack and `bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg` with the explanation of what it asserts |
| The library's own build declares the metadata and the non-shipping projects cannot be packed | **PASS** | T01: `PackagePurityTests` 11/11, plus three negative controls that break one invariant each and are restored byte-for-byte (`docs/uat-logs/S07/t01-negative-controls.txt`) |
| A fresh consumer project installs the package from the local feed and shows a working icon on each of the three frameworks (R010, R012) | **PASS** | T03: `docs/uat-logs/S07/t03-consumer-proof.txt` — one build, one surface assertion and one probed live run per framework, each `18/18` presence samples, `gdi 13/25`, `sample-exit 0`, `icon-after-exit: gone`. That the package came from `artifacts/` and from nowhere else is proved by two controls: the same restore with a fresh global-packages folder succeeds with the feed present and fails with `NU1101 ... in source(s): artifacts-local-feed` with the feed emptied |
| The consumer sees exactly the documented public surface, checked from its own assembly | **PASS** | T03: `samples/consumer-proof/App.xaml.cs` carries its own copy of the seven documented type names and reports `7 exported type(s)` with a PASS on every framework; it also asserts the package's assembly references carry no WinForms, no System.Drawing and no other tray implementation |
| A shell click at the icon reaches a consumer application | **FAIL, unchanged from S06/F1** | T03 section 8 of the log: the probe injected a right click into the icon's own rectangle (this run's coordinates: `1518,1164`; they follow the tray slot, so the log is the record of where this run's icon sat) and the consumer reported `clicks=0`, `menu opens=0`. The instrument limit `docs/UAT-S06.md` recorded is therefore not a property of the sample; the consumer proof reaches its menu through the documented `OnTrayClick` hook instead, and says so |
| The README documents install, code-first use, declarative use and windowless shutdown | *pending* | S07/T04 |

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

**Raw evidence:** `docs/uat-logs/S07/t03-consumer-proof.txt` (407 lines), produced by `bash docs/uat-logs/S07/t03-consumer-proof.sh`.

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
net9.0-windows   18          13/25         0            gone
net10.0-windows  18          13/25         0            gone
```

Each row also carried `icons-in-notification-area: 1`, `surface assertion: PASS` and `consumer evidence: 1 menu open(s), 1 dismissal(s), 1 balloon request(s), 0 shell click(s) delivered`. The single step in the GDI series (13 → 25) is the menu's own WPF popup window, created when the assigned menu opens; the count does not return to 13 after the dismissal, because WPF keeps the popup's resources, and from that point the series is unchanged at 25 for the rest of the run — no growth per balloon request, per menu open or per disposal, which is the property the README's GDI paragraph reports for a fixed frozen source.

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
[probe] click injected: right click at (1518,1164) - the icon's own rectangle ...
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

The same re-materialized worktree produced a second, quieter trap in the test command: `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore` exits `0` and prints **nothing at all** while the test project has never been built there, because the test target never runs — a silent zero-count green that reads exactly like a pass. Measured this attempt: two such invocations produced zero bytes of output and exit `0`; the same command reported `Passed! - Failed: 0, Passed: 402` once the test project had been built (`dotnet build tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release`), and `dotnet vstest …Trustsoft.NotifyIcon.Tests.dll --TestCaseFilter:"FullyQualifiedName~PackagePurityTests"` reported `11` passed independently of the SDK's test subcommand. Build before running tests here, and treat an empty `dotnet test` log as “nothing ran”, not as success.

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
- The script's exit codes are `0` (all assertions hold), `1` (at least one broken), `2` (usage error, including an unreadable or absent package argument). A caller can therefore distinguish "the package is wrong" from "you invoked the inspector wrongly".

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
- **README staleness.** The README's `## Status` section still says NuGet packaging is not delivered; that restructure is S07/T04's task, and T02 added only the packaging line to "Build and test".
