param([Parameter(Mandatory)][string]$Study,[string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=(Resolve-Path -LiteralPath $Study).Path
$runtime=Join-Path $root 'runtime/IncomparableRepair20261101.dll'
$core=Join-Path $root 'runtime/OpticalSystemDesign.Core.dll'
$coreHash=(Get-FileHash -LiteralPath $core).Hash.ToLowerInvariant()
$execution=@()
foreach($request in @(Get-Content -LiteralPath "$root/workbench-jobs.json" -Raw|ConvertFrom-Json)){
 $job=Get-Content -LiteralPath $request -Raw|ConvertFrom-Json
 if(Test-Path -LiteralPath $job.outputDirectory){throw 'Fresh Core output required'}
 $id=[IO.Path]::GetFileNameWithoutExtension($request)
 $dispatch=Join-Path $root "dispatch/$id"
 if(Test-Path -LiteralPath $dispatch){throw 'Fresh dispatch directory required'}
 New-Item -ItemType Directory -Path $dispatch|Out-Null
 @($request)|ConvertTo-Json -AsArray|Set-Content -LiteralPath "$dispatch/workbench-jobs.json" -Encoding utf8
 $started=[DateTimeOffset]::UtcNow
 & dotnet $runtime execute $repo $dispatch > "$dispatch/stdout.txt" 2> "$dispatch/stderr.txt"
 $code=$LASTEXITCODE
 if((Get-FileHash -LiteralPath $core).Hash.ToLowerInvariant() -ne $coreHash){throw 'Frozen execution Core drifted'}
 $execution+=@{id=$id;request=$request;requestSha256=(Get-FileHash -LiteralPath $request).Hash.ToLowerInvariant()
  startedUtc=$started;completedUtc=[DateTimeOffset]::UtcNow;exitCode=$code;coreSha256=$coreHash
  complete=($code -eq 0 -and (Test-Path -LiteralPath "$($job.outputDirectory)/data.json") -and (Test-Path -LiteralPath "$($job.outputDirectory)/provenance.json"))}
 $execution|ConvertTo-Json -AsArray -Depth 8|Set-Content -LiteralPath "$root/execution.json" -Encoding utf8
 Write-Output "$id : exit $code"
}
