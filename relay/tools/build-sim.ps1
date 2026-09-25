<#
    build-sim.ps1 - test the sim, build it, and copy its DLLs into the Unity project.

    Usage, from the relay folder:
        ./tools/build-sim.ps1
        ./tools/build-sim.ps1 -Configuration Debug

    Why DLLs instead of putting the sim .cs files straight into Assets/:
    it makes the "sim cannot reference the engine" rule structural rather than
    a matter of discipline. Relay.Sim.csproj has no Unity reference available,
    so an accidental 'using UnityEngine;' fails the build instantly.

    Day-to-day you should iterate with 'dotnet test', not with Unity. Only run
    this when you actually want to look at grey boxes moving.
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$relayRoot = Split-Path -Parent $PSScriptRoot
$testProj = Join-Path $relayRoot 'sim/Relay.Sim.Tests/Relay.Sim.Tests.csproj'
$simProj = Join-Path $relayRoot 'sim/Relay.Sim/Relay.Sim.csproj'
$outDir = Join-Path $relayRoot "sim/Relay.Sim/bin/$Configuration/netstandard2.1"
$pluginDir = Join-Path $relayRoot 'unity/RelayUnity/Assets/Plugins'

# Relay.Sim depends on Relay.FixedMath. Unity needs both, or the sim fails to load.
$assemblies = @('Relay.Sim', 'Relay.FixedMath')

if (-not (Test-Path (Join-Path $relayRoot 'unity/RelayUnity/Assets'))) {
    throw "No Unity project at unity/RelayUnity. Create it there first (Step 13b)."
}

Write-Host "==> Testing sim before shipping it to Unity" -ForegroundColor Cyan
dotnet test $testProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Sim tests failed. Not copying a broken sim into Unity."
}

Write-Host "==> Building Relay.Sim ($Configuration)" -ForegroundColor Cyan
dotnet build $simProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

if (-not (Test-Path $pluginDir)) {
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
}

foreach ($name in $assemblies) {
    $dll = Join-Path $outDir "$name.dll"
    $pdb = Join-Path $outDir "$name.pdb"

    if (-not (Test-Path $dll)) {
        throw "Expected DLL not found at $dll"
    }

    Copy-Item $dll $pluginDir -Force

    if (Test-Path $pdb) {
        Copy-Item $pdb $pluginDir -Force
    }

    Write-Host "==> Copied $name.dll -> Assets/Plugins" -ForegroundColor Green
}

Write-Host "==> Switch to the Unity window to trigger a reimport." -ForegroundColor DarkGray