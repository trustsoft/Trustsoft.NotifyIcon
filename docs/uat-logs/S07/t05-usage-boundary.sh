#!/usr/bin/env bash
#
# S07/T05 (reopen fix) - the exit-code contract of scripts/verify-package.sh, MEASURED.
#
# Why this producer exists: the T05 UAT text claimed in three places that an absent or unreadable
# package argument exits 2 ("usage error"). The shipped script does not do that - exit 2 is
# reachable only when there is no package argument at all or when `unzip` is missing from PATH;
# a package argument that is absent, a directory, or not a zip exits 1 with the offending path
# quoted. A claim about an exit code is exactly the kind of claim this slice exists to stop
# asserting from memory, so this driver runs every entry point and records what the script does.
#
# Each case below states the expected exit code BEFORE it runs, and a mismatch fails the driver
# (a verifier that only passes is not a verifier). Text checks are applied to the merged
# stdout+stderr, because the usage message goes to stderr and the assertion output to stdout.
#
# Two extra properties are asserted rather than left implied:
#   * every bad-PACKAGE-argument case must NOT print the usage line - that is what makes exit 1
#     ("the package is wrong") distinguishable from exit 2 ("you invoked the inspector wrongly");
#   * exit 2 must occur in exactly the two usage-error cases and nowhere else.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t05-usage-boundary.sh
#
set -u

LOG=docs/uat-logs/S07/t05-usage-boundary.txt
SCRIPT=scripts/verify-package.sh
REAL="$(ls artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null | head -1)"
WORK="$(mktemp -d)"
BASH_ABS="$(command -v bash)"

overall=0
cases=0
rcs="$WORK/rcs"

if [ -z "$REAL" ] || [ ! -f "$REAL" ]; then
  echo "FAIL no package under artifacts/; run 'dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release' first" >&2
  exit 1
fi

# A PATH that holds a self-contained `dirname` and nothing else, so `command -v unzip` fails and
# the script's up-front unzip guard is the next thing it reaches. The shim is written with an
# absolute interpreter path so it needs no PATH entry of its own; copying /usr/bin/dirname.exe
# instead would need its MSYS runtime DLLs and exit 127 without them (measured while building this).
mkdir -p "$WORK/no-unzip-bin"
{
  printf '#!%s\n' "$BASH_ABS"
  printf 'd="${1%%/*}"\n'
  printf '[ "$d" = "$1" ] && d="."\n'
  printf 'printf "%%s\\n" "$d"\n'
} > "$WORK/no-unzip-bin/dirname"
chmod +x "$WORK/no-unzip-bin/dirname"

# An existing file that is not a zip: a real bad package argument that is not a missing path.
printf 'this is not a zip archive\n' > "$WORK/not-a-zip.nupkg"

# case_check <label> <expected exit> <must contain (ERE, empty = skip)> <must not contain (FIXED string, empty = skip)> <command>
case_check() {
  local label="$1" expected="$2" want="$3" forbid="$4" cmd="$5"
  local out rc nlines
  cases=$((cases + 1))
  out="$WORK/case-$cases.out"

  echo "## $label"
  echo "\$ $cmd"
  eval "$cmd" > "$out" 2>&1
  rc=$?
  nlines=$(wc -l < "$out" | tr -d ' ')

  echo "(exit=$rc; contract says $expected)"
  if [ "$nlines" -eq 0 ]; then
    echo "  <no output on stdout or stderr>"
  else
    sed 's/^/  /' "$out"
  fi
  printf '%s=%s\n' "$label" "$rc" >> "$rcs"

  if [ "$rc" -ne "$expected" ]; then
    echo "  FAIL  measured exit $rc, the contract says $expected"
    overall=1
    return
  fi
  if [ -n "$want" ] && ! grep -qE "$want" "$out"; then
    echo "  FAIL  exit matched but the output does not contain the required text: /$want/"
    overall=1
    return
  fi
  if [ -n "$forbid" ] && grep -qF "$forbid" "$out"; then
    echo "  FAIL  the output contains text it must not: '$forbid'"
    overall=1
    return
  fi
  echo "  PASS  exit $rc, and the output is what the corrected contract describes"
  echo
}

{
  echo "# S07/T05 raw evidence: the exit-code contract of scripts/verify-package.sh"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-unknown} / $(uname -s)"
  echo "script: $SCRIPT"
  echo "real package: $REAL sha256=$(sha256sum "$REAL" | cut -d' ' -f1)"
  echo
  echo "Every case below states the exit code the corrected contract predicts, then runs the shipped"
  echo "script and records what it actually did. The reopen finding was the opposite order: the UAT"
  echo "text said an absent or unreadable package argument exits 2; the script exits 1 and quotes the"
  echo "path. The usage message is on stderr, so each case's merged output is printed verbatim."
  echo

  case_check \
    "no package argument at all (a usage error)" \
    2 \
    'usage: bash scripts/verify-package\.sh' \
    'assertions failed' \
    "\"$BASH_ABS\" $SCRIPT"

  case_check \
    "an absent package path" \
    1 \
    "package file exists and is readable: .${REAL%/*}/does-not-exist\.nupkg. does not exist" \
    'usage: bash scripts/verify-package.sh' \
    "\"$BASH_ABS\" $SCRIPT ${REAL%/*}/does-not-exist.nupkg"

  case_check \
    "a directory as the package argument" \
    1 \
    "package file exists and is readable: .${REAL%/*}. does not exist" \
    'usage: bash scripts/verify-package.sh' \
    "\"$BASH_ABS\" $SCRIPT ${REAL%/*}"

  case_check \
    "a file that exists but is not a zip" \
    1 \
    'VERDICT +[0-9]+ of [0-9]+ assertions failed' \
    'usage: bash scripts/verify-package.sh' \
    "\"$BASH_ABS\" $SCRIPT $WORK/not-a-zip.nupkg"

  case_check \
    "unzip absent from PATH (the other usage-error path)" \
    2 \
    'unzip is required to inspect a nupkg and is not on PATH' \
    'assertions failed' \
    "env PATH=\"$WORK/no-unzip-bin\" \"$BASH_ABS\" $SCRIPT $REAL"

  case_check \
    "the real package (the only passing path)" \
    0 \
    'VERDICT +all 15 assertions hold' \
    'usage: bash scripts/verify-package.sh' \
    "\"$BASH_ABS\" $SCRIPT $REAL"

  echo "## every entry point, and the code it returned"
  sed 's/^/  /' "$rcs"
  echo

  echo "## exit 2 is a usage error and nothing else"
  awk -F= '$NF == 2 { $NF=""; sub(/=$/, ""); sub(/[[:space:]]+$/, ""); print }' "$rcs" | sort > "$WORK/two"
  printf '%s\n' 'no package argument at all (a usage error)' 'unzip absent from PATH (the other usage-error path)' | sort > "$WORK/two-expected"
  two_count="$(wc -l < "$WORK/two" | tr -d ' ')"
  if [ "$two_count" -eq 2 ] && cmp -s "$WORK/two" "$WORK/two-expected"; then
    echo "  PASS  exit 2 occurred in exactly the two usage-error cases, and in no package-argument case"
  else
    echo "  FAIL  exit 2 occurred in $two_count case(s) (expected exactly the two usage-error cases):"
    sed 's/^/          /' "$WORK/two"
    overall=1
  fi
  echo

  echo "## the script's own header states the same contract"
  header="$(grep -n '^# Exit codes:' "$SCRIPT")"
  if [ -n "$header" ]; then
    echo "  $SCRIPT:${header%%:*}$(printf '%s' "${header#*:}" | sed 's/^# Exit codes:/  Exit codes:/')"
  fi
  if printf '%s' "$header" | grep -q '2 = usage error'; then
    echo "  PASS  the header says '2 = usage error', which is what the six cases above measure"
  else
    echo "  FAIL  the header no longer says '2 = usage error'; the document and the script disagree again"
    overall=1
  fi
  echo

  echo "## verdict"
  if [ "$overall" -eq 0 ]; then
    echo "  $cases cases, $cases expected exit codes matched. Exit 0 = every invariant holds (all 15";
    echo "  assertions), exit 1 = a package argument is absent, a directory or not a zip (the offending path is quoted), exit 2 = usage error only (no package argument, or unzip absent)."
  else
    echo "  FAILURES ABOVE"
  fi
} > "$LOG" 2>&1

rm -rf "$WORK"
sed -n '/## no package argument/,$p' "$LOG"
exit $overall
