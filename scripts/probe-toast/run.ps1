# Orchestrator for the M002/S01 toast probe (scripts/probe-toast). It runs the probe in its two
# modes against a clean slate and captures the whole chain as evidence: the positive control (register
# + show + subscribe) and the negative control (never-registered identity + the same show path).
#
# The probe itself (Program.cs) is a separate process per phase, so the out-of-process inventory
# (--history) observes the toast from a process other than the one that showed it.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run.ps1
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run.ps1 -WaitSeconds 12
#
# The script exits non-zero if the positive control fails to deliver a toast (history count stays 0),
# or if a read-back mismatch or a factory/show failure is printed by the probe. The negative control's
# expected outcome is documented below: on this machine Show still delivers to the Action Center for an
# unregistered identity, so the control is judged on the absence of an activation callback, not on a
# zero history count (see docs/TOAST-MEASUREMENT.md).

param(
    [string]$Aumid = 'Trustsoft.NotifyIcon.ToastProbe',
    [string]$NegativeAumid = 'Trustsoft.NotifyIcon.ToastProbe.Negative',
    [int]$WaitSeconds = 10
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$probeDll = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\probe-toast.dll'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Trustsoft.NotifyIcon.ToastProbe.lnk'

function Invoke-Probe([string[]]$Args) {
    $out = & dotnet $probeDll @Args 2>&1
    $out | ForEach-Object { Write-Output $_ }
    return ($LASTEXITCODE)
}

Write-Output "probe-toast run: start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') on $env:COMPUTERNAME"

# ---- clean state ---------------------------------------------------------------
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
& dotnet $probeDll --clear-history $Aumid | Out-Null
& dotnet $probeDll --clear-history $NegativeAumid | Out-Null

# ---- positive control ----------------------------------------------------------
Write-Output '--- positive control (register + show + subscribe) ---'
$positive = & dotnet $probeDll --aumid $Aumid --wait-seconds $WaitSeconds 2>&1
$positive | ForEach-Object { Write-Output $_ }
$positiveExit = $LASTEXITCODE

$positiveHistory = & dotnet $probeDll --history $Aumid 2>&1
$positiveHistory | ForEach-Object { Write-Output $_ }
$historyCount = ($positiveHistory | Select-String -Pattern 'count=(\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }) | Select-Object -Last 1
$readBackOk = $positive | Select-String -Pattern 'read-back matches expected .* = True' -Quiet
$showOk = $positive | Select-String -Pattern 'notifier.Show\(toast\) hr=0x00000000' -Quiet

if ($positiveExit -ne 0) { Write-Output 'verdict: POSITIVE FAILED (probe exit non-zero)'; exit 1 }
if (-not $readBackOk) { Write-Output 'verdict: POSITIVE FAILED (read-back mismatch)'; exit 1 }
if (-not $showOk) { Write-Output 'verdict: POSITIVE FAILED (Show did not succeed)'; exit 1 }
if ([int]$historyCount -lt 1) { Write-Output "verdict: POSITIVE FAILED (history count=$historyCount, expected >= 1)"; exit 1 }
Write-Output "verdict: POSITIVE PASS (read-back matched, Show S_OK, history count=$historyCount)"

# ---- negative control ----------------------------------------------------------
Write-Output '--- negative control (never-registered identity, same show path) ---'
$negative = & dotnet $probeDll --aumid $NegativeAumid --skip-register --wait-seconds $WaitSeconds 2>&1
$negative | ForEach-Object { Write-Output $_ }
$negativeExit = $LASTEXITCODE

$negativeHistory = & dotnet $probeDll --history $NegativeAumid 2>&1
$negativeHistory | ForEach-Object { Write-Output $_ }
$activated = $negative | Select-String -Pattern 'result: activated=([0-9]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }

if ($negativeExit -ne 0) { Write-Output 'verdict: NEGATIVE FAILED (probe exit non-zero)'; exit 1 }
if ([int]$activated -gt 0) {
    Write-Output "verdict: NEGATIVE FAILED (an activation callback was delivered to an unregistered identity)"; exit 1
}
Write-Output "verdict: NEGATIVE PASS (no activation delivered to the unregistered identity; activated=$activated)"

Write-Output 'probe-toast run: complete'
