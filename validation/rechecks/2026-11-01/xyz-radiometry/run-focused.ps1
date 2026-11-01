$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Set-Location -LiteralPath $bsdfRepo
$env:OPTICAL_COMMERCIAL_CALIBRATION_EVIDENCE_DIR = Join-Path $PSScriptRoot 'gamut-final-results'
$env:OPTICAL_SPEOS_V11_XYZ_EVIDENCE_DIR = Join-Path $PSScriptRoot 'gamut-final-chain'
$bsdfBinaryPaths = @(
    'tools/diagnostics/SpeosIsolatedCapture20261031/bin/Release/net10.0/SpeosIsolatedCapture20261031.dll',
    'tools/diagnostics/CommercialBsdf20261031/bin/Release/net10.0/CommercialBsdf20261031.dll',
    'tools/diagnostics/CommercialBsdf20261031/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.Application/bin/Release/net10.0/OpticalSystemDesign.Application.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Application.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Application.dll',
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.App.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll'
)
$bsdfSourcePaths = @(
    'src/OpticalSystemDesign.Core/Scattering/SpeosThetaSlice.cs',
    'src/OpticalSystemDesign.Core/Scattering/SpeosThetaDistribution.cs',
    'src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.NonSequential.cs',
    'src/OpticalSystemDesign.Application/Services/CameraImagingService.NonSequential.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfNativeTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-speos-grid4-frozen.zip',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11-energy.json',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11-no-white-reference.json',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11-no-white.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11.NOTICE.txt',
    'tools/diagnostics/CommercialBsdf20261031/NativeComparison.cs',
    'tools/diagnostics/CommercialBsdf20261031/NativeXmpControl.cs',
    'tools/diagnostics/SpeosIsolatedCapture20261031/Program.cs',
    'tools/diagnostics/SpeosIsolatedCapture20261031/RpcCapture.cs',
    'tools/diagnostics/SpeosIsolatedCapture20261031/SpeosIsolatedCapture20261031.csproj',
    'src/OpticalSystemDesign.Core/Scattering/XyzRadiometricCalibrator.cs',
    'src/OpticalSystemDesign.Core/Scattering/BsdfReferenceComparer.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfCalibrationTests.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfCalibrationChainTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-xyz-grid4-frozen.zip',
    'tools/diagnostics/CommercialBsdf20261031/Program.cs',
    'tools/diagnostics/CommercialBsdf20261031/CommercialBsdf20261031.csproj',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique_XYZ.astm',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm.gz',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique_XYZ.astm.gz',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/probe-native-com.ps1',
    'validation/rechecks/2026-10-31/commercial-bsdf-calibration/SpeosTypeLibrary.cs',
    'src/OpticalSystemDesign.Core/Scattering/SpeosPeakBsdf.cs',
    'src/OpticalSystemDesign.Core/Scattering/SpeosPeakBuilder.cs',
    'src/OpticalSystemDesign.Core/Scattering/SpeosSpectralBsdf.cs',
    'src/OpticalSystemDesign.Core/Scattering/ZemaxXyzBsdf.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11XyzTests.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11BinaryTests.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11StatisticsTests.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11ChainTests.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11NscTests.cs',
    'tests/OpticalSystemDesign.Tests/SpeosV11WindowTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-gaussian-v11.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-gaussian-v11.NOTICE.txt',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-projected-grid3-frozen.zip',
    'src/OpticalSystemDesign.Core/Scattering/SpeosBinaryBsdfReader.cs',
    'src/OpticalSystemDesign.Core/Scattering/ZemaxBsdf.cs',
    'src/OpticalSystemDesign.Core/Scattering/ZemaxBsdfReader.cs',
    'tests/OpticalSystemDesign.Tests/BinaryZemaxBsdfTests.cs',
    'tests/OpticalSystemDesign.Tests/BinaryZemaxBsdfNscTests.cs',
    'tests/OpticalSystemDesign.Tests/BinaryZemaxBsdfChainTests.cs',
    'tests/OpticalSystemDesign.Tests/BinaryZemaxBsdfWindowTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-r-test-v9.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-r-test-v9.NOTICE.txt',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-speos-text-grid2-frozen.zip',
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
$bsdfSourcePaths = @($bsdfSourcePaths + @(
    'tools/diagnostics/SpeosIsolatedCapture20261031/RpcTrace.cs',
    'tools/diagnostics/CommercialBsdf20261031/RuntimeReview.cs',
    'tools/diagnostics/CommercialBsdf20261031/RuntimePowerAudit.cs',
    'tools/diagnostics/SpeosIsolatedCapture20261031/NativePowerStateProbe.cs',
    'src/OpticalSystemDesign.Core/Scattering/SpeosRuntimeWhiteDistribution.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfRuntimeWhiteTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-white-half-degree.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-white-quarter-degree.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/camera-speos-grid5-frozen.zip',
    'tools/diagnostics/CommercialBsdf20261031/CalibrationExploration.cs',
    'tools/diagnostics/CommercialBsdf20261031/MeasuredPairCalibration.cs',
    'tools/diagnostics/CommercialBsdf20261031/DerivedGreenCalibration.cs',
    'tools/diagnostics/CommercialBsdf20261031/DirectReview.cs',
    'tests/OpticalSystemDesign.Tests/CommercialBsdfRuntimeGapTests.cs',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11-white-runtime.anisotropicbsdf',
    'tests/OpticalSystemDesign.Tests/Fixtures/speos-native-v11-white-runtime-reference.json',
    'validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm',
    'validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman_XYZ.astm'
) | Sort-Object -Unique)
$env:OPTICAL_XYZ_GAMUT_EVIDENCE_DIR = Join-Path $PSScriptRoot 'gamut-final-xyz-chain'
$bsdfBinaryPaths += @('tools/diagnostics/XyzRadiometry20261101/bin/Release/net10.0/XyzRadiometry20261101.dll','tools/diagnostics/XyzRadiometry20261101/bin/Release/net10.0/OpticalSystemDesign.Core.dll')
$bsdfSourcePaths = @($bsdfSourcePaths + @(
'src/OpticalSystemDesign.Core/Scattering/XyzCalibrationGamut.cs',
'src/OpticalSystemDesign.Core/Scattering/GamutXyzBsdfDistribution.cs',
'tests/OpticalSystemDesign.Tests/XyzGamutCalibrationTests.cs',
'tests/OpticalSystemDesign.Tests/XyzGamutChainTests.cs',
'tests/OpticalSystemDesign.Tests/Fixtures/camera-speos-grid6-frozen.zip',
'tests/OpticalSystemDesign.Tests/Fixtures/camera-speos-grid6-frozen.NOTICE.txt',
'tools/diagnostics/XyzRadiometry20261101/XyzRadiometry20261101.csproj',
'tools/diagnostics/XyzRadiometry20261101/Program.cs',
'tools/diagnostics/XyzRadiometry20261101/DerivedGamutRun.cs',
'tools/diagnostics/XyzRadiometry20261101/GamutExploration.cs',
'validation/rechecks/2026-11-01/xyz-radiometry/CIE_std_illum_D65.csv',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/garnet_red.astm',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/garnet_red_XYZ.astm',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/krylon_blue.astm',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/krylon_blue_XYZ.astm',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/acryl_blue.astm',
'validation/rechecks/2026-11-01/xyz-radiometry/measured/acryl_blue_XYZ.astm'
) | Sort-Object -Unique)
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
Save-BsdfSnapshot 'before-gamut-final-run.json'
$bsdfFilter = 'FullyQualifiedName~XyzGamut|FullyQualifiedName~SpeosV11|FullyQualifiedName~BinaryZemaxBsdf|FullyQualifiedName~CommercialBsdf|FullyQualifiedName~MeasuredBsdf|FullyQualifiedName~BsdfTests|FullyQualifiedName~CameraBsdf|FullyQualifiedName~CameraNonSequential|FullyQualifiedName~NonSequentialCameraSensorTests|FullyQualifiedName~NonSequentialStrayLightTests|FullyQualifiedName~DocumentFormatBoundaryTests|FullyQualifiedName~CoreArchitectureTests.ExperimentalLossApproximations'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter $bsdfFilter --logger 'trx;LogFileName=gamut-final-focused.trx' --results-directory $PSScriptRoot *> (Join-Path $PSScriptRoot 'test-gamut-final-focused.log')
$bsdfTestExit = $LASTEXITCODE
Save-BsdfSnapshot 'after-gamut-final-run.json'
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-gamut-final-focused.log') -Tail 35
exit $bsdfTestExit




