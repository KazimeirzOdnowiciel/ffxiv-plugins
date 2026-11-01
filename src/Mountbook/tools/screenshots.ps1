# Mountbook listing screenshots: the five images in docs/screenshots, from the preview scenes.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/screenshots.ps1
#
# One command, because a screenshot that only some remembered flags can reproduce decays into a lie.
# It builds the previews assembly if it is stale, renders each scene named below on WARP with the
# same face the game's UI uses, writes docs/screenshots/<name>.png, and then proves every file is
# the size pinned in this script - a scene that changes size fails the command instead of quietly
# landing a different-shaped image in the listing.
#
#   -Check          render into a scratch folder and compare with what is committed; nothing is
#                   written to docs/screenshots. Exits 1 if any image is stale, with the names, so
#                   this is the form a gate can call.
#   -Scene <name>   render one scene of the five (the rest are left alone).
#   -NoBuild        do not build the previews project first; use the assembly in bin/Release as-is.
#   -Tool <file>    the glasswork-preview executable (default: %USERPROFILE%\.dotnet\tools).
#   -Font <file>    the face to rasterise with (default: Segoe UI, the system UI face).
#
# The images are not the goldens. A golden renders with ImGui's default font, whose 0x20-0xFF range
# has no ellipsis, so a truncated mount name draws as "Auspicio?" - fine as a pixel baseline, wrong
# on a listing page. WARP is deliberate for the other half of the same reason: a hardware adapter
# rasterises the same pixels on this machine and a slightly different set on the next one, and these
# files are committed. See docs/screenshots.md for the sizes and the rest of the reasoning.

[CmdletBinding()]
param(
    [switch] $Check,
    [string] $Scene,
    [switch] $NoBuild,
    [string] $Tool = (Join-Path $env:USERPROFILE '.dotnet\tools\glasswork-preview.exe'),
    [string] $Font = (Join-Path $env:WINDIR 'Fonts\segoeui.ttf')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$pluginDir = Split-Path -Parent $toolsDir
$root = Split-Path -Parent (Split-Path -Parent $pluginDir)

$previewsProject = Join-Path $root 'src\Mountbook.Previews\Mountbook.Previews.csproj'
$assembly = Join-Path $root 'src\Mountbook.Previews\bin\Release\Mountbook.Previews.dll'
$screenshots = Join-Path $root 'docs\screenshots'

# Scene -> file, and the size it must come out at. The size is the scene's own: the two columns of the
# book at the flagship window, the settings window as tall as its content, the grid with its group
# headings. Pinned here so a layout change cannot rename or reshape a listing image without this line
# moving with it.
$shots = @(
    [pscustomobject]@{ Scene = 'main';   File = 'card-grid.png';   Size = '1088x708'; About = 'the card grid at the flagship window, with a mount selected' }
    [pscustomobject]@{ Scene = 'table';  File = 'table.png';       Size = '1088x708'; About = 'the same book as a sortable table' }
    [pscustomobject]@{ Scene = 'price';  File = 'detail-pane.png'; Size = '1088x708'; About = 'the detail pane: badges, market price, how to obtain' }
    [pscustomobject]@{ Scene = 'config'; File = 'config.png';      Size = '608x1108'; About = 'the settings window, whole' }
    [pscustomobject]@{ Scene = 'grouped'; File = 'grouped.png';     Size = '1088x948'; About = 'the same book grouped by expansion' }
)

if ($Scene) {
    $shots = @($shots | Where-Object { $_.Scene -eq $Scene })
    if ($shots.Count -eq 0) {
        throw "-Scene '$Scene' is not one of the listing scenes; pick from main, table, price, config, grouped."
    }
}

if (-not (Test-Path -LiteralPath $Tool)) {
    throw "No preview tool at $Tool. Install it (dotnet tool install --global glasswork.preview) or pass -Tool."
}

if (-not (Test-Path -LiteralPath $Font)) {
    throw "No font at $Font. Pass -Font with a face that has real ellipsis and control glyphs."
}

$outDir = $screenshots
if ($Check) {
    $outDir = Join-Path ([System.IO.Path]::GetTempPath()) ('mountbook-shots-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

if (-not $NoBuild) {
    Write-Host '== build the previews assembly'
    & dotnet build $previewsProject -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $assembly)) {
    throw "No previews assembly at $assembly. Drop -NoBuild so the script builds it."
}

Add-Type -AssemblyName System.Drawing

$stale = @()
$written = @()

foreach ($shot in $shots) {
    $out = Join-Path $outDir $shot.File
    Write-Host ''
    Write-Host ("== {0} ({1})" -f $shot.File, $shot.About) -ForegroundColor Cyan

    & $Tool --assembly $assembly --scene $shot.Scene --warp --font $Font --out $out
    if ($LASTEXITCODE -ne 0) {
        throw "glasswork-preview exited $LASTEXITCODE on scene '$($shot.Scene)'."
    }

    $image = [System.Drawing.Image]::FromFile($out)
    try {
        $size = '{0}x{1}' -f $image.Width, $image.Height
    }
    finally {
        $image.Dispose()
    }

    if ($size -ne $shot.Size) {
        throw "$($shot.File) came out $size, but this script pins $($shot.Size). The scene's layout moved: check it, then move the pinned size with it and re-run."
    }

    $kb = [math]::Round((Get-Item -LiteralPath $out).Length / 1kb)
    Write-Host ("  {0}  {1}  {2} KB" -f $shot.File, $size, $kb)
    $written += $shot.File

    if ($Check) {
        $committed = Join-Path $screenshots $shot.File
        if (-not (Test-Path -LiteralPath $committed)) {
            $stale += "$($shot.File) (missing)"
        }
        elseif ((Get-FileHash -LiteralPath $committed).Hash -ne (Get-FileHash -LiteralPath $out).Hash) {
            $stale += "$($shot.File) (differs)"
        }
    }
}

Write-Host ''
if ($Check) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
    if ($stale.Count -gt 0) {
        Write-Host ("{0} of {1} listing image(s) are not what the scenes draw now:" -f $stale.Count, $written.Count) -ForegroundColor Red
        foreach ($entry in $stale) {
            Write-Host "  $entry" -ForegroundColor Red
        }
        Write-Host 'Re-run this script without -Check and commit the changed images.' -ForegroundColor Red
        exit 1
    }

    Write-Host ("all {0} listing image(s) match the scenes" -f $written.Count) -ForegroundColor Green
    exit 0
}

Write-Host ("wrote {0} listing image(s) to {1}" -f $written.Count, $screenshots) -ForegroundColor Green
Write-Host 'Look at them before you commit: this command proves the size, not that the layout is good.'
exit 0
