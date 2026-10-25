param([string]$Root='artifacts/validation/afocal-20261024/current-source',
      [string]$Inputs='artifacts/validation/afocal-20261024')
$ErrorActionPreference='Stop'
function CheckExit { if($LASTEXITCODE -ne 0){throw "Command failed: $LASTEXITCODE"} }
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json -Depth 100}
if(Test-Path -LiteralPath $Root){throw 'Fresh acceptance directory required'}
New-Item -ItemType Directory -Path $Root|Out-Null
$coreSources=@(Get-ChildItem -LiteralPath src/OpticalSystemDesign.Core -Recurse -File|Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'}|ForEach-Object {
    @{path=[IO.Path]::GetRelativePath((Get-Location).Path,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
ConvertTo-Json -InputObject $coreSources -Depth 5|Set-Content -LiteralPath "$Root/core-source-before.json" -Encoding utf8
foreach($configuration in 'Debug','Release'){
    dotnet build OpticalSystemDesign.slnx -c $configuration --no-restore -m:2 -p:UseSharedCompilation=false -v:q *> "$Root/build-default-$configuration-complete.log"
    CheckExit
}
# Isolated diagnostic outputs prevent a still-running earlier replay from being
# overwritten. Desktop acceptance above always uses its ordinary output paths.
foreach($project in 'Afocal20261024','Objectives20261020','SequentialCorpus20261019'){
    $bin="$Root/runners/$project"
    if(Test-Path -LiteralPath $bin){throw 'Fresh diagnostic runner required'}
    dotnet build "tools/diagnostics/$project/$project.csproj" -c Release --no-restore -m:2 -p:UseSharedCompilation=false -o $bin -v:q *> "$Root/build-$project-complete.log"
    CheckExit
}
$seq="$Root/runners/SequentialCorpus20261019/SequentialCorpus20261019.dll"
$afocal="$Root/runners/Afocal20261024/Afocal20261024.dll"
$objectives="$Root/runners/Objectives20261020/Objectives20261020.dll"
foreach($kind in 'matrix','cutoff'){
    $run="$Root/final-$kind"
    if(Test-Path -LiteralPath $run){throw 'Fresh final run required'}
    dotnet $seq prepare . $run "$Inputs/selected-sources" "config:$Inputs/scoped-policy.json" *> "$Root/prepare-final-$kind.log"
    CheckExit
    $before="$Inputs/matrix-corrected-contract"
    if($kind -eq 'cutoff'){
        & "$PSScriptRoot/Set-CutoffControls.ps1" -RunDirectory $run
        $before="$Inputs/matrix-corrected-cutoff"
    }
    & tools/diagnostics/Objectives20261020/Reuse-NativeCaptures.ps1 -Before $before -After $run
    if((ReadJson "$run/native-reuse.json").count -ne 50){throw 'All 50 final references must be exact native copies'}
    dotnet $seq workbench . $run *> "$Root/workbench-final-$kind.log"
    CheckExit
    dotnet $seq compare . $run *> "$Root/compare-final-$kind.log"
    CheckExit
}
dotnet $afocal ray-audit "$Root/final-matrix" "$Root/final-ray-audit" *> "$Root/final-ray-audit.log"
CheckExit
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~AfocalEvaluationContractTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~ZemaxImportParameterAndSurfaceMappingTests|FullyQualifiedName~FieldCurvatureNativeMatrixTests' --logger 'trx;LogFileName=afocal-core-complete.trx' --results-directory "$Root/tests" *> "$Root/tests-core-complete.log"
CheckExit
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Afocal' --logger 'trx;LogFileName=afocal-comparison-complete.trx' --results-directory "$Root/tests" *> "$Root/tests-comparison-complete.log"
CheckExit
dotnet $objectives objectives-sampling-controls "$Root/final-focal-supplemental-analyses" "$Inputs/focal-supplemental-manifest" *> "$Root/final-focal-supplemental-analyses.log"
CheckExit
dotnet $objectives objectives-completion "$Root/final-focal-supplemental-points" artifacts/validation/objectives-completion-20261023/native-final-fans-v2 *> "$Root/final-focal-supplemental-points.log"
CheckExit
dotnet $afocal focal-guards "$Root/final-focal-guards" *> "$Root/final-focal-guards.log"
CheckExit
Write-Output 'Final replay finished; structured acceptance assertions remain mandatory.'
