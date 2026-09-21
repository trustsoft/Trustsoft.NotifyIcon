#!/usr/bin/env bash
#
# S07/T04 - raw evidence that every command the README quotes runs as written.
#
# The README is the package's PackageReadmeFile, so a command in it that does not run is a defect a
# consumer would meet before they ever read the API. This script executes each quoted command
# verbatim through `bash -c` (so the line in the log is the line in the document), records the
# command, its raw output, its exit code and its duration, and prints the README's hash at the top
# and the bottom so the log is bound to the revision of the document it checked.
#
# It also runs the two live paths the README documents (the code-first run and the declarative
# `--xaml` run) and the probe line, because those are the commands most likely to rot silently: a
# documented invocation that no shell would accept is exactly the kind of claim this task is meant to
# stop from being asserted from memory.
#
# The shell environment is repaired first, exactly as the S07/T01, T02 and T03 scripts do: an agent
# shell can arrive without the Windows known-folder variables, and with that environment the SDK
# fails during NuGet's restore-graph evaluation with exit 1 and "error : Value cannot be null.
# (Parameter 'path1')" from NuGet.targets GetRestoreSettingsTask. Only absent variables are filled
# in, so a normal login shell is untouched.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t04-readme-verification.sh
#
set -u

LOG=docs/uat-logs/S07/t04-readme-verification.txt
FAILURES=0
COMMANDS=0

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

README_HASH_BEFORE="$(sha256sum README.md | cut -d' ' -f1)"

{
  printf 'S07/T04 - every command the README quotes, run as written\n'
  printf 'date:        %s\n' "$(date '+%Y-%m-%d %H:%M:%S')"
  printf 'machine:     %s\n' "$(uname -n)"
  printf 'shell:       %s\n' "$BASH_VERSION"
  printf 'os:          %s\n' "$(uname -s -r 2>/dev/null || echo unknown)"
  printf 'dotnet:      %s\n' "$(dotnet --version 2>/dev/null || echo 'not on PATH')"
  printf 'README.md:   sha256 %s\n' "$README_HASH_BEFORE"
  printf 'environment repair applied: APPDATA=%s LOCALAPPDATA=%s\n' "$APPDATA" "$LOCALAPPDATA"
} | tee "$LOG"

section() {
  printf '\n----------------------------------------------------------------\n%s\n----------------------------------------------------------------\n' "$1" | tee -a "$LOG"
}

# Runs one command string verbatim: the log's `README command:` line is the line in the README.
run() {
  local command="$1"
  COMMANDS=$((COMMANDS + 1))
  section "README command: $command"
  local start=$SECONDS
  bash -c "$command" 2>&1 | tee -a "$LOG"
  local status=${PIPESTATUS[0]}
  local duration=$((SECONDS - start))
  printf '=> exit=%d duration=%ds\n' "$status" "$duration" | tee -a "$LOG"
  if [ "$status" -ne 0 ]; then
    FAILURES=$((FAILURES + 1))
  fi
}

# Runs a command the README documents as never exiting by itself, under a watchdog.
#
# `dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release` runs until the session ends
# or the user stops it - that is what the README says it does. It is therefore run as written and
# stopped by this harness at the bound, which is also the only way to check that the unbounded
# invocation reaches the registration lines without a switch. Anything other than the watchdog
# firing (exit 124) before the bound is a real failure and is counted as one.
run_until_stopped() {
  local command="$1"
  local seconds="$2"
  COMMANDS=$((COMMANDS + 1))
  section "README command (documented as never exiting by itself, stopped by this harness at ${seconds}s): $command"
  local start=$SECONDS
  timeout "$seconds" bash -c "$command" 2>&1 | tee -a "$LOG"
  local status=${PIPESTATUS[0]}
  local duration=$((SECONDS - start))
  printf '=> exit=%d duration=%ds\n' "$status" "$duration" | tee -a "$LOG"
  if [ "$status" -eq 124 ]; then
    printf '=> the watchdog stopped it at the bound, as documented; the lines above are its output\n' | tee -a "$LOG"
  else
    printf '=> FAIL the command did not run until the bound (exit %d is not the watchdog)\n' "$status" | tee -a "$LOG"
    FAILURES=$((FAILURES + 1))
  fi
  # `dotnet run` parents the sample, so the sample can outlive the watchdog. Remove it and any
  # process it left behind, then prove nothing of this application is still running: a stray
  # process holding an icon would corrupt every later live reading in this log.
  taskkill //F //IM Trustsoft.NotifyIcon.Sample.exe >/dev/null 2>&1
  local leftovers
  leftovers="$(tasklist //FI 'IMAGENAME eq Trustsoft.NotifyIcon.Sample.exe' 2>/dev/null | grep -c 'Trustsoft.NotifyIcon.Sample.exe' || true)"
  printf '=> leftover sample processes after cleanup: %s\n' "$leftovers" | tee -a "$LOG"
  if [ "$leftovers" -ne 0 ]; then
    FAILURES=$((FAILURES + 1))
  fi
}

# --- The consumer path the README's first example describes -------------------------------
# The bare invocation, as the "The headless sample and the live probe" section quotes it.
run_until_stopped 'dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release' 12

# The documented suffix, with the documented value.
run 'dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --run-seconds 20'

# The declarative line, exactly as the "Declarative usage" section quotes it.
run 'dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 20'

# --- The repository-facing commands -------------------------------------------------------
run 'dotnet build Trustsoft.NotifyIcon.sln -c Release'
run 'dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows'
run 'dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
run 'bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
run 'dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release -f net8.0-windows'
run 'dotnet run --project scripts/probe-live -c Release -- samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 12 --run-seconds 8'

README_HASH_AFTER="$(sha256sum README.md | cut -d' ' -f1)"

{
  printf '\n================================================================\n'
  printf 'SUMMARY  %d command(s), %d failure(s)\n' "$COMMANDS" "$FAILURES"
  printf 'README.md sha256 before: %s\n' "$README_HASH_BEFORE"
  printf 'README.md sha256 after:  %s\n' "$README_HASH_AFTER"
  if [ "$README_HASH_BEFORE" = "$README_HASH_AFTER" ]; then
    printf 'the README was not edited while its commands ran (the log belongs to this revision)\n'
  else
    printf 'WARNING the README changed during the run; this log does not describe the file it names\n'
  fi
} | tee -a "$LOG"

exit "$FAILURES"
