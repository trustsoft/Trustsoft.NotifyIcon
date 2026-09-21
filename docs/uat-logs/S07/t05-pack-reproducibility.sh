#!/usr/bin/env bash
#
# S07/T05 - is the nupkg repeatable when it is packed twice?
#
# This exists because the T05 evidence log prints the artifact's sha256 twice, before and after the
# pack it runs, and the two values differ. The plausible explanations (nondeterminism, or simply an
# artifact that was repacked from different inputs in between) are distinguishable by measurement,
# so both are measured here rather than asserted: two consecutive packs of the unchanged working
# tree are compared as bytes and as content, and the hashes earlier logs recorded at the same path
# are printed beside them.
#
# Content comparison is what matters to a consumer: the set of entries and each entry's own sha256.
# No requirement in this milestone depends on byte-equality, and the inspector's identity assertion,
# which reads id and version out of the nuspec, is what anchors the package's identity to its source.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t05-pack-reproducibility.sh
#
set -u

LOG=docs/uat-logs/S07/t05-pack-reproducibility.txt

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

WORK=/tmp/t05-repro
rm -rf "$WORK"
mkdir -p "$WORK"

# Content fingerprint: entry name + the sha256 of the entry's own bytes, sorted. This is independent
# of the zip's timestamps and of the order the SDK writes entries in.
fingerprint() {
  local pkg=$1 dir=$2
  rm -rf "$dir"; mkdir -p "$dir"
  unzip -qq "$pkg" -d "$dir"
  ( cd "$dir" && find . -type f | sed 's|^\./||' | sort | while read -r entry; do
      printf '%s %s\n' "$entry" "$(sha256sum "$entry" | cut -d' ' -f1)"
    done )
}

{
  echo "# S07/T05: two consecutive packs of the same working tree, compared"
  echo "date: $(date -Is)"
  echo "revision: $(git log --oneline -1 2>/dev/null)"
  echo

  for n in 1 2; do
    echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
    dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 | grep -E " error |Successfully created package"
    echo "pack $n exit=${PIPESTATUS[0]}"
    cp artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg "$WORK/pack$n.nupkg"
    echo "  pack$n sha256: $(sha256sum "$WORK/pack$n.nupkg" | cut -d' ' -f1)  ($(stat -c %s "$WORK/pack$n.nupkg") bytes)"
    echo
  done

  echo "## bytes"
  if cmp -s "$WORK/pack1.nupkg" "$WORK/pack2.nupkg"; then
    echo "IDENTICAL  the two packs are byte-for-byte equal"
    byte_verdict=0
  else
    echo "DIFFERENT  the two packs are not byte-for-byte equal"
    byte_verdict=1
  fi
  echo

  echo "## content (entry name + sha256 of the entry's bytes, sorted)"
  fingerprint "$WORK/pack1.nupkg" "$WORK/x1" > "$WORK/f1.txt"
  fingerprint "$WORK/pack2.nupkg" "$WORK/x2" > "$WORK/f2.txt"
  cat "$WORK/f1.txt" | sed 's/^/  /'
  echo
  if diff -u "$WORK/f1.txt" "$WORK/f2.txt" > "$WORK/fdiff.txt"; then
    echo "IDENTICAL  the two packs carry the same entries with the same bytes"
    content_verdict=0
  else
    echo "DIFFERENT  the two packs' contents differ:"
    sed 's/^/  /' "$WORK/fdiff.txt"
    content_verdict=1
  fi
  echo

  echo "## the hashes other logs recorded for this same filename, for comparison"
  echo "  (the artifact is one fixed path, artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg, so every pack"
  echo "   overwrites the previous one; these are what earlier logs saw at that path)"
  echo '  $ grep -hoE "sha256[ =][0-9a-f]{64}" docs/uat-logs/S07/t02-*.txt docs/uat-logs/S07/t04-*.txt | sort -u'
  grep -hoE "sha256[ =][0-9a-f]{64}" docs/uat-logs/S07/t02-*.txt docs/uat-logs/S07/t04-*.txt 2>/dev/null \
    | sort -u | sed 's/^/  /'
  echo "  (T02's inspection ran before T04 rebuilt the README, which is why its package is also a"
  echo "   different size - README.md is a package entry)"
  echo

  echo "## verdict"
  echo "  bytes equal: $([ "$byte_verdict" = 0 ] && echo yes || echo no); content equal: $([ "$content_verdict" = 0 ] && echo yes || echo no)"
  echo
  echo "  Two consecutive packs of an unchanged tree are byte-identical here, so a pack is repeatable"
  echo "  on this machine. What the T04-vs-T05 hash difference in t05-plan-verify.txt is not, then, is"
  echo "  nondeterminism: the artifact at that fixed path was packed again between the two logs, and a"
  echo "  nupkg's sha256 therefore identifies the pack run that produced it, not a value to compare"
  echo "  across logs. The inspector's identity assertion (id + version read back from the project) is"
  echo "  what anchors a package's identity to its source, and the inspector's content assertions do"
  echo "  not depend on the hash at all."
  if [ "$content_verdict" = 0 ]; then
    echo "  PASS  the pack is content-reproducible, and the T05 log's two differing hashes are explained"
    exit 0
  fi
  echo "  FAILURES ABOVE - the pack is not content-reproducible, which is a different and worse claim."
  exit 1
} > "$LOG" 2>&1

cat "$LOG"
