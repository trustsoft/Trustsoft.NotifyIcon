#!/usr/bin/env bash
#
# S07/T02 - raw evidence for the packed-package inspection path.
#
# Runs the slice's own verify command exactly as the plan writes it - pack the library, then
# hand the packed file to scripts/verify-package.sh - and records what both steps printed, plus
# the environment that produced it. The log is the evidence; this script exists so the log can be
# regenerated from a working tree.
#
# scripts/verify-package.sh itself needs no SDK; the pack step in front of it does. The plan's
# verify command is therefore run through the same environment shim the S07/T01 scripts carry,
# documented in t01-package-inspection.sh: an agent shell can arrive without the Windows profile
# variables, and with that environment `dotnet pack` fails during NuGet's restore-graph evaluation
# with exit 1 and "error : Value cannot be null. (Parameter 'path1')" from NuGet.targets
# GetRestoreSettingsTask, while the same content in the same directory packs cleanly once those
# variables are present. Only absent variables are filled in, so a normal login shell is untouched,
# and the environment actually used is printed in the log.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t02-verify-package.sh
#
set -u

LOG=docs/uat-logs/S07/t02-verify-package.txt

if [ -z "${APPDATA:-}" ] || [ -z "${LOCALAPPDATA:-}" ]; then
  export APPDATA="${APPDATA:-$USERPROFILE\\AppData\\Roaming}"
  export LOCALAPPDATA="${LOCALAPPDATA:-$USERPROFILE\\AppData\\Local}"
  export ALLUSERSPROFILE="${ALLUSERSPROFILE:-$SYSTEMDRIVE\\ProgramData}"
  export ProgramData="${ProgramData:-$SYSTEMDRIVE\\ProgramData}"
  export PUBLIC="${PUBLIC:-$SYSTEMDRIVE\\Users\\Public}"
  export PROGRAMFILES="${PROGRAMFILES:-$SYSTEMDRIVE\\Program Files}"
  export COMMONPROGRAMFILES="${COMMONPROGRAMFILES:-$SYSTEMDRIVE\\Program Files\\Common Files}"
  export ProgramW6432="${ProgramW6432:-$SYSTEMDRIVE\\Program Files}"
  export CommonProgramW6432="${CommonProgramW6432:-$SYSTEMDRIVE\\Program Files\\Common Files}"
  export COMSPEC="${COMSPEC:-$SYSTEMROOT\\system32\\cmd.exe}"
  export OS="${OS:-Windows_NT}"
  export PATHEXT="${PATHEXT:-.COM;.EXE;.BAT;.CMD}"
  export TEMP="${LOCALAPPDATA}\\Temp"
  export TMP="$TEMP"
fi

{
  echo "# S07/T02 raw evidence: the packaged artifact, inspected by scripts/verify-package.sh"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "environment: APPDATA='$APPDATA' LOCALAPPDATA='$LOCALAPPDATA' TEMP='$TEMP'"
  echo
  echo "## 1. the plan's verify command, as written"
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release && bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
  echo
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 \
    | grep -E " error |warning |Successfully created package"
  PACK_EXIT=${PIPESTATUS[0]}
  echo "pack exit=$PACK_EXIT"
  echo
  bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
  VERIFY_EXIT=$?
  echo "verify-package.sh exit=$VERIFY_EXIT"
  echo
  echo '## 2. what the script does not print, and why that is deliberate'
  echo '  The nuspec is never dumped: every assertion above read the one value it is about and named it.'
  echo '  The entry list is never dumped either - the per-framework and per-entry assertions name the'
  echo '  entries they found, and an unexpected entry would be quoted by the assertion that rejects it.'
  echo
  echo "## verdict: pack exit=$PACK_EXIT, inspection exit=$VERIFY_EXIT"
  if [ "$PACK_EXIT" != 0 ] || [ "$VERIFY_EXIT" != 0 ]; then
    echo '  FAILURES ABOVE'
    exit 1
  fi
  echo '  The package a consumer would install matches the declared identity and carries nothing it must not.'
  exit 0
} > "$LOG" 2>&1

cat "$LOG"
