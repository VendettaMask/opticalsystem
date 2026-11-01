$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$sourcePaths = @(
    'src/OpticalSystemDesign.Core/Scattering/Bsdf.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialSurfaceBsdf.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialDocument.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialPathFilter.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialRayDatabase.cs',
    'src/OpticalSystemDesign.Core/Interactions/IInteractionModel.cs',
    'src/OpticalSystemDesign.Core/Serialization/StarOptProjectStore.cs',
    'src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraScene.cs',
    'src/OpticalSystemDesign.Application/Contracts/WorkspaceContracts.cs',
    'src/OpticalSystemDesign.Application/Contracts/CameraNonSequentialContracts.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialDocumentService.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialAnalysisService.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialAnalysisService.Camera.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialVisualizationBuilder.cs',
    'src/OpticalSystemDesign.Application/Services/CameraImagingService.NonSequential.cs',
    'src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.NonSequential.cs',
    'src/OpticalSystemDesign.App/Panels/NonSequentialObjectEditorPanel.cs',
    'src/OpticalSystemDesign.App/Panels/NonSequentialStrayLightWindows.cs',
    'src/OpticalSystemDesign.App/Services/UiLocalization.CameraNonSequential.cs',
    'tests/OpticalSystemDesign.Tests/BsdfTests.cs',
    'tests/OpticalSystemDesign.Tests/NonSequentialBsdfTests.cs',
    'tests/OpticalSystemDesign.Tests/CameraBsdfChainTests.cs',
    'tests/OpticalSystemDesign.Tests/CameraBsdfWindowTests.cs',
    'tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-nsc-v9-frozen.zip',
    'docs/LAMBERTIAN_BSDF_2026-10-31.md',
    'docs/CURRENT_STATUS.md',
    'docs/CAMERA_IMAGING_LAB.md',
    'docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md',
    'docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md',
    'docs/NON_SEQUENTIAL_TRACING.md',
    'docs/STAROPT_FILE_FORMAT.md',
    'docs/FILE_FORMATS_AND_PLUGINS.md',
    'docs/README.md'
)
function Entry([string] $relative) {
    $file = Get-Item -LiteralPath (Join-Path $repoRoot $relative)
    [ordered]@{ Path = $relative.Replace('\', '/'); Bytes = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$corePaths = @(
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.Application/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll'
)
$coreCopies = @($corePaths | ForEach-Object { Entry $_ })
$preRunCopies = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pre-final-core-copies.json') -Raw | ConvertFrom-Json)
if (@($preRunCopies.Hash | Select-Object -Unique).Count -ne 1) { throw 'Core copies differed at test start.' }
$verifiedCoreHash = $preRunCopies[0].Hash.ToLowerInvariant()
$postRunCopies = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'post-final-core-copies.json') -Raw | ConvertFrom-Json)
if ($postRunCopies[2].Hash.ToLowerInvariant() -ne $verifiedCoreHash -or $postRunCopies[5].Hash.ToLowerInvariant() -ne $verifiedCoreHash) {
    throw 'App/test Core hashes differed between test start and the completion audit.'
}
$workingOutputDrift = @($coreCopies | Where-Object Sha256 -ne $verifiedCoreHash)
[xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-focused.trx') -Raw
$runStart = [DateTimeOffset]::Parse($trx.TestRun.Times.start)
if ($runStart.UtcDateTime -lt ([DateTimeOffset]$postRunCopies[5].Date).UtcDateTime) {
    throw 'Final test run predates the verified test Core build.'
}
foreach ($source in $sourcePaths | Where-Object { $_ -like '*.cs' -or $_ -like '*.csproj' }) {
    if ((Get-Item -LiteralPath (Join-Path $repoRoot $source)).LastWriteTimeUtc -gt $runStart.UtcDateTime) {
        throw "Scoped source changed during the test run: $source"
    }
}
$results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
if (@($results | Where-Object outcome -ne 'Passed').Count -gt 0) { throw 'Final test run contains non-passing results.' }
$groups = @($results | Group-Object { ($_.testName -split '\.')[2] } | Sort-Object Name | ForEach-Object { [ordered]@{ Class = $_.Name; Passed = $_.Count } })
$energies = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'energy-*.json' | Sort-Object Name | ForEach-Object {
    $data = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    [ordered]@{ Rho = $data.rho; RelativeEnergyClosureError = [Math]::Abs($data.e.AccountedPowerWatts / $data.e.SourcePowerWatts - 1)
        DetectorFraction = $data.e.DetectorPowerWatts / $data.e.SourcePowerWatts; ExpectedDetectorFraction = $data.rho * $data.viewFactor
        SixSigmaFraction = $data.sixSigma; SampleCount = $data.BsdfStatistics.SampleCount }
})
$manifest = [ordered]@{
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    GitBaseCommit = (git -C $repoRoot rev-parse HEAD).Trim()
    WorkingTree = 'Shared dirty checkout; scoped source hashes identify this verification. Other tasks are preserved.'
    Platform = [Environment]::OSVersion.VersionString
    FinalPassed = $results.Count
    FinalFailed = 0
    TestClasses = $groups
    EnergyChecks = $energies
    CoreCopies = $coreCopies
    VerifiedAppAndTestCoreSha256 = $verifiedCoreHash
    PreRunCoreCopies = $preRunCopies
    PostRunCoreCopies = $postRunCopies
    WorkingOutputDriftAfterTestStart = $workingOutputDrift
    Sources = @($sourcePaths | ForEach-Object { Entry $_ })
    CurrentWorkingBinariesAfterVerification = @('src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
        'src/OpticalSystemDesign.Application/bin/Release/net10.0/OpticalSystemDesign.Application.dll',
        'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll' | ForEach-Object { Entry $_ })
    Artifacts = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'manifest.json' | Sort-Object Name | ForEach-Object {
        Entry ($_.FullName.Substring($repoRoot.Length + 1))
    })
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
[pscustomobject]$manifest | Select-Object FinalPassed, FinalFailed, VerifiedAppAndTestCoreSha256, EnergyChecks, WorkingOutputDriftAfterTestStart | ConvertTo-Json -Depth 5
