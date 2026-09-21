#!/usr/bin/env bash
#
# S07/T04 - negative controls for the README guard.
#
# `PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface` asserts that the
# README the package ships still carries the facts a consumer copies: the install line built from the
# library project's own PackageId and Version, the consumer namespace URI and its `tni` prefix, the
# consumer sections, and the seven public type names. A guard that has never been seen to fail is not
# a guard, so each control breaks exactly one of those facts in the real README, runs the real test,
# and records the failure message - then restores the file and proves it is byte-for-byte unchanged.
#
# The tests read the README at run time, so nothing is rebuilt between controls: the controls change
# the input, not the assertion.
#
# The shell environment is repaired first, exactly as the S07/T01-T03 scripts do and for the same
# measured reason: an agent shell can arrive without the Windows known-folder variables, and with that
# environment the SDK fails during NuGet's restore-graph evaluation with exit 1 and
# "error : Value cannot be null. (Parameter 'path1')" from NuGet.targets GetRestoreSettingsTask.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t04-readme-guard-controls.sh
#
set -u

LOG=docs/uat-logs/S07/t04-readme-guard-controls.txt
TEST_DLL=tests/Trustsoft.NotifyIcon.Tests/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Tests.dll
FILTER='FullyQualifiedName~Readme_documents_the_install_line_usage_and_the_shipped_surface'
SCRATCH="$TEMP/t04-readme-guard-controls"
BACKUP="$SCRATCH/README.md.original"
FAILURES=0

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

mkdir -p "$SCRATCH"
cp README.md "$BACKUP"
ORIGINAL_HASH="$(sha256sum README.md | cut -d' ' -f1)"

{
  printf 'S07/T04 - negative controls for the README guard\n'
  printf 'date:          %s\n' "$(date '+%Y-%m-%d %H:%M:%S')"
  printf 'machine:       %s\n' "$(uname -n)"
  printf 'test:          PackagePurityTests.Readme_documents_the_install_line_usage_and_the_shipped_surface\n'
  printf 'filter:        --TestCaseFilter:"%s"\n' "$FILTER"
  printf 'README.md:     sha256 %s\n' "$ORIGINAL_HASH"
} | tee "$LOG"

section() {
  printf '\n----------------------------------------------------------------\n%s\n----------------------------------------------------------------\n' "$1" | tee -a "$LOG"
}

# Watches the guard run after one named mutation of the README.
#
# $1 = control label, $2 = python expression mutating the README text, $3 = a fragment the failure
# message must contain for this control to count.
control() {
  local label="$1"
  local mutation="$2"
  local expected_fragment="$3"
  section "$label"

  python -c "
import io, sys
path = 'README.md'
with io.open(path, encoding='utf-8') as handle:
    text = handle.read()
mutated = $mutation
if mutated == text:
    sys.exit('the mutation did not change the README; this control would prove nothing')
with io.open(path, 'w', encoding='utf-8', newline='') as handle:
    handle.write(mutated)
" || {
    printf '  FAIL  the mutation could not be applied\n' | tee -a "$LOG"
    cp "$BACKUP" README.md
    FAILURES=$((FAILURES + 1))
    return
  }

  printf 'mutated README.md sha256: %s\n' "$(sha256sum README.md | cut -d' ' -f1)" | tee -a "$LOG"

  local output status
  output="$(dotnet vstest "$TEST_DLL" --TestCaseFilter:"$FILTER" 2>&1)"
  status=$?
  printf '%s\n' "$output" | tee -a "$LOG"

  if [ "$status" -eq 0 ]; then
    printf '  FAIL  the guard passed on a README that broke this fact (exit 0); the assertion is not testing it\n' | tee -a "$LOG"
    FAILURES=$((FAILURES + 1))
  elif printf '%s' "$output" | grep -qF "$expected_fragment"; then
    printf '  PASS  the guard failed (exit %d) and named the broken fact: %s\n' "$status" "$expected_fragment" | tee -a "$LOG"
  else
    printf '  FAIL  the guard failed (exit %d) but not with the message this control expects: %s\n' "$status" "$expected_fragment" | tee -a "$LOG"
    FAILURES=$((FAILURES + 1))
  fi

  cp "$BACKUP" README.md

  local restored
  restored="$(sha256sum README.md | cut -d' ' -f1)"

  if [ "$restored" = "$ORIGINAL_HASH" ]; then
    printf '  PASS  the README is restored byte-for-byte (%s)\n' "$restored" | tee -a "$LOG"
  else
    printf '  FAIL  the README was not restored (%s != %s)\n' "$restored" "$ORIGINAL_HASH" | tee -a "$LOG"
    FAILURES=$((FAILURES + 1))
  fi
}

# --- Positive control: the unmutated README passes the guard -------------------------------
section 'positive control - the README as it stands'
POSITIVE_OUTPUT="$(dotnet vstest "$TEST_DLL" --TestCaseFilter:"$FILTER" 2>&1)"
POSITIVE_STATUS=$?
printf '%s\n' "$POSITIVE_OUTPUT" | tail -4 | tee -a "$LOG"
if [ "$POSITIVE_STATUS" -eq 0 ]; then
  printf '  PASS  the guard passes on the delivered README (exit 0)\n' | tee -a "$LOG"
else
  printf '  FAIL  the guard does not pass on the delivered README (exit %d)\n' "$POSITIVE_STATUS" | tee -a "$LOG"
  FAILURES=$((FAILURES + 1))
fi

# --- Control 1: the install line names a version the project does not declare --------------
control \
  'control 1 - the install line advertises version 9.9.9 (the version-bump trap)' \
  'text.replace("Version=\"1.0.0\"", "Version=\"9.9.9\"", 1)' \
  'must show the install line a consumer copies'

# --- Control 2: the consumer namespace URI disappears from the markup snippet --------------
# Every occurrence, not just the first: the URI is documented in three places (the section prose,
# the XAML snippet's xmlns declaration and the trap note), and a control that rewrote one of them
# would leave the guard legitimately satisfied by the other two - measured on this script's first
# run, where the single-occurrence mutation produced exit 0 and proved nothing.
control \
  'control 2 - the consumer markup namespace URI is removed everywhere it appears' \
  'text.replace("http://schemas.trustsoft.com/notifyicon", "http://schemas.example.com/nothing")' \
  'must document the consumer markup namespace'

# --- Control 3: a consumer section is dropped ---------------------------------------------
control \
  'control 3 - the "## Windowless shutdown" section is dropped' \
  'text.replace("## Windowless shutdown", "## Deployment", 1)' \
  "must carry a '## Windowless shutdown' section"

# --- Control 4: repository content moves in front of the consumer content ------------------
control \
  'control 4 - "## Repository notes" is moved above the install line' \
  "text.replace('## Repository notes\n', '', 1).replace('# Trustsoft.NotifyIcon\n', '# Trustsoft.NotifyIcon\n\n## Repository notes\n', 1)" \
  'repository-facing content must sit below the consumer content'

# --- Control 5: a public type is no longer named by the readme -----------------------------
control \
  'control 5 - BalloonTipOptions is no longer named anywhere in the README' \
  'text.replace("BalloonTipOptions", "BalloonOptions", 1).replace("BalloonTipOptions", "BalloonOptions")' \
  'does not name [BalloonTipOptions]'

{
  printf '\n================================================================\n'
  printf 'SUMMARY  %d control failure(s)\n' "$FAILURES"
  printf 'README.md sha256 at the end: %s\n' "$(sha256sum README.md | cut -d' ' -f1)"
  printf 'README.md sha256 at the start: %s\n' "$ORIGINAL_HASH"
} | tee -a "$LOG"

exit "$FAILURES"
