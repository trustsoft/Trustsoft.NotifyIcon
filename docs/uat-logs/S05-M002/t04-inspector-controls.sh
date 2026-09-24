#!/usr/bin/env bash
#
# S05/T04 - negative controls for the exported-surface assertions in scripts/verify-package.sh.
#
# The plan's requirement: the three new surface assertions are only worth having if each one has
# been seen to FAIL on an artifact that breaks exactly its invariant, naming the offending entry.
# This driver builds deliberately broken COPIES of the real nupkg in a temporary directory, points
# the inspector at each copy by absolute path, records the exit code plus the failing assertion and
# the entry it quotes, and proves at the end that the real package is untouched (same sha256).
#
# Why copies: the inspector is a read-only zip reader, but a control that mutated artifacts/ would
# destroy the artifact-chain evidence the rest of S05 rests on. Every control is a copy under $WORK,
# written with python's zipfile so every other entry survives byte-for-byte, and handed to the
# script as an absolute path so nothing writes into artifacts/.
#
# The four controls, one broken invariant each:
#   A. the T: entry for Trustsoft.NotifyIcon.TrayMenuActivation is removed from ONE lib folder's XML
#      documentation (lib/net8.0-windows7.0/) -> the set comparison (fact ii) must fail and name that
#      entry as MISSING. This is the control that proves the rule compares sets per folder rather
#      than unioning the three folders, which would mask a doc that lost an entry.
#   B. a T:Contoso.NotifyIcon.Sneaky entry is ADDED to one lib folder's XML -> the namespace rule
#      (fact i) must fail and name it;
#   C. a T:Windows.UI.Notifications.ToastNotification entry is ADDED to one lib folder's XML -> the
#      WinRT rule (fact iii) must fail and name it;
#   D. a <dependency> entry is ADDED inside one target-framework group of the nuspec -> the
#      PRE-EXISTING no-dependency assertion (R011 / D038) must still fail, proving the rewrite that
#      added the surface section did not weaken it.
#
# The real package's sha256 is printed before and after and compared; the copies live only under a
# temporary directory that is removed on exit.
#
# Usage (from the repository root): bash docs/uat-logs/S05-M002/t04-inspector-controls.sh
#
set -u

LOG=docs/uat-logs/S05-M002/t04-inspector-controls.txt
REAL=artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
WORK=$(mktemp -d)
overall=0

if [ ! -f "$REAL" ]; then
  echo "FAIL $REAL does not exist; run dotnet pack first" >&2
  exit 1
fi

REAL_BEFORE=$(sha256sum "$REAL" | cut -d' ' -f1)

# Build the four broken copies. Python's zipfile is used rather than re-zipping by hand because the
# nuspec, the XML documentation and the entry names have to survive byte-for-byte apart from the one
# mutation; the mutated entry is decoded and re-encoded as UTF-8 (the nuspec's BOM is a U+FEFF
# character, so it round-trips).
python - "$REAL" "$WORK" <<'PY'
import os, re, sys, zipfile

real, work = sys.argv[1], sys.argv[2]

# Only this folder's documentation is mutated, so the per-folder reading is what is exercised.
DOC_TARGET = "lib/net8.0-windows7.0/Trustsoft.NotifyIcon.xml"
REMOVED_TYPE = "Trustsoft.NotifyIcon.TrayMenuActivation"


def rewrite(dest, mutate):
    with zipfile.ZipFile(real) as zin:
        items = [(i, zin.read(i.filename)) for i in zin.infolist()]
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zout:
        for info, data in items:
            zout.writestr(info, mutate(info.filename, data))


def drop_documented_type(name, data):
    if name != DOC_TARGET:
        return data
    text = data.decode("utf-8")
    # Drop the whole <member name="T:..."> ... </member> block, indentation included. A missing
    # block is a broken control, not a passing one, so fail loudly here.
    pattern = r'[ \t]*<member name="T:' + re.escape(REMOVED_TYPE) + r'">.*?</member>'
    text, count = re.subn(pattern, "", text, count=1, flags=re.S)
    if count != 1:
        raise SystemExit("control A: T:" + REMOVED_TYPE + " was not found in " + name)
    return text.encode("utf-8")


def add_documented_type(entry):
    def mutate(name, data):
        if name != DOC_TARGET:
            return data
        text = data.decode("utf-8")
        if "</members>" not in text:
            raise SystemExit("control: no </members> in " + name)
        addition = (
            '        <member name="T:' + entry + '">\n'
            "            <summary>Synthetic control entry, not a real library type.</summary>\n"
            "        </member>\n"
        )
        return text.replace("</members>", addition + "    </members>", 1).encode("utf-8")
    return mutate


def add_dependency(name, data):
    if not name.endswith(".nuspec"):
        return data
    text = data.decode("utf-8")
    group = '<group targetFramework="net8.0-windows7.0" />'
    if group not in text:
        raise SystemExit("control D: the empty net8.0-windows7.0 group was not found in " + name)
    replacement = (
        '<group targetFramework="net8.0-windows7.0">'
        '<dependency id="H.NotifyIcon" version="9.9.9" /></group>'
    )
    return text.replace(group, replacement, 1).encode("utf-8")


rewrite(os.path.join(work, "broken-removed-type.nupkg"), drop_documented_type)
rewrite(os.path.join(work, "broken-foreign-namespace.nupkg"), add_documented_type("Contoso.NotifyIcon.Sneaky"))
rewrite(os.path.join(work, "broken-winrt-type.nupkg"), add_documented_type("Windows.UI.Notifications.ToastNotification"))
rewrite(os.path.join(work, "broken-dependency.nupkg"), add_dependency)
PY

run_control() {
  local label="$1" pkg="$2" assertPattern="$3" entryPattern="$4" runFile="$WORK/run.txt"

  echo "## $label"
  echo "\$ bash scripts/verify-package.sh $pkg"
  bash scripts/verify-package.sh "$pkg" > "$runFile" 2>&1
  local exit_code=$?
  echo "(exit=$exit_code)"
  echo
  # Only the lines that matter: the failing assertion lines, the offenders they quote, the verdict.
  grep -E '^  FAIL|^          |^VERDICT' "$runFile"
  echo
  if [ "$exit_code" -eq 0 ]; then
    echo "  CONTROL FAILED: the script exited 0 on a package that breaks an invariant"
    overall=1
  elif ! grep -qE "$assertPattern" "$runFile"; then
    echo "  CONTROL FAILED: the expected assertion did not fail (looked for: $assertPattern)"
    overall=1
  elif ! grep -A 5 -E "$assertPattern" "$runFile" | grep -qE "$entryPattern"; then
    echo "  CONTROL FAILED: the expected assertion failed but did not name the offending entry (looked for: $entryPattern)"
    overall=1
  else
    echo "  control holds: non-zero exit, the expected assertion failed, and it names the offending entry"
  fi
  echo
}

{
  echo "# S05/T04 raw evidence: negative controls for the exported-surface assertions in"
  echo "# scripts/verify-package.sh."
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-unknown} / $(uname -s)"
  echo "shell: $BASH_VERSION"
  echo "real package: $REAL sha256=$REAL_BEFORE ($(wc -c < "$REAL") bytes)"
  echo
  echo "Every control below runs the real inspector against a deliberately broken COPY of the real"
  echo "package, in a temporary directory. The real package is never modified; the sha256 comparison"
  echo "at the end proves it, and the copies are removed with the temporary directory."
  echo

  run_control \
    "control A - the T: entry for Trustsoft.NotifyIcon.TrayMenuActivation removed from lib/net8.0-windows7.0/'s XML documentation (the set comparison, fact ii, must name it as missing)" \
    "$WORK/broken-removed-type.nupkg" \
    'the documented types outside Trustsoft\.NotifyIcon\.Interop\.' \
    'missing Trustsoft\.NotifyIcon\.TrayMenuActivation'

  run_control \
    "control B - a T:Contoso.NotifyIcon.Sneaky entry added to one lib folder's XML documentation (the namespace rule, fact i, must name it)" \
    "$WORK/broken-foreign-namespace.nupkg" \
    'every documented type lies inside the Trustsoft\.NotifyIcon namespace' \
    'Contoso\.NotifyIcon\.Sneaky'

  run_control \
    "control C - a T:Windows.UI.Notifications.ToastNotification entry added to one lib folder's XML documentation (the WinRT rule, fact iii, must name it)" \
    "$WORK/broken-winrt-type.nupkg" \
    'no documented type names a WinRT namespace' \
    'Windows\.UI\.Notifications\.ToastNotification'

  run_control \
    "control D - a <dependency> entry added inside the net8.0-windows7.0 group (the pre-existing R011 / D038 assertion must still fail)" \
    "$WORK/broken-dependency.nupkg" \
    'the nuspec declares [0-9]+ <dependency> entry/entries' \
    'H\.NotifyIcon" version="9\.9\.9"'

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
    echo "  Four controls, four non-zero exits, each naming the entry or dependency that broke its"
    echo "  invariant. The green run against the real package is a separate command"
    echo "  (bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg)."
  else
    echo "  FAILURES ABOVE"
  fi
} > "$LOG" 2>&1

rm -rf "$WORK"
sed -n '/## control A/,$p' "$LOG"
exit $overall
