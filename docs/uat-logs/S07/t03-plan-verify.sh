#!/usr/bin/env bash
#
# S07/T03 - raw evidence for the task's own verify command and for the regression checks the new
# project makes necessary.
#
# samples/consumer-proof is a new project in this repository's directory tree, so besides the task's
# verify command this script re-runs the guards that inspect the repository as a whole: the
# packaging-purity tests (which sweep every csproj/props file and read the solution file), the full
# test suite (to show the new project changed nothing else), and the package inspection (to show the
# consumer proof did not leak into the nupkg).
#
# The shell environment is repaired first, exactly as t03-consumer-proof.sh does and for the same
# measured reason: an agent shell can arrive without the Windows known-folder variables, and with
# that environment the SDK fails inside NuGet's restore-graph evaluation with "Value cannot be null.
# (Parameter 'path1')".
#
# Usage (from the repository root): bash docs/uat-logs/S07/t03-plan-verify.sh
#
set -u

LOG=docs/uat-logs/S07/t03-plan-verify.txt

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

PLAN_EXIT=0
PURITY_EXIT=0
SUITE_EXIT=0
VERIFY_EXIT=0

{
  echo "# S07/T03 raw evidence: the plan's verify command, and the regression checks around it"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo
  echo "## 1. the plan's verify command, exactly as the task plan writes it"
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  echo '$ for tfm in net8.0-windows net9.0-windows net10.0-windows; do dotnet build samples/consumer-proof -c Release -f $tfm; done'
  echo '$ dotnet run --project scripts/probe-live -c Release --no-build -- samples/consumer-proof/bin/Release/net8.0-windows/ConsumerProof.exe 20'
  echo
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 \
    | grep -E " error | warning |Successfully created package"
  pack_exit=${PIPESTATUS[0]}
  echo "pack exit=$pack_exit"
  for tfm in net8.0-windows net9.0-windows net10.0-windows; do
    dotnet build samples/consumer-proof -c Release -f "$tfm" 2>&1 \
      | grep -E " error | warning |Build succeeded|-> "
    echo "build $tfm exit=${PIPESTATUS[0]}"
  done
  echo
  echo "  The run below uses --no-build, and a fresh or re-materialized worktree has no"
  echo "  scripts/probe-live/bin (bin is gitignored): without this build the probe cannot start, and the"
  echo "  missing instrument reads as an absent icon. Measured - T03's consumer script failed exactly"
  echo "  that way on its first run in a re-materialized worktree."
  echo '$ dotnet build scripts/probe-live -c Release'
  dotnet build scripts/probe-live -c Release 2>&1 \
    | grep -E " error | warning |Build succeeded| -> "
  PROBE_BUILD_EXIT=${PIPESTATUS[0]}
  echo "probe-live build exit=$PROBE_BUILD_EXIT"
  echo
  dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/consumer-proof/bin/Release/net8.0-windows/ConsumerProof.exe 20
  PLAN_EXIT=$?
  echo "plan verify exit=$PLAN_EXIT"
  echo
  echo "  Note: with no run switch the consumer uses its documented default (a 20s run and one balloon"
  echo "  mid-run), which is exactly as long as the probe's observation window, so this run ends with the"
  echo "  probe killing the still-live process. That is the probe's own teardown path, and the verdict it"
  echo "  prints for it is quoted above; the paced runs with a self-open menu and a balloon are in"
  echo "  t03-consumer-proof.txt."
  echo
  echo "## 2. the packaging-purity guards, which sweep every project and props file in the repository"
  echo "   The test runs below pass --no-restore --no-build, so the solution is built first: a fresh"
  echo "   worktree has no bin/obj, and --no-build without them fails on missing output rather than on"
  echo "   the code it is meant to judge. Section 4 repeats the same build for the plan's own reporting."
  echo '$ dotnet build Trustsoft.NotifyIcon.sln -c Release'
  dotnet build Trustsoft.NotifyIcon.sln -c Release 2>&1 | grep -E " error | warning |Build succeeded"
  SOLUTION_PREREQ_EXIT=${PIPESTATUS[0]}
  echo "solution build (prerequisite for the --no-build test runs) exit=$SOLUTION_PREREQ_EXIT"
  echo
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~PackagePurityTests"'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore --no-build \
    --filter "FullyQualifiedName~PackagePurityTests" 2>&1 | tail -3
  PURITY_EXIT=${PIPESTATUS[0]}
  echo "purity-filter exit=$PURITY_EXIT"
  echo
  echo "## 3. the full test suite, to show the new project changed nothing else"
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore --no-build 2>&1 | tail -3
  SUITE_EXIT=${PIPESTATUS[0]}
  echo "full-suite exit=$SUITE_EXIT"
  echo
  echo "## 4. the partial passes that need the library built first, as in the task plan"
  echo '$ dotnet build Trustsoft.NotifyIcon.sln -c Release'
  dotnet build Trustsoft.NotifyIcon.sln -c Release 2>&1 | grep -E " error | warning |Build succeeded"
  echo "solution build exit=${PIPESTATUS[0]}"
  echo
  echo "## 5. the package inspection, to show the consumer proof did not leak into the nupkg"
  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
  bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
  VERIFY_EXIT=$?
  echo "verify-package.sh exit=$VERIFY_EXIT"
  echo
  echo "## verdict"
  echo "  pack=$pack_exit; probe instrument build=$PROBE_BUILD_EXIT; plan verify=$PLAN_EXIT; purity filter=$PURITY_EXIT; full suite=$SUITE_EXIT; inspection=$VERIFY_EXIT"
  if [ "$pack_exit" != 0 ] || [ "$PROBE_BUILD_EXIT" != 0 ] || [ "$PLAN_EXIT" != 0 ] || [ "$PURITY_EXIT" != 0 ] || [ "$SUITE_EXIT" != 0 ] || [ "$VERIFY_EXIT" != 0 ]; then
    echo '  FAILURES ABOVE'
    exit 1
  fi
  echo "  The plan's verify command passes as written, the purity guards and the full suite are green"
  echo "  with the consumer proof present, and the packed artifact still contains nothing but the library."
  exit 0
} > "$LOG" 2>&1

cat "$LOG"
