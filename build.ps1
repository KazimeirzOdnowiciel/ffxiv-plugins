param(
    [string] $BaseUrl = "https://raw.githubusercontent.com/KazimeirzOdnowiciel/ffxiv-plugins/main",
    [switch] $NoBuild
)

# Rebuilds every plugin, copies its release zip into dist/, and regenerates repo.json.
# Run from anywhere: paths are relative to this script.

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$dist = Join-Path $root "dist"

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$plugins = @(
    @{ Folder = "SlotYield"; Project = "SlotYield.csproj" },
    @{ Folder = "RouletteBingo"; Project = "RouletteBingo.csproj" },
    @{ Folder = "CritSound"; Project = "CritSound.csproj" },
    @{ Folder = "EnemyListCastBars"; Project = "EnemyListCastBars.csproj" },
    @{ Folder = "DesynthLens"; Project = "DesynthLens.csproj" },
    @{ Folder = "MechanicWatch"; Project = "MechanicWatch.csproj" },
    @{ Folder = "GilLedger"; Project = "GilLedger.csproj" },
    @{ Folder = "DutyRecap"; Project = "DutyRecap.csproj" }
)

# Plugin sources live inside this repository under src/ so the repo is
# self-contained; EnemyListCastBars still builds from the Dalamud dev-plugin
# folder via its explicit Root.
$parent = Join-Path $root "src"
$entries = @()

foreach ($plugin in $plugins)
{
    $base = if ($plugin.ContainsKey("Root")) { $plugin.Root } else { $parent }
    $folder = Join-Path $base $plugin.Folder
    $project = Join-Path $folder $plugin.Project
    $name = [System.IO.Path]::GetFileNameWithoutExtension($plugin.Project)

    if (-not (Test-Path $project))
    {
        Write-Warning "missing project: $project"
        continue
    }

    if (-not $NoBuild)
    {
        Write-Host "building $name..."
        & dotnet build $project -c Release -v q | Out-Null

        if ($LASTEXITCODE -ne 0)
        {
            throw "build failed for $name"
        }
    }

    $output = Join-Path $folder "bin\Release\$name"
    $zip = Join-Path $output "latest.zip"
    $manifestPath = Join-Path $output "$name.json"

    if (-not (Test-Path $zip) -or -not (Test-Path $manifestPath))
    {
        Write-Warning "no package output for $name"
        continue
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $target = Join-Path $dist "$($manifest.InternalName)-$($manifest.AssemblyVersion).zip"

    Copy-Item $zip $target -Force
    Write-Host "  packaged $($manifest.InternalName).zip ($([math]::Round((Get-Item $target).Length / 1kb)) KB)"

    # Version-tagged so a CDN cannot hand back a previous release or a missing icon.
    # Version in the path: a CDN cannot serve a previous release for a new version.
    $tag = $manifest.AssemblyVersion
    $link = "$BaseUrl/dist/$($manifest.InternalName)-$tag.zip"

    $iconPath = Join-Path $root "icons/$($manifest.InternalName).png"
    $iconUrl = ""

    if (Test-Path $iconPath)
    {
        New-Item -ItemType Directory -Force -Path (Join-Path $dist "icons") | Out-Null
        Copy-Item $iconPath (Join-Path $dist "icons/$($manifest.InternalName)-$tag.png") -Force
        $iconUrl = "$BaseUrl/dist/icons/$($manifest.InternalName)-$tag.png"
    }

    # The installed-plugin list reads the manifest inside the zip, not the repo entry, so the icon
    # has to live in there too or it vanishes the moment the plugin is installed.
    if ($iconUrl -ne "")
    {
        Add-Type -AssemblyName System.IO.Compression.FileSystem

        $staging = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $staging | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($target, $staging)

        $innerManifest = Join-Path $staging "$($manifest.InternalName).json"

        if (Test-Path $innerManifest)
        {
            $inner = Get-Content $innerManifest -Raw | ConvertFrom-Json
            $inner | Add-Member -NotePropertyName IconUrl -NotePropertyValue $iconUrl -Force
            $innerJson = ConvertTo-Json -InputObject $inner -Depth 6
            [System.IO.File]::WriteAllText($innerManifest, $innerJson, (New-Object System.Text.UTF8Encoding($false)))

            Remove-Item $target -Force
            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $staging, $target, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        }

        Remove-Item $staging -Recurse -Force
    }

    $entries += [ordered]@{
        Author             = $manifest.Author
        Name               = $manifest.Name
        InternalName       = $manifest.InternalName
        AssemblyVersion    = $manifest.AssemblyVersion
        Description        = $manifest.Description
        ApplicableVersion  = $manifest.ApplicableVersion
        RepoUrl            = $manifest.RepoUrl
        Tags               = $manifest.Tags
        DalamudApiLevel    = $manifest.DalamudApiLevel
        LoadRequiredState  = 0
        LoadSync           = $false
        CanUnloadAsync     = $false
        LoadPriority       = 0
        Punchline          = $manifest.Punchline
        AcceptsFeedback    = $true
        DownloadLinkInstall = $link
        DownloadLinkUpdate  = $link
        DownloadLinkTesting = $link
        IconUrl            = $iconUrl
        IsHide             = $false
        IsTestingExclusive = $false
        LastUpdate         = [int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    }
}

if ($entries.Count -eq 0)
{
    throw "nothing was packaged"
}

$json = ConvertTo-Json -InputObject $entries -Depth 6
[System.IO.File]::WriteAllText((Join-Path $root "repo.json"), $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "repo.json written with $($entries.Count) plugin(s), links pointing at $BaseUrl"
