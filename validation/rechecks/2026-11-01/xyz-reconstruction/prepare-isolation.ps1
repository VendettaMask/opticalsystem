$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfFrozen=Join-Path $bsdfRepo 'tools/diagnostics/XyzReconstruction20261101/bin/FrozenReconstruction20261101'
if(Test-Path -LiteralPath $bsdfFrozen){throw 'Execution snapshot already exists; never overwrite it.'}
foreach($bsdfPair in @(@('tests/OpticalSystemDesign.Tests/bin/Release/net10.0','tests'),@('tools/diagnostics/XyzReconstruction20261101/bin/Release/net10.0','driver'),@('tools/diagnostics/CommercialBsdf20261031/bin/Release/net10.0','white'))){Copy-Item -LiteralPath (Join-Path $bsdfRepo $bsdfPair[0]) -Destination (Join-Path $bsdfFrozen $bsdfPair[1]) -Recurse}
$bsdfPaths=@((Get-Content (Join-Path $PSScriptRoot '../xyz-angular/source-snapshot-map.json') -Raw | ConvertFrom-Json).OriginalPath)
$bsdfPaths+=@('src/OpticalSystemDesign.Core/Analysis/Shared/PivotedLinearSystem.cs','src/OpticalSystemDesign.Core/Scattering/MeasuredBsdfLogInterpolation.cs','src/OpticalSystemDesign.Core/Scattering/LogBsdfSlice.cs','src/OpticalSystemDesign.Core/Scattering/XyzAbsoluteRbfModel.cs','src/OpticalSystemDesign.Core/Scattering/XyzAbsoluteRbfCalibrator.cs','tests/OpticalSystemDesign.Tests/LogMeasuredBsdfTests.cs','tests/OpticalSystemDesign.Tests/LogMeasuredBsdfChainTests.cs','tests/OpticalSystemDesign.Tests/XyzAbsoluteRbfTests.cs','tests/OpticalSystemDesign.Tests/XyzAbsoluteRbfChainTests.cs','tests/OpticalSystemDesign.Tests/Fixtures/camera-polar-grid9-frozen.zip','tests/OpticalSystemDesign.Tests/Fixtures/camera-polar-grid9-frozen.NOTICE.txt')
$bsdfPaths+=@(Get-ChildItem (Join-Path $bsdfRepo 'tools/diagnostics/XyzReconstruction20261101') -File | ForEach-Object {[IO.Path]::GetRelativePath($bsdfRepo,$_.FullName).Replace('\','/')})
$bsdfPaths+=@(Get-ChildItem (Join-Path $bsdfRepo 'tools/diagnostics/LegacyGrid9Capture20261101') -File | ForEach-Object {[IO.Path]::GetRelativePath($bsdfRepo,$_.FullName).Replace('\','/')})
$bsdfMap=@()
foreach($bsdfRelative in @($bsdfPaths | Sort-Object -Unique)){
    $bsdfSnapshot="validation/rechecks/2026-11-01/xyz-reconstruction/source-snapshot/$bsdfRelative"
    $bsdfDestination=Join-Path $bsdfRepo $bsdfSnapshot
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($bsdfDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $bsdfRepo $bsdfRelative) -Destination $bsdfDestination
    $bsdfMap+=[pscustomobject]@{OriginalPath=$bsdfRelative;SnapshotPath=$bsdfSnapshot;Sha256=(Get-FileHash -LiteralPath $bsdfDestination).Hash.ToLowerInvariant()}
}
$bsdfMap | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Encoding utf8
Write-Output "Isolated execution and $($bsdfMap.Count) source/input files."
