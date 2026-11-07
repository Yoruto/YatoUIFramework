param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$survivorSourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$survivorTargetRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $survivorTargetRoot) { throw "Consumer destination already exists: $survivorTargetRoot" }
if ($survivorTargetRoot.StartsWith($survivorSourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Consumer must be outside the source project' }
$survivorSourceManifest = Get-Content -LiteralPath (Join-Path $survivorSourceRoot 'Packages/manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$survivorUrpVersion = [string]$survivorSourceManifest.dependencies.'com.unity.render-pipelines.universal'
if ($survivorUrpVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'Source manifest must declare an exact com.unity.render-pipelines.universal version.'
}
if (-not (Test-Path -LiteralPath (Join-Path $survivorSourceRoot 'Assets/SurvivorSample/Rendering') -PathType Container)) {
    throw 'Source Survivor rendering assets are missing. Complete the source project URP setup before preparing a consumer.'
}
$survivorProjectFiles = @('ProjectVersion.txt', 'EditorBuildSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset')
foreach ($file in $survivorProjectFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $survivorSourceRoot ('ProjectSettings/' + $file)) -PathType Leaf)) {
        throw "Required source project setting is missing: $file"
    }
}
foreach ($relative in @('Assets', 'Assets/GameRes', 'Assets/GameRes/UI', 'Assets/GameRes/Config', 'Assets/GameRes/Audio', 'Packages', 'ProjectSettings', 'Logs')) {
    New-Item -ItemType Directory -Path (Join-Path $survivorTargetRoot $relative) -Force | Out-Null
}
foreach ($package in @('core', 'mvc', 'persistence', 'resources', 'resources.bundle', 'resources.hotupdate', 'ui', 'app', 'settings', 'audio', 'flow', 'config', 'pooling', 'save', 'input', 'ui.input', 'editor', 'framework')) {
    Copy-Item -LiteralPath (Join-Path $survivorSourceRoot "Packages/com.yato.$package") -Destination (Join-Path $survivorTargetRoot 'Packages') -Recurse
}
foreach ($relative in @('Assets/SurvivorSample', 'Assets/GameRes/Survivor', 'Assets/GameRes/UI/Survivor', 'Assets/GameRes/Config/Survivor', 'Assets/GameRes/Audio/Survivor')) {
    $source = Join-Path $survivorSourceRoot $relative
    $parent = Split-Path -Parent (Join-Path $survivorTargetRoot $relative)
    Copy-Item -LiteralPath $source -Destination $parent -Recurse
    if (Test-Path -LiteralPath ($source + '.meta')) { Copy-Item -LiteralPath ($source + '.meta') -Destination ($parent + '/' + (Split-Path -Leaf $source) + '.meta') }
}
foreach ($file in $survivorProjectFiles) {
    Copy-Item -LiteralPath (Join-Path $survivorSourceRoot ('ProjectSettings/' + $file)) -Destination (Join-Path $survivorTargetRoot 'ProjectSettings')
}
$manifest = @{ dependencies = @{
    'com.unity.render-pipelines.universal' = $survivorUrpVersion;
    'com.unity.ugui' = '1.0.0'; 'com.unity.test-framework' = '1.1.33'; 'com.unity.inputsystem' = '1.20.0';
    'com.unity.modules.tilemap' = '1.0.0'; 'com.unity.modules.imageconversion' = '1.0.0'; 'com.unity.modules.audio' = '1.0.0';
    'com.unity.modules.animation' = '1.0.0'; 'com.unity.modules.physics2d' = '1.0.0'; 'com.unity.modules.ui' = '1.0.0';
    'com.unity.modules.uielements' = '1.0.0'; 'com.unity.modules.screencapture' = '1.0.0'
} }
[IO.File]::WriteAllText((Join-Path $survivorTargetRoot 'Packages/manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$consumerPlayerSettings = @'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 29
  companyName: Yato
  productName: The Last Watch Consumer
  defaultScreenWidth: 1280
  defaultScreenHeight: 720
  activeInputHandler: 2
'@
[IO.File]::WriteAllText((Join-Path $survivorTargetRoot 'ProjectSettings/ProjectSettings.asset'), $consumerPlayerSettings, [Text.UTF8Encoding]::new($false))
$searchIndex = @{ name = 'assets'; type = 'asset'; roots = @('Assets'); includes = @(); excludes = @(); options = @{ disabled = $false; types = $true; properties = $false; extended = $false; dependencies = $false }; baseScore = 100 }
[IO.File]::WriteAllText((Join-Path $survivorTargetRoot 'Assets/SurvivorVerification.index'), ($searchIndex | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "Prepared isolated Survivor consumer: $survivorTargetRoot"
Write-Output "Survivor rendering baseline: URP $survivorUrpVersion; source GraphicsSettings/QualitySettings and sample rendering assets preserved."
