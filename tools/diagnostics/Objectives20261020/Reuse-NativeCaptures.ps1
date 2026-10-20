param([Parameter(Mandatory)][string[]]$Before, [Parameter(Mandatory)][string]$After)
$ErrorActionPreference='Stop'
$new=(Resolve-Path -LiteralPath $After).Path
$oldJobs=@{}
foreach($directory in $Before) {
    $old=(Resolve-Path -LiteralPath $directory).Path
    if((Get-FileHash (Join-Path $old 'comparison-settings.json')).Hash -ne (Get-FileHash (Join-Path $new 'comparison-settings.json')).Hash){throw 'Comparison policy changed'}
    foreach($job in @(Get-Content (Join-Path $old 'native-jobs.json') -Raw | ConvertFrom-Json)) {
        $completion=Join-Path $job.output 'batch-result.json'
        if((Test-Path -LiteralPath $completion) -and (Get-Content $completion -Raw | ConvertFrom-Json).exitCode -eq 0) {
            $oldJobs[($job.id -replace '^\d{4}-','')]=$job
        }
    }
}
$reused=@(); $pending=@()
foreach($job in @(Get-Content (Join-Path $new 'native-jobs.json') -Raw | ConvertFrom-Json)) {
    $prior=$oldJobs[($job.id -replace '^\d{4}-','')]
    if(!$prior){$pending+=$job;continue}
    $left=Get-Content -LiteralPath $prior.request -Raw | ConvertFrom-Json
    $right=Get-Content -LiteralPath $job.request -Raw | ConvertFrom-Json
    $sourceHash=(Get-FileHash -LiteralPath $left.input).Hash
    if($sourceHash -ne (Get-FileHash -LiteralPath $right.input).Hash){throw 'Source bytes differ'}
    $left.PSObject.Properties.Remove('input'); $right.PSObject.Properties.Remove('input')
    if(($left | ConvertTo-Json -Depth 70 -Compress) -cne ($right | ConvertTo-Json -Depth 70 -Compress)){$pending+=$job;continue}
    $completion=Join-Path $prior.output 'batch-result.json'
    if(!(Test-Path -LiteralPath $completion) -or (Get-Content $completion -Raw | ConvertFrom-Json).exitCode -ne 0){$pending+=$job;continue}
    if(Test-Path -LiteralPath (Join-Path $job.output 'batch-result.json')){throw 'Destination already completed'}
    $files=@()
    foreach($file in Get-ChildItem -LiteralPath $prior.output -File | Where-Object Name -ne 'request.json'){
        $destination=Join-Path $job.output $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $hash=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
        if((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $hash){throw 'Capture copy mismatch'}
        $files+=@{name=$file.Name;sha256=$hash}
    }
    $reused+=@{id=$job.id;original=$prior.output;destination=$job.output;sourceSha256=$sourceHash.ToLowerInvariant();allNonPathRequestPropertiesExact=$true;files=$files}
}
[ordered]@{reason='Reuse successful native outputs only when original source bytes and all request properties except the workspace input path are identical. Raw arrays/settings/environment/completion records copied byte-for-byte; originals retained.';count=$reused.Count;pending=$pending.Count;jobs=$reused} |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $new 'native-reuse.json') -Encoding utf8
$pendingRoot=Join-Path $new 'native-pending'
New-Item -ItemType Directory -Path $pendingRoot | Out-Null
ConvertTo-Json -InputObject $pending -Depth 10 | Set-Content -LiteralPath (Join-Path $pendingRoot 'native-jobs.json') -Encoding utf8
Write-Output "Reused $($reused.Count) verified native captures; pending $($pending.Count)."
