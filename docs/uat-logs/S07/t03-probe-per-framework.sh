#!/usr/bin/env bash
#
# S07/T03 - one live run per target framework, this attempt's raw evidence.
#
# The task's Done-when names three frameworks, each with a live run showing a
# present icon, a flat GDI series and a clean teardown verdict. The previous
# attempt recorded that in docs/uat-logs/S07/t03-consumer-proof.txt for all
# three; this script re-measures all three from the current binaries so the
# evidence for this attempt is self-contained, using the same probe columns as
# every other slice (identity, presence series, GDI count, teardown verdict).
#
# The shell environment is repaired first (only absent variables are filled),
# for the reason T01/T02/T03 already documented: an instrumented shell can
# arrive without the Windows known-folder variables and the SDK then fails
# inside NuGet's restore-graph evaluation with "Value cannot be null.
# (Parameter 'path1')".
#
# Usage (from the repository root): bash docs/uat-logs/S07/t03-probe-per-framework.sh
#
set -u

LOG=docs/uat-logs/S07/t03-probe-per-framework.txt

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

FAILURES=0

{
  echo "# S07/T03 raw evidence: one probed live run per target framework"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)}"
  echo

  for tfm in net8.0-windows net9.0-windows net10.0-windows; do
    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    echo "## $tfm"
    echo "\$ dotnet run --project scripts/probe-live -c Release --no-build -- $exe 20"
    dotnet run --project scripts/probe-live -c Release --no-build -- "$exe" 20
    exit_code=$?
    echo "probe exit=$exit_code"
    if [ "$exit_code" != 0 ]; then FAILURES=$((FAILURES + 1)); fi
    echo
  done

  echo "## verdict"
  if [ "$FAILURES" != 0 ]; then
    echo "  $FAILURES framework run(s) did not pass"
    exit 1
  fi
  echo "  All three frameworks ran with the icon observed present for the whole observation"
  echo "  window and with the probe's own teardown verdict after the process died."
  exit 0
} > "$LOG" 2>&1

cat "$LOG"
