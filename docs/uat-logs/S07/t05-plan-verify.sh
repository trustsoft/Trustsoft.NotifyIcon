#!/usr/bin/env bash
#
# S07/T05 - raw evidence for the task's own verify command, run verbatim as one invocation:
#
#   dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release
#   && bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
#   && dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore
#
# T05 assembles the slice's evidence pack; this script is the piece of that pack that is a command
# rather than a reading. It also records the identity of the artifact it judged (size + sha256) and
# the package's entry count, so the requirement verdicts in docs/UAT-S07.md anchor to a specific file
# instead of to "the package we had at the time".
#
# The shell environment is repaired first, exactly as t03-plan-verify.sh does and for the same
# measured reason: an agent shell can arrive without the Windows known-folder variables, and with
# that environment the SDK fails inside NuGet's restore-graph evaluation with "Value cannot be null.
# (Parameter 'path1')".
#
# Usage (from the repository root): bash docs/uat-logs/S07/t05-plan-verify.sh
#
set -u

LOG=docs/uat-logs/S07/t05-plan-verify.txt

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

PLAN_EXIT=1

{
  echo "# S07/T05 raw evidence: the task plan's verify command, as one invocation"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "revision: $(git log --oneline -1 2>/dev/null)"
  echo
  echo "## 0. the artifact under test, identified before it is judged"
  echo '$ sha256sum artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg'
  sha256sum artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg 2>/dev/null || echo "  (absent before the pack)"
  echo
  echo "## 1. the plan's verify line, exactly as the task plan writes it"
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore'
  echo
  echo "--- pack ---"
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 \
    | grep -E " error | warning |Successfully created package"
  pack_exit=${PIPESTATUS[0]}
  echo "pack exit=$pack_exit"
  echo
  echo "--- inspection ---"
  bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
  verify_exit=$?
  echo "verify-package.sh exit=$verify_exit"
  echo
  echo "--- full test suite ---"
  dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore 2>&1 | tail -6
  suite_exit=${PIPESTATUS[0]}
  echo "test suite exit=$suite_exit"
  echo
  echo "## 2. the artifact after the run, identified again"
  echo '$ sha256sum artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg'
  sha256sum artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg 2>/dev/null
  echo '$ unzip -l artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg | tail -2'
  unzip -l artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg 2>/dev/null | tail -2
  echo
  echo "## verdict"
  echo "  pack=$pack_exit; inspection=$verify_exit; suite=$suite_exit"
  if [ "$pack_exit" = 0 ] && [ "$verify_exit" = 0 ] && [ "$suite_exit" = 0 ]; then
    echo "  The plan's verify command passes as written on this revision: the package packs, the"
    echo "  inspector accepts it entry by entry, and the full suite is green."
    PLAN_EXIT=0
    exit 0
  fi
  echo "  FAILURES ABOVE"
  exit 1
} > "$LOG" 2>&1

cat "$LOG"
exit "$PLAN_EXIT"
