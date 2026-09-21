#!/usr/bin/env bash
#
# S07/T03 - verification-field remediation evidence.
#
# The persisted task-plan Verify line for T03 is ONE line containing a
# `for ...; do ...; done` loop. The host verification gate validates each
# newline-separated command and rejects shell control syntax (`;` < > `||`
# backticks `$(`), so it classified the field as `task-plan-unsafe`, ran ZERO
# commands, and - by design (#1922) - did not substitute project-wide checks.
# The task's implementation work is unaffected; the plan field is the defect.
#
# This script records, as durable raw evidence:
#   1. the offending line and the gate's verdict on it (classifier script),
#   2. the proposed replacement (one command per line), each command run
#      separately and its exit code recorded - i.e. exactly how the gate runs
#      a newline-separated Verify field,
#   3. the project-wide checks the gate skipped in place of the unsafe field:
#      the packaging-purity tests, the full suite, and the package inspection.
#
# The shell environment is repaired first (only absent variables are filled),
# for the measured reason documented by T01/T02/T03: an instrumented shell can
# arrive without the Windows known-folder variables and the SDK then fails
# inside NuGet's restore-graph evaluation with "Value cannot be null.
# (Parameter 'path1')".
#
# Usage (from the repository root): bash docs/uat-logs/S07/t03-verify-field-remediation.sh
#
set -u

LOG=docs/uat-logs/S07/t03-verify-field-remediation.txt

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

PACK_EXIT=0
B8_EXIT=0
B9_EXIT=0
B10_EXIT=0
RUN_EXIT=0
PURITY_EXIT=0
SUITE_EXIT=0
INSPECT_EXIT=0

{
  echo "# S07/T03 raw evidence: the unsafe Verify field, its replacement, and the skipped project-wide checks"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo

  echo "## 1. the gate's verdict on the persisted Verify field, and on the replacement"
  echo '$ node docs/uat-logs/S07/t03-verify-field-classifier.mjs'
  node docs/uat-logs/S07/t03-verify-field-classifier.mjs
  echo

  echo "## 2. the replacement, run command by command exactly as the gate would"
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 \
    | grep -E " error | warning |Successfully created package"
  PACK_EXIT=${PIPESTATUS[0]}
  echo "pack exit=$PACK_EXIT"
  echo

  for tfm in net8.0-windows net9.0-windows net10.0-windows; do
    echo "\$ dotnet build samples/consumer-proof -c Release -f $tfm"
    dotnet build samples/consumer-proof -c Release -f "$tfm" 2>&1 \
      | grep -E " error | warning |Build succeeded|-> "
    case "$tfm" in
      net8.0-windows) B8_EXIT=${PIPESTATUS[0]} ;;
      net9.0-windows) B9_EXIT=${PIPESTATUS[0]} ;;
      net10.0-windows) B10_EXIT=${PIPESTATUS[0]} ;;
    esac
    echo "build $tfm exit=$B8_EXIT$B9_EXIT$B10_EXIT"
    echo
  done

  echo '$ dotnet run --project scripts/probe-live -c Release --no-build -- samples/consumer-proof/bin/Release/net8.0-windows/ConsumerProof.exe 20'
  dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/consumer-proof/bin/Release/net8.0-windows/ConsumerProof.exe 20
  RUN_EXIT=$?
  echo "probe run exit=$RUN_EXIT"
  echo

  echo "## 3. the project-wide checks the gate skipped in place of the unsafe field"
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~PackagePurityTests"'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore \
    --filter "FullyQualifiedName~PackagePurityTests" 2>&1 | tail -3
  PURITY_EXIT=${PIPESTATUS[0]}
  echo "purity-filter exit=$PURITY_EXIT"
  echo

  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore 2>&1 | tail -3
  SUITE_EXIT=${PIPESTATUS[0]}
  echo "full-suite exit=$SUITE_EXIT"
  echo

  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
  bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
  INSPECT_EXIT=$?
  echo "verify-package.sh exit=$INSPECT_EXIT"
  echo

  echo "## verdict"
  echo "  pack=$PACK_EXIT; builds=$B8_EXIT/$B9_EXIT/$B10_EXIT; probe=$RUN_EXIT; purity=$PURITY_EXIT; suite=$SUITE_EXIT; inspection=$INSPECT_EXIT"
  if [ "$PACK_EXIT" != 0 ] || [ "$B8_EXIT" != 0 ] || [ "$B9_EXIT" != 0 ] || [ "$B10_EXIT" != 0 ] \
     || [ "$RUN_EXIT" != 0 ] || [ "$PURITY_EXIT" != 0 ] || [ "$SUITE_EXIT" != 0 ] || [ "$INSPECT_EXIT" != 0 ]; then
    echo '  FAILURES ABOVE'
    exit 1
  fi
  echo "  Every command of the proposed newline-separated Verify field exits 0, so the field is"
  echo "  runnable once the plan data is rewritten; the purity guards, the full suite and the package"
  echo "  inspection are green with the consumer proof present."
  exit 0
} > "$LOG" 2>&1

cat "$LOG"
