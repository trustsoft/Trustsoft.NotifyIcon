# M002/S04/T05 instrument: run the windowless sample's image demonstration and judge its readings.
#
# What it does, in order:
#   1. resolves and exports the real user environment. The sandbox this instrument runs in strips
#      APPDATA and LOCALAPPDATA and leaves TMP unset, and the sample needs the roaming profile to
#      register its shortcut; TEMP/TMP are normalised too, so the observer and the sample agree on the
#      library's temp folder instead of resolving two different ones;
#   2. empties the library's temp folder (toast-*.png only) and says how many files it removed, so the
#      "0 file(s) after teardown" reading is this run's own and not a leftover's;
#   3. starts observe-toast-temp.ps1 as a SEPARATE process against that folder;
#   4. launches samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe
#      with --toast --toast-image --toast-after 2 --toast-dispose-after 8 --run-seconds 18;
#   5. waits for both, prints their captures, and asserts the readings below.
#
# The readings asserted (a run that misses any of them fails, and the missing ones are named):
#   - the content line names the image:
#       [sample] toast content: ... image=source(frame=Frames[0], placement=AppLogoOverride)
#   - the library resolved the source into a file and a file:/// reference:
#       [trace] toast notifier: image resolved path='...' reference='file:///...' bytes=N pixels=WxH
#   - the library deleted it on the show's unwind path:
#       [trace] toast show: delete image file='...' existed=True
#   - a second process saw the file while the toast was live, and ran to completion:
#       observer: t+Ns ... files=1 present=True [toast-<guid>.png bytes=N]
#       observer: complete at HH:mm:ss.fff
#   - the show was torn down:
#       [sample] toast teardown: N show(s) unsubscribed and released ...
#   - the sample's own count of the folder after teardown is 0:
#       [sample] toast image temp folder: 0 file(s) after teardown (0 is the lifetime rule) - folder='...'
#   - the run's totals report no refused show:
#       [sample] toast totals: ..., refused=0, image=1, ...
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run-image-demo.ps1
#
# Exit code: 0 when every reading is present; non-zero when one is missing or a process failed.

param(
    [int]$ToastAfterSeconds = 2,
    [int]$DisposeAfterSeconds = 8,
    [int]$RunSeconds = 18,
    [string]$WorkRoot = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

if ($DisposeAfterSeconds -le $ToastAfterSeconds) {
    throw "-DisposeAfterSeconds ($DisposeAfterSeconds) must exceed -ToastAfterSeconds ($ToastAfterSeconds) so the toast is live before the teardown deletes its file."
}

if ($RunSeconds -le $DisposeAfterSeconds) {
    throw "-RunSeconds ($RunSeconds) must exceed -DisposeAfterSeconds ($DisposeAfterSeconds) so the process keeps pumping after the teardown."
}

# ---- the real user environment ---------------------------------------------------
# The sandbox strips these; a Windows path is required, so a missing or POSIX-flavoured value (for
# example '/tmp' inherited from a non-Windows shell) is replaced by the profile-derived default.
$userProfile = $env:USERPROFILE
if ([string]::IsNullOrEmpty($userProfile)) { $userProfile = [Environment]::GetFolderPath('UserProfile') }
if ([string]::IsNullOrEmpty($userProfile)) { throw 'Neither USERPROFILE nor the UserProfile known folder resolved; the sample cannot register its shortcut without a profile.' }

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

$tempFolder = Join-Path $tempRoot 'Trustsoft.NotifyIcon'

Write-Output ("runner: environment resolved for the child process - APPDATA='{0}' LOCALAPPDATA='{1}' TEMP='{2}' TMP='{3}'" -f $env:APPDATA, $env:LOCALAPPDATA, $env:TEMP, $env:TMP)
Write-Output ("runner: the library's temp folder is Path.Combine(Path.GetTempPath(), 'Trustsoft.NotifyIcon') = '{0}'" -f $tempFolder)

# ---- the sample executable -------------------------------------------------------
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sampleExe = Join-Path $repoRoot 'samples\Trustsoft.NotifyIcon.Sample\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Sample.exe'
$sampleDll = Join-Path $repoRoot 'samples\Trustsoft.NotifyIcon.Sample\bin\Release\net8.0-windows\Trustsoft.NotifyIcon.Sample.dll'

# Invoke the apphost when it exists: it resolves the shared runtime itself, so the run does not depend
# on 'dotnet' being resolvable on PATH. The framework-dependent .dll stays as a fallback.
if (Test-Path $sampleExe) {
    $sampleCommand = $sampleExe
    $samplePrefix = @()
} elseif (Test-Path $sampleDll) {
    $dotnetHost = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnetHost)) {
        $dotnetHost = (Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue).Source
    }
    if (-not $dotnetHost) {
        throw "the sample is built but neither the apphost '$sampleExe' nor a 'dotnet' host could be found."
    }
    $sampleCommand = $dotnetHost
    $samplePrefix = @($sampleDll)
} else {
    throw "the sample is not built at '$sampleExe'; build the solution first: dotnet build Trustsoft.NotifyIcon.sln -c Release"
}

if ([string]::IsNullOrEmpty($WorkRoot)) {
    $WorkRoot = Join-Path $tempRoot 'toast-image-demo'
}
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

$sampleLog = Join-Path $WorkRoot 'sample-image-demo.log'
$sampleErr = Join-Path $WorkRoot 'sample-image-demo.err.log'
$observerLog = Join-Path $WorkRoot 'observe-toast-temp.log'
$observerErr = Join-Path $WorkRoot 'observe-toast-temp.err.log'

$observerScript = Join-Path $PSScriptRoot 'observe-toast-temp.ps1'
if (-not (Test-Path $observerScript)) {
    throw "the observer script is missing at '$observerScript'."
}

# ---- a clean folder, so the after-teardown count is this run's --------------------
Write-Output ''
Write-Output ("runner: clearing '{0}' of toast-*.png files before the run" -f $tempFolder)

$removed = 0
if (Test-Path -LiteralPath $tempFolder -PathType Container) {
    foreach ($file in @(Get-ChildItem -LiteralPath $tempFolder -Filter 'toast-*.png' -File -ErrorAction SilentlyContinue)) {
        try {
            Remove-Item -LiteralPath $file.FullName -Force
            $removed++
        } catch {
            Write-Output ("runner: could not remove '{0}': {1}" -f $file.FullName, $_.Exception.Message)
        }
    }
} else {
    New-Item -ItemType Directory -Force -Path $tempFolder | Out-Null
}
Write-Output ("runner: removed {0} leftover file(s); folder exists={1}" -f $removed, (Test-Path -LiteralPath $tempFolder -PathType Container))

# ---- the observer, as a separate process -----------------------------------------
$observerSeconds = $RunSeconds + 2
$observerHost = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path $observerHost)) {
    $observerHost = (Get-Command 'powershell.exe' -ErrorAction SilentlyContinue).Source
}
if (-not $observerHost) {
    throw 'no powershell.exe host could be found to start the observer with.'
}

Write-Output ''
Write-Output ("runner: starting the observer as a separate process for {0}s (it only reads the folder; the library is not loaded in it)" -f $observerSeconds)
$observer = Start-Process -FilePath $observerHost -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $observerScript,
    '-Seconds', "$observerSeconds", '-IntervalSeconds', '1', '-TempFolder', $tempFolder
) -RedirectStandardOutput $observerLog -RedirectStandardError $observerErr -NoNewWindow -PassThru
Write-Output ("runner: observer pid={0} log='{1}'" -f $observer.Id, $observerLog)

# ---- the sample ------------------------------------------------------------------
$sampleArgs = @(
    '--toast', '--toast-image',
    '--toast-after', "$ToastAfterSeconds",
    '--toast-dispose-after', "$DisposeAfterSeconds",
    '--run-seconds', "$RunSeconds"
)

Write-Output ''
Write-Output ("runner: launching '{0}' {1}" -f $sampleCommand, ((@($samplePrefix) + $sampleArgs) -join ' '))

# -Wait is not decoration: Start-Process without it returns a Process object whose ExitCode stays empty
# in this PowerShell (measured), so the sample's exit status would be silently unreadable. The sample
# bounds itself with --run-seconds, which is what keeps this wait finite.
$sample = Start-Process -FilePath $sampleCommand -ArgumentList (@($samplePrefix) + $sampleArgs) -RedirectStandardOutput $sampleLog -RedirectStandardError $sampleErr -NoNewWindow -PassThru -Wait

$sampleExit = $sample.ExitCode
Write-Output ("runner: sample exit={0}" -f $sampleExit)

# The observer keeps ticking for two seconds past the sample's own run, so it covers the teardown with
# room to spare. It was started before the sample and is waited for now; its completeness is judged on
# its own final line rather than on an exit code, because Start-Process without -Wait does not expose
# one here (the same measured limitation the sample's launch above works around with -Wait).
$observerExited = $observer.WaitForExit(($observerSeconds + 20) * 1000)
if (-not $observerExited) {
    Write-Output 'runner: the observer did not finish in time - stopping it'
    try { $observer.Kill() } catch { }
    $observer.WaitForExit(10000) | Out-Null
}
Write-Output ("runner: observer finishedInTime={0}" -f $observerExited)

# ---- the captures ----------------------------------------------------------------
$sampleLines = @(Get-Content -LiteralPath $sampleLog -Encoding UTF8 -ErrorAction SilentlyContinue)
$sampleErrorLines = @(Get-Content -LiteralPath $sampleErr -Encoding UTF8 -ErrorAction SilentlyContinue)
$observerLines = @(Get-Content -LiteralPath $observerLog -Encoding UTF8 -ErrorAction SilentlyContinue)
$observerErrorLines = @(Get-Content -LiteralPath $observerErr -Encoding UTF8 -ErrorAction SilentlyContinue)

Write-Output ''
Write-Output '--- node: the sample capture ---'
$sampleLines | ForEach-Object { Write-Output $_ }
if ($sampleErrorLines.Count -gt 0) {
    Write-Output '--- node: the sample stderr ---'
    $sampleErrorLines | ForEach-Object { Write-Output $_ }
}

Write-Output ''
Write-Output '--- node: the second process''s reading of the temp folder ---'
$observerLines | ForEach-Object { Write-Output $_ }
if ($observerErrorLines.Count -gt 0) {
    Write-Output '--- node: the observer stderr ---'
    $observerErrorLines | ForEach-Object { Write-Output $_ }
}

# ---- the assertions --------------------------------------------------------------
$script:failures = New-Object 'System.Collections.Generic.List[string]'

function Assert-Reading {
    param([string]$Name, [string]$Pattern, [string[]]$Lines)

    if ($Lines | Select-String -Pattern $Pattern -Quiet) {
        Write-Output ("runner: reading present - {0}" -f $Name)
        return
    }

    Write-Output ("runner: reading MISSING - {0} (pattern /{1}/)" -f $Name, $Pattern)
    $script:failures.Add($Name)
}

Write-Output ''
Write-Output '--- node: asserted readings ---'
Assert-Reading -Name 'the content line names the image source' -Lines $sampleLines -Pattern '\[sample\] toast content: .*image=source\(frame=Frames\[0\], placement=AppLogoOverride\)'
Assert-Reading -Name 'the library traced the resolved reference' -Lines $sampleLines -Pattern "\[trace\] toast notifier: image resolved path='.*' reference='file:///.*' bytes=\d+ pixels=\d+x\d+"
Assert-Reading -Name 'the library deleted the file on the unwind path' -Lines $sampleLines -Pattern "\[trace\] toast show: delete image file='.*' existed=True"
Assert-Reading -Name 'a second process saw the file while the toast was live' -Lines $observerLines -Pattern 'observer: t\+[\d.]+s .*files=[1-9]\d* present=True \[.*toast-.*\.png bytes=\d+'
Assert-Reading -Name 'the observer ran to completion' -Lines $observerLines -Pattern 'observer: complete at \d{2}:\d{2}:\d{2}\.\d{3}'
Assert-Reading -Name 'the teardown line' -Lines $sampleLines -Pattern '\[sample\] toast teardown: \d+ show\(s\) unsubscribed and released'
Assert-Reading -Name 'the sample counted 0 files after teardown' -Lines $sampleLines -Pattern '\[sample\] toast image temp folder: 0 file\(s\) after teardown \(0 is the lifetime rule\)'
Assert-Reading -Name 'the totals line reports no refused show' -Lines $sampleLines -Pattern '\[sample\] toast totals: .*refused=0,'

if ($sampleExit -ne 0) {
    Write-Output ("runner: the sample exited with {0}; a run that did not shut down cleanly cannot be read as a pass" -f $sampleExit)
    $script:failures.Add("sample exit code $sampleExit")
}

$finalCount = 0
if (Test-Path -LiteralPath $tempFolder -PathType Container) {
    $finalCount = @(Get-ChildItem -LiteralPath $tempFolder -Filter 'toast-*.png' -File -ErrorAction SilentlyContinue).Count
}
Write-Output ("runner: the folder holds {0} toast-*.png file(s) after the run, read by the runner itself (the sample said 0 above)" -f $finalCount)
if ($finalCount -ne 0) {
    $script:failures.Add("the temp folder still holds $finalCount toast-*.png file(s) after the run")
}

Write-Output ''
if ($script:failures.Count -gt 0) {
    Write-Output 'runner verdict: FAIL'
    foreach ($failure in $script:failures) { Write-Output ("  - missing or failed: {0}" -f $failure) }
    Write-Output ("runner: captures kept at '{0}'" -f $WorkRoot)
    exit 1
}

Write-Output 'runner verdict: PASS (the image was shown from an ImageSource, a second process saw the temp file while the toast was live, and the folder was empty after teardown)'
Write-Output ("runner: captures kept at '{0}'" -f $WorkRoot)
exit 0
