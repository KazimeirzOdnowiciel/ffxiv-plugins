<#
    Builds and verifies Slot Yield.

        ./verify.ps1
        ./verify.ps1 -Live          # also runs a real scan against Universalis

    Needs the .NET SDK. On the default execution policy run it as
    powershell -ExecutionPolicy Bypass -File .\verify.ps1
#>

param(
    [switch]$Live
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

function Step($name, [scriptblock]$body) {
    Write-Host ""
    Write-Host "== $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) {
        throw "step failed: $name (exit $LASTEXITCODE)"
    }
}

Step "build" {
    dotnet build -c Release -v q --nologo
}

Step "core tests (model + exact optimizer)" {
    dotnet run --project tests/CoreTests -v q
}

Step "cost tests (recipes, yields, cycles, partial costs)" {
    dotnet run --project tests/CostTests -v q
}

Step "market tests (parsing, trimming, cache)" {
    dotnet run --project tests/MarketTests -v q
}

Step "smoke test (packaged dll)" {
    dotnet run --project tests/SmokeTest -v q -- (Join-Path $root "bin/Release/SlotYield.dll")
}

if ($Live) {
    Step "live scan" {
        dotnet run --project tests/LiveScan -v q -- Light 10 20
    }
}

Write-Host ""
Write-Host "all checks passed" -ForegroundColor Green
