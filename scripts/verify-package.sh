#!/usr/bin/env bash
#
# scripts/verify-package.sh - inspect a packed Trustsoft.NotifyIcon nupkg and assert its invariants.
#
# Why a shell script rather than a small C# tool: the inspection is zip reading plus a handful of
# string comparisons, and `unzip` is already what anyone reaches for to look inside a nupkg. A
# shell script adds no project, no target framework and no restore to the verification path - and
# this script deliberately does NOT invoke the SDK at all, so the artifact check still runs on a
# machine whose dotnet environment is broken (measured in this repository: an instrumented shell
# without the Windows profile variables makes `dotnet build` fail inside NuGet's restore-graph
# evaluation while this script keeps working). The source-side half of the same claims is pinned
# by the tests in tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs; this is the artifact side.
#
# The expected identity is read from the library project file rather than hardcoded, so this check
# cannot silently agree with a stale version number: if the csproj says 1.0.1 and the package says
# 1.0.0, the identity assertion fails and names both values.
#
# Each assertion prints exactly one line and a failure quotes the offending entry, so the output is
# the evidence rather than a summary of it. The nuspec is never dumped wholesale.
#
# Usage (from the repository root):
#   bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
#
# Exit codes: 0 = every invariant holds; 1 = at least one invariant is broken; 2 = usage error.
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
LIBRARY_PROJECT="$ROOT_DIR/src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj"

# The SDK appends the platform version to a `-windows` TFM in the package's lib folder name:
# `net8.0-windows` becomes `lib/net8.0-windows7.0/`. Measured, and recorded in D038 / the S07/T01
# hand-off - asserting `lib/net8.0-windows/` would fail on a correct package.
TFMS=(net8.0-windows7.0 net9.0-windows7.0 net10.0-windows7.0)
ASSEMBLY=Trustsoft.NotifyIcon
DOC=Trustsoft.NotifyIcon.xml

# Entry substrings that must never appear in a shipped package. `Sample` covers the sample project
# and its bin output; `Tests` the test project; `testhost` the VSTest runner that sits beside the
# test assembly; `probe-live` and `consumer-proof` are instruments that are absent from the
# solution precisely so they cannot be packed.
FORBIDDEN_PATTERNS=(Sample Tests testhost probe-live consumer-proof)

# R011: WPF is the only framework the package may require. It is a framework reference, not a
# package dependency, and the SDK writes it into the nuspec.
WPF_FRAMEWORK=Microsoft.WindowsDesktop.App.WPF

checks=0
failures=0

ok()   { checks=$((checks + 1)); printf '  PASS  %s\n' "$1"; }
bad()  { checks=$((checks + 1)); failures=$((failures + 1)); printf '  FAIL  %s\n' "$1"; }
note() { printf '        %s\n' "$1"; }

if [ "$#" -eq 0 ]; then
  printf 'usage: bash scripts/verify-package.sh <package.nupkg> [more.nupkg ...]\n' >&2
  exit 2
fi

if ! command -v unzip >/dev/null 2>&1; then
  printf 'FAIL  unzip is required to inspect a nupkg and is not on PATH\n' >&2
  exit 2
fi

# ---------------------------------------------------------------------------------------------
# The expected package identity, read from the project that produces the package.
# ---------------------------------------------------------------------------------------------
expected_id="$(sed -n 's:.*<PackageId>\([^<]*\)</PackageId>.*:\1:p' "$LIBRARY_PROJECT" 2>/dev/null | head -1)"
expected_version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$LIBRARY_PROJECT" 2>/dev/null | head -1)"

# ---------------------------------------------------------------------------------------------
# Per-package inspection.
# ---------------------------------------------------------------------------------------------
verify_one() {
  local pkg="$1"
  local entries nuspec_name nuspec

  echo "package: $pkg"
  if [ ! -f "$pkg" ]; then
    bad "package file exists and is readable: '$pkg' does not exist (nothing else can be checked)"
    echo
    return
  fi
  if command -v sha256sum >/dev/null 2>&1; then
    note "sha256 $(sha256sum "$pkg" | cut -d' ' -f1)"
  fi
  note "expected identity from src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj: id='${expected_id:-<none declared>}' version='${expected_version:-<none declared>}'"

  if [ -z "$expected_id" ] || [ -z "$expected_version" ]; then
    bad "the library project declares <PackageId> and <Version> (one is missing, so the package cannot be pinned to a declared identity)"
  else
    ok "the library project declares the expected identity id='$expected_id' version='$expected_version'"
  fi

  entries="$(unzip -Z1 "$pkg" 2>/dev/null)"

  echo "-- identity (the nuspec)"
  nuspec_name="$(printf '%s\n' "$entries" | grep -E '^[^/]+\.nuspec$' | head -1)"
  if [ -z "$nuspec_name" ]; then
    bad "the package carries a root-level .nuspec (none found, so every nuspec assertion below is unverifiable)"
    nuspec=''
  else
    nuspec="$(unzip -p "$pkg" "$nuspec_name" 2>/dev/null)"
    ok "the package carries a root-level nuspec: $nuspec_name"

    local nuspec_id nuspec_version
    nuspec_id="$(printf '%s\n' "$nuspec" | sed -n 's:.*<id>\([^<]*\)</id>.*:\1:p' | head -1)"
    nuspec_version="$(printf '%s\n' "$nuspec" | sed -n 's:.*<version>\([^<]*\)</version>.*:\1:p' | head -1)"

    if [ "$nuspec_id" = "$expected_id" ]; then
      ok "the nuspec id is '$expected_id'"
    else
      bad "the nuspec id is '${nuspec_id:-<absent>}' but the library project declares '$expected_id'"
    fi

    if [ "$nuspec_version" = "$expected_version" ]; then
      ok "the nuspec version is '$expected_version'"
    else
      bad "the nuspec version is '${nuspec_version:-<absent>}' but the library project declares '$expected_version'"
    fi
  fi

  echo "-- dependency groups (R011)"
  if [ -z "$nuspec" ]; then
    bad "no <dependency> entry appears in any target-framework group (unverifiable: the package has no nuspec)"
  else
    # D038: the SDK writes one EMPTY <group targetFramework="..."/> per lib folder. That element is
    # how the nuspec declares which frameworks the package has a lib for, not a dependency claim -
    # removing it makes the pack fail with NU5128. R011 is therefore asserted as zero <dependency>
    # ENTRIES, named entry by entry when one appears.
    local dep_lines dep_count dep_block group_count
    dep_lines="$(printf '%s\n' "$nuspec" | grep -oE '<dependency[ />][^>]*>?' | sed 's/^[[:space:]]*//' || true)"
    dep_count="$(printf '%s\n' "$dep_lines" | grep -c . || true)"
    # Only the groups INSIDE <dependencies> are dependency groups; the groups inside
    # <frameworkReferences> are a different element and are checked separately below.
    dep_block="$(printf '%s\n' "$nuspec" | sed -n '/<dependencies>/,/<\/dependencies>/p')"
    group_count="$(printf '%s\n' "$dep_block" | grep -cE '<group targetFramework=' || true)"
    if [ "$dep_count" -eq 0 ]; then
      ok "no <dependency> entry appears in any of the $group_count target-framework group(s) under <dependencies> (the SDK's empty groups, D038)"
    else
      bad "the nuspec declares $dep_count <dependency> entry/entries; offenders:"
      printf '%s\n' "$dep_lines" | sed 's/^/          /'
    fi

    local fw_names offenders extra_fw
    fw_names="$(printf '%s\n' "$nuspec" | grep -oE '<frameworkReference name="[^"]*"' | sed 's:.*name="\([^"]*\)":\1:' | sort -u)"
    extra_fw="$(printf '%s\n' "$fw_names" | grep -vxF "$WPF_FRAMEWORK" | grep . || true)"
    if [ -n "$extra_fw" ]; then
      bad "no framework reference beyond $WPF_FRAMEWORK is declared; offenders:"
      printf '%s\n' "$extra_fw" | sed 's/^/          /'
    elif printf '%s\n' "$fw_names" | grep -qxF "$WPF_FRAMEWORK"; then
      ok "the only framework reference in the nuspec is $WPF_FRAMEWORK"
    else
      bad "the nuspec declares the framework reference $WPF_FRAMEWORK (none found, so the package declares no framework at all)"
    fi
  fi

  echo "-- package content"
  local lib_folders
  lib_folders="$(printf '%s\n' "$entries" | grep -E '^lib/[^/]+/' | sed 's:^lib/\([^/]*\)/.*:\1:' | sort -u)"
  local expected_folders extra_folders missing_folders
  expected_folders="$(printf '%s\n' "${TFMS[@]}" | sort -u)"
  extra_folders="$(printf '%s\n' "$lib_folders" | grep -vxF "$(printf '%s\n' "$expected_folders")" | grep . || true)"
  missing_folders="$(printf '%s\n' "$expected_folders" | grep -vxF "$(printf '%s\n' "$lib_folders")" | grep . || true)"
  if [ -n "$extra_folders" ]; then
    bad "the package carries a lib folder only for the three target frameworks; unexpected folder(s):"
    printf '%s\n' "$extra_folders" | sed 's/^/          lib\//'
  elif [ -n "$missing_folders" ]; then
    bad "the package carries a lib folder for each of the three target frameworks; missing:"
    printf '%s\n' "$missing_folders" | sed 's/^/          lib\//'
  else
    ok "the package carries exactly the three target-framework lib folders: $(printf '%s' "$lib_folders" | tr '\n' ' ')"
  fi

  local tfm missing f
  for tfm in "${TFMS[@]}"; do
    missing=()
    for f in "$ASSEMBLY.dll" "$DOC"; do
      printf '%s\n' "$entries" | grep -qxF "lib/$tfm/$f" || missing+=("lib/$tfm/$f")
    done
    if [ "${#missing[@]}" -eq 0 ]; then
      ok "lib/$tfm/ carries $ASSEMBLY.dll and its XML documentation $DOC"
    else
      bad "lib/$tfm/ is incomplete; missing entry/entries: ${missing[*]}"
    fi
  done

  if printf '%s\n' "$entries" | grep -qxF 'README.md'; then
    ok "the package carries README.md at its root"
  else
    bad "the package carries README.md at its root (missing; PackageReadmeFile would resolve to nothing)"
  fi

  if [ -z "$nuspec" ]; then
    bad "the nuspec <readme> element names the shipped README (unverifiable: the package has no nuspec)"
  else
    local readme_tag
    readme_tag="$(printf '%s\n' "$nuspec" | sed -n 's:.*<readme>\([^<]*\)</readme>.*:\1:p' | head -1)"
    if [ "$readme_tag" = 'README.md' ] && printf '%s\n' "$entries" | grep -qxF "$readme_tag"; then
      ok "the nuspec <readme> names 'README.md' and that entry is in the package"
    else
      bad "the nuspec <readme> names '${readme_tag:-<absent>}' and that entry is in the package (offending entry: '${readme_tag:-<absent>}')"
    fi
  fi

  if printf '%s\n' "$entries" | grep -qxF 'LICENSE'; then
    ok "the package carries the licence file LICENSE at its root"
  else
    bad "the package carries the licence file LICENSE at its root (missing, though the nuspec declares the MIT expression)"
  fi

  local offenders e p
  offenders=()
  while IFS= read -r e; do
    [ -z "$e" ] && continue
    for p in "${FORBIDDEN_PATTERNS[@]}"; do
      case "$e" in
        *"$p"*) offenders+=("$e"); break ;;
      esac
    done
  done <<<"$entries"
  if [ "${#offenders[@]}" -eq 0 ]; then
    ok "no entry belongs to the sample, the tests, the probe or the consumer proof (patterns: ${FORBIDDEN_PATTERNS[*]})"
  else
    bad "the package contains ${#offenders[@]} entry/entries it must not; offenders:"
    printf '%s\n' "${offenders[@]}" | sed 's/^/          /'
  fi

  local unexpected
  unexpected=()
  while IFS= read -r e; do
    [ -z "$e" ] && continue
    case "$e" in
      _rels/.rels | "[Content_Types].xml" | README.md | LICENSE) ;;
      package/services/metadata/core-properties/*.psmdcp) ;;
      lib/*/"$ASSEMBLY.dll" | lib/*/"$DOC") ;;
      *)
        if [ -n "$nuspec_name" ] && [ "$e" = "$nuspec_name" ]; then :; else unexpected+=("$e"); fi
        ;;
    esac
  done <<<"$entries"
  if [ "${#unexpected[@]}" -eq 0 ]; then
    ok "every package entry is one the package is allowed to contain (nuspec, README, LICENSE, lib/<tfm>/assembly+xml, package metadata)"
  else
    bad "the package contains ${#unexpected[@]} entry/entries outside the allowed set; offenders:"
    printf '%s\n' "${unexpected[@]}" | sed 's/^/          /'
  fi

  echo
}

for pkg in "$@"; do
  verify_one "$pkg"
  echo
done

if [ "$failures" -eq 0 ]; then
  printf 'VERDICT  all %d assertions hold\n' "$checks"
  exit 0
fi

printf 'VERDICT  %d of %d assertions failed\n' "$failures" "$checks"
exit 1
