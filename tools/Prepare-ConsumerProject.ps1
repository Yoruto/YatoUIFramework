# Supported editor: Unity 6 only (6000.0+); current verified version: 6000.5.6f1.
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [ValidateSet('Minimal', 'Full')][string]$PackageSet = 'Full',
    [switch]$IncludeInput
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$targetRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $targetRoot) { throw "Destination must not exist: $targetRoot" }
$packages = @('core', 'persistence', 'resources', 'mvc', 'ui')
if ($PackageSet -eq 'Full') { $packages += @('app', 'settings', 'audio', 'editor', 'framework') }
if ($IncludeInput) { $packages += 'input' }
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Packages') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'ProjectSettings') -Force | Out-Null
foreach ($package in $packages) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "Packages/com.yato.$package") -Destination (Join-Path $targetRoot 'Packages') -Recurse
}
$manifest = @{ dependencies = @{ 'com.unity.ugui' = '1.0.0'; 'com.unity.test-framework' = '1.1.33' } }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetRoot 'Packages/manifest.json') -Encoding utf8
if ($PackageSet -eq 'Full') {
    $sampleRoot = Join-Path $targetRoot 'Assets/Samples/YatoFramework'
    New-Item -ItemType Directory -Path $sampleRoot -Force | Out-Null
    foreach ($sample in @('BasicSample', 'FullSample')) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot "Packages/com.yato.framework/Samples~/$sample") -Destination $sampleRoot -Recurse
    }
    $legacyTarget = Join-Path $sampleRoot 'LegacyCompatibility'
    New-Item -ItemType Directory -Path $legacyTarget -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.framework/Samples~/YatoFrameworkDemo/Scripts') -Destination $legacyTarget -Recurse
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.framework/Samples~/YatoFrameworkDemo/Yato.FrameworkDemo.asmdef') -Destination $legacyTarget
    New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Assets/Editor') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityConsumerVerification.cs') -Destination (Join-Path $targetRoot 'Assets/Editor')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerPlayerSmoke.cs') -Destination (Join-Path $targetRoot 'Assets')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerSmokeTests') -Destination (Join-Path $targetRoot 'Assets') -Recurse
}
Write-Output "Prepared $PackageSet consumer project at $targetRoot (Input: $IncludeInput). Open with the Unity version being verified."
