<#
.SYNOPSIS
Installs a validated Yato package profile into an existing Unity project as embedded packages.
.DESCRIPTION
Copies only selected com.yato.* package directories. Assets, ProjectSettings and manifest.json
are never changed. The target must not already contain Yato packages or Yato manifest entries;
this conservative policy avoids overwrites and mixing independently installed releases.
All dependencies and conflicts are checked before any target write. -WhatIf previews the plan.
PackageSource defaults to Packages beside this script (offline ZIP), then ../Packages (repository).
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet('ResourcesOnly', 'MinimalUI', 'LocalBundles', 'OfflineFull', 'Full')]
    [string]$PackageSet = 'ResourcesOnly',
    [string]$PackageSource,
    [switch]$IncludeHotUpdate,
    [switch]$IncludeBundles,
    [switch]$IncludeInput,
    [switch]$IncludeSave
)

$ErrorActionPreference = 'Stop'
$pathComparison = [StringComparison]::OrdinalIgnoreCase

function Get-NormalPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) }
    return $full
}

function Test-Within([string]$Path, [string]$Root) {
    return $Path.Equals($Root, $pathComparison) -or
        $Path.StartsWith($Root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $pathComparison)
}

function Assert-NoLink([string]$Path) {
    $cursor = Get-Item -LiteralPath $Path -Force
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse points, symbolic links and junctions are not supported: $($cursor.FullName)"
        }
        if ($cursor -is [IO.DirectoryInfo]) { $cursor = $cursor.Parent }
        else { $cursor = $cursor.Directory }
    }
}

function Read-Json([string]$Path) {
    Assert-NoLink $Path
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "Cannot read valid JSON at ${Path}: $($_.Exception.Message)" }
}

function Assert-PackageTree([string]$Root) {
    Assert-NoLink $Root
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push($Root)
    while ($directories.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
            if (-not (Test-Within (Get-NormalPath $item.FullName) $Root)) { throw "Package path leaves its root: $($item.FullName)" }
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Package contains a reparse point: $($item.FullName)" }
            if ($item.PSIsContainer) { $directories.Push($item.FullName) }
        }
    }
}

function Assert-CleanTarget([string]$PackagesPath) {
    Assert-NoLink $PackagesPath
    $manifestPath = Join-Path $PackagesPath 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Read-Json $manifestPath
        foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
            if ($dependency.Name.StartsWith('com.yato.', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Target manifest already declares Yato package $($dependency.Name); installation would mix releases. No packages were copied."
            }
        }
    }
    foreach ($item in Get-ChildItem -LiteralPath $PackagesPath -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Target Packages contains a reparse point: $($item.FullName)" }
        if ($item.Name.StartsWith('com.yato.', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Target already contains Yato package path $($item.FullName). No existing packages will be overwritten."
        }
        if ($item.PSIsContainer) {
            $metadataPath = Join-Path $item.FullName 'package.json'
            if (Test-Path -LiteralPath $metadataPath) {
                $metadata = Read-Json $metadataPath
                if ([string]$metadata.name -like 'com.yato.*') { throw "Target already embeds Yato package $($metadata.name) in $($item.FullName)." }
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    $adjacent = Join-Path $PSScriptRoot 'Packages'
    if (Test-Path -LiteralPath $adjacent -PathType Container) { $PackageSource = $adjacent }
    else { $PackageSource = Join-Path (Split-Path -Parent $PSScriptRoot) 'Packages' }
}
$sourceRoot = Get-NormalPath $PackageSource
$projectRoot = Get-NormalPath $ProjectPath
foreach ($directory in @($sourceRoot, $projectRoot)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Directory not found: $directory" }
    Assert-NoLink $directory
}
if ((Test-Within $sourceRoot $projectRoot) -or (Test-Within $projectRoot $sourceRoot)) {
    throw 'PackageSource and the target Unity project must not overlap.'
}
foreach ($required in @('Assets', 'Packages', 'ProjectSettings')) {
    $directory = Join-Path $projectRoot $required
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Target is not an existing Unity project; missing directory: $directory" }
    Assert-NoLink $directory
}
$targetPackages = Join-Path $projectRoot 'Packages'
Assert-CleanTarget $targetPackages

$roots = switch ($PackageSet) {
    'ResourcesOnly' { @('core', 'resources') }
    'MinimalUI' { @('core', 'resources', 'mvc', 'ui') }
    'LocalBundles' { @('core', 'resources', 'resources.bundle') }
    'OfflineFull' { @('core', 'resources', 'mvc', 'ui', 'persistence', 'app', 'settings', 'audio', 'flow', 'config', 'pooling', 'editor') }
    'Full' { @('framework') }
}
if ($IncludeBundles) { $roots += 'resources.bundle' }
if ($IncludeHotUpdate) { $roots += 'resources.hotupdate' }
if ($IncludeInput) { $roots += @('input', 'ui.input') }
if ($IncludeSave) { $roots += 'save' }
$allowBundles = $IncludeBundles -or $IncludeHotUpdate -or $PackageSet -in @('LocalBundles', 'Full')
$allowHotUpdate = $IncludeHotUpdate -or $PackageSet -eq 'Full'
$queue = [Collections.Generic.Queue[string]]::new()
foreach ($root in $roots) { $queue.Enqueue('com.yato.' + $root) }
$metadataByName = @{}
$external = @{}
$versionPattern = '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
while ($queue.Count -gt 0) {
    $name = $queue.Dequeue()
    if ($metadataByName.ContainsKey($name)) { continue }
    if ($name -cnotmatch '^com\.yato\.[a-z0-9][a-z0-9.-]*$') { throw "Invalid Yato package name: $name" }
    if (($name -eq 'com.yato.resources.bundle' -and -not $allowBundles) -or ($name -eq 'com.yato.resources.hotupdate' -and -not $allowHotUpdate)) {
        throw "Profile unexpectedly requires optional package $name. Select it explicitly or fix the package dependencies."
    }
    $packagePath = Get-NormalPath (Join-Path $sourceRoot $name)
    if (-not (Test-Within $packagePath $sourceRoot)) { throw "Source package escapes PackageSource: $name" }
    if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) { throw "Required source package is missing: $name" }
    Assert-PackageTree $packagePath
    $metadata = Read-Json (Join-Path $packagePath 'package.json')
    if ([string]$metadata.name -cne $name) { throw "Package name does not match its folder: $name / $($metadata.name)" }
    if ([string]$metadata.version -notmatch $versionPattern) { throw "Invalid package version: $name $($metadata.version)" }
    $metadataByName[$name] = $metadata
    foreach ($dependency in $metadata.dependencies.PSObject.Properties) {
        if ($dependency.Name.StartsWith('com.yato.', [StringComparison]::Ordinal)) { $queue.Enqueue($dependency.Name) }
        else {
            if ($external.ContainsKey($dependency.Name) -and $external[$dependency.Name] -ne [string]$dependency.Value) {
                throw "Conflicting external dependency requirements for $($dependency.Name): $($external[$dependency.Name]) / $($dependency.Value)"
            }
            $external[$dependency.Name] = [string]$dependency.Value
        }
    }
}

$names = @($metadataByName.Keys | Sort-Object)
foreach ($name in $names) {
    foreach ($dependency in $metadataByName[$name].dependencies.PSObject.Properties) {
        if ($dependency.Name.StartsWith('com.yato.', [StringComparison]::Ordinal)) {
            if ([string]$dependency.Value -cne [string]$metadataByName[$dependency.Name].version) {
                throw "Yato version mismatch: $name requires $($dependency.Name) $($dependency.Value), source provides $($metadataByName[$dependency.Name].version)."
            }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $targetPackages $name)) { throw "Target package already exists: $name" }
}
$externalPlan = [ordered]@{}
foreach ($name in @($external.Keys | Sort-Object)) { $externalPlan[$name] = $external[$name] }
$packagePlan = @($names | ForEach-Object { [pscustomobject]@{ name = $_; version = [string]$metadataByName[$_].version } })
$installed = $false

if ($PSCmdlet.ShouldProcess($projectRoot, "Install $($names.Count) validated embedded Yato packages ($PackageSet)")) {
    # Stage complete copies before exposing any package. No recursive deletion or overwrite is used.
    $lockPath = Join-Path $targetPackages '.yato-install.lock'
    $installLock = $null
    $stageRoot = $null
    try {
        $installLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
        Assert-CleanTarget $targetPackages
        $stageRoot = Join-Path $targetPackages ('.yato-install-stage-' + [guid]::NewGuid().ToString('N'))
        if (-not (Test-Within $stageRoot $targetPackages)) { throw 'Invalid package staging path' }
        New-Item -ItemType Directory -Path $stageRoot | Out-Null
        foreach ($name in $names) {
            $sourcePath = Join-Path $sourceRoot $name
            Assert-PackageTree $sourcePath
            Copy-Item -LiteralPath $sourcePath -Destination $stageRoot -Recurse
        }
        Assert-CleanTarget $targetPackages
        foreach ($name in $names) {
            $destination = Get-NormalPath (Join-Path $targetPackages $name)
            if (-not (Test-Within $destination $targetPackages) -or (Test-Path -LiteralPath $destination)) { throw "Unsafe or occupied target path: $destination" }
        }
        foreach ($name in $names) { Move-Item -LiteralPath (Join-Path $stageRoot $name) -Destination (Join-Path $targetPackages $name) }
        Remove-Item -LiteralPath $stageRoot
        $stageRoot = $null
        $installed = $true
    }
    catch {
        if ($null -ne $stageRoot -and (Test-Path -LiteralPath $stageRoot)) {
            Write-Warning "Installation stopped. Staged files were retained for inspection: $stageRoot"
        }
        throw
    }
    finally { if ($null -ne $installLock) { $installLock.Dispose() } }
}

[pscustomobject][ordered]@{
    status = $(if ($installed) { 'Installed' } else { 'Preview' })
    projectPath = $projectRoot
    packageSource = $sourceRoot
    packageSet = $PackageSet
    packageCount = $names.Count
    packages = $packagePlan
    requiredExternalDependencies = [pscustomobject]$externalPlan
    manifestChanged = $false
    note = 'Unity Package Manager resolves external dependencies from package metadata. No source Assets, ProjectSettings, or Unity MCP packages were installed.'
}
