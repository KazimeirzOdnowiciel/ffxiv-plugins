# Mountbook patch-day: regenerate the obtain snapshot and prove it before it ships.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/patch-day.ps1
#
# One command, because a patch day is the worst moment to remember a checklist: it regenerates
# src/Mountbook/Data/mount-sources.json into a scratch file, validates it against the committed
# snapshot and the game's Mount sheet, and only replaces the committed file when nothing is wrong.
# A failed validation leaves the working tree exactly as it was, so "the site was down" cannot
# become a committed dataset.
#
#   -NoWrite        validate what is there and change nothing (the rehearsal / CI path)
#   -SkipRegenerate validate the committed snapshot as-is; no network, no ffxivcollect
#   -SkipSheet      skip the Mount sheet ids check (see -SheetIds / -ModelMap)
#   -SheetIds <f>   sheet ids: one mount id per line, or the mdlrip model map CSV
#   -ModelMap <f>   the mdlrip model map to read sheet ids from (default: the tools/mdlrip copy)
#   -ReportPath <f> where the markdown report goes (default: artifacts/patch-day/validation-<stamp>.md)
#
# The offline half of a patch day - goldens, changelog, version bump, build.ps1, the commit - is the
# runbook at docs/patch-day.md. This script deliberately stops before the first of those: a version
# bump is a decision, not a step.

[CmdletBinding()]
param(
    [string] $SheetIds,
    [string] $ModelMap = 'D:\Deepseek\tools\mdlrip\out\mount-model-map.csv',
    [switch] $SkipRegenerate,
    [switch] $SkipSheet,
    [switch] $NoWrite,
    [string] $ReportPath,
    [int] $AllowedShrink = 8,
    [int] $AllowedGrowth = 40,
    [int] $Minimum = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$pluginDir = Split-Path -Parent $toolsDir
$root = Split-Path -Parent (Split-Path -Parent $pluginDir)
$dataFile = Join-Path $pluginDir 'Data\mount-sources.json'
$generator = Join-Path $toolsDir 'generate-mount-sources.ps1'
$toolProject = Join-Path $root 'src\Mountbook.Tools\Mountbook.Tools.csproj'

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor Cyan
}

function Stop-WithError {
    param([string] $Text)
    Write-Host ''
    Write-Host "patch-day: $Text" -ForegroundColor Red
    exit 2
}

foreach ($required in @($dataFile, $generator, $toolProject)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Stop-WithError "missing $required - run this from the repository checkout."
    }
}

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) "mountbook-patchday-$stamp"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

if (-not $ReportPath) {
    $ReportPath = Join-Path $root "artifacts\patch-day\validation-$stamp.md"
}

# The baseline is the snapshot that is committed right now: whatever this run replaces. It is the only
# reference that can answer "what changed", so it is captured before anything touches the data file.
$baseline = Join-Path $scratch 'baseline.json'
Copy-Item -LiteralPath $dataFile -Destination $baseline -Force

Write-Step "Regenerating the snapshot"
$candidate = Join-Path $scratch 'candidate.json'

if ($SkipRegenerate) {
    Write-Host '  skipped (-SkipRegenerate): validating the committed file as it stands'
    Copy-Item -LiteralPath $dataFile -Destination $candidate -Force
}
else {
    Write-Host "  ffxivcollect.com -> $candidate"
    try {
        & $generator -OutputPath $candidate
    }
    catch {
        Stop-WithError "the generator failed ($($_.Exception.Message)); $dataFile is untouched."
    }
}

if (-not (Test-Path -LiteralPath $candidate)) {
    Stop-WithError "the generator wrote nothing; $dataFile is untouched."
}

Write-Step 'Resolving the Mount sheet ids'
$sheetPath = $null

if ($SheetIds) {
    if (-not (Test-Path -LiteralPath $SheetIds)) {
        Stop-WithError "no sheet ids at $SheetIds"
    }

    $sheetPath = $SheetIds
    Write-Host "  using $SheetIds"
}
elseif ($SkipSheet) {
    Write-Host '  skipped (-SkipSheet): ids are checked for shape only'
}
elseif (Test-Path -LiteralPath $ModelMap) {
    # The model map is one row per Mount sheet row, so its mount_id column is the sheet. Converting it
    # here keeps the validator's input contract trivial (one id per line) and the CSV shape in one place.
    $sheetPath = Join-Path $scratch 'sheet-ids.txt'
    $ids = @(Import-Csv -LiteralPath $ModelMap | ForEach-Object { $_.mount_id })
    if ($ids.Count -eq 0) {
        Stop-WithError "the model map at $ModelMap has no mount_id column."
    }

    [System.IO.File]::WriteAllLines($sheetPath, [string[]] $ids)
    Write-Host "  $($ids.Count) ids from $ModelMap"

    # A map older than the snapshot cannot know the patch's new mounts, so it would pass a mount the
    # game has not published - the exact failure the sheet check exists for.
    if ((Get-Item -LiteralPath $ModelMap).LastWriteTime -lt (Get-Item -LiteralPath $dataFile).LastWriteTime) {
        Write-Host '  WARNING: that map is older than the snapshot. Refresh it first:' -ForegroundColor Yellow
        Write-Host '           cd D:\Deepseek\tools\mdlrip; dotnet run -c Release -- --resolve' -ForegroundColor Yellow
    }
}
else {
    Write-Host "  no sheet ids: $ModelMap not found and -SheetIds not given (-SkipSheet to silence)" -ForegroundColor Yellow
}

Write-Step 'Validating'
& dotnet build $toolProject -c Release -v q | Out-Host
if ($LASTEXITCODE -ne 0) {
    Stop-WithError 'the validator project did not build.'
}

$validatorArgs = @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'validate',
    '--candidate', $candidate,
    '--baseline', $baseline,
    '--report', $ReportPath,
    '--allowed-shrink', $AllowedShrink,
    '--allowed-growth', $AllowedGrowth,
    '--minimum', $Minimum
)

if ($sheetPath) {
    $validatorArgs += @('--sheet-ids', $sheetPath)
}

& dotnet @validatorArgs
$status = $LASTEXITCODE

if ($status -eq 2) {
    Stop-WithError "the validator could not run (see above); $dataFile is untouched."
}

if ($status -ne 0) {
    Write-Host ''
    Write-Host 'patch-day: FAILED - the new snapshot must not ship.' -ForegroundColor Red
    Write-Host "  committed dataset: UNCHANGED ($dataFile)"
    Write-Host "  rejected snapshot:  $candidate"
    Write-Host "  report:             $ReportPath"
    Write-Host '  Read the report, fix the source or the generator, then run this again. Table of what'
    Write-Host '  each code means: docs/patch-day.md.'
    exit 1
}

Write-Step 'Result'
if ($NoWrite) {
    Write-Host '  valid, but -NoWrite: the committed dataset is unchanged.' -ForegroundColor Yellow
}
elseif ($SkipRegenerate) {
    Write-Host '  valid: the committed dataset already passes; nothing to replace.'
}
else {
    Copy-Item -LiteralPath $candidate -Destination $dataFile -Force
    Write-Host "  wrote $dataFile"
}

Write-Host "  report: $ReportPath"
Write-Host '  scratch: ' -NoNewline
Write-Host $scratch -ForegroundColor DarkGray

if (-not $NoWrite -and -not $SkipRegenerate) {
    Write-Host ''
    Write-Host 'Next (docs/patch-day.md): goldens -> tests/format -> changelog + version -> build.ps1 ->'
    Write-Host 'commit dist/ + repo.json together.'
}

exit 0
