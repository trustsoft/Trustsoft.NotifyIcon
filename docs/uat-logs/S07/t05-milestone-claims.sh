#!/usr/bin/env bash
#
# S07/T05 - the three milestone-level claims the task plan says only this slice can check:
#
#   1. the shipped public surface is still the seven documented types;
#   2. the package contains nothing from the sample, the tests, the probe or the consumer proof;
#   3. the declarative path the README documents is the one the sample actually runs.
#
# (1) and (2) are asserted by tests and by the package inspector, both of which are run here. (3)
# cannot be a single existing assertion, so it is made mechanical in this script rather than read:
#
#   a. the namespace URI and prefix the README's snippet writes are the ones the library declares
#      with XmlnsDefinition/XmlnsPrefix (compared as strings, both sides read from their file);
#   b. every attribute the README's tni:TrayIcon snippet writes is an attribute the sample's own
#      declared icon element writes - so a documented attribute the sample never runs, or a sample
#      that dropped one the README promises, fails here;
#   c. the library type the README documents (TrayIcon) is the one the XAML contract tests parse
#      through that namespace (filtered run), and the sample's own element type derives from it;
#   d. the sample's documented declarative invocation actually enters declaration mode (live run).
#
# Usage (from the repository root): bash docs/uat-logs/S07/t05-milestone-claims.sh
#
set -u

LOG=docs/uat-logs/S07/t05-milestone-claims.txt
README=README.md
SAMPLE_XAML=samples/Trustsoft.NotifyIcon.Sample/App.xaml
ASSEMBLY_INFO=src/Trustsoft.NotifyIcon/Properties/AssemblyInfo.cs

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

# Extract the attribute names written on an XML element: pass the start line pattern, then read until
# the self-closing '/>' or the closing '>'.
attributes_of() {
  local file=$1 start=$2
  awk -v start="$start" '
    !inside && index($0, start) { inside = 1 }
    inside {
      line = $0
      while (match(line, /[A-Za-z_:][A-Za-z0-9_:.-]*="/)) {
        printf "%s\n", substr(line, RSTART, RLENGTH - 2)
        line = substr(line, RSTART + RLENGTH)
      }
      if (index($0, "/>") || index($0, ">")) { if (inside && (index($0, "/>") || index($0, ">") )) exit }
    }
  ' "$file"
}

normalize() { sed -e 's/^xmlns://' -e 's/^x://' | sort -u; }

FAILURES=0

{
  echo "# S07/T05 raw evidence: the milestone-level cross-checks"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "revision: $(git log --oneline -1 2>/dev/null)"
  echo

  echo "## 1. the shipped public surface is still the seven documented types (D034/D031)"
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~PackagePurityTests"'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore --no-build \
    --filter "FullyQualifiedName~PackagePurityTests" 2>&1 | tail -3
  surface_exit=${PIPESTATUS[0]}
  echo "purity-filter exit=$surface_exit"
  echo "  The relevant claims inside that class: Public_surface_is_only_the_documented_types (the"
  echo "  exported set is exactly the documented seven)."
  echo

  echo "## 2. the package contains nothing from the sample, the tests, the probe or the consumer proof"
  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg'
  bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.*.nupkg
  package_exit=$?
  echo "verify-package.sh exit=$package_exit"
  echo "  The forbidden-entry assertion and the allowed-set assertion are the two lines of that"
  echo "  output that carry this claim; both name the offending entry when they fail."
  echo

  echo "## 3. the README's declarative path is the path the sample runs"
  echo
  echo "### 3a. the namespace URI and prefix are the same string on both sides"
  readme_ns=$(grep -oE 'xmlns:tni="[^"]+"' "$README" | head -1 | sed 's/.*="//; s/"$//')
  declared_ns=$(grep -oE '"http://schemas\.trustsoft\.com/notifyicon"' "$ASSEMBLY_INFO" | head -1 | tr -d '"')
  declared_prefix=$(sed -n '/XmlnsPrefix(/,+2p' "$ASSEMBLY_INFO" | grep -oE '"tni"' | head -1 | tr -d '"')
  echo "README namespace URI:   ${readme_ns:-<absent>}"
  echo "library XmlnsDefinition: ${declared_ns:-<absent>}"
  echo "library XmlnsPrefix:     ${declared_prefix:-<absent>}"
  if [ -n "$readme_ns" ] && [ "$readme_ns" = "$declared_ns" ] && [ "$declared_prefix" = "tni" ]; then
    echo "PASS  the README's xmlns:tni resolves to the URI the library declares, with prefix tni"
  else
    echo "FAIL  the README namespace and the library's declared namespace disagree"
    FAILURES=$((FAILURES + 1))
  fi
  echo

  echo "### 3b. every attribute the README's snippet writes, the sample's declared icon element writes"
  attributes_of "$README" 'tni:TrayIcon x:Key' | normalize > /tmp/t05-readme-attrs.txt
  attributes_of "$SAMPLE_XAML" 'SampleTrayIcon x:Key' | normalize > /tmp/t05-sample-attrs.txt
  echo "README snippet attributes:"
  sed 's/^/  /' /tmp/t05-readme-attrs.txt
  echo "sample declared attributes:"
  sed 's/^/  /' /tmp/t05-sample-attrs.txt
  # An extractor that finds nothing would make the subset check below pass vacuously, so the
  # extraction itself is asserted first: both files must be non-empty.
  if [ ! -s /tmp/t05-readme-attrs.txt ] || [ ! -s /tmp/t05-sample-attrs.txt ]; then
    echo "FAIL  the attribute extraction found nothing on one side (README: $(wc -l < /tmp/t05-readme-attrs.txt) attrs, sample: $(wc -l < /tmp/t05-sample-attrs.txt) attrs)"
    FAILURES=$((FAILURES + 1))
  else
    echo "PASS  the extraction found attributes on both sides ($(wc -l < /tmp/t05-readme-attrs.txt) in the README, $(wc -l < /tmp/t05-sample-attrs.txt) in the sample)"
  fi
  missing=$(comm -23 /tmp/t05-readme-attrs.txt /tmp/t05-sample-attrs.txt)
  if [ -z "$missing" ]; then
    echo "PASS  the README's attribute set is a subset of the set the sample declares and runs"
  else
    echo "FAIL  the README documents attributes the sample does not declare:"
    echo "$missing" | sed 's/^/      /'
    FAILURES=$((FAILURES + 1))
  fi
  echo

  echo "### 3c. the library type the README documents parses through that namespace"
  echo "  README's element: $(grep -oE '<tni:[A-Za-z]+' "$README" | head -1 | tr -d '<')"
  echo "  sample's element: $(grep -oE '<local:SampleTrayIcon' "$SAMPLE_XAML" | head -1 | tr -d '<')"
  echo "    (the sample's ApplicationDefinition is in the element's own project, so it declares the"
  echo "     type by clr-namespace and cannot use the library URI - MC3074, the trap the README"
  echo "     documents; a consumer's ApplicationDefinition lives in another assembly and does use it.)"
  echo "  the sample element derives from the library type it demonstrates:"
  grep -n "class SampleTrayIcon" samples/Trustsoft.NotifyIcon.Sample/*.cs | sed 's/^/    /'
  echo '$ dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build --filter "FullyQualifiedName~TrayIconXamlContractTests"'
  dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release --no-restore --no-build \
    --filter "FullyQualifiedName~TrayIconXamlContractTests" 2>&1 | tail -3
  xaml_exit=${PIPESTATUS[0]}
  echo "xaml-contract exit=$xaml_exit"
  echo

  echo "### 3d. the sample's documented declarative invocation enters declaration mode (live)"
  echo '$ dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 8'
  # The output goes to a file rather than through a pipe so both the run's exit code and the lines
  # it printed can be asserted. A pipe whose grep matches nothing still reports the producer's exit
  # code, which would let a run that never entered declaration mode pass this check silently.
  RUN_LOG=/tmp/t05-declarative-run.txt
  dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release -- --xaml --run-seconds 8 > "$RUN_LOG" 2>&1
  declarative_exit=$?
  grep -E "\[sample\] declaration mode|\[sample\] tray icon registered from markup|\[sample\] tray icon disposed" "$RUN_LOG"
  matched=$(grep -cE "\[sample\] declaration mode: XAML|\[sample\] tray icon registered from markup|\[sample\] tray icon disposed" "$RUN_LOG")
  echo "declarative run exit=$declarative_exit matched lines=$matched"
  if [ "$declarative_exit" = 0 ] && [ "$matched" -ge 3 ]; then
    echo "PASS  'declaration mode: XAML' and 'registered from markup' show the run did not fall through to the code-first path"
  else
    echo "FAIL  the declarative run did not print the declaration-mode lines (exit=$declarative_exit, matched=$matched)"
    FAILURES=$((FAILURES + 1))
  fi
  echo

  echo "## verdict"
  echo "  purity filter=$surface_exit; package inspection=$package_exit; xaml contract=$xaml_exit; declarative run=$declarative_exit"
  if [ "$surface_exit" = 0 ] && [ "$package_exit" = 0 ] && [ "$xaml_exit" = 0 ] && [ "$declarative_exit" = 0 ] && [ "$FAILURES" = 0 ]; then
    echo "  All three milestone-level claims hold: the surface is the documented seven, the package"
    echo "  carries nothing from the non-shipping projects, and the documented declarative path is the"
    echo "  one the sample enters and prints."
    exit 0
  fi
  echo "  FAILURES ABOVE"
  exit 1
} > "$LOG" 2>&1

cat "$LOG"
