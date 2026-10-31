# Mountbook dev-time dataset generator.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/generate-mount-sources.ps1
#
# Pulls every mount from ffxivcollect.com, keeps only the fields the plugin needs (id, patch,
# item_id, the community ownership fraction, the tradeable / own-music flags and the obtain
# sources) and writes src/Mountbook/Data/mount-sources.json. The plugin
# never calls this API at runtime: the JSON is committed and embedded into the assembly
# (CONTRIBUTING.md N2). Re-run this only when the game adds mounts or the site corrects data,
# then commit the regenerated file.
#
# Two facts about the live endpoint (checked 2026-10-30):
#
#   * `page` is ignored. The upstream controller ends in `.distinct.limit(params[:limit])` with
#     no `.page(...)`, so every page returns the same rows. The loop below therefore pages for
#     forward compatibility but stops as soon as a page adds no new mount ids.
#   * the response is already narrowed to the dataset's "available" rows (`patch IS NOT NULL`),
#     which is currently 356 mounts. A large page size fetches them all in one request.
#
# The output is written explicitly as UTF-8 without a BOM and with LF line endings so the
# committed file does not churn on checkout (.editorconfig: end_of_line = lf).

[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\Data\mount-sources.json'),
    [int] $PageSize = 1000,
    [int] $MaxPages = 40,
    [int] $TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiUrl = 'https://ffxivcollect.com/api/mounts'
$scriptRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
}
else {
    Join-Path $scriptRoot $OutputPath
}

function ConvertTo-JsonText {
    param([string] $Value)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($character in $Value.ToCharArray()) {
        $code = [int] $character
        if ($code -eq 0x22) {
            [void]$builder.Append('\"')
        }
        elseif ($code -eq 0x5C) {
            [void]$builder.Append('\\')
        }
        elseif ($code -eq 0x08) {
            [void]$builder.Append('\b')
        }
        elseif ($code -eq 0x0C) {
            [void]$builder.Append('\f')
        }
        elseif ($code -eq 0x0A) {
            [void]$builder.Append('\n')
        }
        elseif ($code -eq 0x0D) {
            [void]$builder.Append('\r')
        }
        elseif ($code -eq 0x09) {
            [void]$builder.Append('\t')
        }
        elseif ($code -lt 0x20) {
            [void]$builder.Append(('\u{0:x4}' -f $code))
        }
        else {
            [void]$builder.Append($character)
        }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-MountPage {
    param([int] $Page)

    $uri = '{0}?limit={1}&page={2}' -f $apiUrl, $PageSize, $Page
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $TimeoutSeconds `
                -Headers @{ 'User-Agent' = 'Mountbook-dataset-generator/1.0' }
        }
        catch {
            if ($attempt -eq 4) {
                throw "ffxivcollect page $Page failed after 4 attempts: $($_.Exception.Message)"
            }
            $delay = 3 * $attempt
            Write-Warning ("page {0} attempt {1} failed ({2}); retrying in {3}s" -f $Page, $attempt, $_.Exception.Message, $delay)
            Start-Sleep -Seconds $delay
        }
    }
}

Write-Host "Fetching mounts from $apiUrl (limit $PageSize) ..."
$byId = @{}
$page = 1
while ($page -le $MaxPages) {
    $response = Invoke-MountPage -Page $page
    if ($null -eq $response -or $null -eq $response.results) {
        Write-Host ("page {0}: no results, stopping" -f $page)
        break
    }

    $results = @($response.results)
    if ($results.Count -eq 0) {
        Write-Host ("page {0}: no results, stopping" -f $page)
        break
    }

    $new = 0
    foreach ($result in $results) {
        if ($null -eq $result.id) {
            continue
        }

        $id = [uint32] $result.id
        if ($id -eq 0 -or $byId.ContainsKey($id)) {
            continue
        }

        $byId[$id] = $result
        $new++
    }

    Write-Host ("page {0}: {1} rows ({2} new, {3} total)" -f $page, $results.Count, $new, $byId.Count)
    if ($new -eq 0) {
        Write-Warning ("page {0} repeated an earlier page; the endpoint is ignoring 'page', so paging stops here." -f $page)
        break
    }
    if ($results.Count -lt $PageSize) {
        Write-Host ("page {0}: short page, stopping" -f $page)
        break
    }
    $page++
}

$mounts = @($byId.Values | Sort-Object -Property id)
$generated = (Get-Date).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('{')
$lines.Add(('  "generated": {0},' -f (ConvertTo-JsonText $generated)))
$lines.Add(('  "source": {0},' -f (ConvertTo-JsonText 'ffxivcollect.com')))
$lines.Add('  "mounts": [')

$mountIndex = 0
$sourceCount = 0
foreach ($mount in $mounts) {
    $mountIndex++
    $mountLast = $mountIndex -eq $mounts.Count
    $patch = ([string] $mount.patch).Trim()
    $itemId = if ($null -eq $mount.item_id) { 0 } else { [uint32] $mount.item_id }

    $lines.Add('    {')
    $lines.Add(('      "id": {0},' -f [uint32] $mount.id))
    $lines.Add(('      "patch": {0},' -f (ConvertTo-JsonText $patch)))
    $lines.Add(('      "itemId": {0},' -f $itemId))

    # The three fields ffxivcollect carries but this dataset did not: how many collectors own the
    # mount ("0.3%"), and whether it is tradeable / has its own music. Each is written as null when
    # the site omits it, so the reader can tell "unknown" from "false" - a wrong "tradeable" badge is
    # worse than no badge.
    $ownedText = if ($mount.PSObject.Properties['owned']) { ([string] $mount.owned).Trim().TrimEnd('%') } else { '' }
    $ownedValue = 0.0
    $ownedJson = if (
        $ownedText.Length -gt 0 -and
        [double]::TryParse($ownedText, [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref] $ownedValue)) {
        $ownedValue.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        'null'
    }
    $lines.Add(('      "owned": {0},' -f $ownedJson))

    $tradeable = if ($mount.PSObject.Properties['tradeable'] -and $null -ne $mount.tradeable) {
        if ([bool] $mount.tradeable) { 'true' } else { 'false' }
    }
    else {
        'null'
    }
    $lines.Add(('      "tradeable": {0},' -f $tradeable))

    $ownMusic = if ($mount.PSObject.Properties['custom_music'] -and $null -ne $mount.custom_music) {
        if ([bool] $mount.custom_music) { 'true' } else { 'false' }
    }
    else {
        'null'
    }
    $lines.Add(('      "customMusic": {0},' -f $ownMusic))

    $sourceLines = New-Object System.Collections.Generic.List[string]
    foreach ($source in @($mount.sources)) {
        if ($null -eq $source) {
            continue
        }

        $text = ([string] $source.text).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $relatedType = ([string] $source.related_type).Trim()
        $relatedTypeJson = if ($relatedType.Length -eq 0) { 'null' } else { ConvertTo-JsonText $relatedType }
        $relatedId = if ($null -eq $source.related_id) { 0 } else { [uint32] $source.related_id }
        $sourceLines.Add(
            ('        {{ "type": {0}, "text": {1}, "relatedType": {2}, "relatedId": {3} }}' -f `
                    (ConvertTo-JsonText ([string] $source.type).Trim()),
                (ConvertTo-JsonText $text),
                $relatedTypeJson,
                $relatedId))
        $sourceCount++
    }

    if ($sourceLines.Count -eq 0) {
        $lines.Add('      "sources": []')
    }
    else {
        $lines.Add('      "sources": [')
        for ($i = 0; $i -lt $sourceLines.Count; $i++) {
            $separator = if ($i -lt $sourceLines.Count - 1) { ',' } else { '' }
            $lines.Add($sourceLines[$i] + $separator)
        }
        $lines.Add('      ]')
    }

    $lines.Add($(if ($mountLast) { '    }' } else { '    },' }))
}

$lines.Add('  ]')
$lines.Add('}')
$json = ($lines -join "`n") + "`n"

$directory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($resolvedOutput, $json, $utf8WithoutBom)

$sizeKb = [math]::Round((Get-Item -LiteralPath $resolvedOutput).Length / 1KB, 1)
Write-Host ("Wrote {0} mounts ({1} source lines) to {2} ({3} KB)" -f `
        $mounts.Count, $sourceCount, $resolvedOutput, $sizeKb)

if ($mounts.Count -lt 300) {
    Write-Warning ("Only {0} mounts were written; the ffxivcollect dataset normally carries at least 350." -f $mounts.Count)
}
