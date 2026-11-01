param([Parameter(Mandatory)][string]$Study,[string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path;$root=(Resolve-Path -LiteralPath $Study).Path
$runner=Join-Path $repo 'tools/diagnostics/FftProfileNative20261029/bin/Release/net10.0/win-x64/FftProfileNative20261029.dll'
$jobs=Get-Content -LiteralPath "$root/jobs.json" -Raw|ConvertFrom-Json;$execution=@()
foreach($job in $jobs){
 $folder=Join-Path $root ('job-'+$job.id);if(Test-Path -LiteralPath $folder){throw 'Fresh native job directory required'}
 New-Item -ItemType Directory -Path $folder|Out-Null
 Copy-Item -LiteralPath (Join-Path $root $job.request) -Destination (Join-Path $folder $job.request)
 @($job)|ConvertTo-Json -AsArray -Depth 8|Set-Content -LiteralPath "$folder/jobs.json" -Encoding utf8
 $started=[DateTimeOffset]::UtcNow
 & dotnet $runner $repo $folder 'D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio'
 $exitCode=$LASTEXITCODE
 $actual=if(Test-Path -LiteralPath "$folder/execution.json"){@(Get-Content -LiteralPath "$folder/execution.json" -Raw|ConvertFrom-Json)[-1]}else{$null}
 if($null -ne $actual -and $actual.budgetSeconds -ne $job.budgetSeconds){throw 'Runner did not acknowledge the declared native budget'}
 $execution+=@{id=$job.id;exitCode=$exitCode;startedUtc=$started;completedUtc=[DateTimeOffset]::UtcNow;result="$folder/execution.json"
  requestedBudgetSeconds=$job.budgetSeconds;effectiveBudgetSeconds=$actual.budgetSeconds;elapsedSeconds=$actual.elapsedSeconds
  runnerSha256=(Get-FileHash -LiteralPath $runner).Hash.ToLowerInvariant();hostSha256=(Get-FileHash -LiteralPath "$folder/ZemaxHost.exe").Hash.ToLowerInvariant()}
 $execution|ConvertTo-Json -AsArray -Depth 8|Set-Content -LiteralPath "$root/independent-execution.json" -Encoding utf8
}
