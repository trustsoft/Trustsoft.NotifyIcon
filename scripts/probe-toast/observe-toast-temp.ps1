# M002/S04/T05 instrument: read the library's toast-image temp folder from a SECOND process.
#
# The slice's demo clause is "the library's temp file is observed while the toast is live and is gone
# after teardown". The library's own trace lines say which file it created and which file it deleted;
# this observer is the independent half: it is a different process, it never loads the library, and it
# only counts what is on disk. One line per tick, so a capture shows the folder empty, then holding one
# toast-*.png while the show is live, then empty again after the show is torn down.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/observe-toast-temp.ps1 -Seconds 18
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/observe-toast-temp.ps1 -Seconds 18 -IntervalSeconds 0.5 -TempFolder 'C:\Temp\Trustsoft.NotifyIcon'
#
# Exit code: always 0. This is a reading instrument, not an assertion - the runner that needs a verdict
# (run-image-demo.ps1) parses these lines and decides.
#
# The default folder is the library's own: Path.Combine(Path.GetTempPath(), 'Trustsoft.NotifyIcon'),
# which ToastImageFile.DefaultFolder fixes. A caller that launches the observed process with a
# different TEMP/TMP must pass -TempFolder explicitly, so the observer and the library agree on which
# folder they are talking about.

param(
    [int]$Seconds = 18,
    [double]$IntervalSeconds = 1.0,
    [string]$TempFolder = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

if ($Seconds -le 0) {
    throw "-Seconds must be positive, got $Seconds."
}

if ($IntervalSeconds -le 0) {
    throw "-IntervalSeconds must be positive, got $IntervalSeconds."
}

if ([string]::IsNullOrEmpty($TempFolder)) {
    $TempFolder = Join-Path ([System.IO.Path]::GetTempPath()) 'Trustsoft.NotifyIcon'
}

Write-Output ("observer: watching '{0}' for {1}s, one tick every {2}s (a separate process; the library is not loaded here)" -f $TempFolder, $Seconds, $IntervalSeconds)

$start = Get-Date

# The first tick is taken immediately, so a caller that starts this observer and the observed process
# together sees the folder's state before the first show as well as after the teardown.
while ($true) {
    $elapsed = ((Get-Date) - $start).TotalSeconds
    $exists = Test-Path -LiteralPath $TempFolder -PathType Container
    $files = @()

    if ($exists) {
        $files = @(Get-ChildItem -LiteralPath $TempFolder -Filter 'toast-*.png' -File -ErrorAction SilentlyContinue)
    }

    $reading = ($files | ForEach-Object { "{0} bytes={1}" -f $_.Name, $_.Length }) -join ', '

    Write-Output ("observer: t+{0:0.0}s folder='{1}' exists={2} files={3} present={4} [{5}]" -f `
        $elapsed, $TempFolder, $exists, $files.Count, ($files.Count -gt 0), $reading)

    if ($elapsed -ge $Seconds) {
        break
    }

    Start-Sleep -Milliseconds ([int]($IntervalSeconds * 1000))
}

Write-Output ("observer: complete at {0:HH:mm:ss.fff}" -f (Get-Date))
exit 0
