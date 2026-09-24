# M002/S04/T05 instrument: the measured per-application notification recipe.
#
# The slice's demo clause is "with notifications disabled the sample reports the documented outcome
# instead of appearing to succeed". The state that produces it is one registry value, measured in
# docs/TOAST-MEASUREMENT.md: a DWORD Enabled = 0 under
# HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<AppUserModelID>.
#
# This script owns exactly that value and nothing else:
#   -Action set     writes Enabled = 0 (creating the key when it does not exist)
#   -Action clear   removes ONLY the Enabled value, never the key - the key holds the platform's own
#                   counters and other state, and deleting it would be a destructive side effect a
#                   measurement has no business having
#   -Action status  prints the value and says whether it is absent
#
# Every action prints its read-back, so a disabled run is reproducible and provably reversible: the
# capture of a -Action clear is the evidence that the machine was left as it was found.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action status
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action set
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action clear
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/toast-notification-setting.ps1 -Action status -AppUserModelId Trustsoft.NotifyIcon.Sample
#
# Exit code: 0 when the action ran and its read-back is reported; non-zero when the argument or the
# registry access fails.

param(
    [ValidateSet('set', 'clear', 'status')]
    [string]$Action = 'status',

    [string]$AppUserModelId = 'Trustsoft.NotifyIcon.Sample'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

if ([string]::IsNullOrWhiteSpace($AppUserModelId)) {
    throw '-AppUserModelId must be a non-empty identity.'
}

# A backslash would make the registry path address a different subkey than the caller asked for, so it
# is rejected rather than silently traversed.
if ($AppUserModelId.Contains('\')) {
    throw "-AppUserModelId must not contain a backslash, got '$AppUserModelId'."
}

$keyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\$AppUserModelId"

function Get-EnabledState {
    # Returns a hashtable so "absent" and "0" cannot be confused at the call site: 0 is the value that
    # means notifications are off, and a missing value means the platform's default applies.
    if (-not (Test-Path -LiteralPath $keyPath)) {
        return @{ KeyExists = $false; Present = $false; Value = $null }
    }

    $item = Get-ItemProperty -LiteralPath $keyPath -ErrorAction SilentlyContinue
    $property = $null
    if ($null -ne $item) {
        $property = $item.PSObject.Properties | Where-Object { $_.Name -eq 'Enabled' } | Select-Object -First 1
    }

    if ($null -eq $property) {
        return @{ KeyExists = $true; Present = $false; Value = $null }
    }

    return @{ KeyExists = $true; Present = $true; Value = [int]$property.Value }
}

function Describe-EnabledState {
    param([hashtable]$State)

    if (-not $State.Present) {
        return 'Enabled=(absent) - no per-application override is stored, so the platform default applies (the shell will show this application''s toasts)'
    }

    if ($State.Value -eq 0) {
        return 'Enabled=0 (DWORD) - notifications are disabled for this application: the shell will not show its toasts'
    }

    return ('Enabled={0} (DWORD) - the value is present; 0 is the measured "disabled" spelling, so {0} is reported as-is rather than interpreted' -f $State.Value)
}

Write-Output ("[setting] action={0} aumid='{1}' key='{2}'" -f $Action, $AppUserModelId, $keyPath)

switch ($Action) {
    'set' {
        if (-not (Test-Path -LiteralPath $keyPath)) {
            New-Item -Path $keyPath -Force | Out-Null
            Write-Output ("[setting] created the key '{0}' (it does not exist on a machine that never stored a per-application setting for this identity)" -f $keyPath)
        } else {
            Write-Output ("[setting] the key '{0}' already exists; only the Enabled value is written" -f $keyPath)
        }

        New-ItemProperty -LiteralPath $keyPath -Name 'Enabled' -PropertyType DWord -Value 0 -Force | Out-Null
        Write-Output '[setting] wrote Enabled = DWORD 0'
    }

    'clear' {
        if (-not (Test-Path -LiteralPath $keyPath)) {
            Write-Output ("[setting] the key '{0}' does not exist, so there is no Enabled value to remove" -f $keyPath)
        } elseif (-not (Get-EnabledState).Present) {
            Write-Output ("[setting] the key '{0}' exists but holds no Enabled value; nothing to remove" -f $keyPath)
        } else {
            Remove-ItemProperty -LiteralPath $keyPath -Name 'Enabled' -Force
            Write-Output ("[setting] removed only the Enabled value from '{0}'; the key itself is kept (it holds the platform's own counters)" -f $keyPath)
        }
    }

    'status' {
        # Nothing is written; the read-back below is the whole action.
    }
}

$state = Get-EnabledState
Write-Output ("[setting] key exists={0}" -f $state.KeyExists)
Write-Output ("[setting] read-back: {0}" -f (Describe-EnabledState -State $state))

# The shorthand a caller greps for: enabled=present|absent plus the numeric value, so a run can be
# checked without parsing the sentence above.
$valueText = if ($state.Present) { "$($state.Value)" } else { '(absent)' }
Write-Output ("[setting] reading: enabled={0} value={1} key='{2}'" -f $(if ($state.Present) { 'present' } else { 'absent' }), $valueText, $keyPath)

exit 0
