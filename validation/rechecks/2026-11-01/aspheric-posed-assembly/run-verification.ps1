$ErrorActionPreference='Stop'
$posedRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $posedRepo
if(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json')){throw 'Evidence is sealed.'}
$posedExecution=(Get-Content (Join-Path $PSScriptRoot 'execution-location.json') -Raw | ConvertFrom-Json).ExecutionDirectory
function Save-Identity([string]$Name){
 $posedBinaries=@(Get-ChildItem $posedExecution -File | Where-Object {$_.Extension -in '.dll','.pdb','.json'} | Sort-Object Name | ForEach-Object {[pscustomobject]@{Path=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
 $posedSources=@(foreach($posedEntry in (Get-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Raw | ConvertFrom-Json)){[pscustomobject]@{Path=$posedEntry.OriginalPath;Sha256=(Get-FileHash -LiteralPath $posedEntry.OriginalPath).Hash.ToLowerInvariant()}})
 [ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Binaries=$posedBinaries;SourcesAndInputs=$posedSources} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $PSScriptRoot $Name) -Encoding utf8
}
Save-Identity 'before-final-run.json'
$env:OPTICAL_POSED_ASSEMBLY_EVIDENCE_DIR=Join-Path $PSScriptRoot 'final-controls'
$posedFilter='FullyQualifiedName~OpticalAssembly|FullyQualifiedName~NonSequentialDocumentTests|FullyQualifiedName~NonSequentialRayTracerTests|FullyQualifiedName~CameraSensorPipelineTests|FullyQualifiedName~CameraSensorFileTests|FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~AnalysisNumericalRepairTests|FullyQualifiedName~AnalysisExpansionPhysicsTests.JonesBulkAbsorption|FullyQualifiedName~XyzAbsoluteRbf|FullyQualifiedName~MeasuredAngular|FullyQualifiedName~XyzPositive|FullyQualifiedName~XyzGamut|FullyQualifiedName~SpeosV11|FullyQualifiedName~BinaryZemaxBsdf|FullyQualifiedName~CommercialBsdf|FullyQualifiedName~MeasuredBsdf|FullyQualifiedName~BsdfTests|FullyQualifiedName~CameraBsdf|FullyQualifiedName~CameraNonSequential|FullyQualifiedName~NonSequentialCameraSensorTests|FullyQualifiedName~NonSequentialStrayLightTests|FullyQualifiedName~DocumentFormatBoundaryTests|FullyQualifiedName~CoreArchitectureTests.ExperimentalLossApproximations'
dotnet vstest (Join-Path $posedExecution 'OpticalSystemDesign.Tests.dll') "/TestCaseFilter:$posedFilter" '/Logger:trx;LogFileName=final-focused.trx' "/ResultsDirectory:$PSScriptRoot" *> (Join-Path $PSScriptRoot 'test-final-focused.log')
$posedExit=$LASTEXITCODE;Save-Identity 'after-final-run.json'
Get-Content (Join-Path $PSScriptRoot 'test-final-focused.log') -Tail 30
if($posedExit -ne 0){throw "Focused tests failed: $posedExit"}
