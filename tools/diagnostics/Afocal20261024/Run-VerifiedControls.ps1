param([string]$Root='artifacts/validation/afocal-20261024')
$ErrorActionPreference='Stop'
function CheckExit { if($LASTEXITCODE -ne 0){throw "Command failed: $LASTEXITCODE"} }
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json -Depth 100}
function WriteJson([string]$p,$v){ConvertTo-Json -InputObject $v -Depth 100|Set-Content -LiteralPath $p -Encoding utf8}
$seq='tools/diagnostics/SequentialCorpus20261019/bin/Release/net10.0/SequentialCorpus20261019.dll'
foreach($project in 'Afocal20261024','Objectives20261020'){
    dotnet build "tools/diagnostics/$project/$project.csproj" -c Release --no-restore -m:2 -p:UseSharedCompilation=false -v:q *> "$Root/build-$project-acceptance.log"
    CheckExit
}
$cutoff="$Root/matrix-corrected-cutoff"
if(Test-Path -LiteralPath $cutoff){throw 'Fresh cutoff output required'}
dotnet $seq prepare . $cutoff "$Root/selected-sources" "config:$Root/scoped-policy.json" *> "$Root/prepare-corrected-cutoff.log"
CheckExit
& "$PSScriptRoot/Set-CutoffControls.ps1" -RunDirectory $cutoff
& tools/diagnostics/Objectives20261020/Reuse-NativeCaptures.ps1 -Before "$Root/matrix-corrected-contract" -After $cutoff
$reuse=ReadJson "$cutoff/native-reuse.json"
if($reuse.count -ne 45 -or $reuse.pending -ne 5){throw 'Expected 45 exact native reuses and five new MTF captures'}
& tools/diagnostics/StandardMatrix20261015/Run-Native.ps1 -RunDirectory "$cutoff/native-pending" *> "$Root/native-corrected-cutoff.log"
CheckExit
dotnet $seq workbench . $cutoff *> "$Root/workbench-corrected-cutoff.log"
CheckExit
dotnet $seq compare . $cutoff *> "$Root/compare-corrected-cutoff.log"
CheckExit
dotnet tools/diagnostics/Afocal20261024/bin/Release/net10.0/Afocal20261024.dll ray-audit "$Root/matrix-corrected-contract" "$Root/ray-audit-acceptance" *> "$Root/ray-audit-acceptance.log"
CheckExit

# New captures get new fixture directories; earlier requests/arrays remain intact.
$fixtures='validation/zemax/2026-r1/afocal-repair-2026-10-24/captures'
$manifest=@()
foreach($item in (ReadJson "$Root/matrix-corrected-contract/items.json")|Where-Object {$_.run.key -in @('MTF','Field Curvature and Distortion') -and $_.run.request}){
    $id='corrected/'+$item.run.directory
    $destination=Join-Path $fixtures $id
    if(Test-Path -LiteralPath $destination){throw 'New fixture directory already exists'}
    New-Item -ItemType Directory -Path $destination|Out-Null
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $item.path 'native') -File){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name)}
    $manifest+=@{id=$id;lens=$item.lens;configuration=$item.configuration;key=$item.run.key;request=$item.run.request;nativeSha256=(Get-FileHash -LiteralPath "$destination/data.json").Hash.ToLowerInvariant()}
}
if($manifest.Count -ne 10){throw 'Ten primary fixtures required'}
WriteJson "$fixtures/manifest.json" $manifest

dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~AfocalEvaluationContractTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~ZemaxImportParameterAndSurfaceMappingTests|FullyQualifiedName~FieldCurvatureNativeMatrixTests' --logger 'trx;LogFileName=afocal-core-acceptance.trx' --results-directory "$Root/tests" *> "$Root/tests-core-acceptance.log"
CheckExit
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Afocal' --logger 'trx;LogFileName=afocal-comparison-acceptance.trx' --results-directory "$Root/tests" *> "$Root/tests-comparison-acceptance.log"
CheckExit

dotnet tools/diagnostics/Objectives20261020/bin/Release/net10.0/Objectives20261020.dll objectives-sampling-controls "$Root/focal-supplemental-analyses-acceptance" "$Root/focal-supplemental-manifest" *> "$Root/focal-supplemental-analyses-acceptance.log"
CheckExit
dotnet tools/diagnostics/Objectives20261020/bin/Release/net10.0/Objectives20261020.dll objectives-completion "$Root/focal-supplemental-points-acceptance" artifacts/validation/objectives-completion-20261023/native-final-fans-v2 *> "$Root/focal-supplemental-points-acceptance.log"
CheckExit
dotnet tools/diagnostics/Afocal20261024/bin/Release/net10.0/Afocal20261024.dll focal-guards "$Root/focal-guards-complete" *> "$Root/focal-guards-complete.log"
CheckExit
Write-Output 'All selected controls completed; inspect structured conclusions and identities before reporting acceptance.'
