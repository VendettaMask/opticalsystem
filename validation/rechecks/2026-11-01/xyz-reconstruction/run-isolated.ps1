$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Set-Location -LiteralPath $bsdfRepo
$bsdfFrozen='tools/diagnostics/XyzReconstruction20261101/bin/FrozenReconstruction20261101'
$bsdfBinaries=@('tests/OpticalSystemDesign.Tests.dll','tests/OpticalSystemDesign.App.dll','tests/OpticalSystemDesign.Core.dll','tests/OpticalSystemDesign.Application.dll','driver/XyzReconstruction20261101.dll','driver/OpticalSystemDesign.Core.dll','driver/OpticalSystemDesign.Application.dll','white/CommercialBsdf20261031.dll','white/OpticalSystemDesign.Core.dll') | ForEach-Object {"$bsdfFrozen/$_"}
$bsdfSources=(Get-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Raw | ConvertFrom-Json).SnapshotPath
function Save-Snapshot([string] $name){
    function Identity([string] $relative){$bsdfItem=Get-Item -LiteralPath (Join-Path $bsdfRepo $relative);[pscustomobject]@{Path=$relative;Bytes=$bsdfItem.Length;Sha256=(Get-FileHash -LiteralPath $bsdfItem.FullName).Hash.ToLowerInvariant()}}
    [ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');BaseCommit=(git rev-parse HEAD).Trim();Binaries=@($bsdfBinaries | ForEach-Object {Identity $_});ScopedSources=@($bsdfSources | ForEach-Object {Identity $_})} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot $name) -Encoding utf8
}
Save-Snapshot 'before-isolated-final-run.json'
$bsdfIdentity=Get-Content (Join-Path $PSScriptRoot 'before-isolated-final-run.json') -Raw | ConvertFrom-Json
foreach($bsdfAssembly in @('OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll')){if(@($bsdfIdentity.Binaries | Where-Object {$_.Path.EndsWith($bsdfAssembly)} | Select-Object -ExpandProperty Sha256 -Unique).Count -ne 1){throw "Execution copies differ: $bsdfAssembly"}}
$env:OPTICAL_COMMERCIAL_CALIBRATION_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-test-calibration'
$env:OPTICAL_SPEOS_V11_XYZ_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-test-xyz'
$env:OPTICAL_XYZ_GAMUT_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-test-gamut'
$env:OPTICAL_XYZ_POSITIVE_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-test-positive'
$env:OPTICAL_XYZ_ABSOLUTE_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-test-absolute'
$bsdfFilter='FullyQualifiedName~XyzAbsoluteRbf|FullyQualifiedName~MeasuredAngular|FullyQualifiedName~XyzPositive|FullyQualifiedName~XyzGamut|FullyQualifiedName~SpeosV11|FullyQualifiedName~BinaryZemaxBsdf|FullyQualifiedName~CommercialBsdf|FullyQualifiedName~MeasuredBsdf|FullyQualifiedName~BsdfTests|FullyQualifiedName~CameraBsdf|FullyQualifiedName~CameraNonSequential|FullyQualifiedName~NonSequentialCameraSensorTests|FullyQualifiedName~NonSequentialStrayLightTests|FullyQualifiedName~DocumentFormatBoundaryTests|FullyQualifiedName~CoreArchitectureTests.ExperimentalLossApproximations'
dotnet vstest "$bsdfFrozen/tests/OpticalSystemDesign.Tests.dll" "/TestCaseFilter:$bsdfFilter" '/Logger:trx;LogFileName=isolated-final-focused.trx' "/ResultsDirectory:$PSScriptRoot" *> (Join-Path $PSScriptRoot 'test-isolated-final-focused.log')
if($LASTEXITCODE -ne 0){Save-Snapshot 'after-isolated-failed-tests.json';throw 'Focused tests failed.'}
dotnet "$bsdfFrozen/driver/XyzReconstruction20261101.dll" --fit (Join-Path $PSScriptRoot 'final-rbf-fit') $bsdfRepo *> (Join-Path $PSScriptRoot 'final-rbf-fit.log')
if($LASTEXITCODE -ne 0){throw 'Calibration diagnostic failed.'}
python (Join-Path $PSScriptRoot 'rbf_witnesses.py') (Join-Path $PSScriptRoot 'final-rbf-fit') (Join-Path $PSScriptRoot 'final-rbf-witnesses.json')
if($LASTEXITCODE -ne 0){throw 'Independent witness computation failed.'}
dotnet "$bsdfFrozen/driver/XyzReconstruction20261101.dll" --calibrated-chain (Join-Path $PSScriptRoot 'final-calibrated-chain') $bsdfRepo (Join-Path $PSScriptRoot 'final-rbf-fit') (Join-Path $PSScriptRoot 'final-rbf-witnesses.json') *> (Join-Path $PSScriptRoot 'final-calibrated-chain.log')
if($LASTEXITCODE -ne 0){throw 'Calibrated chain failed.'}
dotnet "$bsdfFrozen/driver/XyzReconstruction20261101.dll" --import (Join-Path $PSScriptRoot 'final-log-import') $bsdfRepo *> (Join-Path $PSScriptRoot 'final-log-import.log')
if($LASTEXITCODE -ne 0){throw 'All-incidence import audit failed.'}
dotnet "$bsdfFrozen/driver/XyzReconstruction20261101.dll" --chain (Join-Path $PSScriptRoot 'final-angular-chain') $bsdfRepo *> (Join-Path $PSScriptRoot 'final-angular-chain.log')
if($LASTEXITCODE -ne 0){throw 'Measured angular chain failed.'}
dotnet "$bsdfFrozen/white/CommercialBsdf20261031.dll" --runtime-power-audit (Join-Path $bsdfRepo 'validation/rechecks/2026-10-31/white-specular-runtime') (Join-Path $PSScriptRoot 'final-white') *> (Join-Path $PSScriptRoot 'final-white.log')
if($LASTEXITCODE -ne 0){throw 'White power audit failed.'}
Save-Snapshot 'after-isolated-final-run.json'
Get-Content (Join-Path $PSScriptRoot 'test-isolated-final-focused.log') -Tail 8
