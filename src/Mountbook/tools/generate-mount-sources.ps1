# Mountbook dev-time dataset generator.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/generate-mount-sources.ps1
#
# Pulls every mount from ffxivcollect.com, keeps only the metadata the plugin needs (id, patch,
# item_id, the community ownership fraction, the tradeable / own-music flags and the obtain
# sources) and writes src/Mountbook/Data/mount-sources.json. The plugin never calls this API at
# runtime: the JSON is committed and embedded into the assembly (CONTRIBUTING.md N2). Re-run this
# only when the game adds mounts or the site corrects data, then commit the regenerated file.
#
# What it deliberately does NOT take, and why (docs/data-provenance.md has the full reading):
#
#   * description / enhanced_description / tooltip / image / icon / bgm - the plugin reads the
#     game's own Mount and MountTransient sheets for that text, and the site's copy of it is
#     Square Enix's prose. Dropping it is also what keeps the file inside the size budget:
#     251 KB with the copy, 83 KB metadata only, budget 120 KB.
#   * anything else ffxivcollect adds to a record that is not one of the fields above.
#
# The snapshot carries `schemaVersion` (the shape this generator writes and MountShelf's reader
# understands), `attribution` + `attributionUrl` (the credit line the plugin shows and the
# notices repeat) and `enrichment` (`full` when the site answered, `sheet-only` when this run was
# offline), so a stale or unenriched file can be told apart from a fresh one without guessing.
#
# Two ways to run it:
#
#   * online (default): fetch ffxivcollect, and fail loudly - exit non-zero, nothing written -
#     when the API is down or answers with fewer than -Minimum rows. A partial or empty fetch must
#     never replace a good snapshot.
#   * -Offline: build the snapshot from the game's Mount sheet alone (the id list in the mdlrip
#     model map). The committed snapshot's rows are carried over and pruned to the ids the sheet
#     still has, so nothing is invented and nothing is lost; with no committed snapshot to carry
#     over it writes one bare row per sheet id instead. Either way the file says
#     "enrichment": "sheet-only", the run prints a red banner and the validator warns, so shipping
#     it is always a decision.
#
# The snapshot is an enrichment layer keyed by mount id, not the list of mounts: the plugin
# iterates the game's own Mount sheet, so an offline snapshot that carries 356 of its 450 rows is
# complete for what it does. -Offline therefore never invents rows for sheet ids the site has never
# listed (they have no obtain data to write) - it reports them instead.
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
    [int] $TimeoutSeconds = 30,
    [int] $Minimum = 300,
    [switch] $Offline,
    [string] $SheetIds,
    [string] $ModelMap = 'D:\Deepseek\tools\mdlrip\out\mount-model-map.csv',
    [string] $CarryOver,
    [int] $MaxBytes = 122880
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The shape this generator writes. MountShelf.CurrentSchemaVersion must match it and the validator
# fails the snapshot when it does not, so a plugin build and a snapshot can never drift apart
# silently.
$schemaVersion = 2
$apiUrl = 'https://ffxivcollect.com/api/mounts'
$attribution = 'Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.'
$attributionUrl = 'https://ffxivcollect.com'

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

# Reads one member without tripping StrictMode: a record from the site (or from an older snapshot)
# may simply not have it, and "absent" has to stay distinct from "empty".
function Get-MemberValue {
    param($Object, [string] $Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

# One normalized row, whatever it was read from: the API, or a committed snapshot being carried
# over. Booleans and the ownership percentage are kept as JSON literal text ('null' included) so the
# writer has one job and the "unknown vs false" distinction survives both paths.
function New-MountRow {
    param(
        [uint32] $Id,
        [string] $Patch = '',
        [uint32] $ItemId = 0,
        [string] $OwnedJson = 'null',
        [string] $TradeableJson = 'null',
        [string] $CustomMusicJson = 'null',
        [object[]] $Sources = @()
    )

    return [pscustomobject] @{
        Id              = $Id
        Patch           = $Patch
        ItemId          = $ItemId
        OwnedJson       = $OwnedJson
        TradeableJson   = $TradeableJson
        CustomMusicJson = $CustomMusicJson
        Sources         = @($Sources)
    }
}

function New-SourceRow {
    param(
        [string] $Type,
        [string] $Text,
        [string] $RelatedType = '',
        [uint32] $RelatedId = 0
    )

    return [pscustomobject] @{
        Type        = $Type
        Text        = $Text
        RelatedType = $RelatedType
        RelatedId   = $RelatedId
    }
}

function ConvertTo-PercentJson {
    param($Value)

    if ($null -eq $Value) {
        return 'null'
    }

    $text = ([string] $Value).Trim().TrimEnd('%').Trim()
    if ($text.Length -eq 0) {
        return 'null'
    }

    $number = 0.0
    if (-not [double]::TryParse(
            $text,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref] $number)) {
        return 'null'
    }

    return $number.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-BoolJson {
    param($Value)

    if ($null -eq $Value) {
        return 'null'
    }

    if ([bool] $Value) {
        return 'true'
    }

    return 'false'
}

# Normalizes a source array from either shape: the API's snake_case members, or the camelCase ones
# this generator writes (which is what a carried-over snapshot carries).
function ConvertTo-SourceRows {
    param($Sources)

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($source in @($Sources)) {
        if ($null -eq $source) {
            continue
        }

        $text = ([string] (Get-MemberValue $source 'text')).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $relatedType = [string] (Get-MemberValue $source 'related_type')
        if ([string]::IsNullOrWhiteSpace($relatedType)) {
            $relatedType = [string] (Get-MemberValue $source 'relatedType')
        }

        $relatedIdValue = Get-MemberValue $source 'related_id'
        if ($null -eq $relatedIdValue) {
            $relatedIdValue = Get-MemberValue $source 'relatedId'
        }

        $relatedId = [uint32] 0
        if ($null -ne $relatedIdValue) {
            $relatedId = [uint32] $relatedIdValue
        }

        $rows.Add((New-SourceRow `
                    -Type ([string] (Get-MemberValue $source 'type')).Trim() `
                    -Text $text `
                    -RelatedType $relatedType.Trim() `
                    -RelatedId $relatedId))
    }

    return $rows.ToArray()
}

function ConvertTo-ApiRow {
    param($Mount)

    $itemIdValue = Get-MemberValue $Mount 'item_id'
    $itemId = [uint32] 0
    if ($null -ne $itemIdValue) {
        $itemId = [uint32] $itemIdValue
    }

    return New-MountRow `
        -Id ([uint32] $Mount.id) `
        -Patch (([string] (Get-MemberValue $Mount 'patch')).Trim()) `
        -ItemId $itemId `
        -OwnedJson (ConvertTo-PercentJson (Get-MemberValue $Mount 'owned')) `
        -TradeableJson (ConvertTo-BoolJson (Get-MemberValue $Mount 'tradeable')) `
        -CustomMusicJson (ConvertTo-BoolJson (Get-MemberValue $Mount 'custom_music')) `
        -Sources (ConvertTo-SourceRows -Sources (Get-MemberValue $Mount 'sources'))
}

function Invoke-MountPage {
    param([int] $Page)

    $uri = '{0}?limit={1}&page={2}' -f $apiUrl, $PageSize, $Page
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $TimeoutSeconds `
                -Headers @{ 'User-Agent' = 'Mountbook-dataset-generator/2.0' }
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

# Reads the ids the game's Mount sheet carries: one id per line, or the mdlrip model map CSV
# (mount_id in its first column, header and all). Same input contract as the validator's
# --sheet-ids on purpose - patch-day.ps1 hands the generator the file it already built.
function Read-SheetIdList {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "no Mount sheet id list at $Path."
    }

    $ids = New-Object System.Collections.Generic.List[uint32]
    $first = $true
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line[0] -eq '#') {
            continue
        }

        $comma = $line.IndexOf(',')
        if ($comma -lt 0) {
            $head = $line
        }
        else {
            $head = $line.Substring(0, $comma)
        }

        $head = $head.Trim().Trim('"')

        $id = [uint32] 0
        if (-not [uint32]::TryParse($head, [ref] $id)) {
            # One non-numeric first field is the header row of a CSV; anything else is a broken file.
            if ($first) {
                $first = $false
                continue
            }

            throw "the Mount sheet id list carries a line that is not an id: `"$line`"."
        }

        $first = $false
        if ($id -ne 0) {
            $ids.Add($id)
        }
    }

    if ($ids.Count -eq 0) {
        throw "the Mount sheet id list at $Path carries no id at all."
    }

    return $ids.ToArray()
}

# Reads a committed snapshot back into normalized rows so -Offline can carry its enrichment over to
# the ids the sheet still has. A file with no schemaVersion is read too: the carry-over needs only
# the per-mount fields, which have not moved since the first snapshot.
function Read-SnapshotRows {
    param([string] $Path)

    $rows = @{}
    if (-not (Test-Path -LiteralPath $Path)) {
        return $rows
    }

    $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($mount in @($document.mounts)) {
        if ($null -eq $mount) {
            continue
        }

        $idValue = Get-MemberValue $mount 'id'
        if ($null -eq $idValue) {
            continue
        }

        $id = [uint32] $idValue
        if ($id -eq 0) {
            continue
        }

        $itemIdValue = Get-MemberValue $mount 'itemId'
        $itemId = [uint32] 0
        if ($null -ne $itemIdValue) {
            $itemId = [uint32] $itemIdValue
        }

        $rows[$id] = New-MountRow `
            -Id $id `
            -Patch (([string] (Get-MemberValue $mount 'patch')).Trim()) `
            -ItemId $itemId `
            -OwnedJson (ConvertTo-PercentJson (Get-MemberValue $mount 'owned')) `
            -TradeableJson (ConvertTo-BoolJson (Get-MemberValue $mount 'tradeable')) `
            -CustomMusicJson (ConvertTo-BoolJson (Get-MemberValue $mount 'customMusic')) `
            -Sources (ConvertTo-SourceRows -Sources (Get-MemberValue $mount 'sources'))
    }

    return $rows
}

$rowsById = @{}
$enrichment = 'full'

if ($Offline) {
    Write-Host 'Offline: building the snapshot from the game sheets alone (ffxivcollect is not called).' -ForegroundColor Yellow

    $idSource = $ModelMap
    if ($SheetIds) {
        $idSource = $SheetIds
    }

    $sheet = Read-SheetIdList -Path $idSource
    # NOT $sheetIds: PowerShell variable names are case-insensitive, so that name is the -SheetIds
    # parameter (a string) and assigning a hashtable to it would be coerced into a string.
    $sheetIdSet = @{}
    foreach ($id in $sheet) {
        $sheetIdSet[$id] = $true
    }

    Write-Host ("  {0} Mount sheet ids from {1}" -f $sheet.Count, $idSource)

    $carryPath = $CarryOver
    if (-not $carryPath -and (Test-Path -LiteralPath $resolvedOutput)) {
        $carryPath = $resolvedOutput
    }

    $previous = @{}
    if ($carryPath) {
        $previous = Read-SnapshotRows -Path $carryPath
        Write-Host ("  {0} row(s) of enrichment to carry over from {1}" -f $previous.Count, $carryPath)
    }
    else {
        Write-Host '  no previous snapshot to carry over: every sheet row will be bare' -ForegroundColor Yellow
    }

    $pruned = New-Object System.Collections.Generic.List[uint32]
    foreach ($id in $previous.Keys) {
        if ($sheetIdSet.ContainsKey($id)) {
            $rowsById[$id] = $previous[$id]
        }
        else {
            $pruned.Add($id)
        }
    }

    if ($pruned.Count -gt 0) {
        $pruned.Sort()
        Write-Host ("  {0} row(s) dropped: the Mount sheet no longer has them ({1})" -f `
                $pruned.Count, (($pruned | Select-Object -First 10) -join ', ')) -ForegroundColor Yellow
    }

    if ($rowsById.Count -eq 0) {
        # Nothing to carry over (no committed snapshot, or one that no longer matches the sheet), so
        # the only honest sheet-only snapshot is one bare row per Mount sheet id. The plugin still
        # reads names, icons and descriptions from the sheets; only patch and "How to obtain" go.
        foreach ($id in $sheet) {
            $rowsById[$id] = New-MountRow -Id $id
        }

        Write-Host ("  wrote all {0} sheet ids bare: no patch, no sources, no community data" -f $rowsById.Count) -ForegroundColor Yellow
    }
    else {
        $uncovered = 0
        foreach ($id in $sheet) {
            if (-not $rowsById.ContainsKey($id)) {
                $uncovered++
            }
        }

        Write-Host ("  {0} sheet id(s) carry no enrichment; the site has never listed them, so nothing was invented." -f $uncovered)
    }

    $enrichment = 'sheet-only'
}
else {
    Write-Host "Fetching mounts from $apiUrl (limit $PageSize) ..."
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
            if ($id -eq 0 -or $rowsById.ContainsKey($id)) {
                continue
            }

            $rowsById[$id] = ConvertTo-ApiRow -Mount $result
            $new++
        }

        Write-Host ("page {0}: {1} rows ({2} new, {3} total)" -f $page, $results.Count, $new, $rowsById.Count)
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
}

if ($rowsById.Count -lt $Minimum) {
    $origin = 'ffxivcollect'
    if ($Offline) {
        $origin = 'the Mount sheet'
    }

    $message = 'only {0} mounts came from {1}; the dataset normally carries at least {2}, so this is a partial or failed fetch. Nothing was written.' -f $rowsById.Count, $origin, $Minimum
    $message += ' Check the endpoint, or run with -Offline to build from the game sheets alone; that file says "enrichment": "sheet-only".'
    throw $message
}

$mounts = @($rowsById.Values | Sort-Object -Property Id)
$generated = (Get-Date).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('{')
$lines.Add(('  "schemaVersion": {0},' -f $schemaVersion))
$lines.Add(('  "generated": {0},' -f (ConvertTo-JsonText $generated)))
$lines.Add(('  "source": {0},' -f (ConvertTo-JsonText 'ffxivcollect.com')))
$lines.Add(('  "enrichment": {0},' -f (ConvertTo-JsonText $enrichment)))
$lines.Add(('  "attribution": {0},' -f (ConvertTo-JsonText $attribution)))
$lines.Add(('  "attributionUrl": {0},' -f (ConvertTo-JsonText $attributionUrl)))
$lines.Add('  "mounts": [')

$mountIndex = 0
$sourceCount = 0
foreach ($mount in $mounts) {
    $mountIndex++
    $mountLast = $mountIndex -eq $mounts.Count

    $lines.Add('    {')
    $lines.Add(('      "id": {0},' -f $mount.Id))
    $lines.Add(('      "patch": {0},' -f (ConvertTo-JsonText $mount.Patch)))
    $lines.Add(('      "itemId": {0},' -f $mount.ItemId))
    $lines.Add(('      "owned": {0},' -f $mount.OwnedJson))
    $lines.Add(('      "tradeable": {0},' -f $mount.TradeableJson))
    $lines.Add(('      "customMusic": {0},' -f $mount.CustomMusicJson))

    if ($mount.Sources.Count -eq 0) {
        $lines.Add('      "sources": []')
    }
    else {
        $lines.Add('      "sources": [')
        for ($i = 0; $i -lt $mount.Sources.Count; $i++) {
            $source = $mount.Sources[$i]
            $separator = ''
            if ($i -lt $mount.Sources.Count - 1) {
                $separator = ','
            }

            $relatedTypeJson = 'null'
            if ($source.RelatedType.Length -gt 0) {
                $relatedTypeJson = ConvertTo-JsonText $source.RelatedType
            }

            $lines.Add(
                ('        {{ "type": {0}, "text": {1}, "relatedType": {2}, "relatedId": {3} }}' -f `
                        (ConvertTo-JsonText $source.Type),
                    (ConvertTo-JsonText $source.Text),
                    $relatedTypeJson,
                    $source.RelatedId) + $separator)
            $sourceCount++
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

$byteLength = (Get-Item -LiteralPath $resolvedOutput).Length
$sizeKb = [math]::Round($byteLength / 1KB, 1)
Write-Host ("Wrote {0} mounts ({1} source lines) to {2} ({3} KB, schema v{4}, enrichment {5})" -f `
        $mounts.Count, $sourceCount, $resolvedOutput, $sizeKb, $schemaVersion, $enrichment)

if ($byteLength -gt $MaxBytes) {
    throw ('the snapshot is {0} bytes, over the {1}-byte budget. Drop a field rather than raising the budget: the plugin embeds this file and ships it to every player.' -f $byteLength, $MaxBytes)
}

if ($Offline) {
    Write-Host ''
    Write-Host '  SHEET-ONLY SNAPSHOT: the site was not consulted. Every mount keeps only what the previous' -ForegroundColor Red
    Write-Host '  snapshot carried; new mounts have no obtain data. The file says "enrichment": "sheet-only"' -ForegroundColor Red
    Write-Host '  and the validator reports it as a warning - ship it only when you mean to.' -ForegroundColor Red
}
