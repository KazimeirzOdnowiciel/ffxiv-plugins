param(
    [string] $BaseUrl = "https://raw.githubusercontent.com/YOURNAME/ffxiv-plugins/main",
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
    @{ Folder = "EnemyListCastBars"; Project = "EnemyListCastBars.csproj"; Root = "$env:LOCALAPPDATA\XIVLauncher\devPlugins\sdsd" }
)

$parent = Split-Path $root -Parent
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
    $target = Join-Path $dist "$($manifest.InternalName).zip"

    Copy-Item $zip $target -Force
    Write-Host "  packaged $($manifest.InternalName).zip ($([math]::Round((Get-Item $target).Length / 1kb)) KB)"

    $link = "$BaseUrl/dist/$($manifest.InternalName).zip"

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
        IconUrl            = ""
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
