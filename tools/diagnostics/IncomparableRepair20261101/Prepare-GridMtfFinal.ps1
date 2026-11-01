param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=Join-Path $repo 'artifacts/validation/grid-mtf-final-core-20261101'
$regression=Join-Path $repo 'artifacts/validation/grid-mtf-final-regression-20261101'
$controls=Join-Path $repo 'artifacts/validation/grid-mtf-final-controls-20261101'
foreach($path in @("$root/workbench-jobs.json",$regression,$controls)){if(Test-Path -LiteralPath $path){throw 'Fresh final execution required'}}
$identity=Get-Content -LiteralPath "$root/identity.json" -Raw|ConvertFrom-Json
if($identity.coreSha256 -ne $identity.defaultCoreSha256 -or $identity.focusedTests -ne 125 -or $identity.gatesChanged){throw 'Frozen final build/test identity mismatch'}
$requests=@();$comparisons=@()
foreach($n in 1024,2048){
 $req=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-bounded-knot-core-20261101/core-497-$n.request.json" -Raw|ConvertFrom-Json
 $req.outputDirectory=Join-Path $root "mtf$n";$path=Join-Path $root "mtf$n.request.json"
 $req|ConvertTo-Json -Depth 25|Set-Content -LiteralPath $path;$requests+=$path
 $comparisons+=@{id="final$n-native2048";index=497;native="$repo/artifacts/validation/incomparable-grid-mtf-native-high-corrected-20261101/mtf-2048-step.01"
  workbench="$($req.outputDirectory)/data.json";workbenchRequest=$path}
}
$requests|ConvertTo-Json -AsArray|Set-Content -LiteralPath "$root/workbench-jobs.json"
$comparisons|ConvertTo-Json -AsArray -Depth 10|Set-Content -LiteralPath "$root/comparison-jobs.json"
@(@{id='final1024-to2048';index=497;requestA=$requests[0];requestB=$requests[1]})|ConvertTo-Json -AsArray -Depth 10|Set-Content -LiteralPath "$root/core-convergence-jobs.json"
New-Item -ItemType Directory $regression|Out-Null;Copy-Item -LiteralPath "$root/runtime" -Destination "$regression/runtime" -Recurse
$original=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-current-accepted-assessment-20261101/comparison-jobs.json" -Raw|ConvertFrom-Json
$jobs=@($original|Where-Object {$_.index -ne 499}|Group-Object index|ForEach-Object {$_.Group|Select-Object -First 1})
if($jobs.Count -ne 32){throw 'Expected 24 accepted and 8 adjacent original rows'}
$requests=@();$comparisons=@()
foreach($j in $jobs){
 $req=Get-Content -LiteralPath $j.workbenchRequest -Raw|ConvertFrom-Json;$id="final-$($j.index)"
 $req.outputDirectory=Join-Path $regression $id;$path=Join-Path $regression "$id.request.json"
 $req|ConvertTo-Json -Depth 25|Set-Content -LiteralPath $path;$requests+=$path
 $comparisons+=@{id=$id;index=$j.index;native=$j.native;workbench="$($req.outputDirectory)/data.json";workbenchRequest=$path}
}
$requests|Sort-Object {[int]([regex]::Match([IO.Path]::GetFileName($_),'\d+').Value)}|ConvertTo-Json -AsArray|Set-Content -LiteralPath "$regression/workbench-jobs.json"
$comparisons|ConvertTo-Json -AsArray -Depth 10|Set-Content -LiteralPath "$regression/comparison-jobs.json"
New-Item -ItemType Directory $controls|Out-Null
$models=@(Get-Content -LiteralPath "$repo/artifacts/validation/grid-rule-rays-native-20261101/models.json" -Raw|ConvertFrom-Json)+
 @(Get-Content -LiteralPath "$repo/artifacts/validation/grid-rule-holdout-native-20261101/models.json" -Raw|ConvertFrom-Json)
$models|ConvertTo-Json -Depth 16|Set-Content -LiteralPath "$controls/models.json"
$index=Join-Path $controls 'index';New-Item -ItemType Directory $index|Out-Null
Copy-Item -LiteralPath "$repo/artifacts/validation/grid-floor-query-index-20261101/models.json" -Destination "$index/models.json"
& dotnet "$root/runtime/IncomparableRepair20261101.dll" grid-rule-rays $repo $controls
if($LASTEXITCODE -ne 0){throw 'Final rule-ray execution failed'}
& dotnet "$root/runtime/IncomparableRepair20261101.dll" grid-index-profiles $repo $index
if($LASTEXITCODE -ne 0){throw 'Final index-profile execution failed'}
