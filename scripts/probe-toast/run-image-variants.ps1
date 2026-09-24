# M002/S04/T01 instrument: measure the toast image contract live, with the independent probe.
#
# The probe (Program.cs) is a hand-written WinRT program that is absent from Trustsoft.NotifyIcon.sln
# and takes no project reference to the library, so it stays an independent oracle: what it reports is
# a fact about the shell and the WinRT runtime, not about the library's own code. This runner creates
# the test images it needs, drives the four variants the S04 plan names - no-id / id=1 / missing-file /
# delete-at-t+2s - tees every [probe] line to a per-variant log, and finally reads a COPY of the
# notification platform's database to see whether the file:/// reference (rather than the image bytes)
# was stored. The live database is never opened for write and never read in place.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run-image-variants.ps1
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run-image-variants.ps1 -WaitSeconds 6
#
# Exit code: 0 when every variant printed the readings it is expected to print; non-zero when one is
# missing. The wpndatabase reading is reported, never asserted - its value is itself the measurement.
#
# Note on the non-ASCII folder name: Windows PowerShell 5.1 reads a .ps1 file without a byte-order
# mark as the ANSI code page, so a non-ASCII literal would depend on this file's encoding. The
# characters are therefore built from code points - 0x00E4/0x00F6/0x00FC are a-umlaut, o-umlaut,
# u-umlaut - and the script stays correct under any encoding.

param(
    [string]$Aumid = 'Trustsoft.NotifyIcon.ToastProbe.Image',
    [int]$WaitSeconds = 6,
    [int]$DeleteAfterSeconds = 2,
    [string]$WorkRoot = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

if ($WaitSeconds -le $DeleteAfterSeconds) {
    throw "-WaitSeconds ($WaitSeconds) must exceed -DeleteAfterSeconds ($DeleteAfterSeconds) so the delete-while-live variant actually fires."
}

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
    $tempRoot = $env:TEMP
    if ([string]::IsNullOrEmpty($tempRoot) -or $tempRoot -notmatch '^[A-Za-z]:[\\/]') {
        # A stripped or POSIX-flavoured TEMP (for example '/tmp' inherited from a non-Windows shell) is
        # not a Windows path; fall back to the platform's own temp directory so the work root stays
        # predictable instead of resolving under the current drive's root.
        $tempRoot = [System.IO.Path]::GetTempPath()
    }
    $WorkRoot = Join-Path $tempRoot 'probe-image-variants'
}
$NonAscii = [string]([char]0x00E4) + [char]0x00F6 + [char]0x00FC
$SpaceFolder = Join-Path $WorkRoot ("image variants " + $NonAscii)
New-Item -ItemType Directory -Force -Path $WorkRoot, $SpaceFolder | Out-Null

$script:variantFailures = New-Object 'System.Collections.Generic.List[string]'

function New-TestPng {
    param([string]$Path, [int]$Width, [int]$Height, [int]$Red, [int]$Green, [int]$Blue)
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([System.Drawing.Color]::FromArgb(255, $Red, $Green, $Blue)) } finally { $graphics.Dispose() }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

function Invoke-Variant {
    param([string]$Name, [string[]]$ProbeArgs, [string[]]$Expected)
    $logPath = Join-Path $WorkRoot ("variant-$Name.log")
    Write-Output ''
    Write-Output ("=== variant: {0} ===" -f $Name)
    Write-Output ("runner: {0} {1}" -f $probeCommand, ((@($probePrefix) + $ProbeArgs) -join ' '))

    $lines = @(& $probeCommand @probePrefix @ProbeArgs 2>&1)
    $exit = $LASTEXITCODE
    $lines | ForEach-Object { Write-Output $_ }
    $lines | Set-Content -Path $logPath -Encoding UTF8
    Write-Output ("runner: variant {0} exit={1} log={2}" -f $Name, $exit, $logPath)

    if ($exit -ne 0) {
        $script:variantFailures.Add("$Name`: probe exit code $exit")
        return
    }

    foreach ($pattern in $Expected) {
        if (-not ($lines | Select-String -Pattern $pattern -Quiet)) {
            $script:variantFailures.Add("$Name`: expected reading is missing: /$pattern/")
        }
    }
}

$machine = $env:COMPUTERNAME
if ([string]::IsNullOrEmpty($machine)) { $machine = [Environment]::MachineName }
Write-Output ("probe image variants: start {0:yyyy-MM-dd HH:mm:ss} on {1}; wait-seconds={2}; delete-after={3}s" -f (Get-Date), $machine, $WaitSeconds, $DeleteAfterSeconds)

# The probe registers this shortcut on every show and removes nothing automatically: a run leaves it
# in place, so the capture names it rather than pretending the run was side-effect free.
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Trustsoft.NotifyIcon.ToastProbe.lnk'
Write-Output ("runner: the probe registers '{0}' when it shows a toast; this runner does not remove it." -f $shortcut)

# ---- clean slate ---------------------------------------------------------------
& $probeCommand @probePrefix --clear-history $Aumid | Out-Null

# ---- test images ---------------------------------------------------------------
$heroPng = Join-Path $WorkRoot 'toast-hero.png'
$spacePng = Join-Path $SpaceFolder 'toast-applogo.png'
$missingPng = Join-Path $WorkRoot 'this-image-was-never-created.png'

New-TestPng -Path $heroPng -Width 364 -Height 180 -Red 20 -Green 140 -Blue 90
New-TestPng -Path $spacePng -Width 48 -Height 48 -Red 200 -Green 60 -Blue 20
if (Test-Path $missingPng) { Remove-Item $missingPng -Force }

foreach ($image in @($heroPng, $spacePng)) {
    $bytes = [System.IO.File]::ReadAllBytes($image)
    $magic = ($bytes[0..7] | ForEach-Object { $_.ToString('X2') }) -join ' '
    Write-Output ("runner: created '{0}' bytes={1} magic={2}" -f $image, $bytes.Length, $magic)
}
Write-Output ("runner: the missing-file variant points at '{0}' (exists={1})" -f $missingPng, (Test-Path $missingPng))

# ---- the four variants ---------------------------------------------------------
# Every variant must print the LoadXml acceptance, the CreateToastNotification result, the Show result
# and the aggregate result line. What differs between variants is the image element and the file state,
# and those are asserted per variant so a silently different payload cannot pass.
$imageAccepted = @(
    '\[probe\] show: IXmlDocumentIO\.LoadXml hr=0x00000000',
    '\[probe\] show: CreateToastNotification\(xml\) hr=0x00000000',
    '\[probe\] show: notifier\.Show\(toast\) hr=0x00000000',
    '\[probe\] result: activated=\d+ .* failed=\d+ errorCode=0x[0-9A-F]{8}'
)

Invoke-Variant -Name 'no-id' -ProbeArgs @(
    '--aumid', $Aumid, '--wait-seconds', "$WaitSeconds",
    '--image', $heroPng, '--image-placement', 'hero'
) -Expected ($imageAccepted + @(
    '\[probe\] show: payload xml=.*<image src="file:///[^"]+" placement="hero"/>.*</binding>',
    '\[probe\] image: at send exists=True bytes=\d+'
))

Invoke-Variant -Name 'id-1-applogo-space-nonascii' -ProbeArgs @(
    '--aumid', $Aumid, '--wait-seconds', "$WaitSeconds",
    '--image', $spacePng, '--image-placement', 'appLogoOverride', '--image-id', '1'
) -Expected ($imageAccepted + @(
    '\[probe\] show: payload xml=.*<image src="file:///[^"]+" placement="appLogoOverride" id="1"/>.*</binding>',
    '\[probe\] image: at send exists=True bytes=\d+'
))

Invoke-Variant -Name 'missing-file' -ProbeArgs @(
    '--aumid', $Aumid, '--wait-seconds', "$WaitSeconds",
    '--image', $missingPng, '--image-placement', 'hero'
) -Expected ($imageAccepted + @(
    '\[probe\] show: payload xml=.*<image src="file:///[^"]+" placement="hero"/>.*</binding>',
    '\[probe\] image: at send exists=False bytes=\(absent\)'
))

Invoke-Variant -Name 'delete-at-t-plus-2s' -ProbeArgs @(
    '--aumid', $Aumid, '--wait-seconds', "$WaitSeconds",
    '--image', $heroPng, '--image-placement', 'hero', '--delete-image-after', "$DeleteAfterSeconds"
) -Expected ($imageAccepted + @(
    '\[probe\] image: at send exists=True bytes=\d+',
    "\[probe\] image: delete requested after ${DeleteAfterSeconds}s, deleted at t=[\d.]+s; after delete exists=False"
))

if (Test-Path $heroPng) {
    Write-Output ("runner: after the delete-while-live variant the image '{0}' still exists - the deletion did not stick" -f $heroPng)
    $script:variantFailures.Add('delete-at-t-plus-2s: the image file still exists after the run')
} else {
    Write-Output ("runner: independent check after the delete-while-live variant: '{0}' is absent" -f $heroPng)
}

# ---- out-of-process delivery inventory -----------------------------------------
# The image variants are judged on the payload readings above; this is the independent confirmation
# that the image-bearing payloads actually reached the platform, from a process other than the ones
# that showed them.
Write-Output ''
Write-Output '--- node: out-of-process delivery inventory ---'
$historyLines = @(& $probeCommand @probePrefix --history $Aumid 2>&1)
$historyLines | ForEach-Object { Write-Output $_ }
$historyCount = ($historyLines | Select-String -Pattern 'count=(\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }) | Select-Object -Last 1
Write-Output ("runner: history count for '{0}' after the four variants: {1} (a snapshot - the Action Center entry can be purged between runs, so a 0 here is a reading, not a variant failure)" -f $Aumid, $historyCount)

# ---- the notification platform's stored state ----------------------------------
# The question: does the platform store the file:/// REFERENCE (its own notification state) or copy the
# image BYTES (its downloaded-image cache)? Only a copy of the database is read; the live file is
# never opened for write and never read in place.
Write-Output ''
Write-Output '--- node: what a copy of the notification database holds ---'

$dbDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Notifications'
$copyDir = Join-Path $WorkRoot 'wpndatabase-copy'
New-Item -ItemType Directory -Force -Path $copyDir | Out-Null

$copies = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in @('wpndatabase.db', 'wpndatabase.db-wal')) {
    $live = Join-Path $dbDir $name
    if (-not (Test-Path $live)) {
        Write-Output ("dbcopy: '{0}' is absent" -f $live)
        continue
    }

    $destination = Join-Path $copyDir $name
    try {
        [System.IO.File]::Copy($live, $destination, $true)
        $copies.Add($destination)
        Write-Output ("dbcopy: copied '{0}' -> '{1}' bytes={2}" -f $live, $destination, (Get-Item $destination).Length)
    } catch {
        Write-Output ("dbcopy: copying '{0}' failed: {1}" -f $live, $_.Exception.Message)
    }
}

$needles = @(
    (New-Object System.Uri $heroPng).AbsoluteUri,
    (New-Object System.Uri $spacePng).AbsoluteUri,
    (Split-Path $heroPng -Leaf),
    (Split-Path $spacePng -Leaf)
)

$uriFound = $false
foreach ($copy in $copies) {
    $bytes = [System.IO.File]::ReadAllBytes($copy)
    $asLatin1 = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $asUtf8 = [System.Text.Encoding]::UTF8.GetString($bytes)
    $asUtf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    Write-Output ("dbcopy: '{0}' contains a file URI at all: latin1={1} utf8={2} utf16le={3}" -f `
        (Split-Path $copy -Leaf), $asLatin1.Contains('file:///'), $asUtf8.Contains('file:///'), $asUtf16.Contains('file:///'))
    foreach ($needle in $needles) {
        $latin1Hit = $asLatin1.Contains($needle)
        $utf8Hit = $asUtf8.Contains($needle)
        $utf16Hit = $asUtf16.Contains($needle)
        if ($latin1Hit -or $utf8Hit -or $utf16Hit) { $uriFound = $true }
        Write-Output ("dbcopy: '{0}' contains '{1}': latin1={2} utf8={3} utf16le={4}" -f `
            (Split-Path $copy -Leaf), $needle, $latin1Hit, $utf8Hit, $utf16Hit)
    }
}

Write-Output ("dbcopy: verdict - a variant's image reference is present in the database copy: {0}" -f $uriFound)

# The reference-versus-bytes question has a second, cheaper witness: the platform's own downloaded-image
# cache. A local file:/// image must leave it empty, because the shell renders the file from disk.
$downloadCache = Join-Path $dbDir 'wpnidm'
if (Test-Path $downloadCache) {
    $cached = @(Get-ChildItem $downloadCache -Force -Recurse -ErrorAction SilentlyContinue)
    Write-Output ("dbcopy: the downloaded-image cache '{0}' holds {1} entr{2} (a local file:/// image is rendered from disk, not cached)" -f `
        $downloadCache, $cached.Count, $(if ($cached.Count -eq 1) { 'y' } else { 'ies' }))
    $cached | Select-Object -First 10 | ForEach-Object { Write-Output ("dbcopy:   cached '{0}' bytes={1}" -f $_.FullName, $_.Length) }
} else {
    Write-Output ("dbcopy: the downloaded-image cache '{0}' is absent" -f $downloadCache)
}

Write-Output 'dbcopy: note - the banner painting the picture is NOT machine-visible from here; only a human can confirm the image rendered.'

# ---- verdict -------------------------------------------------------------------
Write-Output ''
if ($script:variantFailures.Count -gt 0) {
    Write-Output 'runner verdict: FAIL'
    foreach ($failure in $script:variantFailures) { Write-Output ("  - {0}" -f $failure) }
    exit 1
}

Write-Output 'runner verdict: PASS (every variant printed its expected readings)'
exit 0
