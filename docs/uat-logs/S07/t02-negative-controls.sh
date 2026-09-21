#!/usr/bin/env bash
#
# S07/T02 - negative controls for scripts/verify-package.sh.
#
# The plan's requirement on this task: "Prove the script can fail rather than only passing ...
# A verifier that has never been seen to fail is not a verifier." This driver builds deliberately
# broken COPIES of the real package in a temporary directory, points the script at each of them,
# records the exit code and the offending entry the script names, and proves at the end that the
# real package is untouched (same sha256) and that the copies are gone.
#
# The real package is never modified: every control is a copy under $WORK, and the copy is handed
# to the script as an absolute path so nothing writes into artifacts/.
#
# The three controls, one broken invariant each:
#   1. a <dependency> entry inside one of the target-framework groups (R011 / D038), and its exact
#      text must appear in the failure so the offending entry is named, not summarised;
#   2. an entry the package must not contain (a sample assembly under lib/), which the forbidden-entry
#      assertion and the allowed-set assertion must both catch;
#   3. a nuspec version that disagrees with the library project (1.0.0 -> 9.9.9), which must fail
#      and name BOTH values - this is what proves the identity check reads the csproj rather than
#      trusting whatever the package says.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t02-negative-controls.sh
#
set -u

LOG=docs/uat-logs/S07/t02-negative-controls.txt
REAL=artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
WORK=$(mktemp -d)
overall=0

if [ ! -f "$REAL" ]; then
  echo "FAIL $REAL does not exist; run dotnet pack first" >&2
  exit 1
fi

REAL_BEFORE=$(sha256sum "$REAL" | cut -d' ' -f1)

# Build the three broken copies. Python's zipfile is used rather than re-zipping by hand because
# the nuspec and the entry names have to survive byte-for-byte apart from the one mutation.
python - "$REAL" "$WORK" <<'PY'
import os, sys, zipfile

real, work = sys.argv[1], sys.argv[2]


def rewrite(dest, mutate):
    with zipfile.ZipFile(real) as zin:
        items = [(i, zin.read(i.filename)) for i in zin.infolist()]
    out = []
    for info, data in items:
        out.append((info, mutate(info.filename, data)))
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zout:
        for info, data in out:
            zout.writestr(info, data)


def add_dependency(name, data):
    if name.endswith(".nuspec"):
        text = data.decode("utf-8")
        text = text.replace(
            '<group targetFramework="net8.0-windows7.0" />',
            '<group targetFramework="net8.0-windows7.0">'
            '<dependency id="H.NotifyIcon" version="9.9.9" /></group>',
        )
        return text.encode("utf-8")
    return data


def bump_version(name, data):
    if name.endswith(".nuspec"):
        return data.decode("utf-8").replace("<version>1.0.0</version>", "<version>9.9.9</version>").encode("utf-8")
    return data


rewrite(os.path.join(work, "broken-dependency.nupkg"), add_dependency)
rewrite(os.path.join(work, "broken-version.nupkg"), bump_version)

# The sample-entry control adds an entry rather than editing one, so it needs its own pass.
with zipfile.ZipFile(real) as zin:
    items = [(i, zin.read(i.filename)) for i in zin.infolist()]
with zipfile.ZipFile(os.path.join(work, "broken-sample-entry.nupkg"), "w", zipfile.ZIP_DEFLATED) as zout:
    for info, data in items:
        zout.writestr(info, data)
    zout.writestr("lib/net8.0-windows7.0/Trustsoft.NotifyIcon.Sample.dll", b"MZ not a real assembly")
PY

run_control() {
  local label="$1" pkg="$2" pattern="$3" runFile="$WORK/run.txt"

  echo "## $label"
  echo "\$ bash scripts/verify-package.sh $pkg"
  bash scripts/verify-package.sh "$pkg" > "$runFile" 2>&1
  local exit_code=$?
  echo "(exit=$exit_code)"
  echo
  # Only the lines that matter: the failing assertion and the offender it quotes.
  grep -E '^  FAIL|^          |^VERDICT' "$runFile"
  echo
  if [ "$exit_code" -eq 0 ]; then
    echo "  CONTROL FAILED: the script exited 0 on a package that breaks an invariant"
    overall=1
  elif ! grep -qE "$pattern" "$runFile"; then
    echo "  CONTROL FAILED: exit was non-zero but the output does not name what broke (looked for: $pattern)"
    overall=1
  else
    echo "  control holds: non-zero exit, and the offending entry is quoted above"
  fi
  echo
}

{
  echo "# S07/T02 raw evidence: negative controls for scripts/verify-package.sh"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-unknown} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "real package: $REAL sha256=$REAL_BEFORE"
  echo
  echo "Every control below runs the real script against a deliberately broken COPY of the real"
  echo "package, in a temporary directory. The real package is never modified; the sha256 comparison"
  echo "at the end proves it, and the copies are removed with the temporary directory."
  echo

  run_control \
    "control 1 - a <dependency> entry inside the net8.0-windows group (R011 / D038)" \
    "$WORK/broken-dependency.nupkg" \
    'H\.NotifyIcon" version="9\.9\.9"'

  run_control \
    "control 2 - an entry from the sample under lib/ (a shipped package must contain nothing of the sort)" \
    "$WORK/broken-sample-entry.nupkg" \
    'lib/net8\.0-windows7\.0/Trustsoft\.NotifyIcon\.Sample\.dll'

  run_control \
    "control 3 - the nuspec version disagrees with the library project (1.0.0 -> 9.9.9)" \
    "$WORK/broken-version.nupkg" \
    "the nuspec version is '9\.9\.9' but the library project declares '1\.0\.0'"

  echo "## the real package is untouched"
  REAL_AFTER=$(sha256sum "$REAL" | cut -d' ' -f1)
  echo "  before: $REAL_BEFORE"
  echo "  after:  $REAL_AFTER"
  if [ "$REAL_BEFORE" = "$REAL_AFTER" ]; then
    echo "  PASS  the real package is byte-for-byte unchanged ($(wc -c < "$REAL") bytes)"
  else
    echo "  FAIL  the real package changed while the controls ran"
    overall=1
  fi
  echo
  echo "## verdict"
  if [ "$overall" -eq 0 ]; then
    echo "  Three controls, three non-zero exits, each naming the offending entry. The green run against"
    echo "  the real package is a separate command (docs/uat-logs/S07/t02-verify-package.txt)."
  else
    echo "  FAILURES ABOVE"
  fi
} > "$LOG" 2>&1

rm -rf "$WORK"
sed -n '/## control 1/,$p' "$LOG"
exit $overall
