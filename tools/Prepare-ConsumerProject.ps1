# Supported editor: Unity 6 only (6000.0+); current verified version: 6000.5.6f1.
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [ValidateSet('ResourcesOnly', 'Minimal', 'MinimalUI', 'LocalBundles', 'OfflineFull', 'Full')][string]$PackageSet = 'Full',
    [switch]$IncludeInput,
    [switch]$IncludeSave,
    [switch]$IncludeBundles,
    [switch]$IncludeHotUpdate
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$targetRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $targetRoot) { throw "Destination must not exist: $targetRoot" }
if ($targetRoot.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Consumer must be outside the source project' }
$isFull = $PackageSet -in @('Full', 'OfflineFull')
$withBundles = $IncludeBundles -or $IncludeHotUpdate -or $PackageSet -in @('LocalBundles', 'Full')
$withHotUpdate = $IncludeHotUpdate -or $PackageSet -eq 'Full'
$packages = @('core', 'resources')
if ($PackageSet -in @('Minimal', 'MinimalUI', 'OfflineFull', 'Full')) { $packages += @('mvc', 'ui') }
if ($isFull) { $packages += @('persistence', 'app', 'settings', 'audio', 'flow', 'config', 'pooling', 'editor') }
# The framework package is the full aggregate and declares both optional resource modules.
# OfflineFull composes its subpackages and imports their standalone samples without that aggregate.
if ($PackageSet -eq 'Full') { $packages += 'framework' }
if ($withBundles) { $packages += 'resources.bundle' }
if ($withHotUpdate) { $packages += @('persistence', 'resources.hotupdate') }
if ($IncludeInput) { $packages += @('input', 'ui.input') }
if ($IncludeSave) { $packages += @('persistence', 'save') }
# Copy the declared Yato dependency closure, but never silently opt into Bundle or HotUpdate.
$selectedPackages = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$queue = [Collections.Generic.Queue[string]]::new()
foreach ($package in $packages) { $queue.Enqueue($package) }
while ($queue.Count -gt 0) {
    $package = $queue.Dequeue()
    if (-not $selectedPackages.Add($package)) { continue }
    if (($package -eq 'resources.bundle' -and -not $withBundles) -or ($package -eq 'resources.hotupdate' -and -not $withHotUpdate)) {
        throw "Unexpected optional dependency com.yato.$package. Install it explicitly or fix the declaring package dependency."
    }
    $packagePath = Join-Path $sourceRoot "Packages/com.yato.$package/package.json"
    $metadata = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
    foreach ($dependency in $metadata.dependencies.PSObject.Properties.Name) {
        if ($dependency.StartsWith('com.yato.', [StringComparison]::Ordinal)) { $queue.Enqueue($dependency.Substring(9)) }
    }
}
$packages = @($selectedPackages | Sort-Object)
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Packages') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetRoot 'ProjectSettings') -Force | Out-Null
# A bare Unity 6.5 batch project needs a search index before repeated domain reloads.
$searchIndex = @{ name = 'assets'; type = 'asset'; roots = @('Assets'); includes = @(); excludes = @(); options = @{ disabled = $false; types = $true; properties = $false; extended = $false; dependencies = $false }; baseScore = 100 }
$searchIndex | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetRoot 'Assets/YatoVerification.index') -Encoding utf8
foreach ($package in $packages) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "Packages/com.yato.$package") -Destination (Join-Path $targetRoot 'Packages') -Recurse
}
$manifest = @{ dependencies = @{ 'com.unity.test-framework' = '1.1.33' } }
if ('ui' -in $packages) { $manifest.dependencies['com.unity.ugui'] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetRoot 'Packages/manifest.json') -Encoding utf8
if ($isFull) {
    $sampleRoot = Join-Path $targetRoot 'Assets/Samples/YatoFramework'
    New-Item -ItemType Directory -Path $sampleRoot -Force | Out-Null
    foreach ($sample in @('BasicSample', 'FullSample')) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot "Packages/com.yato.framework/Samples~/$sample") -Destination $sampleRoot -Recurse
    }
    if ($withHotUpdate) { Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.resources.hotupdate/Samples~/HotUpdateSample') -Destination $sampleRoot -Recurse }
    if ($IncludeSave) { Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.save/Samples~/CounterSample') -Destination $sampleRoot -Recurse }
    if ($IncludeInput) { Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.ui.input/Samples~/FocusSample') -Destination $sampleRoot -Recurse }
    if ($withHotUpdate) {
        $legacyTarget = Join-Path $sampleRoot 'LegacyCompatibility'
        New-Item -ItemType Directory -Path $legacyTarget -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.framework/Samples~/YatoFrameworkDemo/Scripts') -Destination $legacyTarget -Recurse
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'Packages/com.yato.framework/Samples~/YatoFrameworkDemo/Yato.FrameworkDemo.asmdef') -Destination $legacyTarget
    }
    New-Item -ItemType Directory -Path (Join-Path $targetRoot 'Assets/Editor') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityConsumerVerification.cs') -Destination (Join-Path $targetRoot 'Assets/Editor')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityLifecycleVerification.cs') -Destination (Join-Path $targetRoot 'Assets/Editor')
    if ($IncludeInput -and $IncludeSave) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OptionalConsumerSmokeTests') -Destination (Join-Path $targetRoot 'Assets') -Recurse }
    if ($withHotUpdate) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerPlayerSmoke.cs') -Destination (Join-Path $targetRoot 'Assets') }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerSmokeTests') -Destination (Join-Path $targetRoot 'Assets') -Recurse
    if (-not $withHotUpdate) {
        $testsRoot = Join-Path $targetRoot 'Assets/ConsumerSmokeTests'
        Remove-Item -LiteralPath (Join-Path $testsRoot 'LegacySampleLifecycleTests.cs')
        $testAssemblyPath = Join-Path $testsRoot 'Yato.Consumer.Tests.asmdef'
        $testAssembly = Get-Content -LiteralPath $testAssemblyPath -Raw | ConvertFrom-Json
        $testAssembly.references = @($testAssembly.references | Where-Object { $_ -ne 'Yato.FrameworkDemo' })
        $testAssembly | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $testAssemblyPath -Encoding utf8
    }
}
$modularRoot = Join-Path $targetRoot 'Assets/ModularConsumerTests'
$editorRoot = Join-Path $targetRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $modularRoot, $editorRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerTests/ResourcesConsumerTests.cs') -Destination $modularRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerVerification.cs') -Destination $editorRoot
if ($PackageSet -eq 'ResourcesOnly' -and $packages.Count -eq 2) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularResourcesPlayerSmoke.cs') -Destination (Join-Path $targetRoot 'Assets')
}
$references = @('UnityEngine.TestRunner', 'Yato.Core', 'Yato.Resources.Abstractions', 'Yato.Resources')
if ('ui' -in $packages) {
    $references += @('Yato.Mvc', 'Yato.UI')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerTests/UIConsumerTests.cs') -Destination $modularRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularUIVerification.cs') -Destination $editorRoot
}
if ($withBundles) {
    $references += 'Yato.Resources.Bundle'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerTests/BundleConsumerTests.cs') -Destination $modularRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularBundleVerification.cs') -Destination $editorRoot
    if (-not $withHotUpdate) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerTests/LocalBundlePolicyTests.cs') -Destination $modularRoot }
}
if ($withHotUpdate) {
    $references += @('Yato.Persistence', 'Yato.Resources.HotUpdate')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModularConsumerTests/HotUpdateConsumerTests.cs') -Destination $modularRoot
}
@{ name = 'Yato.Modular.Consumer.Tests'; references = $references; includePlatforms = @(); overrideReferences = $true; precompiledReferences = @('nunit.framework.dll'); autoReferenced = $false; defineConstraints = @('UNITY_INCLUDE_TESTS') } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $modularRoot 'Yato.Modular.Consumer.Tests.asmdef') -Encoding utf8
@{ packageSet = $PackageSet; yatoPackages = @($packages | ForEach-Object { "com.yato.$_" }); includeBundles = [bool]$withBundles; includeHotUpdate = [bool]$withHotUpdate } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $targetRoot 'modular-preparation.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $sourceRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $targetRoot 'ProjectSettings')
Write-Output "Prepared $PackageSet consumer at $targetRoot (Bundles: $withBundles; HotUpdate: $withHotUpdate; Input: $IncludeInput; Save: $IncludeSave)."
Write-Output 'Run Yato.ModularConsumerVerification.PrepareAndVerify before the Yato.Modular.Consumer.Tests PlayMode suite. Preparation is not a Unity validation result.'
