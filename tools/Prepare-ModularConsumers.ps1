# Creates independent projects only; Unity compilation and tests are separate, explicit steps.
param([Parameter(Mandatory = $true)][string]$DestinationRoot)
$ErrorActionPreference = 'Stop'
$matrixRoot = [IO.Path]::GetFullPath($DestinationRoot)
$plans = @('ResourcesOnly', 'MinimalUI', 'LocalBundles', 'OfflineFull', 'Full')
foreach ($set in $plans) {
    $consumerPath = Join-Path $matrixRoot $set
    if (Test-Path -LiteralPath $consumerPath) { throw "Destination must not exist: $consumerPath" }
}
New-Item -ItemType Directory -Path $matrixRoot -Force | Out-Null
$matrix = foreach ($set in $plans) {
    $consumerPath = Join-Path $matrixRoot $set
    & (Join-Path $PSScriptRoot 'Prepare-ConsumerProject.ps1') -Destination $consumerPath -PackageSet $set | Write-Host
    $prepared = Get-Content -LiteralPath (Join-Path $consumerPath 'modular-preparation.json') -Raw | ConvertFrom-Json
    [ordered]@{
        packageSet = $set
        projectPath = $consumerPath
        expectedPackages = $prepared.yatoPackages
        preparationOnly = $true
        unityVerified = $false
        prepareEntryPoint = 'Yato.ModularConsumerVerification.PrepareAndVerify'
        testAssembly = 'Yato.Modular.Consumer.Tests'
        editorReport = (Join-Path $consumerPath 'Logs/Modular-Verify.json')
        testsReport = (Join-Path $consumerPath 'Logs/Modular-PlayMode.xml')
    }
}
$matrix | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $matrixRoot 'modular-matrix.json') -Encoding utf8
Write-Output "Prepared five isolated package sets: $matrixRoot. No Unity validation was run."
