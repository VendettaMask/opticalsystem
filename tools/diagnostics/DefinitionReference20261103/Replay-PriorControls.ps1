param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository)
$authority=Join-Path $repo 'validation/rechecks/2026-11-03/residual-closure'
$output=Join-Path $repo 'artifacts/validation/definition-prior-inputs-20261103'
if(Test-Path $output){throw 'Fresh inputs required'}
$index=Get-Content "$authority/archive-index.json" -Raw|ConvertFrom-Json
$lookup=@{};foreach($entry in $index){$lookup[$entry.logicalPath]=$entry}
function Restore($logical,$target){
 $entry=$lookup[$logical];if(!$entry){throw "Missing authority: $logical"}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
 $path=[IO.Path]::GetFullPath((Join-Path $authority $entry.archive))
 if($entry.entry){$zip=[IO.Compression.ZipFile]::OpenRead($path);try{$input=$zip.GetEntry($entry.entry).Open();$stream=[IO.File]::Create($target);try{$input.CopyTo($stream)}finally{$stream.Dispose();$input.Dispose()}}finally{$zip.Dispose()}}
 else{[IO.File]::Copy($path,$target)}
 if((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $entry.sha256 -or (Get-Item $target).Length -ne $entry.length){throw 'Restored input differs'}
}
$plan=@()
foreach($id in @(108,1785)){foreach($n in @(256,512)){
 $study=if($id -eq 108){'residual-closure-native1-20261103'}else{'residual-closure-native2-20261103'}
 $folder="$output/$id-$n/native"
 foreach($name in @('data.json','captured-settings.json')){Restore "runs/$study/$id-$n/native/$name" "$folder/$name"}
 $request=(Get-Content "$folder/captured-settings.json" -Raw|ConvertFrom-Json).request
 $job="$output/$id-$n/request.json"
 @{sourceSnapshot='reimported';outputDirectory='replaced';request=$request}|ConvertTo-Json -Depth 40|Set-Content $job
 $plan+=@{index=$id;sampling=$n;native=$folder;reference="$output/$id-$(768-$n)/native";coreRequest=$job;coreReference=$(if($n -eq 512){"$repo/artifacts/validation/definition-prior-convergence-20261103/$id-256"}else{$null})}
}}
$plan|ConvertTo-Json -Depth 10|Set-Content "$repo/artifacts/validation/definition-prior-convergence-plan-20261103.json"
& dotnet exec --additionalprobingpath C:/Users/19851/.nuget/packages "$repo/artifacts/validation/definition-final-runtime-20261103/ResidualClosure20261103.dll" compare $repo "$repo/artifacts/validation/definition-prior-convergence-20261103" "$repo/artifacts/validation/definition-prior-convergence-plan-20261103.json"
if($LASTEXITCODE -ne 0){throw 'Prior convergence replay failed'}
