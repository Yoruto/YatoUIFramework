param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$survivorSourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$survivorTargetRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $survivorTargetRoot) { throw "Consumer destination already exists: $survivorTargetRoot" }
if ($survivorTargetRoot.StartsWith($survivorSourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Consumer must be outside the source project' }
foreach ($relative in @('Assets', 'Assets/GameRes', 'Assets/GameRes/UI', 'Assets/GameRes/Config', 'Assets/GameRes/Audio', 'Packages', 'ProjectSettings', 'Logs')) {
    New-Item -ItemType Directory -Path (Join-Path $survivorTargetRoot $relative) -Force | Out-Null
}
foreach ($package in @('core', 'mvc', 'persistence', 'resources', 'ui', 'app', 'settings', 'audio', 'flow', 'config', 'pooling', 'save', 'input', 'ui.input', 'editor', 'framework')) {
    Copy-Item -LiteralPath (Join-Path $survivorSourceRoot "Packages/com.yato.$package") -Destination (Join-Path $survivorTargetRoot 'Packages') -Recurse
}
foreach ($relative in @('Assets/SurvivorSample', 'Assets/GameRes/Survivor', 'Assets/GameRes/UI/Survivor', 'Assets/GameRes/Config/Survivor', 'Assets/GameRes/Audio/Survivor')) {
    $source = Join-Path $survivorSourceRoot $relative
    $parent = Split-Path -Parent (Join-Path $survivorTargetRoot $relative)
    Copy-Item -LiteralPath $source -Destination $parent -Recurse
    if (Test-Path -LiteralPath ($source + '.meta')) { Copy-Item -LiteralPath ($source + '.meta') -Destination ($parent + '/' + (Split-Path -Leaf $source) + '.meta') }
}
Copy-Item -LiteralPath (Join-Path $survivorSourceRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $survivorTargetRoot 'ProjectSettings')
Copy-Item -LiteralPath (Join-Path $survivorSourceRoot 'ProjectSettings/EditorBuildSettings.asset') -Destination (Join-Path $survivorTargetRoot 'ProjectSettings')
$manifest = @{ dependencies = @{
    'com.unity.ugui' = '1.0.0'; 'com.unity.test-framework' = '1.1.33'; 'com.unity.inputsystem' = '1.20.0';
    'com.unity.modules.tilemap' = '1.0.0'; 'com.unity.modules.imageconversion' = '1.0.0'; 'com.unity.modules.audio' = '1.0.0';
    'com.unity.modules.animation' = '1.0.0'; 'com.unity.modules.physics2d' = '1.0.0'; 'com.unity.modules.ui' = '1.0.0';
    'com.unity.modules.uielements' = '1.0.0'; 'com.unity.modules.screencapture' = '1.0.0'
} }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $survivorTargetRoot 'Packages/manifest.json') -Encoding utf8
@'
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
'@ | Set-Content -LiteralPath (Join-Path $survivorTargetRoot 'ProjectSettings/ProjectSettings.asset') -Encoding utf8
$searchIndex = @{ name = 'assets'; type = 'asset'; roots = @('Assets'); includes = @(); excludes = @(); options = @{ disabled = $false; types = $true; properties = $false; extended = $false; dependencies = $false }; baseScore = 100 }
$searchIndex | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $survivorTargetRoot 'Assets/SurvivorVerification.index') -Encoding utf8
Write-Output "Prepared isolated Survivor consumer: $survivorTargetRoot"
