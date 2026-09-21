#!/usr/bin/env bash
#
# S07/T01 - the packed-package inspection, as raw evidence.
#
# Packs the library, then asserts the package invariants one at a time and names the offending
# entry when one fails: the nuspec id and version, zero <dependency> entries, a lib folder with
# the assembly AND its XML documentation for each target framework, the README and the licence at
# the package root, and no entry from the sample, the tests or probe-live. Everything it checks is
# printed, so the log is the evidence rather than a summary of it.
#
# T02 turns this into scripts/verify-package.sh, the repository's documented inspection path.
# This file exists so the T01 evidence can be regenerated from the working tree.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t01-package-inspection.sh
#
set -u

LOG=docs/uat-logs/S07/t01-package-inspection.txt
PKG=artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
TESTS=tests/Trustsoft.NotifyIcon.Tests
fail=0

# ---------------------------------------------------------------------------------------------
# Environment shim. An agent/instrumented shell can arrive without the Windows profile
# variables - measured here: APPDATA, LOCALAPPDATA, ProgramData, PUBLIC, PROGRAMFILES, COMSPEC,
# PATHEXT and OS all absent, 20 variables instead of the 68 a normal login shell has. With that
# environment, `dotnet build` fails during NuGet's restore graph evaluation with exit 1 and
# "error : Value cannot be null. (Parameter 'path1')" from NuGet.targets GetRestoreSettingsTask,
# while the same content, same user and same directory build with 0 errors once these variables
# are present. Filling them in only when absent leaves a normal shell untouched and makes this
# evidence reproducible in both. It changes nothing about the sources, and it is recorded in the
# log so a reader can see which environment produced the run.
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

check_present() {
  if printf '%s\n' "$LIST" | grep -qF "$1"; then
    echo "  PASS  packed: $1"
  else
    echo "  FAIL  missing from the package: $1"
    fail=1
  fi
}

check_absent() {
  if printf '%s\n' "$LIST" | grep -qF "$1"; then
    echo "  FAIL  packed but must not be: $1"
    fail=1
  else
    echo "  PASS  not packed: $1"
  fi
}

{
  echo "# S07/T01 raw evidence: the packed package"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "environment: APPDATA='$APPDATA' LOCALAPPDATA='$LOCALAPPDATA' TEMP='$TEMP'"
  echo
  echo '## 1. build and pack'
  echo '$ dotnet build Trustsoft.NotifyIcon.sln -c Release'
  BUILD_OUT=$(dotnet build Trustsoft.NotifyIcon.sln -c Release 2>&1)
  BUILD_EXIT=$?
  printf '%s\n' "$BUILD_OUT" | grep -E " error |Build succeeded|Warning\(s\)|Error\(s\)"
  echo "(exit=$BUILD_EXIT)"
  if [ "$BUILD_EXIT" != 0 ]; then
    echo '  FAIL  the build did not succeed, so the package below cannot be trusted to match these sources'
    fail=1
  fi
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  PACK_OUT=$(dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1)
  PACK_EXIT=$?
  printf '%s\n' "$PACK_OUT" | grep -E " error |Successfully created package" | head -3
  echo "(exit=$PACK_EXIT)"
  if [ "$PACK_EXIT" != 0 ]; then
    echo '  FAIL  the pack did not succeed, so every check below would be reading a stale package'
    fail=1
  fi
  echo
  echo '## 2. the package file'
  ls -l "$PKG"
  echo
  echo '## 3. package entries'
  echo "\$ unzip -l $PKG"
  unzip -l "$PKG"
  echo
  echo '## 4. nuspec as written'
  unzip -p "$PKG" Trustsoft.NotifyIcon.nuspec
  echo
  echo '## 5. the invariants, read out of the nuspec'
  NUSPEC="$(unzip -p "$PKG" Trustsoft.NotifyIcon.nuspec)"
  echo "  id                          : $(grep -o '<id>[^<]*</id>' <<<"$NUSPEC")"
  echo "  version                     : $(grep -o '<version>[^<]*</version>' <<<"$NUSPEC")"
  echo "  authors                     : $(grep -o '<authors>[^<]*</authors>' <<<"$NUSPEC")"
  echo "  license                     : $(grep -o '<license [^>]*>' <<<"$NUSPEC")"
  echo "  readme                      : $(grep -o '<readme>[^<]*</readme>' <<<"$NUSPEC")"
  echo "  tags                        : $(grep -o '<tags>[^<]*</tags>' <<<"$NUSPEC")"
  echo "  repository element          : $(grep -o '<repository [^>]*/>' <<<"$NUSPEC")"
  echo "  <dependency> entries        : $(grep -c '<dependency ' <<<"$NUSPEC") (must be 0)"
  echo "  frameworkReferences named   : $(grep -o '<frameworkReference name="[^"]*"' <<<"$NUSPEC" | sort -u | tr '\n' ' ')"
  echo
  echo '## 6. per-entry checks'
  LIST="$(unzip -l "$PKG")"
  check_present 'README.md'
  check_present 'LICENSE'
  for tfm in net8.0-windows7.0 net9.0-windows7.0 net10.0-windows7.0; do
    check_present "lib/$tfm/Trustsoft.NotifyIcon.dll"
    check_present "lib/$tfm/Trustsoft.NotifyIcon.xml"
  done
  check_absent 'Sample'
  check_absent 'Tests'
  check_absent 'testhost'
  check_absent 'probe-live'
  if [ "$(grep -c '<dependency ' <<<"$NUSPEC")" = 0 ]; then
    echo '  PASS  the nuspec declares no package dependency group entry'
  else
    echo '  FAIL  the nuspec declares a <dependency> entry'
    fail=1
  fi
  echo
  echo '## 7. packaging guards'
  echo "\$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-build --filter \"FullyQualifiedName~PackagePurityTests\""
  dotnet test "$TESTS" -c Release --no-build --filter "FullyQualifiedName~PackagePurityTests" 2>&1 | grep -E "^Passed!|^Failed!"
  echo
  echo '## 8. note on the environment shim (above, in the script)'
  echo '  The shim is what makes this file runnable in an agent shell that starts without the Windows'
  echo '  profile variables. It only fills in variables that are absent, and every check below reads the'
  echo '  package the pack step produced, not the environment.'
  echo
  echo "## verdict: $([ $fail = 0 ] && echo 'all package invariants hold' || echo 'FAILURES ABOVE')"
} > "$LOG" 2>&1

sed -n '/## 1\./,/## 2\./p' "$LOG"
sed -n '/## 6\./,$p' "$LOG"
exit $fail
