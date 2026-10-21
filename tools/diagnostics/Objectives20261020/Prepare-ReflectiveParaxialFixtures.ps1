param(
    [string]$Replay='artifacts/validation/remaining-lenses-20261021/first-order-asphere',
    [string]$Output='validation/zemax/2026-r1/reflective-paraxial-repair-2026-10-21',
    [string]$NativeRun='artifacts/validation/remaining-lenses-20261021/native-repeat'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if((Test-Path -LiteralPath $Output) -or (Test-Path -LiteralPath $NativeRun)){throw 'Fresh output required'}
$rows=Get-Content (Join-Path $Replay 'summary.json') -Raw|ConvertFrom-Json -Depth 100
$keys=@($rows|Where-Object {$_.previous -eq 'Difference' -and $_.conclusion -eq 'Pass'}|ForEach-Object key)
if($keys.Count -ne 11){throw 'Expected the exact eleven repaired first-order requests'}
New-Item -ItemType Directory -Path $Output,$NativeRun|Out-Null
$cases=@();$jobs=@()
foreach($item in (Get-Content 'artifacts/zemax-sequential-corpus/20261021/full-v1/analysis-results.json' -Raw|ConvertFrom-Json -Depth 100)){
    $r=$item.run.request
    if(!$r){continue}
    $key='{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $item.lens,$item.configuration,$item.scope,$r.canonicalAnalysisKey,$r.field,$r.wavelength,$r.pupilSampling
    if($keys -cnotcontains $key){continue}
    $id=$item.run.directory;$folder=Join-Path $Output $id
    New-Item -ItemType Directory -Path $folder|Out-Null
    $r|ConvertTo-Json -Depth 90|Set-Content (Join-Path $folder 'request.json') -Encoding utf8NoBOM
    foreach($file in @('data.json','data.txt','captured-settings.json','request.json','model.json','unit-normalization.json')){
        $source=Join-Path $item.path "native/$file"
        if(!(Test-Path -LiteralPath $source)){if($file -eq 'unit-normalization.json'){continue};throw "Missing $source"}
        $name=if($file -eq 'request.json'){'native-request.json'}else{$file}
        Copy-Item -LiteralPath $source -Destination (Join-Path $folder $name)
    }
    $source=Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$item.lens)
    $cases+=[ordered]@{id=$id;key=$key;lens=$item.lens;configuration=$item.configuration;sourceSha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();baselineConclusion=$item.run.conclusion}
    $nativeFolder=Join-Path (Join-Path $repo $NativeRun) $id
    New-Item -ItemType Directory -Path $nativeFolder|Out-Null
    $nativeRequest=Join-Path $nativeFolder 'request.json'
    Copy-Item -LiteralPath (Join-Path $folder 'native-request.json') -Destination $nativeRequest
    $jobs+=[ordered]@{id=$id;request=$nativeRequest;output=$nativeFolder}
}
[ordered]@{originalPolicySha256=(Get-FileHash 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json').Hash.ToLowerInvariant();cases=$cases}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $Output 'cases.json') -Encoding utf8NoBOM
$files=@(Get-ChildItem -LiteralPath $Output -Recurse -File|Sort-Object FullName|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath((Join-Path $repo $Output),$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
[ordered]@{files=$files}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Output 'manifest.json') -Encoding utf8NoBOM
ConvertTo-Json -InputObject $jobs -Depth 8|Set-Content (Join-Path $NativeRun 'native-jobs.json') -Encoding utf8NoBOM
Write-Output "Prepared $($cases.Count) exact fixtures and native repeat jobs."
