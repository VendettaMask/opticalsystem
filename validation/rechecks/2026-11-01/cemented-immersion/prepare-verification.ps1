param([Parameter(Mandatory=$true)][string]$ExecutionDirectory,[string]$SnapshotDirectoryName='source-snapshot')
$ErrorActionPreference='Stop'
$assemblyRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $assemblyRepo
if(Test-Path (Join-Path $PSScriptRoot 'source-snapshot-map.json')){throw 'Snapshot already exists; use a new evidence directory.'}
$assemblyPaths=@((Get-Content validation/rechecks/2026-11-01/xyz-reconstruction/source-snapshot-map.json -Raw | ConvertFrom-Json).OriginalPath)
$assemblyPaths+=@(
 'src/OpticalSystemDesign.Core/NonSequential/NonSequentialOpticalAssembly.cs',
 'src/OpticalSystemDesign.Core/NonSequential/SequentialOpticalAssemblyConverter.cs',
 'src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.OpticalAssembly.cs',
 'src/OpticalSystemDesign.Core/NonSequential/NonSequentialDocument.cs',
 'src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.cs',
 'src/OpticalSystemDesign.Core/Domain/OpticalSurface.StateTracing.cs',
 'src/OpticalSystemDesign.Core/Propagation/HomogeneousPowerAttenuation.cs',
 'src/OpticalSystemDesign.Core/Serialization/StarOptProjectStore.cs',
 'src/OpticalSystemDesign.Application/Contracts/WorkspaceContracts.cs',
 'src/OpticalSystemDesign.Application/Services/NonSequentialDocumentService.cs',
 'src/OpticalSystemDesign.Application/Services/NonSequentialVisualizationBuilder.cs',
 'src/OpticalSystemDesign.App/Panels/NonSequentialObjectEditorPanel.cs',
 'tests/OpticalSystemDesign.Tests/OpticalAssemblyTests.cs',
 'tests/OpticalSystemDesign.Tests/OpticalAssemblyApplicationTests.cs',
 'tests/OpticalSystemDesign.Tests/OpticalAssemblyWindowTests.cs',
 'tests/OpticalSystemDesign.Tests/NonSequentialDocumentTests.cs',
 'tests/OpticalSystemDesign.Tests/CoherentCoatingTransportTests.cs',
 'tests/OpticalSystemDesign.Tests/AnalysisNumericalRepairTests.cs',
 'tests/OpticalSystemDesign.Tests/AnalysisExpansionPhysicsTests.cs'
)
$assemblyMap=@(foreach($assemblyPath in ($assemblyPaths | Sort-Object -Unique)){
 $assemblySnapshot=Join-Path $PSScriptRoot "$SnapshotDirectoryName/$assemblyPath"
 New-Item -ItemType Directory -Path (Split-Path $assemblySnapshot) -Force | Out-Null
 Copy-Item -LiteralPath $assemblyPath -Destination $assemblySnapshot
 [pscustomobject]@{OriginalPath=$assemblyPath;SnapshotPath=[IO.Path]::GetRelativePath($assemblyRepo,$assemblySnapshot).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $assemblySnapshot).Hash.ToLowerInvariant()}
})
$assemblyMap | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Encoding utf8
# Bind the final compiled product, not stale copies from a locked/default output of an earlier run.
foreach($assemblyName in @('OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.App')){
 foreach($assemblyExtension in @('dll','pdb')){Copy-Item -LiteralPath "src/$assemblyName/bin/Release/net10.0/$assemblyName.$assemblyExtension" -Destination $ExecutionDirectory -Force}
}
[pscustomobject]@{ExecutionDirectory=[IO.Path]::GetFullPath($ExecutionDirectory);SourceCount=$assemblyMap.Count;BaseCommit=(git rev-parse HEAD).Trim()} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'execution-location.json') -Encoding utf8
