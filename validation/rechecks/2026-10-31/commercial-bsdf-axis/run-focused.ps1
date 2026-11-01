$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Set-Location -LiteralPath $bsdfRepo
$env:OPTICAL_COMMERCIAL_BSDF_EVIDENCE_DIR = $PSScriptRoot
$bsdfBinaryPaths = @(
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.App.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll'
)
$bsdfSourcePaths = @(
    'src/OpticalSystemDesign.Core/Scattering/BsdfFileImporter.cs',
    'src/OpticalSystemDesign.Application/Contracts/CameraNonSequentialContracts.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfTests.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfNscTests.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfChainTests.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfWindowTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-measured-v9-frozen.zip',
    'src/OpticalSystemDesign.Core/Scattering/Bsdf.cs',
    'src/OpticalSystemDesign.Core/Scattering/MeasuredBsdf.cs',
    'src/OpticalSystemDesign.Core/Scattering/MeasuredBsdfData.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialSurfaceBsdf.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialDocument.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialPathFilter.cs',
    'src/OpticalSystemDesign.Core/NonSequential/NonSequentialRayDatabase.cs',
    'src/OpticalSystemDesign.Core/Interactions/IInteractionModel.cs',
    'src/OpticalSystemDesign.Core/Serialization/StarOptProjectStore.cs',
    'src/OpticalSystemDesign.Application/Contracts/ServiceContracts.cs',
    'src/OpticalSystemDesign.Application/Contracts/WorkspaceContracts.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialDocumentService.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialBsdfMapper.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialAnalysisService.Camera.cs',
    'src/OpticalSystemDesign.Application/Services/NonSequentialVisualizationBuilder.cs',
    'src/OpticalSystemDesign.Application/Services/CameraImagingService.NonSequential.cs',
    'src/OpticalSystemDesign.App/Panels/NonSequentialObjectEditorPanel.cs',
    'src/OpticalSystemDesign.App/Panels/NonSequentialStrayLightWindows.cs',
    'src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.NonSequential.cs',
    'src/OpticalSystemDesign.App/Services/UiLocalization.CameraNonSequential.cs',
    'tests/OpticalSystemDesign.Tests/MeasuredBsdfTests.cs',
    'tests/OpticalSystemDesign.Tests/MeasuredBsdfNscTests.cs',
    'tests/OpticalSystemDesign.Tests/MeasuredBsdfChainTests.cs',
    'tests/OpticalSystemDesign.Tests/MeasuredBsdfWindowTests.cs',
    'tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-lambertian-v9-frozen.zip'
)
function Get-BsdfIdentity([string] $relative) {
    $bsdfFile = Get-Item -LiteralPath (Join-Path $bsdfRepo $relative)
    [pscustomobject]@{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath $bsdfFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Bytes = $bsdfFile.Length; LastWriteUtc = $bsdfFile.LastWriteTimeUtc.ToString('o') }
}
function Save-BsdfSnapshot([string] $name) {
    [ordered]@{ CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o'); BaseCommit = (git rev-parse HEAD).Trim()
        Binaries = @($bsdfBinaryPaths | ForEach-Object { Get-BsdfIdentity $_ })
        ScopedSources = @($bsdfSourcePaths | ForEach-Object { Get-BsdfIdentity $_ })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot $name) -Encoding utf8
}
Save-BsdfSnapshot 'before-final-run.json'
$bsdfFilter = 'FullyQualifiedName~CommercialBsdf|FullyQualifiedName~MeasuredBsdf|FullyQualifiedName~BsdfTests|FullyQualifiedName~CameraBsdf|FullyQualifiedName~CameraNonSequential|FullyQualifiedName~NonSequentialCameraSensorTests|FullyQualifiedName~NonSequentialStrayLightTests|FullyQualifiedName~DocumentFormatBoundaryTests|FullyQualifiedName~CoreArchitectureTests.ExperimentalLossApproximations'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter $bsdfFilter --logger 'trx;LogFileName=final-focused.trx' --results-directory $PSScriptRoot *> (Join-Path $PSScriptRoot 'test-final-focused.log')
$bsdfTestExit = $LASTEXITCODE
Save-BsdfSnapshot 'after-final-run.json'
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-final-focused.log') -Tail 35
exit $bsdfTestExit

