$ErrorActionPreference='Stop'
$taskEvidence='artifacts/validation/zemax-ra256-single-ray-20261007'
$taskRoot='artifacts/zemax-standard-samples/20261007'
$taskReplay='artifacts/zemax-standard-samples/20261006/core-replay/bin/Release/net10.0/Replay.dll'
$taskVerifier='artifacts/zemax-standard-samples/20261007/ra256-verifier/bin/Release/net10.0/Verifier.dll'
function Write-TaskPhase([string]$taskPhase){
    @{phase=$taskPhase;utc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content (Join-Path $taskEvidence 'phase.json') -Encoding utf8NoBOM
    Write-Output $taskPhase
}
foreach($taskCase in @('tessar-ra256-remove-streamed','tessar-ra256-retain','cooke-40-degree-field-ra256-remove','double-gauss-28-degree-field-ra256-remove','relay-lens-ra256-remove','even-asphere-ra256-remove')){
    Write-TaskPhase "Fresh source per-surface verification: $taskCase"
    & dotnet $taskVerifier (Join-Path $taskRoot "ra256-ray-audit/$taskCase") --import-source > (Join-Path $taskEvidence "$taskCase-after.log")
    if($LASTEXITCODE -ne 0){throw "Fresh-source ray verifier failed: $taskCase"}
}
$taskRuns=@(
    @{summary='artifacts/zemax-standard-samples/20261006/STANDARD_SAMPLE_COMPARISON_SUMMARY.json';name='ra256-single-ray-old-snapshot-original132';import=$false},
    @{summary='artifacts/zemax-standard-samples/20261006/STANDARD_SAMPLE_COMPARISON_SUMMARY.json';name='ra256-single-ray-fresh-import-original132';import=$true},
    @{summary=(Join-Path $taskRoot 'RMS_CONTROL_SUMMARY.json');name='ra256-single-ray-old-snapshot-controls17';import=$false},
    @{summary=(Join-Path $taskRoot 'RMS_CONTROL_SUMMARY.json');name='ra256-single-ray-fresh-import-controls17';import=$true},
    @{summary=(Join-Path $taskRoot 'RA256_CONTROL_SUMMARY.json');name='ra256-single-ray-old-snapshot-controls6';import=$false},
    @{summary=(Join-Path $taskRoot 'RA256_CONTROL_SUMMARY.json');name='ra256-single-ray-fresh-import-controls6';import=$true}
)
foreach($taskRun in $taskRuns){
    Write-TaskPhase "Immutable native reference replay: $($taskRun.name)"
    if($taskRun.import){ & dotnet $taskReplay $taskRun.summary (Join-Path $taskRoot $taskRun.name) --import-source > (Join-Path $taskEvidence "$($taskRun.name).log") }
    else { & dotnet $taskReplay $taskRun.summary (Join-Path $taskRoot $taskRun.name) > (Join-Path $taskEvidence "$($taskRun.name).log") }
    if($LASTEXITCODE -ne 0){throw "Replay failed: $($taskRun.name)"}
}
Write-TaskPhase 'Complete comparison-tool Release regression'
& dotnet test tests/OptilandWorkbench.ZemaxComparison.Tests/OptilandWorkbench.ZemaxComparison.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=tool-current-release.trx' --results-directory $taskEvidence > (Join-Path $taskEvidence 'tool-current-release.log')
$taskToolExit=$LASTEXITCODE
if($taskToolExit -gt 1){throw 'Tool infrastructure failed'}
Write-TaskPhase 'Comparison-tool Debug targeted regression'
& dotnet test tests/OptilandWorkbench.ZemaxComparison.Tests/OptilandWorkbench.ZemaxComparison.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~CapturedRmsFieldSamplingTests|FullyQualifiedName~RmsFieldSamplingContractTests|FullyQualifiedName~RayAuditContractTests|FullyQualifiedName~CapturedRa256SamplingTests|FullyQualifiedName~JsonFileStreamingTests' --logger 'trx;LogFileName=tool-targeted-debug-final.trx' --results-directory $taskEvidence > (Join-Path $taskEvidence 'tool-targeted-debug-final.log')
if($LASTEXITCODE -ne 0){throw 'Tool targeted Debug regression failed'}
Write-TaskPhase 'Complete formal Release regression'
& dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=main-current-release.trx' --results-directory $taskEvidence > (Join-Path $taskEvidence 'main-current-release.log')
$taskMainExit=$LASTEXITCODE
if($taskMainExit -gt 1){throw 'Formal infrastructure failed'}
Write-TaskPhase 'All numerical replays and regression runs completed; validate actual conclusions and failures'
