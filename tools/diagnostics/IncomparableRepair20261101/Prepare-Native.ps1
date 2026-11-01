param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/incomparable-native-20261101')
$ErrorActionPreference='Stop'; $repo=(Resolve-Path -LiteralPath $Repository).Path; $root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null
$stage=Join-Path $repo 'artifacts/validation/model-capabilities-stage5-20261101'
$rows=Get-Content -LiteralPath "$stage/rows.json" -Raw|ConvertFrom-Json
$jobs=@();$comparisons=@();$sources=@()
function Add-Job([string]$id,[int]$index,[string]$inputPath,[int]$sampling){
 $row=$rows|Where-Object index -eq $index
 $request=(Get-Content -LiteralPath "$($row.legacyPath)/native/captured-settings.json" -Raw|ConvertFrom-Json).request
 $request.input=$inputPath;$request.captureScreenshots=$false
 if($sampling -gt 0){$request.pupilSampling=$sampling}
 $hash=(Get-FileHash -LiteralPath $inputPath).Hash.ToLowerInvariant()
 $request|Add-Member -NotePropertyName inputSha256 -NotePropertyValue $hash -Force
 $request|ConvertTo-Json -Depth 25|Set-Content -LiteralPath "$root/$id.request.json" -Encoding utf8
 $script:jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=600}
 $script:comparisons+=@{id=$id;index=$index;native="$root/$id"}
}
foreach($index in 161,191,221,243,265,287){
 $row=$rows|Where-Object index -eq $index
 $old=(Get-Content -LiteralPath "$($row.legacyPath)/native/captured-settings.json" -Raw|ConvertFrom-Json).request
 Add-Job "psf-$index-256" $index $old.input 256
}
$grid=(Get-Content -LiteralPath "$((($rows|Where-Object index -eq 494).legacyPath))/native/captured-settings.json" -Raw|ConvertFrom-Json).request.input
foreach($step in '.005','.001'){
 $path=Join-Path $root "grid-step$step.ZMX"
 $sources+= & "$PSScriptRoot/Copy-GridStepControl.ps1" -Source $grid -Destination $path -MaximumStep ([double]$step)
 Add-Job "wavefront-step$step" 494 $path 64
}
$sources|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$root/source-controls.json" -Encoding utf8
$jobs|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$root/jobs.json" -Encoding utf8
$comparisons|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$root/comparison-jobs.json" -Encoding utf8
