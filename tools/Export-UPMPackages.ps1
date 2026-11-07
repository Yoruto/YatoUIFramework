#requires -Version 5.1
<#
.SYNOPSIS
Exports the declared Yato packages as UPM tarballs and an offline embedded-package kit.
.DESCRIPTION
Only explicit Packages/com.yato.* inputs and the listed distribution documents/tools are copied.
Destination must not exist. Temporary inputs remain under the ignored Build/DistributionWork
directory; the script never recursively deletes or overwrites a previous export.
Asmdef validation is static reference/graph validation, not a substitute for Unity compilation.
#>
[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$sourcePath = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$packageRoot = Join-Path $sourcePath 'Packages'
$tarCommand = (Get-Command tar.exe -ErrorAction Stop).Source
$null = Get-Command git -ErrorAction Stop
$utf8 = New-Object Text.UTF8Encoding($false)

$packageNames = @(
    'com.yato.app', 'com.yato.audio', 'com.yato.config', 'com.yato.core', 'com.yato.editor',
    'com.yato.flow', 'com.yato.framework', 'com.yato.input', 'com.yato.mvc', 'com.yato.persistence',
    'com.yato.pooling', 'com.yato.resources', 'com.yato.resources.bundle', 'com.yato.resources.hotupdate',
    'com.yato.save', 'com.yato.settings', 'com.yato.ui', 'com.yato.ui.input'
)
$documentNames = @(
    'UPM_DISTRIBUTION.md', 'EMBEDDED_INSTALL_AND_UPGRADE.md',
    'verification/MODULAR_RESOURCES_2026-11-07.json', 'verification/LIVE_UNITY_2026-11-07.json'
)
$distributionVerification = 'verification/UPM_DISTRIBUTION_2026-11-07.json'
if (Test-Path -LiteralPath (Join-Path $sourcePath ('docs/' + $distributionVerification)) -PathType Leaf) {
    $documentNames += $distributionVerification
}

function Get-HexHash([byte[]]$Bytes) {
    return [BitConverter]::ToString($Bytes).Replace('-', '').ToLowerInvariant()
}

function Get-TextHash([string]$Text) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return Get-HexHash ($algorithm.ComputeHash($utf8.GetBytes($Text))) }
    finally { $algorithm.Dispose() }
}

function Get-FileSha([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-JsonFile([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 40) + "`n", $utf8)
}

function Assert-ChildPath([string]$Root, [string]$Path) {
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain below $fullRoot : $fullPath"
    }
    return $fullPath
}

function Assert-RegularInput([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked input is not exported: $Path" }
    return $item
}

function Get-PackageSnapshot([string]$Root) {
    $rootItem = Assert-RegularInput $Root
    if (-not $rootItem.PSIsContainer) { throw "Not a package directory: $Root" }
    $pending = New-Object 'Collections.Generic.Queue[string]'
    $files = New-Object 'Collections.Generic.List[string]'
    $directories = New-Object 'Collections.Generic.List[string]'
    $pending.Enqueue($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            $null = Assert-ChildPath $Root $item.FullName
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked input is not exported: $($item.FullName)" }
            $relative = $item.FullName.Substring($Root.Length + 1).Replace('\', '/')
            if ($relative -match '[\r\n]' -or $item.Name -match '^(\.git|\.codex|\.mcp.*|Library|Temp|Logs|UserSettings|ProjectSettings)$') {
                throw "Unexpected project/tool data inside package: $relative"
            }
            if ($item.PSIsContainer) {
                $directories.Add($relative)
                $pending.Enqueue($item.FullName)
            }
            else { $files.Add($relative) }
        }
    }
    $files.Sort([StringComparer]::Ordinal)
    $directories.Sort([StringComparer]::Ordinal)
    $records = @()
    $canonical = New-Object Text.StringBuilder
    foreach ($relative in $directories) { $null = $canonical.Append($relative).Append('/').Append("`n") }
    foreach ($relative in $files) {
        $filePath = Assert-ChildPath $Root (Join-Path $Root $relative)
        $hash = Get-FileSha $filePath
        $records += [ordered]@{ path = $relative; bytes = (Get-Item -LiteralPath $filePath).Length; sha256 = $hash }
        $null = $canonical.Append($relative).Append([char]0).Append($hash).Append("`n")
    }
    return [pscustomobject]@{ files = @($records); directories = @($directories.ToArray()); sha256 = (Get-TextHash $canonical.ToString()) }
}

function Copy-PackageSnapshot([string]$From, [string]$To, $Snapshot) {
    $null = [IO.Directory]::CreateDirectory($To)
    foreach ($relative in $Snapshot.directories) { $null = [IO.Directory]::CreateDirectory((Assert-ChildPath $To (Join-Path $To $relative))) }
    foreach ($file in $Snapshot.files) {
        $inputFile = Assert-ChildPath $From (Join-Path $From $file.path)
        $null = Assert-RegularInput $inputFile
        [IO.File]::Copy($inputFile, (Assert-ChildPath $To (Join-Path $To $file.path)), $false)
    }
}

function Get-DependencyClosure([string[]]$Roots) {
    $selected = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $queue = New-Object 'Collections.Generic.Queue[string]'
    foreach ($name in $Roots) { $queue.Enqueue($name) }
    while ($queue.Count -gt 0) {
        $name = $queue.Dequeue()
        if (-not $selected.Add($name)) { continue }
        if (-not $packages.ContainsKey($name)) { throw "Missing Yato package: $name" }
        foreach ($dependency in $packages[$name].dependencies.Keys) {
            if ($dependency.StartsWith('com.yato.', [StringComparison]::Ordinal)) { $queue.Enqueue($dependency) }
        }
    }
    return @($selected | Sort-Object)
}

function Visit-Assembly([string]$Name) {
    if ($assemblyState[$Name] -eq 1) { throw "Assembly reference cycle at $Name" }
    if ($assemblyState[$Name] -eq 2) { return }
    $assemblyState[$Name] = 1
    foreach ($reference in $assemblies[$Name].resolvedReferences) { Visit-Assembly $reference }
    $assemblyState[$Name] = 2
}

function Assert-ArchivePath([string]$Path, [string]$Prefix) {
    $normalized = $Path.Replace('\', '/')
    if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:' -or @($normalized.Split('/') | Where-Object { $_ -eq '..' }).Count -gt 0) {
        throw "Unsafe archive path: $Path"
    }
    if ($Prefix -and $normalized -ne $Prefix.TrimEnd('/') -and -not $normalized.StartsWith($Prefix, [StringComparison]::Ordinal)) {
        throw "Unexpected archive root: $Path"
    }
    return $normalized
}

function Test-Tarball([string]$Path, $Snapshot) {
    $entries = @(& $tarCommand -tzf $Path)
    if ($LASTEXITCODE -ne 0) { throw "Cannot list tarball: $Path" }
    $actualFiles = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $normalized = Assert-ArchivePath $entry 'package/'
        if (-not $normalized.EndsWith('/')) {
            if (-not $actualFiles.Add($normalized)) { throw "Duplicate tarball entry: $normalized" }
        }
    }
    if (-not $actualFiles.Contains('package/package.json') -or $actualFiles.Count -ne $Snapshot.files.Count) { throw "Incomplete UPM tarball: $Path" }
    foreach ($file in $Snapshot.files) {
        if (-not $actualFiles.Contains('package/' + $file.path)) { throw "Tarball lost file: $($file.path)" }
    }
}

if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) { throw "Source has no Packages directory: $sourcePath" }
$foundPackages = @(Get-ChildItem -LiteralPath $packageRoot -Directory -Force | Where-Object { $_.Name.StartsWith('com.yato.', [StringComparison]::Ordinal) } | Select-Object -ExpandProperty Name)
if (@(Compare-Object $packageNames $foundPackages).Count -ne 0) { throw 'The export requires exactly the 18 declared com.yato.* package directories.' }
$installerPath = Join-Path $sourcePath 'tools/Install-YatoPackages.ps1'
$exporterPath = Join-Path $sourcePath 'tools/Export-UPMPackages.ps1'
foreach ($required in @($installerPath, $exporterPath)) { $null = Assert-RegularInput $required }
foreach ($document in $documentNames) { $null = Assert-RegularInput (Join-Path $sourcePath ('docs/' + $document)) }
$inputRecords = @()
foreach ($relative in @('tools/Install-YatoPackages.ps1', 'tools/Export-UPMPackages.ps1') + @($documentNames | ForEach-Object { 'docs/' + $_ })) {
    $inputRecords += [ordered]@{ path = $relative; sha256 = (Get-FileSha (Join-Path $sourcePath $relative)) }
}

$sourceCommitLines = @(& git -C $sourcePath rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $sourceCommitLines.Count -ne 1 -or $sourceCommitLines[0] -notmatch '^[a-fA-F0-9]{40}$') { throw 'SourceRoot must be a Git working tree with a readable HEAD.' }
$sourceCommit = [string]$sourceCommitLines[0]
$worktreeStatus = @(& git -C $sourcePath status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git worktree status.' }
$sourceInputs = @($packageNames | ForEach-Object { 'Packages/' + $_ })
$sourceInputs += @('tools/Export-UPMPackages.ps1', 'tools/Install-YatoPackages.ps1')
$sourceInputs += @($documentNames | ForEach-Object { 'docs/' + $_ })
$exportStatus = @(& git -C $sourcePath status --porcelain --untracked-files=all -- @sourceInputs)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect exported source status.' }

$packages = @{}
$assemblies = @{}
$assemblyGuids = @{}
foreach ($name in $packageNames) {
    $root = Assert-ChildPath $packageRoot (Join-Path $packageRoot $name)
    $snapshot = Get-PackageSnapshot $root
    $metadata = Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($metadata.name -ne $name -or $metadata.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') { throw "Invalid package identity/version: $name" }
    $dependencies = [ordered]@{}
    if ($metadata.PSObject.Properties.Name -contains 'dependencies') {
        foreach ($property in $metadata.dependencies.PSObject.Properties) { $dependencies[$property.Name] = [string]$property.Value }
    }
    $packages[$name] = [pscustomobject]@{ name = $name; version = [string]$metadata.version; dependencies = $dependencies; root = $root; snapshot = $snapshot }
    foreach ($file in $snapshot.files | Where-Object { $_.path.EndsWith('.asmdef', [StringComparison]::Ordinal) }) {
        $assembly = Get-Content -LiteralPath (Join-Path $root $file.path) -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($assembly.name) -or $assemblies.ContainsKey($assembly.name)) { throw "Invalid or duplicate assembly name in $name/$($file.path)" }
        $assemblyMeta = Join-Path $root ($file.path + '.meta')
        $meta = Get-Content -LiteralPath $assemblyMeta -Raw -Encoding UTF8
        if ($meta -notmatch '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$') { throw "Assembly metadata has no valid GUID: $assemblyMeta" }
        $guid = $Matches[1].ToLowerInvariant()
        if ($assemblyGuids.ContainsKey($guid)) { throw "Duplicate assembly GUID: $guid" }
        $assemblyGuids[$guid] = $assembly.name
        $assemblies[$assembly.name] = [pscustomobject]@{ package = $name; path = $file.path; references = @($assembly.references); resolvedReferences = @() }
    }
}

foreach ($package in $packages.Values) {
    foreach ($dependency in $package.dependencies.Keys) {
        if (-not $dependency.StartsWith('com.yato.', [StringComparison]::Ordinal)) { continue }
        if (-not $packages.ContainsKey($dependency) -or $packages[$dependency].version -ne $package.dependencies[$dependency]) {
            throw "Yato dependency version mismatch: $($package.name) requires $dependency $($package.dependencies[$dependency])"
        }
    }
}
foreach ($assemblyName in $assemblies.Keys) {
    $assembly = $assemblies[$assemblyName]
    $closure = @(Get-DependencyClosure @($assembly.package))
    foreach ($reference in $assembly.references) {
        $resolved = [string]$reference
        if ($resolved.StartsWith('GUID:', [StringComparison]::OrdinalIgnoreCase)) {
            $guid = $resolved.Substring(5).ToLowerInvariant()
            if (-not $assemblyGuids.ContainsKey($guid)) { throw "Unresolved assembly GUID $reference in $assemblyName" }
            $resolved = $assemblyGuids[$guid]
        }
        if (-not $resolved.StartsWith('Yato.', [StringComparison]::Ordinal)) { continue }
        if (-not $assemblies.ContainsKey($resolved)) { throw "Unresolved Yato assembly $resolved in $assemblyName" }
        if ($assembly.path -notmatch '(^|/)(Tests|Samples~)/' -and $assemblies[$resolved].package -notin $closure) {
            throw "Assembly $assemblyName references undeclared package $($assemblies[$resolved].package) via $resolved"
        }
        $assembly.resolvedReferences += $resolved
    }
}
$assemblyState = @{}
foreach ($assemblyName in $assemblies.Keys) { Visit-Assembly $assemblyName }

$version = $packages['com.yato.framework'].version
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $uniqueName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $Destination = Join-Path $sourcePath "Build/Distribution/YatoFramework-$version/$uniqueName"
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw "Destination must not exist: $destinationPath" }
if ($destinationPath.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination cannot be inside the source Packages tree.' }
if ($destinationPath.StartsWith($sourcePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    & git -C $sourcePath check-ignore --quiet -- $destinationPath
    if ($LASTEXITCODE -ne 0) { throw 'An output inside SourceRoot must be Git-ignored; use Build/Distribution or an external Destination.' }
}
$workPath = Join-Path $sourcePath ('Build/DistributionWork/' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $workPath) { throw "Temporary input path already exists: $workPath" }
$null = [IO.Directory]::CreateDirectory($destinationPath)
$null = [IO.Directory]::CreateDirectory($workPath)
$tarOutput = Join-Path $destinationPath 'UPM'
$offlineRoot = Join-Path $workPath 'Offline'
$offlinePackages = Join-Path $offlineRoot 'Packages'
$null = [IO.Directory]::CreateDirectory($tarOutput)
$null = [IO.Directory]::CreateDirectory($offlinePackages)
$archives = @()
$packageRecords = @()
$combinedSource = New-Object Text.StringBuilder

foreach ($name in $packageNames) {
    $package = $packages[$name]
    $offlinePackage = Join-Path $offlinePackages $name
    Copy-PackageSnapshot $package.root $offlinePackage $package.snapshot
    $copiedSnapshot = Get-PackageSnapshot $offlinePackage
    $currentSource = Get-PackageSnapshot $package.root
    if ($copiedSnapshot.sha256 -ne $package.snapshot.sha256 -or $currentSource.sha256 -ne $package.snapshot.sha256) { throw "Source changed while exporting $name; rerun with a new Destination." }
    $tarInput = Join-Path $workPath ("TarInputs/$name/package")
    Copy-PackageSnapshot $offlinePackage $tarInput $copiedSnapshot
    $archiveName = "$name-$($package.version).tgz"
    $archivePath = Join-Path $tarOutput $archiveName
    & $tarCommand -czf $archivePath --format ustar -C (Split-Path -Parent $tarInput) package
    if ($LASTEXITCODE -ne 0) { throw "tar.exe failed for $name" }
    Test-Tarball $archivePath $copiedSnapshot
    $archives += [ordered]@{ path = 'UPM/' + $archiveName; package = $name; version = $package.version; bytes = (Get-Item -LiteralPath $archivePath).Length; sha256 = (Get-FileSha $archivePath) }
    $packageRecords += [ordered]@{ name = $name; version = $package.version; dependencies = $package.dependencies; sourceTreeSha256 = $copiedSnapshot.sha256; files = $copiedSnapshot.files; directories = $copiedSnapshot.directories }
    $null = $combinedSource.Append($name).Append([char]0).Append($copiedSnapshot.sha256).Append("`n")
}

$profileRoots = [ordered]@{
    ResourcesOnly = @('com.yato.core', 'com.yato.resources')
    MinimalUI = @('com.yato.ui')
    LocalBundles = @('com.yato.resources.bundle')
    OfflineFull = @('com.yato.core', 'com.yato.resources', 'com.yato.mvc', 'com.yato.ui', 'com.yato.persistence', 'com.yato.app', 'com.yato.settings', 'com.yato.audio', 'com.yato.flow', 'com.yato.config', 'com.yato.pooling', 'com.yato.editor')
    Full = @('com.yato.framework')
}
$profiles = [ordered]@{}
foreach ($profile in $profileRoots.Keys) {
    $closure = @(Get-DependencyClosure $profileRoots[$profile])
    $external = [ordered]@{}
    foreach ($name in $closure) {
        foreach ($dependency in $packages[$name].dependencies.Keys) {
            if ($dependency.StartsWith('com.yato.', [StringComparison]::Ordinal)) { continue }
            $requiredVersion = $packages[$name].dependencies[$dependency]
            if ($external.Contains($dependency) -and $external[$dependency] -ne $requiredVersion) { throw "Conflicting external dependency $dependency in profile $profile" }
            $external[$dependency] = $requiredVersion
        }
    }
    $profiles[$profile] = [ordered]@{ roots = $profileRoots[$profile]; packages = $closure; requiredExternalDependencies = $external }
}
$profileManifest = [ordered]@{ schemaVersion = 1; frameworkVersion = $version; profiles = $profiles; optionalFlags = [ordered]@{ IncludeBundles = @('com.yato.resources.bundle'); IncludeHotUpdate = @('com.yato.resources.hotupdate'); IncludeInput = @('com.yato.input', 'com.yato.ui.input'); IncludeSave = @('com.yato.save') } }
Write-JsonFile (Join-Path $offlineRoot 'profiles.json') $profileManifest
Write-JsonFile (Join-Path $destinationPath 'profiles.json') $profileManifest
[IO.File]::Copy($installerPath, (Join-Path $offlineRoot 'Install-YatoPackages.ps1'), $false)
$null = [IO.Directory]::CreateDirectory((Join-Path $offlineRoot 'docs/verification'))
foreach ($document in $documentNames) { [IO.File]::Copy((Join-Path $sourcePath ('docs/' + $document)), (Join-Path $offlineRoot ('docs/' + $document)), $false) }
foreach ($record in $inputRecords) {
    if ((Get-FileSha (Join-Path $sourcePath $record.path)) -ne $record.sha256) { throw "Distribution input changed while exporting: $($record.path)" }
    $copiedInput = if ($record.path -eq 'tools/Install-YatoPackages.ps1') { Join-Path $offlineRoot 'Install-YatoPackages.ps1' } elseif ($record.path.StartsWith('docs/', [StringComparison]::Ordinal)) { Join-Path $offlineRoot $record.path } else { $null }
    if ($copiedInput -and (Get-FileSha $copiedInput) -ne $record.sha256) { throw "Copied distribution input changed: $($record.path)" }
}
$instructions = "# Yato Framework $version offline kit`n`nRead [the installation guide](docs/UPM_DISTRIBUTION.md) and choose a package set.`n`nRun ./Install-YatoPackages.ps1 -ProjectPath <your Unity project> -PackageSet ResourcesOnly to install only Core and Resources.`n`nThe kit contains Yato packages only. Unity registry dependencies must be available separately. See profiles.json and release-manifest.json for package/dependency and checksum details.`n`nSome repository-history links in the copied upgrade guide are not included; the complete package API documents remain under Packages/com.yato.framework/Documentation~/.`n"
[IO.File]::WriteAllText((Join-Path $offlineRoot 'INSTALLATION.md'), $instructions, $utf8)
$finalExportStatus = @(& git -C $sourcePath status --porcelain --untracked-files=all -- @sourceInputs)
if ($LASTEXITCODE -ne 0) { throw 'Cannot refresh exported source status.' }
$finalWorktreeStatus = @(& git -C $sourcePath status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot refresh worktree status.' }
$finalCommit = @(& git -C $sourcePath rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $finalCommit.Count -ne 1) { throw 'Cannot refresh source commit.' }
$commitChanged = $finalCommit[0] -ne $sourceCommit
$exportSourceDirty = $exportStatus.Count -gt 0 -or $finalExportStatus.Count -gt 0 -or $commitChanged
$worktreeDirty = $worktreeStatus.Count -gt 0 -or $finalWorktreeStatus.Count -gt 0 -or $commitChanged
$releaseManifest = [ordered]@{
    schemaVersion = 1
    frameworkVersion = $version
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    sourceCommit = $sourceCommit.Trim()
    sourceDirty = $exportSourceDirty
    exportSourceDirty = $exportSourceDirty
    worktreeDirty = $worktreeDirty
    sourceCommitChangedDuringExport = $commitChanged
    sourceDirtyScope = 'The exported Packages/com.yato.* directories and listed distribution tool/document inputs; unrelated workspace content is excluded.'
    packageSourceSha256 = (Get-TextHash $combinedSource.ToString())
    treeHashFormat = 'SHA256 UTF8: sorted directory paths followed by LF; then ordinal file path + NUL + lowercase SHA256 + LF.'
    packageCount = $packageRecords.Count
    packages = $packageRecords
    distributionInputs = $inputRecords
    upmArchives = $archives
    validation = [ordered]@{ exactPackageAllowlist = $true; dependencyVersions = $true; asmdefReferenceGraph = $true; unityCompilationRunByExporter = $false; tarballPathsAndFileSets = $true }
    checksumNote = 'This inner manifest does not hash itself or its containing ZIP. The outer release-manifest.json records the ZIP SHA256.'
}
Write-JsonFile (Join-Path $offlineRoot 'release-manifest.json') $releaseManifest

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipName = "YatoFramework-$version-offline.zip"
$zipPath = Join-Path $destinationPath $zipName
[IO.Compression.ZipFile]::CreateFromDirectory($offlineRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$offlineSnapshot = Get-PackageSnapshot $offlineRoot
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $zipFiles = @{}
    foreach ($entry in $zip.Entries) {
        $normalized = Assert-ArchivePath $entry.FullName ''
        if ($normalized.EndsWith('/')) { continue }
        if ($zipFiles.ContainsKey($normalized)) { throw "Duplicate ZIP entry: $normalized" }
        $stream = $entry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $zipFiles[$normalized] = Get-HexHash ($algorithm.ComputeHash($stream)) }
        finally { $algorithm.Dispose(); $stream.Dispose() }
    }
    if ($zipFiles.Count -ne $offlineSnapshot.files.Count) { throw 'Offline ZIP file count does not match its input.' }
    foreach ($file in $offlineSnapshot.files) {
        if (-not $zipFiles.ContainsKey($file.path) -or $zipFiles[$file.path] -ne $file.sha256) { throw "Offline ZIP file missing or changed: $($file.path)" }
    }
}
finally { $zip.Dispose() }
$releaseManifest['offlineArchive'] = [ordered]@{ path = $zipName; bytes = (Get-Item -LiteralPath $zipPath).Length; sha256 = (Get-FileSha $zipPath); fileCount = $offlineSnapshot.files.Count; contentsValidated = $true }
Write-JsonFile (Join-Path $destinationPath 'release-manifest.json') $releaseManifest

Write-Output "Exported $($packageNames.Count) UPM tarballs and $zipName to $destinationPath"
Write-Output "Source commit: $($sourceCommit.Trim()); exported source dirty: $exportSourceDirty; overall worktree dirty: $worktreeDirty"
Write-Output "Temporary package inputs retained under ignored Build/DistributionWork: $workPath"
