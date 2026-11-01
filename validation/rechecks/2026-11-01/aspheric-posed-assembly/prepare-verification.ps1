param([Parameter(Mandatory=$true)][string]$ExecutionDirectory,[string]$PreviousSourceMap,[string]$Revision='main')
$ErrorActionPreference='Stop'
$posedRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $posedRepo
if(Test-Path (Join-Path $PSScriptRoot 'source-snapshot-map.json')){throw 'A source receipt already exists; use a new evidence directory.'}
$posedDelta=@(
 'src/OpticalSystemDesign.Core/Geometries/RotationalSurfaceDomain.cs','src/OpticalSystemDesign.Core/Geometries/RotationalSurfaceDomain.Intersection.cs','src/OpticalSystemDesign.Core/Geometries/RuledCircularRim.cs',
 'src/OpticalSystemDesign.Core/NonSequential/OpticalAssemblySurfaceDefinition.cs','src/OpticalSystemDesign.Core/NonSequential/OpticalAssemblyBoundaryModel.cs',
 'src/OpticalSystemDesign.Core/NonSequential/NonSequentialOpticalAssembly.cs','src/OpticalSystemDesign.Core/NonSequential/NonSequentialDocument.cs',
 'src/OpticalSystemDesign.Core/NonSequential/SequentialOpticalAssemblyConverter.cs','src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.cs','src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.OpticalAssembly.cs',
 'src/OpticalSystemDesign.Core/Serialization/StarOptProjectStore.cs','src/OpticalSystemDesign.Application/Services/NonSequentialDocumentService.cs','src/OpticalSystemDesign.Application/Services/NonSequentialVisualizationBuilder.cs',
 'src/OpticalSystemDesign.App/Panels/NonSequentialObjectEditorPanel.cs','tests/OpticalSystemDesign.Tests/PosedOpticalAssemblyTests.cs','tests/OpticalSystemDesign.Tests/PosedOpticalAssemblyApplicationTests.cs',
 'tests/OpticalSystemDesign.Tests/OpticalAssemblyApplicationTests.cs','tests/OpticalSystemDesign.Tests/OpticalAssemblyWindowTests.cs'
)
$posedPaths=@((Get-Content validation/rechecks/2026-11-01/cemented-immersion/source-snapshot-map.json -Raw | ConvertFrom-Json).OriginalPath | Where-Object {$_ -match '^(src/|tests/OpticalSystemDesign.Tests/)'})
$posedPaths+=$posedDelta
$posedPaths+=@('src/OpticalSystemDesign.Core/Geometries/GeometryModels.cs','src/OpticalSystemDesign.Core/Geometries/GeometryIntersectionSolver.cs','src/OpticalSystemDesign.Core/Geometries/GeometryIntersectionSolver.Delegates.cs','src/OpticalSystemDesign.Core/Geometries/IGeometry.cs','src/OpticalSystemDesign.Core/Coordinates/CoordinateSystem.cs','src/OpticalSystemDesign.Core/Backend/Vector3D.cs')
$posedMap=@(foreach($posedPath in ($posedPaths | Sort-Object -Unique)){
 $posedReference=$posedPath;$posedCopied=$posedPath -in $posedDelta
 if($posedCopied){
  $posedPrevious=if($PreviousSourceMap){@(Get-Content $PreviousSourceMap -Raw | ConvertFrom-Json | Where-Object OriginalPath -eq $posedPath)[0]}else{$null}
  if($posedPrevious -and $posedPrevious.Sha256 -eq (Get-FileHash -LiteralPath $posedPath).Hash.ToLowerInvariant()){$posedReference=$posedPrevious.SnapshotPath}
  else{$posedDeltaDirectory=if($Revision -eq 'main'){'source-delta'}else{"source-delta-$Revision"};$posedTarget=Join-Path $PSScriptRoot "$posedDeltaDirectory/$posedPath";New-Item -ItemType Directory -Path (Split-Path $posedTarget) -Force | Out-Null;Copy-Item -LiteralPath $posedPath -Destination $posedTarget;$posedReference=[IO.Path]::GetRelativePath($posedRepo,$posedTarget).Replace('\','/')}
 }
 [pscustomobject]@{OriginalPath=$posedPath;SnapshotPath=$posedReference;StoredAsChangedSource=$posedCopied;Sha256=(Get-FileHash -LiteralPath $posedPath).Hash.ToLowerInvariant()}
})
$posedMap | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Encoding utf8
# Reuse the existing task-owned test runtime; copy only current product/test assemblies.
Get-ChildItem tests/OpticalSystemDesign.Tests/bin/Release/net10.0 -File | Where-Object {$_.Name -like 'OpticalSystemDesign.*' -and $_.Extension -in '.dll','.pdb','.json'} | Copy-Item -Destination $ExecutionDirectory -Force
foreach($posedProject in @('OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.App')){foreach($posedExtension in @('dll','pdb')){Copy-Item -LiteralPath "src/$posedProject/bin/Release/net10.0/$posedProject.$posedExtension" -Destination $ExecutionDirectory -Force}}
[ordered]@{ExecutionDirectory=[IO.Path]::GetFullPath($ExecutionDirectory);BaseCommit=(git rev-parse HEAD).Trim();MappedFiles=$posedMap.Count;ChangedSourcePayloads=$posedDelta.Count;UnchangedSourcesAndInputsAreHashReceipts=$true} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot 'execution-location.json') -Encoding utf8
