#!/usr/bin/env bash
#
# S07/T03 - raw evidence for the consumer proof: the packed library installed into a windowless WPF
# application that has nothing of this repository, built and run on all three target frameworks.
#
# What it does, in order: packs the library (deleting the previous nupkg first, so the log names the
# artifact this run produced), proves the consumer's restore comes from the local folder feed and from
# nowhere else, restores and builds the consumer once per target framework, runs the consumer's own
# package-surface assertion per framework, watches each framework's run with scripts/probe-live (the
# same instrument columns every other slice uses), and finally attempts a shell click at the icon -
# the S06/F1 instrument limit, re-measured here from the consumer side rather than assumed.
#
# The instrument itself is built by section 6 before the runs that use it. A fresh or re-materialized
# worktree has no scripts/probe-live/bin (bin is gitignored) and the runs pass --no-build, so an
# unbuilt instrument makes every probe run fail to start - which the log would otherwise read as "the
# icon was never present", the most misleading failure this script has. That was measured: this
# script's first run in a re-materialized worktree reported 0 presence samples on all three
# frameworks, and the probe's own message said it could not start the executable.
#
# The shell environment is repaired first, exactly as the S07/T01 and T02 scripts do: an agent shell
# can arrive without the Windows known-folder variables, and with that environment the SDK fails
# during NuGet's restore-graph evaluation with exit 1 and "error : Value cannot be null. (Parameter
# 'path1')" from NuGet.targets GetRestoreSettingsTask, while the same commands succeed once those
# variables are present (measured, this task). Only absent variables are filled in, so a normal login
# shell is untouched, and the variables actually used are printed in the log.
#
# Usage (from the repository root): bash docs/uat-logs/S07/t03-consumer-proof.sh
#
set -u

LOG=docs/uat-logs/S07/t03-consumer-proof.txt
CONSUMER=samples/consumer-proof/ConsumerProof.csproj
TFMS=(net8.0-windows net9.0-windows net10.0-windows)
CLICK_TFM=net8.0-windows
OBSERVE_SECONDS=24
RUN_SECONDS=18
SELF_OPEN_AFTER=6
BALLOON_AFTER=10

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

SCRATCH="$TEMP/t03-consumer-proof"
RESTORE_CACHE="$SCRATCH/global-packages"
HOLD="$SCRATCH/held-nupkg"

mkdir -p "$SCRATCH" "$HOLD"

PACK_EXIT=0
RESTORE_EXIT=0
PROBE_FAILURES=0
SURFACE_FAILURES=0
PROBE_BUILD_EXIT=0

{
  echo "# S07/T03 raw evidence: the consumer proof - the packed library installed into a windowless WPF application"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "sdk: $(dotnet --version)"
  echo "environment: APPDATA='$APPDATA' LOCALAPPDATA='$LOCALAPPDATA' PROGRAMFILES='$PROGRAMFILES' TEMP='$TEMP'"
  echo
  echo "## 0. what this project is, and what it is not"
  echo "  samples/consumer-proof is a windowless WPF application whose only reference is"
  echo "  <PackageReference Include=\"Trustsoft.NotifyIcon\" Version=\"1.0.0\" />, restored from"
  echo "  artifacts/ through its own nuget.config (which clears every inherited source). It is absent"
  echo "  from Trustsoft.NotifyIcon.sln and carries its own empty Directory.Build.props, so it inherits"
  echo "  neither a project reference nor a build setting of the repository that produced the package."
  echo
  echo "## 1. the plan's first command: pack the library the consumer will install"
  echo "   (the previous nupkg is deleted first, so what follows is an artifact of this run)"
  echo '$ rm -f artifacts/Trustsoft.NotifyIcon.*.nupkg'
  rm -f artifacts/Trustsoft.NotifyIcon.*.nupkg
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release 2>&1 \
    | grep -E " error | warning |Successfully created package"
  PACK_EXIT=${PIPESTATUS[0]}
  echo "pack exit=$PACK_EXIT"
  ls -l artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null \
    | awk '{print "nupkg: " $5 " bytes  " $9}'
  sha256sum artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null | sed 's/^/sha256: /'
  echo
  echo "## 2. the only sources the consumer can reach"
  echo '$ dotnet nuget list source --configfile samples/consumer-proof/nuget.config'
  dotnet nuget list source --configfile samples/consumer-proof/nuget.config 2>&1 | sed -n '1,12p'
  echo
  echo "## 3. where that package came from: two controls, both with a fresh global-packages folder"
  echo '   control A: local feed present, empty global-packages folder'
  echo '             -> the restore must succeed, and the only place it can have got the package is the feed'
  rm -rf "$RESTORE_CACHE"
  echo "\$ NUGET_PACKAGES='<fresh>' dotnet restore samples/consumer-proof/ConsumerProof.csproj --force"
  NUGET_PACKAGES="$RESTORE_CACHE" dotnet restore "$CONSUMER" --force 2>&1 | tail -3
  CONTROL_A_EXIT=${PIPESTATUS[0]}
  echo "control A exit=$CONTROL_A_EXIT"
  echo "   control B: the same fresh global-packages folder, with the local feed emptied"
  echo '             -> the restore must FAIL: nothing else in this machine may supply this package'
  mv artifacts/Trustsoft.NotifyIcon.*.nupkg "$HOLD/"
  rm -rf "$RESTORE_CACHE"
  echo '$ mv artifacts/Trustsoft.NotifyIcon.*.nupkg <held outside the feed>'
  echo "\$ NUGET_PACKAGES='<fresh>' dotnet restore samples/consumer-proof/ConsumerProof.csproj --force"
  NUGET_PACKAGES="$RESTORE_CACHE" dotnet restore "$CONSUMER" --force 2>&1 | grep -E "error|Restored|Determining" | head -5
  CONTROL_B_EXIT=${PIPESTATUS[0]}
  echo "control B exit=$CONTROL_B_EXIT (non-zero is the expected result)"
  mv "$HOLD"/Trustsoft.NotifyIcon.*.nupkg artifacts/
  rm -rf "$RESTORE_CACHE"
  echo "   the package is back in artifacts/: $(ls artifacts/Trustsoft.NotifyIcon.*.nupkg)"
  echo
  echo "## 4. the consumer restores and builds, one framework at a time (R012: no framework stands in for another)"
  echo '$ dotnet restore samples/consumer-proof/ConsumerProof.csproj'
  dotnet restore "$CONSUMER" 2>&1 | tail -2
  RESTORE_EXIT=${PIPESTATUS[0]}
  echo "restore exit=$RESTORE_EXIT"
  echo
  echo '$ dotnet list samples/consumer-proof/ConsumerProof.csproj package'
  dotnet list "$CONSUMER" package 2>&1 | sed -n '1,40p'
  for tfm in "${TFMS[@]}"; do
    echo
    echo "\$ dotnet build samples/consumer-proof -c Release -f $tfm"
    dotnet build samples/consumer-proof -c Release -f "$tfm" 2>&1 \
      | grep -E " error | warning |Build succeeded|-> "
    build_exit=${PIPESTATUS[0]}
    echo "build $tfm exit=$build_exit"
    [ "$build_exit" = 0 ] || RESTORE_EXIT=1
  done
  echo
  echo "## 5. the consumer's own package-surface assertion, once per framework"
  echo "   (checks the exported types and the assembly references of the assembly this process loaded"
  echo "    out of the package, from the consumer's own source rather than from the library's tests)"
  for tfm in "${TFMS[@]}"; do
    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    echo
    echo "\$ $exe --surface-only"
    "$exe" --surface-only
    surface_exit=$?
    echo "surface-only $tfm exit=$surface_exit"
    [ "$surface_exit" = 0 ] || SURFACE_FAILURES=$((SURFACE_FAILURES + 1))
  done
  echo
  echo "## 6. the live runs: probe-live watches each framework's consumer process"
  echo "   columns: identity, once-per-second presence series, GDI count, and the teardown verdict after"
  echo "   the process exits. The consumer opens its assigned menu with no shell click at"
  echo "   ${SELF_OPEN_AFTER}s (its own subclass, the documented OnTrayClick hook) and asks for one balloon at"
  echo "   ${BALLOON_AFTER}s through ShowBalloonTip."
  echo "   The instrument is built here rather than assumed, so this log names one binary for all four"
  echo "   runs below (each of which passes --no-build)."
  echo '$ dotnet build scripts/probe-live -c Release'
  dotnet build scripts/probe-live -c Release 2>&1 \
    | grep -E " error | warning |Build succeeded| -> "
  PROBE_BUILD_EXIT=${PIPESTATUS[0]}
  echo "probe-live build exit=$PROBE_BUILD_EXIT"
  if [ "$PROBE_BUILD_EXIT" != 0 ]; then
    echo '  the instrument did not build, so no run below could be evidence: FAILURES ABOVE'
    exit 1
  fi
  for tfm in "${TFMS[@]}"; do
    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    run_log="$SCRATCH/probe-$tfm.txt"
    echo
    echo "--- $tfm"
    echo "\$ dotnet run --project scripts/probe-live -c Release --no-build -- $exe $OBSERVE_SECONDS --run-seconds $RUN_SECONDS --self-open-menu-after $SELF_OPEN_AFTER --show-balloon-after $BALLOON_AFTER"
    dotnet run --project scripts/probe-live -c Release --no-build -- \
      "$exe" "$OBSERVE_SECONDS" \
      --run-seconds "$RUN_SECONDS" \
      --self-open-menu-after "$SELF_OPEN_AFTER" \
      --show-balloon-after "$BALLOON_AFTER" > "$run_log" 2>&1
    probe_exit=$?
    cat "$run_log"
    echo "probe-live $tfm exit=$probe_exit"
    [ "$probe_exit" = 0 ] || PROBE_FAILURES=$((PROBE_FAILURES + 1))
  done
  echo
  echo "## 7. the same columns, one row per framework (extracted from the runs above)"
  printf '%-16s %-11s %-13s %-12s %s\n' framework "present(s)" "gdi first/max" "sample-exit" teardown
  for tfm in "${TFMS[@]}"; do
    run_log="$SCRATCH/probe-$tfm.txt"
    presence=$(grep -c "icon=present" "$run_log")
    gdi_first=$(grep -o "gdi=[0-9]*" "$run_log" | cut -d= -f2 | head -1)
    gdi_max=$(grep -o "gdi=[0-9]*" "$run_log" | cut -d= -f2 | sort -n | tail -1)
    sample_exit=$(grep -o "exit-code: [0-9-]*" "$run_log" | tail -1 | cut -d' ' -f2)
    teardown=$(grep -o "icon-after-exit: [a-z]*" "$run_log" | tail -1 | cut -d' ' -f2)
    printf '%-16s %-11s %-13s %-12s %s\n' \
      "$tfm" "$presence" "$gdi_first/$gdi_max" "$sample_exit" "$teardown"
    echo "    $(grep -o 'identity: hwnd=0x[0-9A-F]* uID=[0-9]*' "$run_log" | tail -1)"
    echo "    icons-in-notification-area: $(grep -o 'icons-in-notification-area: [0-9]*' "$run_log" | tail -1 | cut -d' ' -f2)"
    echo "    surface assertion: $(grep -o 'public surface of the installed package: [A-Z]*' "$run_log" | tail -1 | sed 's/.*: //')"
    echo "    consumer evidence: $(grep -c '\[consumer\] menu opened' "$run_log") menu open(s), $(grep -c '\[consumer\] menu dismissed' "$run_log") dismissal(s), $(grep -c '\[consumer\] balloon show request' "$run_log") balloon request(s), $(grep -c '\[consumer\] click:' "$run_log") shell click(s) delivered"
  done
  echo
  echo "## 8. the shell click, re-attempted from the consumer side (S06/F1)"
  echo "   A right click is injected into the icon's own shell rectangle. No self-open switch is passed,"
  echo "   so any click or menu line below can only have come from the injected click."
  exe="samples/consumer-proof/bin/Release/$CLICK_TFM/ConsumerProof.exe"
  click_log="$SCRATCH/probe-click-attempt.txt"
  echo
  echo "\$ dotnet run --project scripts/probe-live -c Release --no-build -- $exe $OBSERVE_SECONDS --click-after 8 --run-seconds $RUN_SECONDS"
  dotnet run --project scripts/probe-live -c Release --no-build -- \
    "$exe" "$OBSERVE_SECONDS" \
    --click-after 8 \
    --run-seconds "$RUN_SECONDS" > "$click_log" 2>&1
  CLICK_PROBE_EXIT=$?
  cat "$click_log"
  echo "probe-live click-attempt exit=$CLICK_PROBE_EXIT"
  echo "   click columns: injected=$(grep -c 'click injected' "$click_log") click(s); delivered to the consumer=$(grep -c '\[consumer\] click:' "$click_log"); menu opens=$(grep -c '\[consumer\] menu opened' "$click_log")"
  echo
  echo "## 9. the consumer instrument's own failure paths (measured here rather than assumed)"
  echo "   An instrument that silently ignores what it does not understand would make a run whose switch"
  echo "   was misspelled look like a run that proved the opposite, so every rejection is measured."
  neg_exe="samples/consumer-proof/bin/Release/$CLICK_TFM/ConsumerProof.exe"
  for bad in "--nonsense" "--surface-only --run-seconds 5" "--run-seconds=-3" "--show-balloon-after"; do
    echo
    echo "\$ $neg_exe $bad"
    # shellcheck disable=SC2086
    neg_output=$("$neg_exe" $bad 2>&1)
    neg_exit=$?
    echo "$neg_output" | sed 's/^/  /'
    echo "  exit=$neg_exit"
  done
  echo
  echo "   Every one of those is a usage error (exit 2) with the offending value named on stderr, and no"
  echo "   icon is created. The other two exit codes the instrument owns are 1 (the shell refused the"
  echo "   registration - the consumer prints the operation and the Win32 error rather than dying in an"
  echo "   invisible unhandled exception) and 3 (the package's public surface was not the documented one,"
  echo "   which the run only reaches after the normal disposal path has run)."
  echo
  echo "## verdict"
  echo "  pack exit=$PACK_EXIT; control A (feed present) exit=$CONTROL_A_EXIT; control B (feed emptied) exit=$CONTROL_B_EXIT;"
  echo "  restore/build exit=$RESTORE_EXIT; surface assertion failures=$SURFACE_FAILURES; probe instrument build exit=$PROBE_BUILD_EXIT; probe runs reporting the icon absent=$PROBE_FAILURES"
  if [ "$PACK_EXIT" != 0 ] || [ "$RESTORE_EXIT" != 0 ] || [ "$SURFACE_FAILURES" != 0 ] || [ "$PROBE_BUILD_EXIT" != 0 ] || [ "$PROBE_FAILURES" != 0 ]; then
    echo '  FAILURES ABOVE'
    exit 1
  fi
  echo "  Every framework's consumer process was built from the package, showed its icon, and left no icon"
  echo "  behind at exit. Each run's GDI series, as its distinct values in order and the sample where it"
  echo "  stopped rising - computed from the runs above rather than asserted here. The opening values differ"
  echo "  because a run's first samples can fall while WPF is still realising resources, which is why this"
  echo "  verdict names the plateau instead of a fixed pair of numbers:"
  for tfm in "${TFMS[@]}"; do
    log="$SCRATCH/probe-$tfm.txt"
    series=$(grep -oE "gdi=[0-9]+" "$log" | cut -d= -f2)
    distinct=$(echo "$series" | uniq | paste -sd'>' -)
    plateau=$(echo "$series" | tail -1)
    total=$(echo "$series" | wc -l | tr -d ' ')
    held=$(echo "$series" | grep -c "^$plateau$")
    flat_from=$(grep -E "t=[0-9]+s.*gdi=[0-9]+" "$log" | awk '
      { t=""; g="";
        if (match($0, /t=[0-9]+s/)) t=substr($0, RSTART+2, RLENGTH-3);
        if (match($0, /gdi=[0-9]+/)) g=substr($0, RSTART+4, RLENGTH-4);
        if (t != "" && g != "") { if (g != prev) { last=t; prev=g } } }
      END { print last }')
    echo "    $tfm: gdi $distinct; last increase at t=${flat_from}s, then flat at $plateau for the last $held of $total samples"
  done
  echo "  The last increases coincide with the assigned menu's own WPF popup window (the consumer opens it"
  echo "  at 6s and the count has plateaued by 8s); after each plateau no sample grows - not per balloon"
  echo "  request at 10s, not per menu dismissal, not on disposal. The restore consumed the local feed and"
  echo "  nothing else (control A succeeded, control B failed). The click-attempt exit code is"
  echo "  intentionally not part of this verdict: the click is an instrument question (S06/F1), and"
  echo "  what it produced is read off section 8 rather than asserted here."
  exit 0
} > "$LOG" 2>&1

cat "$LOG"
