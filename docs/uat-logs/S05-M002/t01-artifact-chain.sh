#!/usr/bin/env bash
#
# S05/T01 - the packed-artifact chain on the current tree, and the stale-surface baseline.
#
# Why this script exists. The S05 research measured that this worktree has never run the chain S05
# rests on: `artifacts/` is absent, `samples/consumer-proof/bin` is absent, so nothing has ever gone
# `dotnet pack` -> package inspector -> a restore that can only have come from the local folder feed
# -> the consumer's own `--surface-only` assertion here. Three later tasks (T03, T04, T06) stand on
# that chain and on knowing the baseline it produces, and T03 exists because the consumer proof's
# surface list is stale against the M002 package. This task measures the chain and records the
# baseline; it changes no file under src/, tests/, samples/, scripts/ or README.md.
#
# What it does, in order: builds the toast instrument first (a re-materialised worktree has no
# `scripts/probe-toast/bin`, and a later `--no-build` run would then read as "nothing happened"),
# prints the SDK version, records and deletes any existing nupkg (pack is incremental, MEM147, so an
# artifact left over from an earlier run would make an exit 0 meaningless), packs the library,
# records the artifact's size and sha256 as information only (a nupkg is content-reproducible but not
# byte-reproducible, MEM145 - never asserted), runs the package inspector, prints the consumer's own
# source list, measures the environment precondition the chain needs and repairs it when it is
# broken, proves with two controls that the local feed is the only supplier of the library, restores
# and builds the consumer once per target framework, and finally runs the consumer's own
# `--surface-only` assertion once per framework.
#
# The last step is recorded as a BASELINE, not as a failure: `samples/consumer-proof/App.xaml.cs`
# still lists the seven M001 tray types while the M002 package exports twenty, so every reading is
# expected to print `[consumer] FAIL the package surface is not the documented one` naming the
# unexpected `Trustsoft.NotifyIcon.Toast*` types and to exit 3 (SurfaceMismatchExitCode). That red
# reading is the stale consumer proof T03 fixes. It is asserted in its expected shape rather than
# ignored, because a reading of any other shape would mean the plan's premise is wrong, not that the
# surface is fine.
#
# The environment precondition, measured here rather than assumed. The S07/T03 script's control
# shape - a fresh global-packages folder with the consumer's own feed-only nuget.config - used to
# restore successfully, because the SDK's pinned framework packs were present in
# `C:\Program Files\dotnet\packs` (8.0.30 for net8.0, 9.0.19 for net9.0). On this machine a .NET
# servicing update replaced those folders with 8.0.31 / 9.0.20 while SDK 10.0.303 still pins
# AppHostPackVersion 8.0.30 / 9.0.19, so the SDK now asks NuGet for
# `Microsoft.NETCore.App.Host.win-x64` in versions the machine does not have and a feed-only restore
# cannot produce. That is a provisioning gap in the machine, not a defect of the feed or the
# package: the NU1101 list it produces is framework packs only and never `Trustsoft.NotifyIcon`.
# This task records that reading, fetches the pinned pack once through a temporary nuget.config that
# adds nuget.org beside the feed (no repository file is changed), and records the packs the machine
# now has. It also means the two provenance controls below cannot be feed-only on this machine: they
# carry nuget.org as a second source, and control B - with the feed emptied and nuget.org still
# present - is what proves nuget.org cannot supply the library. Both controls therefore require
# nuget.org to be reachable (verified at the top of the run); the consumer's own restore and builds
# in section 6 stay feed-only, as its nuget.config is designed to be.
#
# The script exits 0 when the chain is intact, and non-zero - naming every failed reading - when it
# is not. The failure conditions are exactly: a non-zero toast-instrument build, a non-zero pack
# exit, an inspector verdict other than "all ... assertions hold", a precondition failure that names
# the library, a failed repair, a non-zero consumer restore or build, a control A that did not
# succeed or did not install the library from a source, a control B that did not fail or did not
# name the library, or a baseline reading whose shape is not the predicted one.
#
# The shell environment is repaired first, in the shape S07/T03 uses: this sandbox strips the Windows
# known-folder variables, and with that environment the SDK fails inside NuGet's restore-graph
# evaluation before it reaches the project (measured; `Directory.Build.targets` at the repository
# root re-derives them for MSBuild, but a live child process still needs them). Only absent variables
# are filled in, so a normal login shell is untouched. TEMP/TMP are additionally normalised to a real
# Windows path even when the shell supplies a POSIX-looking one (this Git-Bash shell exports
# TEMP=/tmp), because a child process must resolve the same temp folder the library uses -
# Path.GetTempPath() reads TMP, then TEMP. Nothing on a command line may carry a URL: this shell
# rewrites an argument that looks like one into a path (measured), so the source URLs live in the
# temporary nuget.config files instead.
#
# Usage (from the repository root): bash docs/uat-logs/S05-M002/t01-artifact-chain.sh
#
set -u

# The log below is a run output, not source: `docs/uat-logs/S05-M002/*.txt` is gitignored, because
# the host verification gate re-runs this script as its check while it hashes every
# untracked-but-unignored file in the tree - a log git could see would be recorded as the source
# changing while the checks ran (measured on this task's first attempt: `gsd-source-integrity`
# failed with "Verification target source changed while host checks were running"). The committed
# evidence is this script, and the log is regenerated by every run.
LOG=docs/uat-logs/S05-M002/t01-artifact-chain.txt
CONSUMER=samples/consumer-proof/ConsumerProof.csproj
NUPKG=artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
TFMS=(net8.0-windows net9.0-windows net10.0-windows)
NUGET_ORG=https://api.nuget.org/v3/index.json

# --- environment shim: fills in only what is absent, so a login shell is untouched ---------------
export APPDATA="${APPDATA:-${USERPROFILE:-$HOME}\\AppData\\Roaming}"
export LOCALAPPDATA="${LOCALAPPDATA:-${USERPROFILE:-$HOME}\\AppData\\Local}"
export ProgramData="${ProgramData:-${SYSTEMDRIVE:-C:}\\ProgramData}"
export ALLUSERSPROFILE="${ALLUSERSPROFILE:-$ProgramData}"
export PUBLIC="${PUBLIC:-${SYSTEMDRIVE:-C:}\\Users\\Public}"
export PROGRAMFILES="${PROGRAMFILES:-${SYSTEMDRIVE:-C:}\\Program Files}"
export COMMONPROGRAMFILES="${COMMONPROGRAMFILES:-$PROGRAMFILES\\Common Files}"
export ProgramW6432="${ProgramW6432:-$PROGRAMFILES}"
export CommonProgramW6432="${CommonProgramW6432:-$COMMONPROGRAMFILES}"
export COMSPEC="${COMSPEC:-${SYSTEMROOT:-C:\\WINDOWS}\\system32\\cmd.exe}"
export OS="${OS:-Windows_NT}"
export PATHEXT="${PATHEXT:-.COM;.EXE;.BAT;.CMD}"

case "${TEMP:-}" in
  [A-Za-z]:*) ;;
  *) export TEMP="$LOCALAPPDATA\\Temp" ;;
esac

case "${TMP:-}" in
  [A-Za-z]:*) ;;
  *) export TMP="$TEMP" ;;
esac

SCRATCH="$TEMP/t01-artifact-chain"
RESTORE_CACHE="$SCRATCH/global-packages"
RESTORE_CACHE_B="$SCRATCH/global-packages-feed-emptied"
HOLD="$SCRATCH/held-nupkg"
REPAIR_CONFIG="$SCRATCH/feed-and-nugetorg.nuget.config"
CONTROL_A_CONFIG="$SCRATCH/feed-and-nugetorg.control-a.nuget.config"
CONTROL_B_CONFIG="$SCRATCH/nugetorg-only.control-b.nuget.config"
GLOBAL_PACKAGES="${NUGET_PACKAGES:-${USERPROFILE:-$HOME}/.nuget/packages}"
HOST_PACK_ID=microsoft.netcore.app.host.win-x64

mkdir -p "$SCRATCH" "$HOLD"

DOTNET_VERSION="$(dotnet --version 2>&1)"
DOTNET_DIR="$(dirname "$(command -v dotnet)")"
PACKS_HOST_DIR="$DOTNET_DIR/packs/Microsoft.NETCore.App.Host.win-x64"

# Absolute feed path in a form NuGet accepts on Windows, for the temporary nuget.config files.
FEED_ABS="$(pwd)/artifacts"
if command -v cygpath >/dev/null 2>&1; then
  FEED_ABS="$(cygpath -m "$FEED_ABS" 2>/dev/null || printf '%s' "$FEED_ABS")"
fi

PROBE_BUILD_EXIT=0
PACK_EXIT=0
NUPKG_SIZE=0
NUPKG_SHA=""
INSPECTOR_EXIT=0
INSPECTOR_VERDICT=""
SOURCE_EXIT=0
NETWORK_EXIT=0
PRECONDITION_EXIT=0
PRECONDITION_NAMES_LIBRARY=0
REPAIR_NEEDED=0
REPAIR_EXIT=-1
HOST_PACKS_BEFORE=""
HOST_PACKS_AFTER=""
STALE_CLEARED=0
PROVENANCE_FAILURES=0
CONTROL_A_EXIT=0
CONTROL_A_LIBRARY=0
CONTROL_B_EXIT=0
CONTROL_B_NAMES_LIBRARY=0
CONTROL_B_NAMES_PLATFORM=0
RESTORE_EXIT=0
BUILD_FAILURES=0
BUILD_SUMMARY=""
BASELINE_AS_PREDICTED=0
BASELINE_UNEXPECTED=0
FINAL_EXIT=0

{
  echo "# S05/T01 raw evidence: the packed-artifact chain, and the stale-surface baseline"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "worktree: $(pwd)"
  echo "sdk (dotnet --version): $DOTNET_VERSION"
  echo "global-packages folder (NUGET_PACKAGES or default): $GLOBAL_PACKAGES"
  echo "environment: APPDATA='$APPDATA' LOCALAPPDATA='$LOCALAPPDATA' PROGRAMFILES='$PROGRAMFILES'"
  echo "             TEMP='$TEMP' TMP='$TMP' PATHEXT='$PATHEXT'"
  echo
  echo "## 0. what this task measures, and what it is not"
  echo "  samples/consumer-proof is a windowless WPF application whose only reference is"
  echo "  <PackageReference Include=\"Trustsoft.NotifyIcon\" Version=\"1.0.0\" />, restored from"
  echo "  artifacts/ through its own nuget.config (which clears every inherited source). It is absent"
  echo "  from Trustsoft.NotifyIcon.sln and carries its own empty Directory.Build.props, so it inherits"
  echo "  neither a project reference nor a build setting of the repository that produced the package."
  echo
  echo "  This task measures that chain and records the baseline it produces. It edits nothing under"
  echo "  src/, tests/, samples/, scripts/ or README.md."
  echo
  echo "## 1. the toast instrument, built first"
  echo "   A re-materialised worktree has no scripts/probe-toast/bin (bin is gitignored), and the later"
  echo "   slices run it with --no-build: an unbuilt instrument turns a probe run into 'nothing"
  echo "   happened' rather than a failure. It is built here so the log names one binary."
  echo '$ dotnet build scripts/probe-toast/ProbeToast.csproj -c Release'
  dotnet build scripts/probe-toast/ProbeToast.csproj -c Release > "$SCRATCH/probe-toast-build.txt" 2>&1
  PROBE_BUILD_EXIT=$?
  grep -E " error | warning |Build succeeded| -> " "$SCRATCH/probe-toast-build.txt" \
    || tail -n 12 "$SCRATCH/probe-toast-build.txt"
  echo "  reading: probe-toast build exit=$PROBE_BUILD_EXIT"
  echo
  echo "## 2. pack the library the consumer will install"
  echo "   (any existing nupkg is recorded and then deleted first: pack is incremental, MEM147, so an"
  echo "    artifact left over from an earlier run would make a later exit 0 mean nothing)"
  previous="$(ls artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null || true)"
  if [ -n "$previous" ]; then
    printf '%s\n' "$previous" | while IFS= read -r p; do
      echo "   previous package found: $p ($(stat -c%s "$p" 2>/dev/null || echo '?') bytes, sha256 $(sha256sum "$p" | cut -d' ' -f1))"
    done
  else
    echo "   previous package found: none - this worktree has no artifacts/ package yet"
  fi
  echo '$ rm -f artifacts/Trustsoft.NotifyIcon.*.nupkg'
  rm -f artifacts/Trustsoft.NotifyIcon.*.nupkg
  mkdir -p artifacts
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release > "$SCRATCH/pack.txt" 2>&1
  PACK_EXIT=$?
  if [ "$PACK_EXIT" != 0 ] && grep -q "NU1900" "$SCRATCH/pack.txt"; then
    echo "   NU1900: the audit endpoint is unreachable and TreatWarningsAsErrors turns that into an"
    echo "   error. This invocation is retried with -p:NuGetAudit=false; no repository property and no"
    echo "   NuGet.config is changed."
    echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release -p:NuGetAudit=false'
    dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release -p:NuGetAudit=false \
      > "$SCRATCH/pack.txt" 2>&1
    PACK_EXIT=$?
  fi
  grep -E " error | warning |Successfully created package" "$SCRATCH/pack.txt" \
    || tail -n 12 "$SCRATCH/pack.txt"
  echo "  reading: pack exit=$PACK_EXIT"
  if [ -f "$NUPKG" ]; then
    NUPKG_SIZE="$(stat -c%s "$NUPKG" 2>/dev/null || echo 0)"
    NUPKG_SHA="$(sha256sum "$NUPKG" | cut -d' ' -f1 | tr -d '\\')"
    echo "  reading: nupkg=$NUPKG size=${NUPKG_SIZE} bytes sha256=$NUPKG_SHA"
    echo "           (information only: a nupkg is content-reproducible but not byte-reproducible, MEM145)"
  else
    echo "  reading: nupkg=$NUPKG is absent after the pack"
  fi
  echo
  echo "## 3. the package inspector, from the artifact"
  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg'
  bash scripts/verify-package.sh "$NUPKG" > "$SCRATCH/inspector.txt" 2>&1
  INSPECTOR_EXIT=$?
  sed 's/^/  /' "$SCRATCH/inspector.txt"
  INSPECTOR_VERDICT="$(grep -E '^VERDICT' "$SCRATCH/inspector.txt" | tail -1)"
  echo "  reading: inspector exit=$INSPECTOR_EXIT verdict='$INSPECTOR_VERDICT'"
  echo
  echo "## 4. the only sources the consumer's own config reaches, and the network the controls need"
  echo '$ dotnet nuget list source --configfile samples/consumer-proof/nuget.config'
  dotnet nuget list source --configfile samples/consumer-proof/nuget.config > "$SCRATCH/sources.txt" 2>&1
  SOURCE_EXIT=$?
  sed 's/^/  /' "$SCRATCH/sources.txt"
  echo "  reading: source list exit=$SOURCE_EXIT"
  echo '$ dotnet nuget list source --configfile NuGet.config'
  dotnet nuget list source --configfile NuGet.config > "$SCRATCH/sources-root.txt" 2>&1
  sed 's/^/  /' "$SCRATCH/sources-root.txt"
  echo "   (the repository's own config is the one the library, the tests and the sample restore"
  echo "    through; the consumer clears it. Neither config is modified by this script.)"
  echo "   reachability of the second source the two controls below need (they cannot be feed-only on"
  echo "   this machine - section 5 measures why):"
  if command -v curl >/dev/null 2>&1; then
    network_code="$(curl -s -o /dev/null -w '%{http_code}' -m 20 "$NUGET_ORG" 2>/dev/null || echo 000)"
    echo "  reading: $NUGET_ORG answered http $network_code"
    case "$network_code" in
      2*|3*) NETWORK_EXIT=0 ;;
      *) NETWORK_EXIT=1 ;;
    esac
  else
    echo "  reading: curl is not on PATH, so nuget.org reachability is not recorded here"
    NETWORK_EXIT=0
  fi
  echo
  echo "## 5. the environment precondition, and the repair it needs"
  echo "   The S07/T03 control shape - a fresh global-packages folder with the consumer's own feed-only"
  echo "   config - is measured first. Since the machine's .NET servicing update, the SDK's pinned"
  echo "   framework packs are no longer present locally:"
  echo "     $PACKS_HOST_DIR: $(ls "$PACKS_HOST_DIR" 2>/dev/null | tr '\n' ' ')"
  echo "     $GLOBAL_PACKAGES/$HOST_PACK_ID: $(ls "$GLOBAL_PACKAGES/$HOST_PACK_ID" 2>/dev/null | tr '\n' ' ')"
  echo "   (the SDK pins AppHostPackVersion 8.0.30 for net8.0 and 9.0.19 for net9.0, so a pack folder"
  echo "    holding 8.0.31 / 9.0.20 cannot serve it and the pack has to come from a source)"
  rm -rf "$RESTORE_CACHE"
  echo "\$ NUGET_PACKAGES='<fresh>' dotnet restore samples/consumer-proof/ConsumerProof.csproj --force"
  NUGET_PACKAGES="$RESTORE_CACHE" dotnet restore "$CONSUMER" --force > "$SCRATCH/precondition.txt" 2>&1
  PRECONDITION_EXIT=$?
  grep -oE "Unable to find package [A-Za-z0-9._-]+" "$SCRATCH/precondition.txt" | sort -u | sed 's/^/    /'
  echo "  reading: fresh-cache feed-only restore exit=$PRECONDITION_EXIT (non-zero expected on this machine)"
  if grep -q "Unable to find package Trustsoft.NotifyIcon" "$SCRATCH/precondition.txt"; then
    PRECONDITION_NAMES_LIBRARY=1
    echo "  the failure names the library itself, so the feed and not only the machine is at fault"
  else
    echo "  the failure names framework packs only: the library is not implicated, and no reading above"
    echo "  says the feed is broken - the machine simply cannot provision the SDK's pinned packs locally"
  fi
  echo
  echo "   Repair: the pinned packs are fetched into the machine's global-packages folder through a"
  echo "   temporary nuget.config that adds nuget.org beside the feed. Path conversion is deliberately"
  echo "   not involved - the source URLs live in the config file, never on a command line."
  if [ "$PRECONDITION_EXIT" != 0 ] && [ "$PRECONDITION_NAMES_LIBRARY" = 0 ]; then
    REPAIR_NEEDED=1
    HOST_PACKS_BEFORE="$(ls "$GLOBAL_PACKAGES/$HOST_PACK_ID" 2>/dev/null | tr '\n' ' ')"
    cat > "$REPAIR_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="artifacts-local-feed" value="$FEED_ABS" />
    <add key="nuget.org" value="$NUGET_ORG" />
  </packageSources>
</configuration>
EOF
    echo "\$ dotnet restore $CONSUMER --configfile <feed + nuget.org> (default global-packages folder)"
    dotnet restore "$CONSUMER" --configfile "$REPAIR_CONFIG" > "$SCRATCH/repair.txt" 2>&1
    REPAIR_EXIT=$?
    tail -3 "$SCRATCH/repair.txt" | sed 's/^/  /'
    HOST_PACKS_AFTER="$(ls "$GLOBAL_PACKAGES/$HOST_PACK_ID" 2>/dev/null | tr '\n' ' ')"
    echo "  reading: repair exit=$REPAIR_EXIT"
    echo "  reading: $HOST_PACK_ID in the global-packages folder before='$HOST_PACKS_BEFORE' after='$HOST_PACKS_AFTER'"
  else
    echo "  no repair attempted: either the precondition already holds or the failure names the library"
  fi
  echo
  echo "## 5.3 the second precondition: a stale extraction shadows the packed artifact"
  echo "   NuGet resolves a package id and version from the global-packages folder before it consults any"
  echo "   source, and this package's version has not moved since M001 (1.0.0). The machine's cache can"
  echo "   therefore still hold the M001 extraction, in which case a consumer restore of the artifact"
  echo "   just packed loads the older assembly and every reading below is green for the wrong artifact."
  echo "   The dll comparison is the evidence, and the extraction is cleared when it is stale so the next"
  echo "   restore re-extracts the artifact from the feed."
  STALE_CACHE_DIR="$GLOBAL_PACKAGES/trustsoft.notifyicon"
  ARTIFACT_DLL_SHA="$(unzip -p "$NUPKG" lib/net8.0-windows7.0/Trustsoft.NotifyIcon.dll 2>/dev/null | sha256sum | cut -d' ' -f1)"
  STALE_DLL="$STALE_CACHE_DIR/1.0.0/lib/net8.0-windows7.0/Trustsoft.NotifyIcon.dll"
  if [ -f "$STALE_DLL" ]; then
    STALE_SHA="$(sha256sum "$STALE_DLL" | cut -d' ' -f1 | tr -d '\\')"
    echo "  reading: cached extraction $STALE_CACHE_DIR/1.0.0 (dll written $(date -r "$STALE_DLL" '+%Y-%m-%d %H:%M' 2>/dev/null || echo '?'))"
    echo "  reading: cached lib/net8.0-windows7.0/Trustsoft.NotifyIcon.dll sha256=$STALE_SHA"
    echo "  reading: artifact lib/net8.0-windows7.0/Trustsoft.NotifyIcon.dll sha256=$ARTIFACT_DLL_SHA"
    if [ "$STALE_SHA" != "$ARTIFACT_DLL_SHA" ]; then
      echo "  the cached extraction is not the artifact just packed: it is stale, and the consumer would"
      echo "  have loaded it. Clearing the cache entry (a package cache, not a repository file):"
      echo "\$ rm -rf $STALE_CACHE_DIR"
      rm -rf "$STALE_CACHE_DIR"
      STALE_CLEARED=1
    else
      echo "  the cached extraction is byte-identical to the artifact: nothing to clear"
    fi
  else
    echo "  reading: no cached extraction of the package exists, so nothing shadows the artifact"
  fi
  echo
  echo "## 6. the consumer restores and builds, one framework at a time (R012: no framework stands in"
  echo "     for another), through its own feed-only config from here on"
  echo "   (--force, so the restore re-evaluates the package graph now that a stale extraction may have"
  echo "    been cleared above)"
  echo '$ dotnet restore samples/consumer-proof/ConsumerProof.csproj --force'
  dotnet restore "$CONSUMER" --force > "$SCRATCH/restore.txt" 2>&1
  RESTORE_EXIT=$?
  tail -2 "$SCRATCH/restore.txt" | sed 's/^/  /'
  echo "  reading: consumer restore exit=$RESTORE_EXIT"
  for tfm in "${TFMS[@]}"; do
    echo
    echo "\$ dotnet build samples/consumer-proof -c Release -f $tfm"
    dotnet build samples/consumer-proof -c Release -f "$tfm" > "$SCRATCH/build-$tfm.txt" 2>&1
    build_exit=$?
    grep -E " error | warning |Build succeeded| -> " "$SCRATCH/build-$tfm.txt" \
      || tail -n 10 "$SCRATCH/build-$tfm.txt"
    echo "  reading: consumer build $tfm exit=$build_exit"
    BUILD_SUMMARY="${BUILD_SUMMARY}${BUILD_SUMMARY:+, }${tfm}=${build_exit}"
    if [ "$build_exit" != 0 ]; then
      BUILD_FAILURES=$((BUILD_FAILURES + 1))
    fi
    lib_tfm="$(printf '%s' "$tfm" | sed 's/-windows$/-windows7.0/')"
    artifact_dll_sha="$(unzip -p "$NUPKG" "lib/$lib_tfm/Trustsoft.NotifyIcon.dll" 2>/dev/null | sha256sum | cut -d' ' -f1)"
    built_dll="samples/consumer-proof/bin/Release/$tfm/Trustsoft.NotifyIcon.dll"
    if [ -f "$built_dll" ]; then
      built_dll_sha="$(sha256sum "$built_dll" | cut -d' ' -f1)"
    else
      built_dll_sha="<absent>"
    fi
    if [ -n "$artifact_dll_sha" ] && [ "$built_dll_sha" = "$artifact_dll_sha" ]; then
      echo "  reading: the assembly $tfm ships is the artifact's own (sha256=$built_dll_sha)"
    else
      echo "  reading: the assembly $tfm ships does NOT match the artifact (built=$built_dll_sha artifact=$artifact_dll_sha)"
      PROVENANCE_FAILURES=$((PROVENANCE_FAILURES + 1))
    fi
  done
  echo
  echo "## 7. where that package came from: two controls, each with its own fresh global-packages folder"
  echo "   Both controls carry nuget.org beside the feed, and only because of the precondition measured"
  echo "   in section 5: the machine cannot provision the SDK's pinned framework packs locally, so a"
  echo "   feed-only fresh folder cannot restore at all, and a control that merely reproduces that"
  echo "   failure would say nothing about the library. What makes the pair a proof is that the local"
  echo "   feed is the only supplier of Trustsoft.NotifyIcon among the two sources and the fresh cache:"
  echo "   control A installs it from a source, control B cannot find it anywhere else."
  echo
  echo "   control A: local feed present, empty global-packages folder"
  echo "             -> the restore must succeed and must install the library into that fresh folder"
  rm -rf "$RESTORE_CACHE"
  cat > "$CONTROL_A_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="artifacts-local-feed" value="$FEED_ABS" />
    <add key="nuget.org" value="$NUGET_ORG" />
  </packageSources>
</configuration>
EOF
  echo "\$ NUGET_PACKAGES='<fresh>' dotnet restore samples/consumer-proof/ConsumerProof.csproj --force --configfile <feed + nuget.org>"
  NUGET_PACKAGES="$RESTORE_CACHE" dotnet restore "$CONSUMER" --force --configfile "$CONTROL_A_CONFIG" \
    > "$SCRATCH/control-a.txt" 2>&1
  CONTROL_A_EXIT=$?
  tail -3 "$SCRATCH/control-a.txt" | sed 's/^/  /'
  if [ -d "$RESTORE_CACHE/trustsoft.notifyicon/1.0.0" ]; then
    CONTROL_A_LIBRARY=1
  fi
  echo "  reading: control A exit=$CONTROL_A_EXIT (0 required); library installed into the fresh cache=$CONTROL_A_LIBRARY (1 required)"
  echo
  echo "   control B: the same fresh-folder shape, the local feed emptied, nuget.org still present"
  echo '             -> the restore must FAIL, and the failure must name Trustsoft.NotifyIcon and no'
  echo '                framework pack, so nothing but the feed could ever have supplied it'
  if ls artifacts/Trustsoft.NotifyIcon.*.nupkg >/dev/null 2>&1; then
    mv artifacts/Trustsoft.NotifyIcon.*.nupkg "$HOLD/"
  fi
  rm -rf "$RESTORE_CACHE_B"
  cat > "$CONTROL_B_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="$NUGET_ORG" />
  </packageSources>
</configuration>
EOF
  echo '$ mv artifacts/Trustsoft.NotifyIcon.*.nupkg <held outside the feed>'
  echo "\$ NUGET_PACKAGES='<fresh>' dotnet restore samples/consumer-proof/ConsumerProof.csproj --force --configfile <nuget.org only>"
  NUGET_PACKAGES="$RESTORE_CACHE_B" dotnet restore "$CONSUMER" --force --configfile "$CONTROL_B_CONFIG" \
    > "$SCRATCH/control-b.txt" 2>&1
  CONTROL_B_EXIT=$?
  grep -oE "Unable to find package [A-Za-z0-9._-]+" "$SCRATCH/control-b.txt" | sort -u | sed 's/^/    /'
  if grep -q "Unable to find package Trustsoft.NotifyIcon" "$SCRATCH/control-b.txt"; then
    CONTROL_B_NAMES_LIBRARY=1
  fi
  if grep -oE "Unable to find package [A-Za-z0-9._-]+" "$SCRATCH/control-b.txt" | grep -q "^Unable to find package Microsoft\."; then
    CONTROL_B_NAMES_PLATFORM=1
  fi
  echo "  reading: control B exit=$CONTROL_B_EXIT (non-zero required); names Trustsoft.NotifyIcon=$CONTROL_B_NAMES_LIBRARY (1 required); also misses a framework pack=$CONTROL_B_NAMES_PLATFORM (0 required)"
  if ls "$HOLD"/Trustsoft.NotifyIcon.*.nupkg >/dev/null 2>&1; then
    mv "$HOLD"/Trustsoft.NotifyIcon.*.nupkg artifacts/
  fi
  rm -rf "$RESTORE_CACHE"
  rm -rf "$RESTORE_CACHE_B"
  echo "   the package is back in artifacts/: $(ls artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null || echo '<absent>')"
  echo
  echo "## 8. the consumer's own package-surface assertion, once per framework"
  echo "   This is the BASELINE. samples/consumer-proof/App.xaml.cs still lists the seven M001 tray"
  echo "   types; the M002 package exports twenty. Each reading below is therefore expected to print the"
  echo "   surface FAIL line naming the unexpected Trustsoft.NotifyIcon.Toast* types and to exit 3"
  echo "   (SurfaceMismatchExitCode). That is the stale consumer proof T03 fixes - the script does not"
  echo "   fail on the mismatch itself. It does fail when a reading has any other shape, because that"
  echo "   would mean something other than the predicted stale surface is happening."
  for tfm in "${TFMS[@]}"; do
    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    surface_log="$SCRATCH/surface-$tfm.txt"
    echo
    echo "\$ $exe --surface-only"
    if [ ! -f "$exe" ]; then
      echo "  the consumer executable is absent, so this reading does not exist"
      echo "  BASELINE UNEXPECTED: $tfm produced no reading at all"
      BASELINE_UNEXPECTED=$((BASELINE_UNEXPECTED + 1))
      continue
    fi
    "$exe" --surface-only > "$surface_log" 2>&1
    surface_exit=$?
    sed 's/^/  /' "$surface_log"
    echo "  reading: surface-only $tfm exit=$surface_exit"
    if [ "$surface_exit" = 3 ] \
      && grep -q "FAIL the package surface is not the documented one" "$surface_log" \
      && grep -q "Trustsoft\.NotifyIcon\.Toast" "$surface_log"; then
      echo "  BASELINE (expected to be red until T03): $tfm exits 3 with the FAIL line naming the"
      echo "  unexpected Trustsoft.NotifyIcon.Toast* types"
      echo "  offending types named by this reading:"
      grep -o "unexpected=\[[^]]*\]" "$surface_log" | sed 's/^/    /'
      BASELINE_AS_PREDICTED=$((BASELINE_AS_PREDICTED + 1))
    else
      echo "  BASELINE UNEXPECTED: the plan predicts exit 3 with a FAIL line naming"
      echo "  Trustsoft.NotifyIcon.Toast*; this run exited $surface_exit"
      BASELINE_UNEXPECTED=$((BASELINE_UNEXPECTED + 1))
    fi
  done
  echo
  echo "## 9. verdict"
  echo "  sdk=$DOTNET_VERSION"
  echo "  probe-toast build exit=$PROBE_BUILD_EXIT (0 required)"
  echo "  pack exit=$PACK_EXIT (0 required); nupkg size=${NUPKG_SIZE} bytes sha256=$NUPKG_SHA (information only)"
  echo "  inspector exit=$INSPECTOR_EXIT (0 required) verdict='$INSPECTOR_VERDICT'"
  echo "  nuget.org reachable=$([ "$NETWORK_EXIT" = 0 ] && echo yes || echo no)"
  echo "  environment precondition: fresh-cache feed-only restore exit=$PRECONDITION_EXIT; names the library=$PRECONDITION_NAMES_LIBRARY (0 required)"
  echo "  repair needed=$REPAIR_NEEDED exit=$REPAIR_EXIT (0 required when needed); $HOST_PACK_ID before='$HOST_PACKS_BEFORE' after='$HOST_PACKS_AFTER'"
  echo "  stale package extraction cleared=$STALE_CLEARED (1 when the cache held an assembly other than the artifact)"
  echo "  framework assemblies not matching the artifact=$PROVENANCE_FAILURES (0 required)"
  echo "  control A exit=$CONTROL_A_EXIT (0 required); library installed from a source=$CONTROL_A_LIBRARY (1 required)"
  echo "  control B exit=$CONTROL_B_EXIT (non-zero required); names the library=$CONTROL_B_NAMES_LIBRARY (1 required); names a framework pack=$CONTROL_B_NAMES_PLATFORM (0 required)"
  echo "  consumer restore exit=$RESTORE_EXIT (0 required); consumer builds: $BUILD_SUMMARY (0 each required)"
  echo "  baseline readings as predicted=$BASELINE_AS_PREDICTED of ${#TFMS[@]}; unexpected=$BASELINE_UNEXPECTED (0 required)"

  verdict_failures=0

  if [ "$PROBE_BUILD_EXIT" != 0 ]; then
    echo "  FAIL the toast instrument did not build (exit=$PROBE_BUILD_EXIT), so no later --no-build run could be evidence"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$PACK_EXIT" != 0 ]; then
    echo "  FAIL the pack exited $PACK_EXIT"
    verdict_failures=$((verdict_failures + 1))
  fi

  case "$INSPECTOR_VERDICT" in
    *"all "*" assertions hold")
      ;;
    *)
      echo "  FAIL the inspector verdict is not 'all ... assertions hold' (exit=$INSPECTOR_EXIT verdict='$INSPECTOR_VERDICT')"
      verdict_failures=$((verdict_failures + 1))
      ;;
  esac

  if [ "$PRECONDITION_NAMES_LIBRARY" != 0 ]; then
    echo "  FAIL the fresh-cache feed-only restore could not find the library itself, not only the machine's framework packs"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$REPAIR_NEEDED" != 0 ] && [ "$REPAIR_EXIT" != 0 ]; then
    echo "  FAIL the environment repair did not succeed (exit=$REPAIR_EXIT), so the consumer cannot restore feed-only"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$CONTROL_A_EXIT" != 0 ] || [ "$CONTROL_A_LIBRARY" != 1 ]; then
    echo "  FAIL control A did not install the library into a fresh cache (exit=$CONTROL_A_EXIT, library present=$CONTROL_A_LIBRARY)"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$CONTROL_B_EXIT" = 0 ] || [ "$CONTROL_B_NAMES_LIBRARY" != 1 ] || [ "$CONTROL_B_NAMES_PLATFORM" != 0 ]; then
    echo "  FAIL control B did not isolate the library (exit=$CONTROL_B_EXIT, names the library=$CONTROL_B_NAMES_LIBRARY, names a framework pack=$CONTROL_B_NAMES_PLATFORM)"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$RESTORE_EXIT" != 0 ] || [ "$BUILD_FAILURES" != 0 ]; then
    echo "  FAIL the consumer restore/build did not succeed (restore exit=$RESTORE_EXIT, build failures=$BUILD_FAILURES)"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$PROVENANCE_FAILURES" != 0 ]; then
    echo "  FAIL $PROVENANCE_FAILURES framework(s) shipped an assembly that is not the one in the artifact"
    verdict_failures=$((verdict_failures + 1))
  fi

  if [ "$BASELINE_UNEXPECTED" != 0 ]; then
    echo "  FAIL $BASELINE_UNEXPECTED baseline reading(s) did not have the predicted shape (exit 3 plus the"
    echo "       Trustsoft.NotifyIcon.Toast* FAIL line), so this is not the stale consumer proof the plan"
    echo "       expects and the slice's premise needs re-checking"
    verdict_failures=$((verdict_failures + 1))
  fi

  echo
  if [ "$verdict_failures" != 0 ]; then
    echo "  VERDICT  not intact: $verdict_failures reading(s) failed, named above"
    FINAL_EXIT=1
  else
    echo "  VERDICT  the chain is intact"
    echo "    - the toast instrument built (exit=$PROBE_BUILD_EXIT), so the later --no-build runs have a binary"
    echo "    - the pack exit=$PACK_EXIT produced $NUPKG"
    echo "    - the inspector read the artifact: $INSPECTOR_VERDICT (exit=$INSPECTOR_EXIT)"
    echo "    - the machine's precondition was measured (fresh-cache feed-only restore exit=$PRECONDITION_EXIT,"
    echo "      framework packs only) and repaired (exit=$REPAIR_EXIT; $HOST_PACK_ID now '$HOST_PACKS_AFTER')"
    echo "    - the packed artifact is what the consumer actually loaded: the stale extraction was cleared"
    echo "      (cleared=$STALE_CLEARED) and every framework's shipped dll matches the artifact byte for byte"
    echo "    - the local feed is the only supplier of the library: control A installed it into a fresh cache"
    echo "      (exit=$CONTROL_A_EXIT, library present=$CONTROL_A_LIBRARY) and control B could not find it with"
    echo "      the feed emptied and nuget.org present (exit=$CONTROL_B_EXIT, library named=$CONTROL_B_NAMES_LIBRARY)"
    echo "    - the consumer restored and built on all three frameworks feed-only ($BUILD_SUMMARY)"
    echo "    - all $BASELINE_AS_PREDICTED baseline --surface-only readings are red as predicted: exit 3 and a"
    echo "      FAIL line naming the unexpected Trustsoft.NotifyIcon.Toast* types, which is the stale"
    echo "      consumer-side allow-list T03 replaces with the twenty documented types"
    echo "  The baseline is a reading, not a defect of this task: the consumer proof asserts against its own"
    echo "  source-side list, and that list is what T03 widens."
    echo "  Two things this task did NOT measure, and does not claim: whether a toast is shown or clicked"
    echo "  (the consumer has no toast path yet - T03 adds it) and anything about the packaged XML"
    echo "  documentation's exported surface (the inspector's artifact-side rule is T04's work)."
    FINAL_EXIT=0
  fi
} > "$LOG" 2>&1

cat "$LOG"

exit "$FINAL_EXIT"
