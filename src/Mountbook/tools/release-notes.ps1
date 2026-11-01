# Mountbook release notes: the changelog section for one version, printed for a GitHub release.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/release-notes.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/release-notes.ps1 -Version 0.2.0
#   ... -Version 0.2.0 > release-notes.md      then:  gh release create <tag> --title "Mountbook 0.2.0" -F release-notes.md
#
# The default version is <Version> in src/Mountbook/Mountbook.csproj, so the notes cannot describe a
# different build from the one being released: a bump and this command are the same version by
# construction. The section is printed verbatim - the changelog is the release text, and this writes
# nothing itself, so there is no second copy to keep in sync.
#
# Exit codes: 0 the section was printed, 1 there is no section for that version (the entry is written
# before the bump, so this is what catches a release that skipped it), 2 the file or the version is
# unreadable.

[CmdletBinding()]
param(
    [string] $Version,
    [string] $Changelog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$pluginDir = Split-Path -Parent $toolsDir
$root = Split-Path -Parent (Split-Path -Parent $pluginDir)

if (-not $Changelog) {
    $Changelog = Join-Path $root 'CHANGELOG.md'
}

if (-not (Test-Path -LiteralPath $Changelog)) {
    [Console]::Error.WriteLine("No changelog at $Changelog.")
    exit 2
}

if (-not $Version) {
    $project = Join-Path $pluginDir 'Mountbook.csproj'
    if (-not (Test-Path -LiteralPath $project)) {
        [Console]::Error.WriteLine("No project file at $project, so the version cannot be read; pass -Version.")
        exit 2
    }

    $match = [regex]::Match((Get-Content -LiteralPath $project -Raw), '<Version>([0-9][0-9.]*)</Version>')
    if (-not $match.Success) {
        [Console]::Error.WriteLine("No <Version> element in $project; pass -Version.")
        exit 2
    }

    $Version = $match.Groups[1].Value
}

# The changelog writes a version as three parts; the project file may carry the fourth (the revision
# Dalamud compares), so a version given either way finds the same section.
if ($Version -match '^([0-9]+\.[0-9]+\.[0-9]+)(?:\.[0-9]+)?$') {
    $Version = $Matches[1]
}
else {
    [Console]::Error.WriteLine("'$Version' is not a version the changelog can have a section for (expected major.minor.patch).")
    exit 2
}

$lines = [System.IO.File]::ReadAllLines($Changelog)
$heading = [regex]::new('^## ' + [regex]::Escape($Version) + ' \((?<status>unreleased|[0-9]{4}-[0-9]{2}-[0-9]{2})\)$')

$start = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($heading.IsMatch($lines[$i])) {
        $start = $i
        break
    }
}

if ($start -lt 0) {
    $known = ($lines | Where-Object { $_ -match '^## ' } | ForEach-Object { $_ -replace '^## ', '' }) -join '; '
    [Console]::Error.WriteLine("CHANGELOG.md has no section for $Version. It has: $known")
    [Console]::Error.WriteLine('Write the entry before the bump.')
    exit 1
}

# The section runs to the next level-two heading, or the end of the file.
$end = $lines.Length
for ($i = $start + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^## ') {
        $end = $i
        break
    }
}

$body = $lines[($start + 1)..($end - 1)]

# Blank lines at both ends are the gap between sections, not part of the notes.
$first = 0
while ($first -lt $body.Length -and [string]::IsNullOrWhiteSpace($body[$first])) { $first++ }
$last = $body.Length - 1
while ($last -ge $first -and [string]::IsNullOrWhiteSpace($body[$last])) { $last-- }

if ($last -lt $first) {
    [Console]::Error.WriteLine("The section for $Version is empty; a release note with nothing in it is a release nobody explained.")
    exit 1
}

$body[$first..$last]
exit 0
