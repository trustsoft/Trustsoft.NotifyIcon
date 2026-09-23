#!/usr/bin/env bash
#
# S05/T06 - the three-framework consumer proof: the packed library showing a live toast per target
# framework, read back from outside the repository.
#
# Why this script exists. This is the slice's demo sentence - "a project that references only the
# package shows a real toast on net8.0-windows, net9.0-windows and net10.0-windows" - and the
# milestone's fifth criterion, and until now no reading of it came from outside this repository: T01
# built the chain and recorded the stale-surface baseline, T03 gave the consumer a toast path, T04 gave
# the inspector an artifact-side surface rule and T05 made the shipped README true, but nothing had run
# the consumer's toast path on all three frameworks and read the result back with a second process.
#
# What this script measures, and why it is shaped this way. Every recorded in-repo run of the toast
# path reports `activations=0, dismissals=0`, most of them because the platform's cached notification
# setting read `DisabledForApplication` (S04 measured that cache lagging a registry change by ~11
# minutes), and S01 measured that a specific activation cannot be credited to a specific click. So the
# runner (a) gives each framework its own identity - Trustsoft.NotifyIcon.ConsumerProof.<tfm> - so no
# run can be helped by another run's leftover shortcut; (b) prints each identity's pre-run read-back so
# a contaminated slate is visible; (c) asserts only the readings an unattended run can actually
# produce; and (d) states plainly, in its own log and in its verdict, the two readings it cannot
# produce and where the human procedure for them lives.
#
# Sections, in order:
#   1. the toast instrument is built (a re-materialised worktree has no scripts/probe-toast/bin and the
#      read-backs below are that binary), the library is packed and the package inspector reads the
#      same artifact the consumer installs, so the artifact-side and consumer-side readings agree.
#   2. the consumer is built once per framework and runs its own `--surface-only` assertion there
#      (R012: no framework stands in for another; R017: the surface claim is about the packed artifact).
#   3. per framework, the live run: the clean-slate read-back, the consumer launched with the toast
#      switches its identity, a read-back WHILE the toast is live, the consumer's exit, the
#      post-teardown read-back, the platform's own notification inventory for that identity and the
#      library temp folder's file count.
#   4. the consumer's own readings, asserted from its capture: the content line with both button
#      arguments and the identity, the setting outcome, the teardown line, the measured 0/0/0
#      post-teardown window, the totals with refused=0/failures=0/identity, and no unhandled exception.
#   5. the two readings no unattended run can produce, and the pointer to the human procedure.
#   6. the machine after the run: no consumer shortcut, in a separate process invocation per identity.
#
# This task measures and records. It edits nothing under src/, tests/, samples/, scripts/ or README.md;
# if a reading were missing for a reason that needs a code change, the right move is to stop and report
# it rather than widen the instrument inside this task.
#
# Two negative controls were run against this script, so its failure paths are measured rather than
# claimed (both are cheap to reproduce from the description and neither is part of the required run):
#   A. the oracle removed - a copy with PROBE pointed at a non-existent file exits 1 and names every
#      reading the missing instrument could not produce (15 of them), including the clean-slate,
#      mid-run and post-teardown read-backs as three separate missing readings. An empty reading is
#      never read as the absent-shortcut reading, which is the same rule T02 applied to the probe's
#      own read-back mode: an unavailable oracle cannot pass.
#   B. a contaminated slate - the pre-run read-back of one identity answered `success=True
#      value='Trustsoft.NotifyIcon.ToastProbe.Image'` because a leftover .lnk was planted at the path
#      the probe computes from the identity. The run prints that reading as the contaminated state,
#      deletes exactly the one file it named, re-reads the identity as absent, and finishes green with
#      0 missing readings - the machine is left as found and the run is re-runnable after a crash.
#
# The shell environment is repaired first, in the shape T01/T03/S07 use: this sandbox strips the
# Windows known-folder variables, and a live run (unlike MSBuild, which the repository's
# Directory.Build.targets repairs) genuinely needs them - the consumer writes its Start-menu shortcut
# through the real roaming profile, and a PowerShell-driven instrument runs an `.exe` only when its
# extension is in PATHEXT. Only absent variables are filled in, so a normal login shell is untouched.
# TEMP/TMP are additionally normalised to a real Windows path even when this Git-Bash shell supplies a
# POSIX-looking one, because the child process must resolve the same temp folder the library uses
# (Path.GetTempPath() reads TMP, then TEMP).
#
# Usage (from the repository root): bash docs/uat-logs/S05-M002/t06-consumer-proof.sh
#
set -u

# The log below is a run output, not source: `docs/uat-logs/S05-M002/*.txt` is gitignored, because the
# host verification gate re-runs this script as its check while it hashes every untracked-but-unignored
# file in the tree - a log git could see would be recorded as the source changing while the checks ran
# (measured on T01's first attempt). The committed evidence is this script.
LOG=docs/uat-logs/S05-M002/t06-consumer-proof.txt
CONSUMER=samples/consumer-proof/ConsumerProof.csproj
NUPKG=artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg
PROBE=scripts/probe-toast/bin/Release/net8.0-windows/probe-toast.exe
TFMS=(net8.0-windows net9.0-windows net10.0-windows)
ID_PREFIX=Trustsoft.NotifyIcon.ConsumerProof
BUTTON_READING='buttons=[consumer-button-1,consumer-button-2]'
WINDOW_LINE='[consumer] toast post-teardown window: activations=0 dismissals=0 errors=0 after the teardown line'

# --- environment shim: fills in only what is absent, so a login shell is untouched ---------------
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

SCRATCH="$TEMP/t06-consumer-proof"
mkdir -p "$SCRATCH"

# Path.GetTempPath() reads TMP first and then TEMP; both are Windows paths after the shim above, so
# the folder this script counts is the one the library writes its toast images into.
case "${TMP:-}" in
  [A-Za-z]:*) TEMP_BASE="$TMP" ;;
  *) TEMP_BASE="$TEMP" ;;
esac
TEMP_BASE="$(printf '%s' "$TEMP_BASE" | tr '\\' '/')"
IMAGE_FOLDER="$TEMP_BASE/Trustsoft.NotifyIcon"

# The Start-menu folder the library registers a consumer's identity in, in the POSIX form this shell's
# file tests need. It is used only to record a foreign file's state, never to delete anything.
PROGRAMS_DIR="$(printf '%s/%s' "$APPDATA" 'Microsoft/Windows/Start Menu/Programs' | tr '\\' '/')"
FOREIGN_SHORTCUT="$PROGRAMS_DIR/Trustsoft.NotifyIcon.ToastProbe.lnk"

# --- reading helpers -----------------------------------------------------------------------------
missing=()

note_missing() {
  missing+=("$1")
  echo "  MISSING: $1"
}

read_shortcut() {
  "$PROBE" --read-shortcut "$1" 2>&1
}

reading_line() {
  printf '%s\n' "$1" | grep -m1 'shortcut read-back:' || true
}

# An empty reading is a distinct failure from a wrong reading: an instrument that printed nothing
# cannot be told apart from an instrument whose answer was "absent" unless the difference is named.
render_reading() {
  if [ -z "$1" ]; then printf '(no reading line at all)'; else printf '%s' "$1"; fi
}

is_absent_reading() {
  case "$1" in
    *"success=False"*"code=0x80070002"*) return 0 ;;
    *) return 1 ;;
  esac
}

is_present_reading() {
  # $1 the reading line, $2 the identity whose value must be read back verbatim
  case "$1" in
    *"success=True value='$2'"*) return 0 ;;
    *) return 1 ;;
  esac
}

count_images() {
  ls "$IMAGE_FOLDER"/toast-*.png 2>/dev/null | wc -l | tr -d ' '
}

PROBE_BUILD_EXIT=0
PACK_EXIT=0
NUPKG_SIZE=0
NUPKG_SHA=""
INSPECTOR_EXIT=0
INSPECTOR_VERDICT=""
declare -a SURFACE_EXIT=()
FOREIGN_BEFORE=0
FOREIGN_AFTER=0

{
  echo "# S05/T06 raw evidence: the three-framework consumer proof, with a live toast per framework"
  echo "date: $(date -Is)"
  echo "machine: ${COMPUTERNAME:-$(hostname)} / $(uname -s)"
  echo "worktree: $(pwd)"
  echo "sdk (dotnet --version): $(dotnet --version 2>&1)"
  echo "environment: APPDATA='$APPDATA' LOCALAPPDATA='$LOCALAPPDATA' TEMP='$TEMP' TMP='$TMP' PATHEXT='$PATHEXT'"
  echo "library temp folder under test: '$IMAGE_FOLDER' (Path.GetTempPath()/Trustsoft.NotifyIcon)"
  echo "identities: ${ID_PREFIX}.${TFMS[0]}, ${ID_PREFIX}.${TFMS[1]}, ${ID_PREFIX}.${TFMS[2]} (one per framework)"
  echo
  echo "## 0. what this task measures, and what it does not"
  echo "  samples/consumer-proof is a windowless WPF application whose only reference is"
  echo "  <PackageReference Include=\"Trustsoft.NotifyIcon\" Version=\"1.0.0\" />, restored from"
  echo "  artifacts/ through its own nuget.config (which clears every inherited source) and built with"
  echo "  its own empty Directory.Build.props. It is absent from Trustsoft.NotifyIcon.sln. Nothing in"
  echo "  this script is a repository test: every reading below is either the artifact inspector's, the"
  echo "  independent probe's, or the consumer process's own."
  echo
  echo "  This task measures and records. It changes no file under src/, tests/, samples/, scripts/ or"
  echo "  README.md."
  echo
  echo "## 1. the instrument, the artifact and the inspector"
  echo "   scripts/probe-toast/bin is gitignored, so a re-materialised worktree has no read-back instrument"
  echo "   at all; it is built here so every read-back below names one binary."
  echo '$ dotnet build scripts/probe-toast/ProbeToast.csproj -c Release'
  dotnet build scripts/probe-toast/ProbeToast.csproj -c Release > "$SCRATCH/probe-build.txt" 2>&1
  PROBE_BUILD_EXIT=$?
  grep -E " error | warning |Build succeeded| -> " "$SCRATCH/probe-build.txt" \
    || tail -n 12 "$SCRATCH/probe-build.txt"
  echo "  reading: probe-toast build exit=$PROBE_BUILD_EXIT"
  echo "  reading: probe-toast binary present=$([ -f "$PROBE" ] && echo yes || echo no)"
  echo
  echo "   the packed artifact this run installs (the previous nupkg is deleted first: pack is"
  echo "   incremental, MEM147, so an artifact left over from an earlier run would make this run say"
  echo "   nothing about the tree it is running on)"
  previous="$(ls artifacts/Trustsoft.NotifyIcon.*.nupkg 2>/dev/null || true)"
  if [ -n "$previous" ]; then
    printf '%s\n' "$previous" | while IFS= read -r p; do
      echo "   previous package found: $p ($(stat -c%s "$p" 2>/dev/null || echo '?') bytes, sha256 $(sha256sum "$p" | cut -d' ' -f1))"
    done
  else
    echo "   previous package found: none"
  fi
  rm -f artifacts/Trustsoft.NotifyIcon.*.nupkg
  mkdir -p artifacts
  echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release'
  dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release > "$SCRATCH/pack.txt" 2>&1
  PACK_EXIT=$?
  if [ "$PACK_EXIT" != 0 ] && grep -q "NU1900" "$SCRATCH/pack.txt"; then
    echo "   NU1900: the audit endpoint is unreachable and TreatWarningsAsErrors turns that into an"
    echo "   error; this invocation is retried with -p:NuGetAudit=false (no repository property, no"
    echo "   NuGet.config changed)."
    echo '$ dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release -p:NuGetAudit=false'
    dotnet pack src/Trustsoft.NotifyIcon/Trustsoft.NotifyIcon.csproj -c Release -p:NuGetAudit=false \
      > "$SCRATCH/pack.txt" 2>&1
    PACK_EXIT=$?
  fi
  grep -E " error | warning |Successfully created package" "$SCRATCH/pack.txt" \
    || tail -n 12 "$SCRATCH/pack.txt"
  echo "  reading: pack exit=$PACK_EXIT"
  if [ -f "$NUPKG" ]; then
    NUPKG_SIZE="$(stat -c%s "$NUPKG" 2>/dev/null || echo 0)"
    NUPKG_SHA="$(sha256sum "$NUPKG" | cut -d' ' -f1 | tr -d '\\')"
    echo "  reading: nupkg=$NUPKG size=${NUPKG_SIZE} bytes sha256=$NUPKG_SHA (information only: MEM145)"
  else
    echo "  reading: nupkg=$NUPKG is absent after the pack"
  fi
  echo
  echo "   the inspector reads the SAME artifact the consumer installs, so the artifact-side surface"
  echo "   rule (T04) and the consumer-side reflection below are about one file:"
  echo '$ bash scripts/verify-package.sh artifacts/Trustsoft.NotifyIcon.1.0.0.nupkg'
  bash scripts/verify-package.sh "$NUPKG" > "$SCRATCH/inspector.txt" 2>&1
  INSPECTOR_EXIT=$?
  sed 's/^/  /' "$SCRATCH/inspector.txt"
  INSPECTOR_VERDICT="$(grep -E '^VERDICT' "$SCRATCH/inspector.txt" | tail -1)"
  echo "  reading: inspector exit=$INSPECTOR_EXIT verdict='$INSPECTOR_VERDICT'"
  echo
  echo "## 2. the consumer, built and surface-checked once per framework"
  echo "   R012 in one line: three frameworks, three builds, three assertions - no framework stands in"
  echo "   for another. The surface reading comes from the assembly THIS process loaded out of the"
  echo "   package, not from the library's own test project."
  if [ -f "$FOREIGN_SHORTCUT" ]; then FOREIGN_BEFORE=1; fi
  echo "  the unrelated shortcut '$FOREIGN_SHORTCUT' exists before the run=$FOREIGN_BEFORE (it is not"
  echo "  this task's file, is never deleted here, and its state is compared again in section 6)"
  echo
  echo '$ dotnet restore samples/consumer-proof/ConsumerProof.csproj --force'
  dotnet restore "$CONSUMER" --force > "$SCRATCH/restore.txt" 2>&1
  restore_exit=$?
  tail -2 "$SCRATCH/restore.txt" | sed 's/^/  /'
  echo "  reading: consumer restore exit=$restore_exit"
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    echo
    echo '$ dotnet build samples/consumer-proof -c Release -f '"$tfm"
    dotnet build samples/consumer-proof -c Release -f "$tfm" > "$SCRATCH/build-$tfm.txt" 2>&1
    build_exit=$?
    grep -E " error | warning |Build succeeded| -> " "$SCRATCH/build-$tfm.txt" \
      || tail -n 10 "$SCRATCH/build-$tfm.txt"
    echo "  reading: consumer build $tfm exit=$build_exit"
    if [ "$build_exit" != 0 ]; then
      note_missing "$tfm consumer build exited $build_exit, so no reading below it exists"
    fi

    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    surface_log="$SCRATCH/surface-$tfm.txt"
    echo "\$ $exe --surface-only"
    if [ ! -f "$exe" ]; then
      echo "  the consumer executable is absent, so this reading does not exist"
      SURFACE_EXIT+=(-1)
      note_missing "$tfm surface-only: the consumer executable is absent"
      continue
    fi
    "$exe" --surface-only > "$surface_log" 2>&1
    surface_exit=$?
    SURFACE_EXIT+=("$surface_exit")
    sed 's/^/  /' "$surface_log"
    echo "  reading: surface-only $tfm exit=$surface_exit"
    if [ "$surface_exit" != 0 ]; then
      note_missing "$tfm surface-only exited $surface_exit (0 required)"
    fi
    if grep -qF "PASS the package surfaces exactly the 20 documented public types" "$surface_log"; then
      echo "  reading: the PASS line names the twenty documented types"
    else
      note_missing "$tfm surface-only: no PASS line naming the twenty documented types"
    fi
  done
  echo
  echo "## 3. the live run, one framework at a time"
  echo "   Identities: one per framework, each registered and removed by its own run. The pre-run"
  echo "   read-back is the clean-slate control; a stale value there would make this run's registration"
  echo "   unreadable, so it is printed before the run rather than after it."
  declare -a PRE_READING=()
  declare -a LIVE_READING=()
  declare -a LIVE_ATTEMPTS=()
  declare -a LIVE_CONSUMER_ALIVE=()
  declare -a POST_READING=()
  declare -a CONSUMER_EXIT=()
  declare -a HIST_OUT=()
  declare -a HIST_EXIT=()
  declare -a HIST_COUNT=()
  declare -a IMAGE_COUNT=()
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    id="$ID_PREFIX.$tfm"
    echo
    echo "--- $tfm (identity '$id')"
    echo
    echo "(a) the clean-slate control, read back before anything runs:"
    pre_out="$(read_shortcut "$id")"
    pre_exit=$?
    sed 's/^/    /' <<<"$pre_out"
    pre_line="$(reading_line "$pre_out")"
    echo "  reading: $tfm pre-run read-back exit=$pre_exit"
    if [ -z "$pre_line" ]; then
      note_missing "$tfm clean-slate control: the read-back printed no reading line at all, so the identity's slate cannot be read"
      echo "  an instrument that printed nothing can never pass as the absent-shortcut reading, so this is"
      echo "  named as missing rather than read as a clean slate"
    elif is_absent_reading "$pre_line"; then
      echo "  reading: the identity has no shortcut yet - success=False operation='OpenShellLink' code=0x80070002"
    else
      echo "  reading: the identity is NOT on a clean slate: '$pre_line'"
      echo "  That is a leftover from an interrupted earlier run of this script (the library removes the"
      echo "  shortcut when the notifier is disposed). It is named, deleted - exactly the one file the"
      echo "  reading named, nothing else - and read back again, because a stale value would otherwise"
      echo "  look like this run's own registration:"
      leftover_path="$(sed -nE "s/.* path='([^']*)'.*/\1/p" <<<"$pre_line")"
      leftover_posix="$(printf '%s' "$leftover_path" | tr '\\' '/')"
      if [ -n "$leftover_posix" ] && [ -f "$leftover_posix" ]; then
        rm -f "$leftover_posix"
        echo "  reading: deleted the leftover shortcut '$leftover_path'"
      else
        echo "  reading: the reading names no file this script could delete (path='$leftover_path')"
      fi
      re_out="$(read_shortcut "$id")"
      pre_exit=$?
      sed 's/^/    /' <<<"$re_out"
      pre_line="$(reading_line "$re_out")"
      if is_absent_reading "$pre_line"; then
        echo "  reading: after the cleanup the identity reads absent - success=False operation='OpenShellLink' code=0x80070002"
      else
        note_missing "$tfm clean-slate control: the identity's shortcut is still not the absent reading after deleting the leftover ('$(render_reading "$pre_line")')"
      fi
    fi
    PRE_READING+=("$pre_line")

    echo
    echo "(b) the live run: a real toast through the public ToastNotifier, two action buttons, the"
    echo "    identity override, the show at 2s and the notifier disposed mid-run at 10s while the"
    echo "    process keeps pumping until the run deadline at 22s:"
    out="$SCRATCH/consumer-$tfm.out"
    err="$SCRATCH/consumer-$tfm.err"
    exe="samples/consumer-proof/bin/Release/$tfm/ConsumerProof.exe"
    echo "\$ $exe --toast --toast-buttons --toast-aumid $id --toast-after 2 --toast-dispose-after 10 --run-seconds 22 > <capture> &"
    if [ -f "$exe" ]; then
      "$exe" --toast --toast-buttons --toast-aumid "$id" --toast-after 2 --toast-dispose-after 10 \
        --run-seconds 22 > "$out" 2> "$err" &
    else
      : > "$out"
      : > "$err"
      echo "  the consumer executable is absent, so no live run exists for $tfm"
    fi
    consumer_pid=$!

    # Bounded wait for the run to reach its content line (the show is scheduled at 2s).
    content_deadline=$((SECONDS + 20))
    while [ "$SECONDS" -lt "$content_deadline" ]; do
      if grep -qF '[consumer] toast content:' "$out" 2>/dev/null; then break; fi
      sleep 0.5
    done
    if grep -qF '[consumer] toast content:' "$out" 2>/dev/null; then
      echo "  reading: the consumer reached its content line within the 20s bound"
    else
      echo "  reading: no content line within the 20s bound"
    fi

    # The read-back WHILE the toast is live. The shortcut is written by the notifier's first Show, a
    # moment after the content line is printed, and removed when the notifier is disposed at 10s - so
    # the reading is retried inside that window rather than taken once against a fixed sleep.
    live_out=""
    live_line=""
    live_attempts=0
    live_deadline=$((SECONDS + 15))
    while [ "$SECONDS" -lt "$live_deadline" ]; do
      live_attempts=$((live_attempts + 1))
      live_out="$(read_shortcut "$id")"
      live_line="$(reading_line "$live_out")"
      if is_present_reading "$live_line" "$id"; then break; fi
      sleep 1
    done
    live_consumer_alive=0
    if kill -0 "$consumer_pid" 2>/dev/null; then live_consumer_alive=1; fi
    echo
    echo "    the read-back taken while the toast is live (attempt $live_attempts):"
    sed 's/^/    /' <<<"$live_out"
    echo "  reading: $tfm mid-run read-back attempts=$live_attempts; the consumer was still running when it was taken=$live_consumer_alive"
    if is_present_reading "$live_line" "$id"; then
      echo "  reading: the identity is registered and carries its own value back: success=True value='$id'"
    else
      note_missing "$tfm mid-run read-back: expected success=True value='$id' while the toast was live, got '$(render_reading "$live_line")'"
    fi
    if [ "$live_consumer_alive" != 1 ]; then
      note_missing "$tfm mid-run read-back was not taken while the consumer was alive, so it is not a reading of a live run"
    fi
    LIVE_READING+=("$live_line")
    LIVE_ATTEMPTS+=("$live_attempts")
    LIVE_CONSUMER_ALIVE+=("$live_consumer_alive")

    echo
    echo "(c) the run's own end, bounded so a hung consumer cannot hang the reader:"
    wait_deadline=$((SECONDS + 60))
    while kill -0 "$consumer_pid" 2>/dev/null && [ "$SECONDS" -lt "$wait_deadline" ]; do
      sleep 1
    done
    consumer_exit=-1
    if kill -0 "$consumer_pid" 2>/dev/null; then
      echo "  the consumer did not exit within 60s of its deadline; killing it"
      kill "$consumer_pid" 2>/dev/null
      note_missing "$tfm consumer: the run did not exit within 60s of its deadline"
    else
      wait "$consumer_pid"
      consumer_exit=$?
    fi
    CONSUMER_EXIT+=("$consumer_exit")
    echo "  reading: consumer exit=$consumer_exit (0 required)"
    if [ "$consumer_exit" != 0 ]; then
      note_missing "$tfm consumer exited $consumer_exit (0 required)"
    fi
    echo
    echo "    the consumer's own capture (stdout):"
    sed 's/^/    /' "$out"
    if [ -s "$err" ]; then
      echo "    the consumer's own capture (stderr):"
      sed 's/^/    /' "$err"
    else
      echo "    the consumer's own capture (stderr): empty"
    fi

    echo
    echo "(d) the post-teardown read-back, in a separate process: nothing remains registered:"
    post_out="$(read_shortcut "$id")"
    post_exit=$?
    sed 's/^/    /' <<<"$post_out"
    post_line="$(reading_line "$post_out")"
    echo "  reading: $tfm post-teardown read-back exit=$post_exit"
    if is_absent_reading "$post_line"; then
      echo "  reading: the identity reads absent again - success=False operation='OpenShellLink' code=0x80070002"
    else
      note_missing "$tfm post-teardown read-back: expected the absent reading with code=0x80070002, got '$(render_reading "$post_line")'"
    fi
    POST_READING+=("$post_line")

    echo
    echo "(e) the platform's own record for this identity (independent of the library):"
    hist_out="$("$PROBE" --history "$id" 2>&1)"
    hist_exit=$?
    sed 's/^/    /' <<<"$hist_out"
    hist_count="$(sed -nE 's/.*history verdict: count=([0-9]+).*/\1/p' <<<"$hist_out" | tail -1)"
    if [ -z "$hist_count" ]; then hist_count=-1; fi
    echo "  reading: $tfm notification inventory exit=$hist_exit count=$hist_count"
    HIST_OUT+=("$hist_out")
    HIST_EXIT+=("$hist_exit")
    HIST_COUNT+=("$hist_count")

    echo
    echo "(f) the library's temp folder, counted by this script rather than trusted from the consumer:"
    image_count="$(count_images)"
    IMAGE_COUNT+=("$image_count")
    echo "  reading: '$IMAGE_FOLDER' holds $image_count toast-*.png file(s) after the run (0 is the lifetime rule)"
    if [ "$image_count" != 0 ]; then
      note_missing "$tfm temp folder: $image_count toast-*.png file(s) remained after the run (0 required)"
    fi
  done

  echo
  echo "## 4. the consumer's own readings, asserted from its capture per framework"
  declare -a SETTING_VALUE=()
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    id="$ID_PREFIX.$tfm"
    out="$SCRATCH/consumer-$tfm.out"
    err="$SCRATCH/consumer-$tfm.err"
    echo
    echo "--- $tfm"
    if [ ! -s "$out" ]; then
      SETTING_VALUE+=("(no capture)")
      note_missing "$tfm capture: the consumer produced no stdout at all"
      continue
    fi

    content_line="$(grep -m1 -F '[consumer] toast content:' "$out" || true)"
    echo "  content: $content_line"
    if [ -z "$content_line" ]; then
      note_missing "$tfm content line: absent from the capture"
    else
      contains_id=0
      contains_buttons=0
      case "$content_line" in *"identity='$id'"*) contains_id=1 ;; esac
      if grep -qF -- "$BUTTON_READING" <<<"$content_line"; then contains_buttons=1; fi
      if [ "$contains_id" = 1 ] && [ "$contains_buttons" = 1 ]; then
        echo "  reading: the content line names this run's identity and both action-button arguments"
      else
        note_missing "$tfm content line: identity named=$contains_id, both button arguments named=$contains_buttons"
      fi
    fi

    setting_line="$(grep -m1 -F '[consumer] toast setting:' "$out" || true)"
    case "$setting_line" in
      *"toast setting: Enabled ("*) setting_value="Enabled" ;;
      *"toast setting: Disabled"*)
        setting_value="$(sed -nE 's/.*toast setting: (Disabled[A-Za-z]*).*/\1/p' <<<"$setting_line" | head -1)"
        ;;
      *"(not read)"*) setting_value="(not read)" ;;
      *) setting_value="(absent)" ;;
    esac
    SETTING_VALUE+=("$setting_value")
    echo "  setting: $setting_line"
    echo "  reading: the platform's setting for this run reads '$setting_value'"
    if [ -z "$setting_line" ]; then
      note_missing "$tfm setting: the capture has no 'toast setting:' line"
    fi

    if [ "$setting_value" = "Enabled" ]; then
      echo "  reading: the shell will show this application's toasts, so this run counts as a delivery"
      if [ "${HIST_COUNT[$i]}" -ge 1 ] 2>/dev/null; then
        echo "  reading: the platform holds ${HIST_COUNT[$i]} notification(s) for '$id', which is the shell's own record that the payload was accepted"
      else
        note_missing "$tfm delivery: the setting reads Enabled but the platform's inventory holds ${HIST_COUNT[$i]} notification(s) (>=1 required)"
      fi
    else
      echo "  reading: DOCUMENTED OUTCOME - the setting is '$setting_value', not Enabled, so the shell"
      echo "  accepts the toast and does not render it (S04's rule: the value is an outcome, never a"
      echo "  failure). The delivery readings for $tfm are NOT established by this run, and this script"
      echo "  does not pretend otherwise: the platform's inventory here reads ${HIST_COUNT[$i]} notification(s)."
      echo "  The identity is re-checked with the measured instrument (no registry value is written by"
      echo "  this script to make a run green):"
      echo "\$ powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action status -AppUserModelId $id"
      set_status_out="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action status -AppUserModelId "$id" 2>&1)"
      set_status_exit=$?
      sed 's/^/    /' <<<"$set_status_out"
      echo "  reading: the setting re-check exit=$set_status_exit (informational: this reading explains the outcome, it cannot turn it into a delivery)"
    fi

    teardown_ln="$(grep -nF '[consumer] toast teardown:' "$out" | head -1 | cut -d: -f1)"
    if [ -n "$teardown_ln" ]; then
      echo "  teardown: $(sed -n "${teardown_ln}p" "$out")"
      echo "  reading: the teardown line is present at capture line $teardown_ln"
    else
      note_missing "$tfm teardown line: absent from the capture"
    fi

    window_ln="$(grep -nF -- "$WINDOW_LINE" "$out" | head -1 | cut -d: -f1)"
    if [ -n "$window_ln" ]; then
      echo "  reading: the measured post-teardown window is 0/0/0 and its line is at capture line $window_ln"
      if [ -n "$teardown_ln" ] && [ "$window_ln" -gt "$teardown_ln" ]; then
        echo "  reading: the window line comes after the teardown line, so the window is a window and not a re-statement"
      else
        note_missing "$tfm window line: it does not appear after the teardown line, so it does not measure a window"
      fi
    else
      note_missing "$tfm window line: 'activations=0 dismissals=0 errors=0 after the teardown line' is absent"
    fi

    totals_line="$(grep -m1 -F '[consumer] totals:' "$out" || true)"
    echo "  totals: $totals_line"
    if [ -z "$totals_line" ]; then
      note_missing "$tfm totals line: absent from the capture"
    else
      refused_ok=0
      failures_ok=0
      identity_ok=0
      if grep -qF -- 'toast refused=0' <<<"$totals_line"; then refused_ok=1; fi
      if grep -qF -- 'toast failures=0' <<<"$totals_line"; then failures_ok=1; fi
      if grep -qF -- "toast identity='$id'" <<<"$totals_line"; then identity_ok=1; fi
      if [ "$refused_ok" = 1 ] && [ "$failures_ok" = 1 ] && [ "$identity_ok" = 1 ]; then
        echo "  reading: the totals report refused=0, failures=0 and identity='$id'"
      else
        note_missing "$tfm totals: refused=0 present=$refused_ok, failures=0 present=$failures_ok, identity='$id' present=$identity_ok"
      fi
      activation_reading="$(sed -nE 's/.*toast activations=([0-9]+).*/\1/p' <<<"$totals_line" | head -1)"
      echo "  reading: this run's own activation count (pre- and post-teardown) is '${activation_reading:-?}'"
    fi

    if grep -qiF 'Unhandled exception' "$out" "$err"; then
      note_missing "$tfm: the capture contains 'Unhandled exception' text"
    else
      echo "  reading: neither stream contains unhandled-exception text"
    fi
  done

  echo
  echo "## 5. the two readings no unattended run can produce"
  echo "  1. a click-attributed activation. S01 measured that an activation arrives as the argument the"
  echo "     toast or button carried and carries no per-element information, and the shell needs injected"
  echo "     input an unattended instrument cannot make land (S07's click attempt measured the same"
  echo "     limit). Nothing in this log claims that an automated instrument proved a click: the captures"
  echo "     above report their own activation counts, and a delivered activation cannot be credited to a"
  echo "     specific click even when one arrives."
  echo "  2. whether the banner actually painted. Rendering is not machine-visible; the shell's acceptance"
  echo "     and its setting are what a run can read, and this script records exactly that."
  echo "  Both are human follow-ups, recorded as NEEDS-HUMAN in docs/TOAST-MEASUREMENT.md:"
  pointer='### 8. Human follow-up: the click clauses of the demo'
  if grep -qF -- "$pointer" docs/TOAST-MEASUREMENT.md; then
    echo "  reading: the pointer resolves - '$pointer' is present in docs/TOAST-MEASUREMENT.md"
    echo "  the per-framework expectation for a person is 'three distinct classified arguments plus one"
    echo "  dismissal, and silence afterwards', for a run started as"
    echo "  '--toast --toast-buttons --toast-after 2 --run-seconds 60'."
  else
    note_missing "the human procedure pointer '$pointer' is not in docs/TOAST-MEASUREMENT.md"
  fi
  echo "  this script's own activation readings (per framework, from the totals above):"
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    activation_reading="$(sed -nE 's/.*toast activations=([0-9]+).*/\1/p' "$SCRATCH/consumer-$tfm.out" 2>/dev/null | head -1)"
    echo "    $tfm: activations=${activation_reading:-?} (an unattended run reports what it observed; a"
    echo "    non-zero value is a delivery this run did not cause by a click)"
  done

  echo
  echo "## 6. the machine after the run"
  echo "   Read in a separate process invocation per identity, so what this says was not learned from the"
  echo "   processes that registered anything:"
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    id="$ID_PREFIX.$tfm"
    final_out="$(read_shortcut "$id")"
    final_line="$(reading_line "$final_out")"
    final_path="$(sed -nE "s/.* path='([^']*)'.*/\1/p" <<<"$final_line")"
    final_posix="$(printf '%s' "$final_path" | tr '\\' '/')"
    file_state="absent"
    if [ -n "$final_posix" ] && [ -f "$final_posix" ]; then file_state="present"; fi
    echo "  $tfm: file=$file_state  reading=$final_line"
    if [ -z "$final_line" ]; then
      note_missing "$tfm final state: the read-back printed no reading line at all, so the machine's state could not be read"
    elif is_absent_reading "$final_line" && [ "$file_state" = "absent" ]; then
      echo "    reading: nothing remains registered for this identity"
    elif [ "$file_state" = "present" ]; then
      note_missing "$tfm final state: the identity's shortcut remains after the run ('$final_line', file=$file_state)"
    else
      note_missing "$tfm final state: the read-back is not the absent reading ('$final_line') although no file is present at the computed path"
    fi
  done
  if [ -f "$FOREIGN_SHORTCUT" ]; then FOREIGN_AFTER=1; fi
  echo "  the unrelated shortcut '$FOREIGN_SHORTCUT' existed before=$FOREIGN_BEFORE exists now=$FOREIGN_AFTER"
  if [ "$FOREIGN_BEFORE" != "$FOREIGN_AFTER" ]; then
    note_missing "the unrelated shortcut '$FOREIGN_SHORTCUT' changed state during this run"
  fi
  echo "  the library temp folder '$IMAGE_FOLDER' now holds $(count_images) toast-*.png file(s)"

  echo
  echo "## 7. verdict"
  echo "  sdk=$(dotnet --version 2>&1)"
  echo "  probe-toast build exit=$PROBE_BUILD_EXIT (0 required)"
  echo "  pack exit=$PACK_EXIT (0 required); nupkg=${NUPKG_SIZE} bytes sha256=$NUPKG_SHA (information only)"
  echo "  inspector exit=$INSPECTOR_EXIT (0 required) verdict='$INSPECTOR_VERDICT'"
  for i in "${!TFMS[@]}"; do
    tfm="${TFMS[$i]}"
    echo "  $tfm: surface-only exit=${SURFACE_EXIT[$i]:-?}; clean-slate='${PRE_READING[$i]:-}'"
    echo "    mid-run='${LIVE_READING[$i]:-}' (attempts=${LIVE_ATTEMPTS[$i]:-?}, consumer alive=${LIVE_CONSUMER_ALIVE[$i]:-?})"
    echo "    post-teardown='${POST_READING[$i]:-}'"
    echo "    inventory exit=${HIST_EXIT[$i]:-?} count=${HIST_COUNT[$i]:-?}; consumer exit=${CONSUMER_EXIT[$i]:-?}; temp files=${IMAGE_COUNT[$i]:-?}; setting='${SETTING_VALUE[$i]:-?}'"
  done
  echo "  readings this script could not produce: a click-attributed activation and whether the banner"
  echo "  painted - human follow-up in docs/TOAST-MEASUREMENT.md, section 8 (NEEDS-HUMAN)."
  echo
  if [ "${#missing[@]}" != 0 ]; then
    echo "  VERDICT  not established: ${#missing[@]} required reading(s) missing, each named above:"
    for m in "${missing[@]}"; do
      echo "    - $m"
    done
    echo
    echo "  Every reading that WAS present is printed above; this verdict names only what is missing."
    echo "  Re-run this script after the cause is fixed - it is re-runnable and leaves no consumer"
    echo "  shortcut behind (section 6 reads that back)."
    FINAL_EXIT=1
  else
    echo "  VERDICT  the three-framework consumer proof holds"
    echo "    - the toast instrument built, and the packed artifact the consumer installed is the one the"
    echo "      inspector read: $INSPECTOR_VERDICT"
    echo "    - each framework's consumer built from the package and reported its own surface: 20 exported"
    echo "      types, exactly the documented set, checked from the consumer assembly"
    echo "    - each framework ran with its own identity, and its pre-run read-back, mid-run read-back and"
    echo "      post-teardown read-back are printed above: absent, then success=True value='<its own id>',"
    echo "      then absent again with 0x80070002 - so the machine is left with no consumer shortcut"
    echo "    - each run printed its content line with both button arguments, its teardown line, a measured"
    echo "      0/0/0 post-teardown window, and totals with refused=0 and failures=0 for its identity"
    echo "    - each framework's delivery is reported against the platform's setting exactly as measured:"
    echo "      counted as a delivery only where the setting reads Enabled, and recorded as the documented"
    echo "      outcome (with the delivery readings explicitly not established) where it does not"
    echo "    - the platform's own inventory and the library temp folder were read by this script, not"
    echo "      trusted from the consumer: counts above"
    echo "    - NOT proved here, and not claimed: a click-attributed activation and whether the banner"
    echo "      painted (section 5; human follow-up in docs/TOAST-MEASUREMENT.md section 8)"
    FINAL_EXIT=0
  fi
} > "$LOG" 2>&1

status=$?
cat "$LOG"
if [ "$status" != 0 ]; then
  echo "(the run itself exited $status before reaching its verdict; the log above is partial)"
  exit "$status"
fi
exit "$FINAL_EXIT"
