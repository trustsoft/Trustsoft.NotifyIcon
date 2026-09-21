# UAT-S07 - packaging and the consumer proof

**Slice:** M001 / S07 (Packaging and consumer proof)
**Requirements:** R010 (package metadata, English README, licence and XML documentation, installable and usable from a consumer project without additional setup), R011 (no runtime dependency group beyond WPF; no WinForms, no H.NotifyIcon, no WinRT contracts), R012 (built and run on `net8.0-windows`, `net9.0-windows` and `net10.0-windows`)
**Date:** 2026-09-21
**Revision tested:** the `milestone/M001` worktree with the S07/T01 and S07/T02 work applied; the raw logs below carry the exact timestamps and the pack's own sha256.
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
| A fresh consumer project installs the package and shows a working icon on each of the three frameworks (R010, R012) | *pending* | S07/T03 |
| The README documents install, code-first use, declarative use and windowless shutdown | *pending* | S07/T04 |

## What this slice does not claim (so far)

- **The package was never pushed to nuget.org, and this document does not claim it was.** Producing and installing the package is the proof this slice offers; publishing is an outward-facing action nobody has authorised. Every install below (once T03 adds them) is from the local `artifacts/` folder feed.
- **One machine, one shell build.** All evidence here comes from one Windows machine and one Git Bash environment (`.NET SDK 10.0.401`). Nothing about a second machine, a release pipeline or a signing step has been exercised.
- **`scripts/verify-package.sh` checks structure, not semantics.** It asserts that the XML documentation file is *present* beside each framework's assembly; whether the documentation is complete or in English is asserted by `PackagePurityTests` on the source side, not by this script.
- **The inspection reads the package, not the published page.** Whether nuget.org would render the metadata (tags, description length, licence expression) is not verified; only that the nuspec and the entries the SDK produces from this project carry it.
- **The nuspec is never dumped as evidence.** By design: each assertion prints the single value it is about, so a green log names what was checked instead of inviting the reader to re-scan XML.

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

The sha256 quoted in each log identifies the artifact that log inspected, and is not stable across packs: a nupkg carries entry timestamps and the SDK writes the repository's commit into the nuspec's `<repository>` element, so repacking the same sources produces a different hash. The identity assertion, not the hash, is what pins the package to the sources.

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

- **S06 click-delivery instrument limit (F1).** A shell click injected at the icon was never observed to arrive; the S06 record describes what was measured instead (`docs/UAT-S06.md`). T03 must say plainly whether its consumer run added a click or whether the same limit applied.
- **Foreground-sensitive popup tests.** At the T01 measurement the full suite reported 398 passed / 4 failed, all four being the popup tests that `docs/UAT-S06.md` documents as environment-dependent (`popupOwner=0x0`, `foreground=0x0()`, `setForegroundWindow=False`); one of the same class also fails in an interactive shell. Not a packaging regression, and not fixed here.
- **Metadata change vs. published package.** Nothing in this repository can retract a version once published; the inspection script is the guard in front of that, not a substitute for a release process.
- **README staleness.** The README's `## Status` section still says NuGet packaging is not delivered; that restructure is S07/T04's task, and T02 added only the packaging line to "Build and test".
