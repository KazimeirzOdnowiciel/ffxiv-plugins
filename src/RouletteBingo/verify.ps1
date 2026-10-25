<#
    Builds and verifies everything.

        ./verify.ps1

    Needs the .NET SDK. The demo steps additionally need python and node; point at them with
    -Python / -Node if they are not on PATH.
#>

param(
    [string]$Python = "python",
    [string]$Node = "node"
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

Step "core tests" {
    dotnet run --project tests/CoreTests -v q
}

Step "smoke test (packaged dll)" {
    dotnet run --project tests/SmokeTest -v q -- (Join-Path $root "bin/Release/RouletteBingo.dll")
}

Step "fixtures" {
    dotnet run --project tests/CoreTests -v q -- --fixtures (Join-Path $root "tools/demo/fixtures.json")
}

Step "browser preview" {
    & $Python (Join-Path $root "tools/make_demo.py")
}

Step "preview parity (c# <-> js)" {
    & $Node (Join-Path $root "tools/check_demo.mjs")
}

Step "preview ui" {
    & $Node (Join-Path $root "tools/check_demo_ui.mjs")
}

Write-Host ""
Write-Host "all checks passed" -ForegroundColor Green
