#!/usr/bin/env bash
#
# S05/T06 - negative controls for docs/uat-logs/S05-M002/t06-consumer-proof.sh.
#
# The plan's requirement: the runner is only worth having if its failure paths have been seen to
# fail. Two negative controls, each one measured in a single command (one exit code, no compound
# shell one-liner whose LAST command's status could be read as the control's), and a third mode that
# re-reads the runner's own log without running anything:
#
#   A. the oracle removed. A copy of the runner with PROBE pointed at a path that does not exist
#      must exit non-zero and NAME every reading the missing instrument could not produce -
#      including, per framework, the clean-slate control, the mid-run read-back and the
#      post-teardown read-back as three separate missing readings. An empty reading can therefore
#      never be read as the absent-shortcut reading, and a missing oracle can never produce a green
#      verdict. (13 of the missing readings are the nine per-framework read-backs plus the per-
#      framework final-state read-backs; where the setting reads Enabled the platform inventory is
#      also unreadable and adds three more.)
#
#   B. the contaminated slate. A leftover shortcut is planted at exactly the path the probe computes
#      from the net9.0 identity - a REAL shortcut (written by the probe's own registration path,
#      which leaves its file in place) carrying a FOREIGN AppUserModelID value, which is what an
#      interrupted run of another instrument leaves behind. The run must print that reading as the
#      contaminated state, delete exactly the one file the reading named, re-read the identity as
#      absent, and still finish green with 0 missing readings - so the runner is re-runnable after a
#      crash and a stale value can never pass as this run's own registration. The machine is left as
#      found, and the unrelated Trustsoft.NotifyIcon.ToastProbe shortcut is compared before/after.
#
# A control "passes" when the runner misbehaves exactly as predicted; a control whose prediction is
# wrong fails this driver (non-zero exit). The green run against a clean slate is a separate command
# (bash docs/uat-logs/S05-M002/t06-consumer-proof.sh).
#
# The --check-runner-log mode re-reads the runner's own log and asserts, per framework, that the
# clean-slate, mid-run and post-teardown read-backs, the surface-only PASS line, the consumer's
# content/setting/teardown/window/totals readings and the final "nothing remains registered" reading
# are all present and that no reading is missing. It runs nothing and writes nothing.
#
# This task measures and records. It changes nothing under src/, tests/, samples/, scripts/ or
# README.md.
#
# Usage (from the repository root):
#   bash docs/uat-logs/S05-M002/t06-consumer-proof-controls.sh
#   bash docs/uat-logs/S05-M002/t06-consumer-proof-controls.sh --check-runner-log
#
set -u

LOG=docs/uat-logs/S05-M002/t06-consumer-proof-controls.txt
RUNNER=docs/uat-logs/S05-M002/t06-consumer-proof.sh
RUNNER_LOG=docs/uat-logs/S05-M002/t06-consumer-proof.txt
CONTROL_A_LOG=docs/uat-logs/S05-M002/t06-consumer-proof-control-a.txt
CONTROL_B_LOG=docs/uat-logs/S05-M002/t06-consumer-proof-control-b.txt
PROBE=scripts/probe-toast/bin/Release/net8.0-windows/probe-toast.exe
TFMS=(net8.0-windows net9.0-windows net10.0-windows)
ID_PREFIX=Trustsoft.NotifyIcon.ConsumerProof
PLANT_AUMID=Trustsoft.NotifyIcon.S05.ControlB

# --- environment shim: fills in only what is absent, so a login shell is untouched ---------------
# The same rule as the runner's: the planted shortcut lives in the real per-user Start menu, so
# SpecialFolder.Programs has to resolve to the real roaming profile, and the probe spawns an .exe.
export APPDATA="${APPDATA:-${USERPROFILE:-$HOME}\\AppData\\Roaming}"
export LOCALAPPDATA="${LOCALAPPDATA:-${USERPROFILE:-$HOME}\\AppData\\Local}"
export ProgramData="${ProgramData:-${SYSTEMDRIVE:-C:}\\ProgramData}"
export ALLUSERSPROFILE="${ALLUSERSPROFILE:-$ProgramData}"
export PUBLIC="${PUBLIC:-${SYSTEMDRIVE:-C:}\\Users\\Public}"
export PROGRAMFILES="${PROGRAMFILES:-${SYSTEMDRIVE:-C:}\\Program Files}"
export COMMONPROGRAMFILES="${COMMONPROGRAMFILES:-$PROGRAMFILES\\Common Files}"
export ProgramW6432="${ProgramW6432:-$PROGRAMFILES}"
export CommonProgramW6432="${CommonProgramW6432:-$COMMONPROGRAMFILES}"
export COMSPEC="${COMSPEC:-${SYSTEMROOT:-C:\\WINDOWS}\\system32\\cmd.exe}"
export OS="${OS:-Windows_NT}"
export PATHEXT="${PATHEXT:-.COM;.EXE;.BAT;.CMD}"

case "${TEMP:-}" in
  [A-Za-z]:*) ;;
  *) export TEMP="$LOCALAPPDATA\\Temp" ;;
esac

case "${TMP:-}" in
  [A-Za-z]:*) ;;
  *) export TMP="$TEMP" ;;
esac

PROGRAMS_DIR="$(printf '%s/%s' "$APPDATA" 'Microsoft/Windows/Start Menu/Programs' | tr '\\' '/')"
FOREIGN_SHORTCUT="$PROGRAMS_DIR/Trustsoft.NotifyIcon.ToastProbe.lnk"
NET9_ID="$ID_PREFIX.net9.0-windows"
NET9_SHORTCUT="$PROGRAMS_DIR/$NET9_ID.lnk"
PLANT_SHORTCUT="$PROGRAMS_DIR/$PLANT_AUMID.lnk"

# --- --check-runner-log: re-read the runner's log, run nothing, write nothing -------------------
check_runner_log() {
  local status=0
  local count

  expect() {
    if grep -qF -- "$2" "$RUNNER_LOG"; then
      echo "  PASS  $1"
    else
      echo "  FAIL  $1"
      echo "        the log does not contain: $2"
      status=1
    fi
  }

  expect_count() {
    count="$(grep -cF -- "$3" "$RUNNER_LOG" || true)"
    if [ "$count" = "$2" ]; then
      echo "  PASS  $1 ($count)"
    else
      echo "  FAIL  $1: expected $2 occurrence(s), found $count"
      status=1
    fi
  }

  # Some readings are echoed more than once in the log on purpose (section 1 echoes the inspector's
  # transcript, section 2 echoes each framework's surface transcript and section 4 echoes the
  # consumer's capture), so the requirement for those is "at least N", never "exactly N": a reading
  # that is present more than once is still present.
  expect_min() {
    count="$(grep -cF -- "$3" "$RUNNER_LOG" || true)"
    if [ "$count" -ge "$2" ] 2>/dev/null; then
      echo "  PASS  $1 ($count, >= $2 required)"
    else
      echo "  FAIL  $1: expected at least $2 occurrence(s), found $count"
      status=1
    fi
  }

  echo "## the runner's own log, re-read"
  if [ ! -s "$RUNNER_LOG" ]; then
    echo "  FAIL  $RUNNER_LOG is absent or empty; run the runner first"
    return 1
  fi

  missing_count="$(grep -c 'MISSING:' "$RUNNER_LOG" || true)"
  if [ "$missing_count" = 0 ]; then
    echo "  PASS  the log names no missing reading (0 'MISSING:' lines)"
  else
    echo "  FAIL  the log names $missing_count missing reading(s)"
    grep -n 'MISSING:' "$RUNNER_LOG" | sed 's/^/        /'
    status=1
  fi

  expect "the verdict is the green one" "VERDICT  the three-framework consumer proof holds"
  expect_min "the artifact inspector's verdict is recorded" 1 "VERDICT  all 18 assertions hold"
  expect_min "each framework reported its own surface from the package" 3 "PASS the package surfaces exactly the 20 documented public types"
  expect_count "the runner recognised each framework's surface PASS line" 3 "reading: the PASS line names the twenty documented types"
  expect_count "each framework's mid-run read-back found its own identity registered" 3 "reading: the identity is registered and carries its own value back"
  expect_count "each framework's post-teardown read-back read absent" 3 "reading: the identity reads absent again - success=False operation='OpenShellLink' code=0x80070002"
  expect_count "each framework's shortcut was gone again in the final sweep" 3 "reading: nothing remains registered for this identity"
  expect_count "each framework delivered against a setting that reads Enabled" 3 "; setting='Enabled'"
  expect_count "each framework's consumer exited 0 with no temp file left" 3 "; consumer exit=0; temp files=0;"
  expect_count "the human-procedure pointer resolves" 1 "the pointer resolves"
  expect "section 5 states it does not claim a click" "Nothing in this log claims that an automated instrument proved a click"

  for tfm in "${TFMS[@]}"; do
    id="$ID_PREFIX.$tfm"
    expect "$tfm clean-slate read-back is the absent reading" "clean-slate='[probe] shortcut read-back: identity='$id' path='"
    expect "$tfm pre-run reading is absent with 0x80070002" "success=False operation='OpenShellLink' code=0x80070002'"
    expect "$tfm mid-run reading carries its own value back verbatim" "success=True value='$id''"
    expect "$tfm mid-run reading was taken while the consumer was alive" "consumer alive=1"
    expect "$tfm post-teardown reading is absent with 0x80070002" "success=False operation='OpenShellLink' code=0x80070002'"
    expect "$tfm content line named both button arguments" "buttons=[consumer-button-1,consumer-button-2]"
    expect "$tfm content line named this run's identity" "identity='$id'"
    expect "$tfm totals report refused=0, failures=0 and the identity" "toast identity='$id'"
    expect "$tfm surface-only exited 0" "surface-only exit=0"
    expect "$tfm surface transcript carries the twenty-type PASS line" "[consumer] PASS the package surfaces exactly the 20 documented public types"
    expect "$tfm consumer capture has no unhandled exception" "reading: neither stream contains unhandled-exception text"
    expect "$tfm mid-run read-back came from a live run" "    mid-run='[probe] shortcut read-back: identity='$id' path='"
    expect "$tfm post-teardown line in the verdict names the identity" "    post-teardown='[probe] shortcut read-back: identity='$id' path='"
  done

  echo
  if [ "$status" -eq 0 ]; then
    echo "  check verdict: PASS - every required reading of the three-framework proof is present in"
    echo "  $RUNNER_LOG, per framework, and no reading is named missing."
  else
    echo "  check verdict: FAIL - see the FAIL lines above."
  fi
  return "$status"
}

if [ "${1:-}" = "--check-runner-log" ]; then
  check_runner_log
  exit $?
fi

WORK="$(mktemp -d)"
overall=0

{
  echo "# S05/T06 negative controls: the missing oracle and the contaminated slate"
  echo "date: $(date -Is)"
  echo "worktree: $(pwd)"
  echo "control work directory: $WORK (outside the repository; removed on exit)"
  echo "identities under test: ${TFMS[0]}, ${TFMS[1]}, ${TFMS[2]} (prefixed with $ID_PREFIX.)"
  echo

  # ---- the instrument both controls depend on ------------------------------------------------
  if [ ! -f "$PROBE" ]; then
    echo "## 0. the probe is absent; building it (control B plants a real shortcut through it)"
    echo '$ dotnet build scripts/probe-toast/ProbeToast.csproj -c Release'
    dotnet build scripts/probe-toast/ProbeToast.csproj -c Release > "$WORK/probe-build.txt" 2>&1
    echo "  reading: probe-toast build exit=$?"
  else
    echo "## 0. the probe is present at '$PROBE'"
  fi
  if [ ! -f "$PROBE" ]; then
    echo "  FAIL  the probe is still absent, so neither control can run"
    overall=1
  fi
  echo

  # ---- control A: the oracle removed ----------------------------------------------------------
  echo "## control A - the oracle removed"
  echo "  A copy of the runner with PROBE pointed at a path that does not exist. The prediction: it"
  echo "  exits non-zero, prints 'VERDICT  not established' rather than the green one, and names every"
  echo "  reading the missing instrument could not produce."
  sed -e 's|^PROBE=scripts/probe-toast/bin/Release/net8.0-windows/probe-toast.exe|PROBE=C:/t06-nonexistent-oracle/probe-toast.exe|' \
      -e "s|^LOG=docs/uat-logs/S05-M002/t06-consumer-proof.txt|LOG=$CONTROL_A_LOG|" \
      "$RUNNER" > "$WORK/control-a.sh"
  copy_probe_ok=0
  copy_log_ok=0
  grep -qF 'PROBE=C:/t06-nonexistent-oracle/probe-toast.exe' "$WORK/control-a.sh" && copy_probe_ok=1
  grep -qF "LOG=$CONTROL_A_LOG" "$WORK/control-a.sh" && copy_log_ok=1
  echo "  reading: the copy points PROBE at a non-existent file=$copy_probe_ok and writes its log to '$CONTROL_A_LOG'=$copy_log_ok"
  if [ "$copy_probe_ok" != 1 ] || [ "$copy_log_ok" != 1 ]; then
    echo "  FAIL  the copy was not built as intended (the runner's PROBE/LOG lines changed shape); control A is void"
    overall=1
  else
    echo '$ bash <copy with the missing oracle>'
    bash "$WORK/control-a.sh" > /dev/null 2>&1
    a_exit=$?
    a_missing="$(grep -c 'MISSING:' "$CONTROL_A_LOG" || true)"
    a_not_established="$(grep -c 'VERDICT  not established' "$CONTROL_A_LOG" || true)"
    a_green="$(grep -c 'the three-framework consumer proof holds' "$CONTROL_A_LOG" || true)"
    echo "  reading: exit=$a_exit (non-zero required), 'VERDICT  not established' lines=$a_not_established (1 required), green-verdict lines=$a_green (0 required), named missing readings=$a_missing"
    if [ "$a_exit" -eq 0 ]; then
      echo "  FAIL  the copy with no oracle exited 0: a missing instrument was reported as a green run"
      overall=1
    fi
    if [ "$a_green" != 0 ]; then
      echo "  FAIL  the copy with no oracle printed the green verdict"
      overall=1
    fi
    if [ "$a_not_established" != 1 ]; then
      echo "  FAIL  the copy with no oracle did not print exactly one 'VERDICT  not established'"
      overall=1
    fi
    if [ "$a_missing" -lt 12 ]; then
      echo "  FAIL  only $a_missing reading(s) were named missing (at least 12 required: three read-backs plus the final sweep, per framework)"
      overall=1
    fi
    for tfm in "${TFMS[@]}"; do
      id="$ID_PREFIX.$tfm"
      for expected in \
        "$tfm clean-slate control: the read-back printed no reading line at all" \
        "$tfm mid-run read-back: expected success=True value='$id' while the toast was live, got '(no reading line at all)'" \
        "$tfm post-teardown read-back: expected the absent reading with code=0x80070002, got '(no reading line at all)'" \
        "$tfm final state: the read-back printed no reading line at all" ; do
        if grep -qF -- "$expected" "$CONTROL_A_LOG"; then
          echo "  PASS  the missing oracle is named for: $expected"
        else
          echo "  FAIL  the missing oracle did NOT name: $expected"
          overall=1
        fi
      done
    done
    cp "$CONTROL_A_LOG" "$WORK/control-a-pristine.txt" 2>/dev/null || true
    echo "  reading: control A's own log is '$CONTROL_A_LOG' ($(wc -l < "$CONTROL_A_LOG" 2>/dev/null || echo '?') lines)"
  fi
  echo

  # ---- control B: the contaminated slate -------------------------------------------------------
  echo "## control B - a leftover shortcut planted for the $NET9_ID identity"
  echo "  The plant is a REAL shortcut carrying a FOREIGN AppUserModelID: it is registered through the"
  echo "  probe's own show path (which leaves its file in place, unlike the library's teardown) and the"
  echo "  resulting file is moved to exactly the path the probe computes from the identity under test."
  echo "  The prediction: the run prints that reading as the contaminated state, deletes exactly the one"
  echo "  file the reading named, re-reads the identity as absent, and finishes green with 0 missing."
  foreign_before=0
  if [ -f "$FOREIGN_SHORTCUT" ]; then foreign_before=1; fi
  # Only files this driver computed are cleared before the plant, so a leftover from an earlier
  # control run cannot be mistaken for the plant.
  if [ -f "$PLANT_SHORTCUT" ]; then rm -f "$PLANT_SHORTCUT"; echo "  reading: cleared this driver's own earlier plant file '$PLANT_SHORTCUT'"; fi
  if [ -f "$NET9_SHORTCUT" ]; then rm -f "$NET9_SHORTCUT"; echo "  reading: cleared a leftover at the identity's computed path before planting"; fi
  echo '$ '"$PROBE"' --aumid '"$PLANT_AUMID"' --wait-seconds 4'
  "$PROBE" --aumid "$PLANT_AUMID" --wait-seconds 4 > "$WORK/plant.log" 2>&1
  plant_exit=$?
  echo "  reading: the planting probe exit=$plant_exit (0 required); it wrote '$PLANT_SHORTCUT'=$([ -f "$PLANT_SHORTCUT" ] && echo yes || echo no)"
  if [ "$plant_exit" != 0 ] || [ ! -f "$PLANT_SHORTCUT" ]; then
    echo "  FAIL  the leftover could not be planted, so control B is void"
    echo "        the planting probe's transcript:"
    sed 's/^/        /' "$WORK/plant.log"
    overall=1
  else
    mv "$PLANT_SHORTCUT" "$NET9_SHORTCUT"
    planted_reading="$("$PROBE" --read-shortcut "$NET9_ID" 2>&1 | grep -m1 'shortcut read-back:' || true)"
    echo "  reading: the identity under test now reads: $planted_reading"
    case "$planted_reading" in
      *"success=True value='$PLANT_AUMID'"*)
        echo "  reading: the planted file answers for '$NET9_ID' with the foreign value '$PLANT_AUMID', so the slate is contaminated"
        ;;
      *)
        echo "  FAIL  the plant did not produce a success=True reading carrying '$PLANT_AUMID' for '$NET9_ID'"
        overall=1
        ;;
    esac

    echo
    echo '$ bash '"$RUNNER"'   (the real runner, against the contaminated slate)'
    bash "$RUNNER" > /dev/null 2>&1
    b_exit=$?
    cp "$RUNNER_LOG" "$CONTROL_B_LOG" 2>/dev/null || true
    b_missing="$(grep -c 'MISSING:' "$CONTROL_B_LOG" || true)"
    b_green="$(grep -c 'VERDICT  the three-framework consumer proof holds' "$CONTROL_B_LOG" || true)"
    b_contaminated="$(grep -c "the identity is NOT on a clean slate" "$CONTROL_B_LOG" || true)"
    b_deleted="$(grep -c "deleted the leftover shortcut" "$CONTROL_B_LOG" || true)"
    b_reclean="$(grep -c "after the cleanup the identity reads absent" "$CONTROL_B_LOG" || true)"
    echo "  reading: exit=$b_exit (0 required), green-verdict lines=$b_green (1 required), named missing readings=$b_missing (0 required), contaminated-state lines=$b_contaminated (1 required), leftover-deletion lines=$b_deleted (1 required), re-read-as-absent lines=$b_reclean (1 required)"
    echo "  reading: the run's log was preserved as '$CONTROL_B_LOG' for this control's evidence"
    if [ "$b_exit" != 0 ]; then echo "  FAIL  the run against the contaminated slate exited $b_exit"; overall=1; fi
    if [ "$b_green" != 1 ]; then echo "  FAIL  the run against the contaminated slate did not finish green"; overall=1; fi
    if [ "$b_missing" != 0 ]; then echo "  FAIL  the run named $b_missing missing reading(s) after the cleanup"; overall=1; fi
    if [ "$b_contaminated" != 1 ]; then echo "  FAIL  the contaminated state was not printed"; overall=1; fi
    if [ "$b_deleted" != 1 ]; then echo "  FAIL  the named leftover was not deleted"; overall=1; fi
    if [ "$b_reclean" != 1 ]; then echo "  FAIL  the identity was not re-read as absent after the cleanup"; overall=1; fi
    echo "  reading: the planted value appears in the run's own contaminated-state line=$([ -n "$planted_reading" ] && echo checked || echo 'no reading')"
    if grep -qF "the identity is NOT on a clean slate: '$planted_reading'" "$CONTROL_B_LOG"; then
      echo "  PASS  the run printed exactly the reading this driver planted as the contaminated state"
    else
      echo "  FAIL  the run's contaminated-state line is not the planted reading"
      grep -n "NOT on a clean slate" "$CONTROL_B_LOG" | sed 's/^/        /'
      overall=1
    fi
    if [ -f "$NET9_SHORTCUT" ]; then
      echo "  FAIL  the planted file is still at '$NET9_SHORTCUT'; the machine was not left as found"
      overall=1
    else
      echo "  PASS  the planted file is gone: the run deleted exactly the file its reading named"
    fi
    echo "  reading: the other two frameworks' mid-run read-backs still found their own identities: $(grep -c 'reading: the identity is registered and carries its own value back' "$CONTROL_B_LOG" || true) of 3"
    if [ "$(grep -c 'reading: the identity is registered and carries its own value back' "$CONTROL_B_LOG" || true)" != 3 ]; then
      echo "  FAIL  the contaminated slate cost another framework its mid-run reading"
      overall=1
    fi
  fi

  foreign_after=0
  if [ -f "$FOREIGN_SHORTCUT" ]; then foreign_after=1; fi
  echo "  reading: the unrelated shortcut '$FOREIGN_SHORTCUT' existed before=$foreign_before exists now=$foreign_after (it is not this driver's file and is never deleted here)"
  if [ "$foreign_before" != "$foreign_after" ]; then
    echo "  FAIL  the unrelated shortcut changed state while the controls ran"
    overall=1
  fi
  echo

  echo "## the machine after the controls"
  for tfm in "${TFMS[@]}"; do
    id="$ID_PREFIX.$tfm"
    echo "  $tfm: $("$PROBE" --read-shortcut "$id" 2>&1 | grep -m1 'shortcut read-back:' || echo '(no reading line)')"
  done
  echo

  echo "## verdict"
  if [ "$overall" -eq 0 ]; then
    echo "  Both controls behaved as predicted: the missing oracle exits non-zero and names every"
    echo "  reading it could not produce, and the contaminated slate is named, deleted and re-read as"
    echo "  absent before a green run with 0 missing readings. The green run against a clean slate is a"
    echo "  separate command (bash $RUNNER)."
  else
    echo "  FAILURES ABOVE - the prediction for at least one control was wrong."
  fi
} > "$LOG" 2>&1

rm -rf "$WORK"
sed -n '/## control A/,$p' "$LOG"
exit $overall
