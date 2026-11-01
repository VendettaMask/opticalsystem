$ErrorActionPreference='Stop'
$assemblyRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $assemblyRepo
if(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json')){throw 'Evidence is sealed; do not rerun in place.'}
$assemblyExecution=(Get-Content (Join-Path $PSScriptRoot 'execution-location.json') -Raw | ConvertFrom-Json).ExecutionDirectory
function Save-Identity([string]$Name){
 $assemblyFiles=@(Get-ChildItem -LiteralPath $assemblyExecution -File | Where-Object {$_.Extension -in '.dll','.pdb','.json'})
 $assemblyBinaries=@(foreach($assemblyFile in $assemblyFiles){[pscustomobject]@{Path=$assemblyFile.Name;Bytes=$assemblyFile.Length;Sha256=(Get-FileHash -LiteralPath $assemblyFile.FullName).Hash.ToLowerInvariant()}})
 $assemblySources=@(foreach($assemblyEntry in (Get-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Raw | ConvertFrom-Json)){[pscustomobject]@{Path=$assemblyEntry.SnapshotPath;Sha256=(Get-FileHash -LiteralPath $assemblyEntry.SnapshotPath).Hash.ToLowerInvariant()}})
 [ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Binaries=$assemblyBinaries;Sources=$assemblySources} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $PSScriptRoot $Name) -Encoding utf8
}
Save-Identity 'before-final-run.json'
$env:OPTICAL_ASSEMBLY_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-controls'
$assemblyFilter='FullyQualifiedName~OpticalAssembly|FullyQualifiedName~NonSequentialDocumentTests|FullyQualifiedName~NonSequentialRayTracerTests|FullyQualifiedName~CameraSensorPipelineTests|FullyQualifiedName~CameraSensorFileTests|FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~AnalysisNumericalRepairTests|FullyQualifiedName~AnalysisExpansionPhysicsTests.JonesBulkAbsorption|FullyQualifiedName~XyzAbsoluteRbf|FullyQualifiedName~MeasuredAngular|FullyQualifiedName~XyzPositive|FullyQualifiedName~XyzGamut|FullyQualifiedName~SpeosV11|FullyQualifiedName~BinaryZemaxBsdf|FullyQualifiedName~CommercialBsdf|FullyQualifiedName~MeasuredBsdf|FullyQualifiedName~BsdfTests|FullyQualifiedName~CameraBsdf|FullyQualifiedName~CameraNonSequential|FullyQualifiedName~NonSequentialCameraSensorTests|FullyQualifiedName~NonSequentialStrayLightTests|FullyQualifiedName~DocumentFormatBoundaryTests|FullyQualifiedName~CoreArchitectureTests.ExperimentalLossApproximations'
dotnet vstest (Join-Path $assemblyExecution 'OpticalSystemDesign.Tests.dll') "/TestCaseFilter:$assemblyFilter" '/Logger:trx;LogFileName=final-focused.trx' "/ResultsDirectory:$PSScriptRoot" *> (Join-Path $PSScriptRoot 'test-final-focused.log')
$assemblyExit=$LASTEXITCODE
Save-Identity 'after-final-run.json'
Get-Content (Join-Path $PSScriptRoot 'test-final-focused.log') -Tail 24
if($assemblyExit -ne 0){throw "Focused tests failed: $assemblyExit"}
