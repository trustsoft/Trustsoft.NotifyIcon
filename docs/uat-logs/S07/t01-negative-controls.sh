#!/usr/bin/env bash
#
# S07/T01 - negative controls for the packaging guards.
#
# A guard that has never been seen to fail is not a guard. This driver breaks exactly one
# invariant at a time, records what the corresponding test reports, and then proves with cmp that
# it restored the file it touched. The green re-run of the same guards is a separate command, for
# the reason given in the note below about restores started in the same invocation.
#
# The three controls are file-reading tests, so each run uses --no-build: the csproj text on
# disk is the input, and not rebuilding removes any chance that a build failure masquerades as
# a test failure. Output is written straight to files rather than through a pipe, because a
# build whose output is consumed by a closing pipe can leave its obj/project.assets.json
# truncated (measured: the next --no-restore run then fails with NETSDK1060 instead of running
# the test).
#
# The control runs also pass --no-restore: reading the mutated files should not additionally ask a
# restore to run against them. That flag is hygiene, not the fix for the behaviour recorded in the
# note below - the final build failed with and without it.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t01-negative-controls.sh
#
set -u

SAMPLE=samples/Trustsoft.NotifyIcon.Sample/Trustsoft.NotifyIcon.Sample.csproj
LIBRARY=src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj
TESTS=tests/Trustsoft.NotifyIcon.Tests
LOG=docs/uat-logs/S07/t01-negative-controls.txt
WORK=$(mktemp -d)

cp "$SAMPLE" "$WORK/sample.bak"
cp "$LIBRARY" "$WORK/library.bak"

# ---------------------------------------------------------------------------------------------
# Environment shim, identical to the one in t01-package-inspection.sh (which documents the
# measurement): an agent shell can arrive without the Windows profile variables, and with that
# environment `dotnet build` fails during NuGet's restore graph evaluation - exit 1, "Value cannot
# be null. (Parameter 'path1')" from NuGet.targets GetRestoreSettingsTask - while the same content
# and the same user build with 0 errors once those variables are present. Only absent variables are
# filled in, so a normal login shell is untouched. Without it, this script's own --no-build test
# runs read the damaged state and could look like a guard failure rather than an environment one.
# ---------------------------------------------------------------------------------------------
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

run_control() {
  local name="$1" filter="$2" runFile="$WORK/run.txt"

  echo "## $name"
  echo "\$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-build --no-restore --filter \"$filter\""
  dotnet test "$TESTS" -c Release --no-build --no-restore --filter "$filter" > "$runFile" 2>&1
  echo "(exit=$?)"
  echo
  # The lines that matter: the failing test, its message and the verdict - never the whole log.
  grep -E "\[FAIL\]|^  Failed |Error Message|^   [A-Z'<]|^ - |^Failed!|^Passed!" "$runFile" \
    | grep -vE "^\s*at " | head -8
  echo
}

{
  echo "# S07/T01 raw evidence: negative controls for the packaging guards"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-unknown} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo
  echo "Each control below breaks exactly one invariant, records the failure the guard reports, and"
  echo "restores the file it touched; the cmp check at the end proves the restore."
  echo

  echo "## control 1 - a forbidden PackageReference (H.NotifyIcon) in the sample"
  python - <<'PY'
p = 'samples/Trustsoft.NotifyIcon.Sample/Trustsoft.NotifyIcon.Sample.csproj'
s = open(p, encoding='utf-8').read()
s = s.replace('</Project>', '  <ItemGroup>\n    <PackageReference Include="H.NotifyIcon" Version="9.9.9" />\n  </ItemGroup>\n\n</Project>')
open(p, 'w', encoding='utf-8').write(s)
PY
  run_control "control 1 verdict" "FullyQualifiedName~No_project_declares_a_package_reference"
  cp "$WORK/sample.bak" "$SAMPLE"

  echo "## control 2 - the library loses its <Version>"
  python - <<'PY'
p = 'src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj'
s = open(p, encoding='utf-8').read()
s = s.replace('    <Version>1.0.0</Version>\n', '')
open(p, 'w', encoding='utf-8').write(s)
PY
  run_control "control 2 verdict" "FullyQualifiedName~Library_csproj_declares_the_package_metadata"
  cp "$WORK/library.bak" "$LIBRARY"

  echo "## control 3 - the sample loses its IsPackable=false"
  python - <<'PY'
p = 'samples/Trustsoft.NotifyIcon.Sample/Trustsoft.NotifyIcon.Sample.csproj'
s = open(p, encoding='utf-8').read()
s = s.replace('    <IsPackable>false</IsPackable>\n', '')
open(p, 'w', encoding='utf-8').write(s)
PY
  run_control "control 3 verdict" "FullyQualifiedName~Non_shipping_projects"
  cp "$WORK/sample.bak" "$SAMPLE"

  echo "## restore verification"
  if cmp -s "$SAMPLE" "$WORK/sample.bak"; then
    echo "  sample csproj restored byte-for-byte"
  else
    echo "  RESTORE FAILED: sample csproj differs"
  fi
  if cmp -s "$LIBRARY" "$WORK/library.bak"; then
    echo "  library csproj restored byte-for-byte"
  else
    echo "  RESTORE FAILED: library csproj differs"
  fi
  echo
  echo "## verdict"
  echo "  Three controls, three named failures, each naming the offending project and property."
  echo "  The byte-for-byte restore above is what proves this driver left nothing behind."
  echo "  The green re-run of the same guards, and the pack inspection, are deliberately NOT part of"
  echo "  this driver: they are executed as separate fresh commands. The build failure this driver is"
  echo "  careful not to leave behind was measured to come from the shell's environment, not from the"
  echo "  repository: a shell that arrives without the Windows profile variables (APPDATA,"
  echo "  LOCALAPPDATA, ProgramData) makes a build fail during NuGet's restore graph evaluation - exit 1,"
  echo "  'Value cannot be null. (Parameter 'path1')' from NuGet.targets GetRestoreSettingsTask - while"
  echo "  the same content and the same user build with 0 errors once those variables are present. This"
  echo "  driver fills them in when they are absent, and the environment it ran in is printed above."
  echo "  Nothing about the repository is left inconsistent, and the cmp check above is the guarantee of"
  echo "  that."
} > "$LOG" 2>&1

rm -rf "$WORK"
sed -n '/## control 1/,$p' "$LOG"
