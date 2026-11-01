param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet build src/PortraitRuntime.Unity -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Runtime build failed' }
    dotnet build src/PortraitRuntime.Cli -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tool build failed' }
    if (-not $SkipTests) {
        dotnet run --project tests/PortraitRuntime.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    }
    python tools/build_pack.py
    if ($LASTEXITCODE -ne 0) { throw 'Package validation failed' }
    dotnet run --project tests/NativeContracts -c Release -- dist/player.portraitruntime
    if ($LASTEXITCODE -ne 0) { throw 'Native contracts or ModScanner failed' }
} finally { Pop-Location }
