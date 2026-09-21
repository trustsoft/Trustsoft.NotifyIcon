# Independent notification-area observation for the S05 teardown check.
#
# The probe (scripts/probe-live) decides whether the shell holds a tray icon by calling
# Shell_NotifyIconGetRect, which is the shell's *app-facing API*. This script answers the same
# question from the other side: it asks the shell's own tray user interface - the taskbar and its
# overflow window - which icons it is displaying, through UI Automation, and prints their names.
# The two oracles share no code path and no API, so agreement between them is evidence and not a
# restatement.
#
# The icon names UI Automation reports for tray icons are the tooltips the owning application set
# (displayed to the user), which is why the tooltip text is the identifier to look for.
#
# Hidden icons on Windows 11 live in the overflow flyout, which only exists while it is open, so
# -OpenOverflow clicks the notification area's own 'Show Hidden Icons' chevron first and waits for
# the flyout host to appear. That is an operator action on the shell, not on the application under
# observation: nothing is injected into the sample, and the flyout is closed again afterwards.
#
# Usage:
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts/tray-inventory.ps1
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts/tray-inventory.ps1 -OpenOverflow -All -Needle "Trustsoft.NotifyIcon"

param(
    [string]$Needle = '',
    [switch]$All,
    [switch]$OpenOverflow,
    [switch]$KeepOverflowOpen,
    [switch]$ListTrayWindows
)

$ErrorActionPreference = 'Stop'
# Write UTF-8 to a redirected stdout so the captured evidence file is readable in git (Windows
# PowerShell 5.1 defaults to the OEM code page, which turns tray tooltips into question marks).
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

# Every window the shell uses for the notification area: the primary taskbar, any secondary
# taskbar (additional monitors), and the two spellings of the overflow host.
$hosts = @(
    'Shell_TrayWnd',
    'Shell_SecondaryTrayWnd',
    'NotifyIconOverflowWindow',
    'TopLevelWindowForOverflowXamlIsland'
)

$root = $AE::RootElement
$named = New-Object System.Collections.ArrayList

Write-Output ("tray-inventory: {0} needle='{1}'" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Needle)
$found = 0

if ($ListTrayWindows) {
    Write-Output '--- top-level windows whose class names mention tray/overflow/flyout ---'
    foreach ($w in $root.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        $c = $w.Current.ClassName
        if ($c -match 'Tray|Overflow|Flyout|XamlIsland') {
            Write-Output ("  class='{0}' name='{1}' hwnd=0x{2:X}" -f $c, $w.Current.Name, $w.Current.NativeWindowHandle)
        }
    }
}

if ($OpenOverflow) {
    $trayCond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')
    $tray = $root.FindFirst($TS::Children, $trayCond)
    $nameCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Show Hidden Icons')
    $chevron = if ($null -ne $tray) { $tray.FindFirst($TS::Descendants, $nameCond) } else { $null }
    if ($null -eq $chevron) {
        Write-Output 'overflow: FAILED - the notification area exposes no Show Hidden Icons chevron'
    }
    else {
        $invoked = $false
        if ($chevron.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$null)) {
            ([System.Windows.Automation.InvokePattern]$chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
            $invoked = $true
            Write-Output 'overflow: opened by invoking the Show Hidden Icons chevron (UI Automation Invoke)'
        }
        if (-not $invoked) {
            Add-Type -Namespace TrayScan -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
'@
            $r = $chevron.Current.BoundingRectangle
            [void][TrayScan.Mouse]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
            Start-Sleep -Milliseconds 200
            [TrayScan.Mouse]::mouse_event(0x0002, 0, 0, 0, [System.UIntPtr]::Zero)
            [TrayScan.Mouse]::mouse_event(0x0004, 0, 0, 0, [System.UIntPtr]::Zero)
            Write-Output 'overflow: opened by clicking the Show Hidden Icons chevron (mouse)'
        }
        Start-Sleep -Milliseconds 1200
    }
}

foreach ($cls in $hosts) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, $cls)
    $win = $root.FindFirst($TS::Children, $cond)
    if ($null -eq $win) {
        Write-Output "host $cls : ABSENT"
        continue
    }

    $hwnd = '0x{0:X}' -f $win.Current.NativeWindowHandle
    $winName = $win.Current.Name
    Write-Output "host $cls : present hwnd=$hwnd name='$winName'"

    $elements = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $count = 0
    foreach ($e in $elements) {
        $name = $e.Current.Name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $count++
        $type = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $rect = $e.Current.BoundingRectangle
        $line = "  {0} '{1}' rect={2},{3} {4}x{5}" -f $type, $name, [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height
        [void]$named.Add($line.Trim())
        if ($All -or ($Needle -ne '' -and $name -like "*$Needle*")) {
            Write-Output $line
        }
        if ($Needle -ne '' -and $name -like "*$Needle*") { $found++ }
    }
    Write-Output "  named-elements: $count"
}

if ($All) {
    Write-Output '--- all named elements, sorted ---'
    $named | Sort-Object | ForEach-Object { Write-Output $_ }
}

Write-Output "named-elements-total: $($named.Count)"

if ($OpenOverflow -and -not $KeepOverflowOpen) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 400
    Write-Output 'overflow: closed again with Escape'
}

if ($Needle -eq '') {
    Write-Output 'verdict: no needle given, nothing was searched for'
} elseif ($found -gt 0) {
    Write-Output "verdict: PRESENT - $found element(s) in the notification area match '$Needle'"
} else {
    Write-Output "verdict: ABSENT - the notification area exposes no element matching '$Needle'"
}
