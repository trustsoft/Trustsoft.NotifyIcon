# M002/S05/T02 instrument: read one identity's shortcut back out of process, and prove the reading
# belongs to the identity that was asked about.
#
# Why this script exists. S05's consumer proof needs an out-of-process reading of a consumer's
# identity - "this identity's shortcut exists and carries this AppUserModelID while the run is live"
# before, during and after teardown. Before T02 the probe could not produce it: its read path used a
# hardcoded file name, so reading back any identity other than the probe's own returned the probe's
# shortcut and a stale unrelated value. This script is the instrument-level check that the fix holds:
# a reading must name the identity it was asked about, and the value it reports must be that
# identity's own.
#
# What it does, in order:
#   1. resolves and exports the real user environment (APPDATA/LOCALAPPDATA/TEMP/TMP/PATHEXT), because
#      this sandbox strips the Windows profile variables and SpecialFolder.Programs must resolve to
#      the real roaming profile; PATHEXT is repaired before anything spawns an .exe, because
#      PowerShell runs an executable through a pipeline only when its extension is in PATHEXT
#      (measured: this sandbox ships PATHEXT='.CPL', and with that value `& <exe> ...` produces NO
#      output and NO exit code at all - a silent zero-line reading that would look like a clean run);
#   2. clears any shortcut of its OWN two identities left by an earlier interrupted run, and says so;
#   3. reads back a never-registered identity of its own and asserts the absent-shortcut reading;
#   4. registers a second identity through the probe's own show path, then reads it back with a
#      SEPARATE process invocation while that show run is still live;
#   5. deletes exactly the one .lnk file it computed, prints that deletion with the file's path, and
#      asserts the read-back has returned to the absent-shortcut reading - the machine is left as found;
#   6. asserts the unrelated Trustsoft.NotifyIcon.ToastProbe shortcut this script never computed is
#      untouched (it is not this task's file and is never deleted here).
#
# The show run is started through .NET directly rather than Start-Process, because
# Start-Process -PassThru returns a process object whose ExitCode is empty in this environment
# (measured: `cmd /c exit 7` then Refresh() still reads '') - a reading that is not there is worse than
# no reading, so the exit code comes from the process object this script starts itself.
#
# The readings asserted (a run that misses any of them fails, and the missing ones are named):
#   - never-registered: `[probe] shortcut read-back: identity='...NeverRegistered' path='<computed>.lnk'
#     success=False operation='OpenShellLink' code=0x80070002` - the absent-shortcut control;
#   - registered, read back while the showing run is live: the same shape with `success=True
#     value='...Registered'` and the value read back verbatim;
#   - the same value after the showing process exited, because the probe leaves its shortcut in place;
#   - after the deletion of the computed file: `success=False ... code=0x80070002` again.
# Every assertion on `path='...'` compares against the path this script computed from the identity, so
# a reading of some other identity's shortcut (the pre-T02 defect) is a failure, not a pass.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/read-shortcut.ps1
#
# Exit code: 0 when every reading is present; non-zero when one is missing or a process failed.

param(
    [int]$ShowWaitSeconds = 3,
    [string]$WorkRoot = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

if ($ShowWaitSeconds -le 0) {
    throw "-ShowWaitSeconds ($ShowWaitSeconds) must be positive so the show run is live long enough to read its shortcut."
}

# The two identities this script owns. Both are named after this task, so a leftover from an earlier
# run is recognisably this script's own and nothing else's.
$NeverRegisteredAumid = 'Trustsoft.NotifyIcon.S05.ReadShortcut.NeverRegistered'
$RegisteredAumid = 'Trustsoft.NotifyIcon.S05.ReadShortcut.Registered'

# ---- the real user environment ---------------------------------------------------
# The sandbox strips these; a Windows path is required, so a missing or POSIX-flavoured value (for
# example '/tmp' inherited from a non-Windows shell) is replaced by the profile-derived default.
$userProfile = $env:USERPROFILE
if ([string]::IsNullOrEmpty($userProfile)) { $userProfile = [Environment]::GetFolderPath('UserProfile') }
if ([string]::IsNullOrEmpty($userProfile)) { throw 'Neither USERPROFILE nor the UserProfile known folder resolved; the identity reading needs the real roaming profile.' }

$roaming = $env:APPDATA
if ([string]::IsNullOrEmpty($roaming) -or $roaming -notmatch '^[A-Za-z]:[\\/]') { $roaming = Join-Path $userProfile 'AppData\Roaming' }

$local = $env:LOCALAPPDATA
if ([string]::IsNullOrEmpty($local) -or $local -notmatch '^[A-Za-z]:[\\/]') { $local = Join-Path $userProfile 'AppData\Local' }

$tempRoot = $env:TEMP
if ([string]::IsNullOrEmpty($tempRoot) -or $tempRoot -notmatch '^[A-Za-z]:[\\/]') { $tempRoot = Join-Path $local 'Temp' }

$env:APPDATA = $roaming
$env:LOCALAPPDATA = $local
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

# PATHEXT must name .EXE before anything spawns an .exe: PowerShell runs an executable through a
# pipeline only when its extension is in PATHEXT. A stripped environment loses the variable, and this
# sandbox supplies a NON-empty but useless value ('.CPL'), so the test is ".EXE is in the list", not
# "the variable is set" - an empty list and a wrong list fail the same way (measured: both make
# `& <exe> ...` return no lines and no LASTEXITCODE, so the probe would appear to have said nothing).
if ([string]::IsNullOrEmpty($env:PATHEXT) -or $env:PATHEXT -notmatch '(?i)\.EXE') {
    $env:PATHEXT = '.COM;.EXE;.BAT;.CMD'
}

Write-Output ("runner: environment resolved for the child process - APPDATA='{0}' LOCALAPPDATA='{1}' TEMP='{2}' TMP='{3}' PATHEXT='{4}'" -f `
    $env:APPDATA, $env:LOCALAPPDATA, $env:TEMP, $env:TMP, $env:PATHEXT)

# ---- the probe -------------------------------------------------------------------
$probeExe = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\probe-toast.exe'
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\probe-toast.dll'

# Invoke the apphost when it exists: it resolves the shared runtime itself, so a run does not depend on
# 'dotnet' being resolvable on PATH. A stripped environment can lose PATHEXT while keeping the dotnet
# directory on PATH, which makes PowerShell unable to find the extensionless 'dotnet'; the apphost has
# no such dependency. The framework-dependent .dll stays as a fallback, resolved through an absolute
# host path for the same reason.
if (Test-Path $probeExe) {
    $probeCommand = $probeExe
    $probePrefix = @()
} elseif (Test-Path $probeDll) {
    $dotnetHost = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnetHost)) {
        $dotnetHost = (Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue).Source
    }
    if (-not $dotnetHost) {
        throw "probe-toast is built but neither the apphost '$probeExe' nor a 'dotnet' host could be found."
    }
    $probeCommand = $dotnetHost
    $probePrefix = @($probeDll)
} else {
    throw "probe-toast is not built at '$probeExe'; build it first: dotnet build scripts/probe-toast/ProbeToast.csproj -c Release"
}

if ([string]::IsNullOrEmpty($WorkRoot)) {
    $WorkRoot = Join-Path $tempRoot 'probe-read-shortcut'
}
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

# ---- the paths this script computes, and the one it must never touch --------------
# The path convention is the library's own: the per-user Start menu, named after the identity. The
# assertions below require the probe's reading to carry exactly these paths, which is what makes a
# reading of some other identity's shortcut a failure rather than evidence.
$programsFolder = [Environment]::GetFolderPath('Programs')
if ([string]::IsNullOrEmpty($programsFolder)) {
    throw 'SpecialFolder.Programs did not resolve, so neither identity has a computable shortcut path.'
}
$neverRegisteredShortcut = Join-Path $programsFolder ($NeverRegisteredAumid + '.lnk')
$registeredShortcut = Join-Path $programsFolder ($RegisteredAumid + '.lnk')
$foreignShortcut = Join-Path $programsFolder 'Trustsoft.NotifyIcon.ToastProbe.lnk'

Write-Output ("runner: Programs folder resolved to '{0}'" -f $programsFolder)
Write-Output ("runner: this script's own computed paths: '{0}' and '{1}'" -f $neverRegisteredShortcut, $registeredShortcut)

$script:failures = New-Object 'System.Collections.Generic.List[string]'

function Write-ProbeLines {
    param([string[]]$Lines)
    foreach ($line in $Lines) { Write-Output ("probe: {0}" -f $line) }
}

# One read-back invocation: a separate process, so what it reports was not learned from the process
# that registered the shortcut under test.
function Invoke-ReadBack {
    param([string]$Aumid)
    $lines = @(& $probeCommand @probePrefix --read-shortcut $Aumid 2>&1)
    $exit = $LASTEXITCODE
    $match = ($lines | Select-String -Pattern '^\[probe\] shortcut read-back:' | Select-Object -Last 1)
    $text = ''
    if ($null -ne $match) { $text = $match.Line }
    return [pscustomobject]@{ Exit = $exit; Reading = $text; Lines = $lines }
}

function Assert-Reading {
    param([string]$Name, [int]$Exit, [string]$Reading, [string]$Pattern)
    if ($Exit -ne 0) {
        $script:failures.Add("$Name`: the read-back process exited $Exit")
    }

    if ([string]::IsNullOrEmpty($Reading)) {
        $script:failures.Add("$Name`: no 'shortcut read-back' line was printed at all")
        return
    }

    if ($Reading -notmatch $Pattern) {
        $script:failures.Add("$Name`: the reading is missing - expected /$Pattern/ but the line was '$Reading'")
    }
}

$foreignExistedBefore = Test-Path -LiteralPath $foreignShortcut

# ---- clear only this script's own leftovers ---------------------------------------
# A previous interrupted run can leave one of OUR OWN two shortcuts behind. Leaving it would turn the
# never-registered control into a contaminated reading, so it is removed here, named, and nothing else
# in the folder is touched.
foreach ($own in @($neverRegisteredShortcut, $registeredShortcut)) {
    if (Test-Path -LiteralPath $own) {
        Remove-Item -LiteralPath $own -Force
        Write-Output ("runner: cleared this script's own leftover from an earlier run: '{0}'" -f $own)
    }
}

# ---- (a) the never-registered identity: the absent-shortcut control ----------------
Write-Output ''
Write-Output ('--- (a) never-registered identity {0} ---' -f $NeverRegisteredAumid)
$neverRegistered = Invoke-ReadBack -Aumid $NeverRegisteredAumid
Write-ProbeLines -Lines $neverRegistered.Lines
$neverRegisteredPattern = "^\[probe\] shortcut read-back: identity='$([regex]::Escape($NeverRegisteredAumid))' path='$([regex]::Escape($neverRegisteredShortcut))' success=False operation='OpenShellLink' code=0x80070002$"
Assert-Reading -Name 'never-registered (absent-shortcut control)' -Exit $neverRegistered.Exit -Reading $neverRegistered.Reading -Pattern $neverRegisteredPattern
if (Test-Path -LiteralPath $neverRegisteredShortcut) {
    $script:failures.Add("never-registered: the read-back created or found a shortcut at '$neverRegisteredShortcut'")
}

# ---- (b) an identity registered through the probe's own show path ------------------
Write-Output ''
Write-Output ('--- (b) registered identity {0} ---' -f $RegisteredAumid)
$showLog = Join-Path $WorkRoot 'probe-show.log'
$showErr = Join-Path $WorkRoot 'probe-show.err.log'
$showArguments = @()
if ($probePrefix.Count -gt 0) { $showArguments += $probePrefix }
$showArguments += @('--aumid', $RegisteredAumid, '--wait-seconds', "$ShowWaitSeconds")

# Quote any argument carrying whitespace: the child gets one command line.
$showArgumentLine = (($showArguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ')
Write-Output ("runner: registering through the probe's own show path: {0} {1}" -f $probeCommand, $showArgumentLine)

$showStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$showStartInfo.FileName = $probeCommand
$showStartInfo.Arguments = $showArgumentLine
$showStartInfo.UseShellExecute = $false
$showStartInfo.RedirectStandardOutput = $true
$showStartInfo.RedirectStandardError = $true

$showProcess = New-Object System.Diagnostics.Process
$showProcess.StartInfo = $showStartInfo
[void]$showProcess.Start()

# Read both streams asynchronously from the moment the child starts: a reader started only after the
# wait would let a full pipe buffer deadlock the probe (its transcript is ~3 KB, but nothing here
# depends on staying under the buffer size).
$showStdoutTask = $showProcess.StandardOutput.ReadToEndAsync()
$showStderrTask = $showProcess.StandardError.ReadToEndAsync()

# Wait for the shortcut to appear (bounded), so the read-back below is taken while the show is live
# rather than on a process that has already gone.
$registrationDeadline = (Get-Date).AddSeconds(20)
while (-not (Test-Path -LiteralPath $registeredShortcut) -and (Get-Date) -lt $registrationDeadline) {
    Start-Sleep -Milliseconds 200
}

$registeredLive = Invoke-ReadBack -Aumid $RegisteredAumid
$showWasLive = -not $showProcess.HasExited
Write-Output ("runner: the showing process was still running when this read-back was taken: {0} (pid={1})" -f $showWasLive, $showProcess.Id)
Write-ProbeLines -Lines $registeredLive.Lines

$registeredLivePattern = "^\[probe\] shortcut read-back: identity='$([regex]::Escape($RegisteredAumid))' path='$([regex]::Escape($registeredShortcut))' success=True value='$([regex]::Escape($RegisteredAumid))'$"
Assert-Reading -Name 'registered while live' -Exit $registeredLive.Exit -Reading $registeredLive.Reading -Pattern $registeredLivePattern

# The showing process is now waited for, so its own transcript is part of the evidence and a failure in
# it cannot pass unnoticed.
if (-not $showProcess.WaitForExit(60000)) {
    $script:failures.Add("the show run (pid=$($showProcess.Id)) did not exit within 60s")
    try { $showProcess.Kill() } catch { }
} else {
    $showStdout = $showStdoutTask.Result
    $showStderr = $showStderrTask.Result
    $showStdout | Set-Content -Path $showLog -Encoding UTF8
    $showStderr | Set-Content -Path $showErr -Encoding UTF8

    Write-Output ''
    Write-Output '--- the showing process transcript ---'
    foreach ($line in ($showStdout -split "`r?`n")) {
        if (-not [string]::IsNullOrEmpty($line)) { Write-Output ("show: {0}" -f $line) }
    }
    foreach ($line in ($showStderr -split "`r?`n")) {
        if (-not [string]::IsNullOrEmpty($line)) { Write-Output ("show (stderr): {0}" -f $line) }
    }

    # The probe's own verdict and its write/read-back agreement are asserted from its transcript, not
    # inferred from the exit code alone: they are the reading that the identity was written and read
    # back as written by the process that registered it.
    Write-Output ("runner: the show run exit={0} log='{1}'" -f $showProcess.ExitCode, $showLog)
    if ($showProcess.ExitCode -ne 0) {
        $script:failures.Add("the show run exited $($showProcess.ExitCode)")
    }
    if ($showStdout -notmatch ('read-back matches expected ''{0}'' = True' -f [regex]::Escape($RegisteredAumid))) {
        $script:failures.Add("the show run's transcript does not report 'read-back matches expected `'$RegisteredAumid`' = True'")
    }
    if ($showStdout -notmatch 'verdict: positive run complete') {
        $script:failures.Add("the show run's transcript does not report 'verdict: positive run complete'")
    }
}

# The probe leaves its shortcut in place when it exits (unlike the library's teardown): the value must
# still be readable, otherwise the deletion below would have nothing to remove and the machine would
# not be left as found.
$registeredAfterShow = Invoke-ReadBack -Aumid $RegisteredAumid
Write-Output ''
Write-Output '--- the same identity, read back after the showing process exited ---'
Write-ProbeLines -Lines $registeredAfterShow.Lines
Assert-Reading -Name 'registered after the showing process exited' -Exit $registeredAfterShow.Exit -Reading $registeredAfterShow.Reading -Pattern $registeredLivePattern

# ---- (c) delete exactly the file this script computed -----------------------------
Write-Output ''
Write-Output '--- (c) cleanup: delete exactly the computed file, then read back again ---'
$deleted = $false
if (Test-Path -LiteralPath $registeredShortcut) {
    Remove-Item -LiteralPath $registeredShortcut -Force
    $deleted = $true
}
Write-Output ("runner: deleted the shortcut this script computed: '{0}' (existed and was removed={1})" -f $registeredShortcut, $deleted)
if (-not $deleted) {
    $script:failures.Add("cleanup: the computed shortcut '$registeredShortcut' was not there to delete, so the machine was not left as found")
}

$postCleanup = Invoke-ReadBack -Aumid $RegisteredAumid
Write-ProbeLines -Lines $postCleanup.Lines
$postCleanupPattern = "^\[probe\] shortcut read-back: identity='$([regex]::Escape($RegisteredAumid))' path='$([regex]::Escape($registeredShortcut))' success=False operation='OpenShellLink' code=0x80070002$"
Assert-Reading -Name 'after cleanup (returned to absent)' -Exit $postCleanup.Exit -Reading $postCleanup.Reading -Pattern $postCleanupPattern

# ---- (d) the file this script never computed is untouched --------------------------
Write-Output ''
Write-Output '--- (d) the unrelated shortcut this script never computed ---'
$foreignExistsNow = Test-Path -LiteralPath $foreignShortcut
Write-Output ("runner: '{0}' existed before this run={1}, exists now={2}; it is not this task's file and is never deleted here" -f `
    $foreignShortcut, $foreignExistedBefore, $foreignExistsNow)
if ($foreignExistedBefore -ne $foreignExistsNow) {
    $script:failures.Add("the unrelated shortcut '$foreignShortcut' changed state during this run (before=$foreignExistedBefore after=$foreignExistsNow)")
}

# ---- verdict ---------------------------------------------------------------------
Write-Output ''
if ($script:failures.Count -gt 0) {
    Write-Output 'runner verdict: FAIL'
    foreach ($failure in $script:failures) { Write-Output ("  - {0}" -f $failure) }
    exit 1
}

Write-Output 'runner verdict: PASS (the instrument names the identity it was asked about, reads the registered value back verbatim, and leaves nothing of its own behind)'
exit 0
