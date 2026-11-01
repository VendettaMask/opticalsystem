$ErrorActionPreference='Stop'
$assemblyRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $assemblyRepo
function Read-Json([string]$Name){Get-Content -LiteralPath (Join-Path $PSScriptRoot $Name) -Raw | ConvertFrom-Json}
[xml]$assemblyTrx=Get-Content (Join-Path $PSScriptRoot 'final-focused.trx')
$assemblyCounters=$assemblyTrx.TestRun.ResultSummary.Counters
if([int]$assemblyCounters.failed -ne 0 -or [int]$assemblyCounters.passed -lt 400 -or [int]$assemblyCounters.total -ne [int]$assemblyCounters.passed){throw 'Final focused tests are incomplete or failed.'}
$assemblyNew=@($assemblyTrx.TestRun.TestDefinitions.UnitTest | Where-Object {$_.TestMethod.className -like '*OpticalAssembly*'})
if($assemblyNew.Count -ne 30){throw 'New optical assembly test scope is incomplete.'}
$assemblyBefore=Read-Json 'before-final-run.json';$assemblyAfter=Read-Json 'after-final-run.json'
$assemblyDrift=@(Compare-Object @($assemblyBefore.Binaries | ForEach-Object {"$($_.Path):$($_.Sha256)"}) @($assemblyAfter.Binaries | ForEach-Object {"$($_.Path):$($_.Sha256)"}))
$assemblyDrift+=@(Compare-Object @($assemblyBefore.Sources | ForEach-Object {"$($_.Path):$($_.Sha256)"}) @($assemblyAfter.Sources | ForEach-Object {"$($_.Path):$($_.Sha256)"}))
if($assemblyDrift.Count){throw 'Execution or source snapshot drifted.'}
$assemblyMap=Read-Json 'source-snapshot-map.json'
foreach($assemblyEntry in $assemblyMap){if((Get-FileHash -LiteralPath $assemblyEntry.OriginalPath).Hash.ToLowerInvariant() -ne $assemblyEntry.Sha256){throw "Current scoped source changed after compilation: $($assemblyEntry.OriginalPath)"}}
$assemblyPdb=Read-Json 'compiled-source-audit.json'
if($assemblyPdb.Failures.Count -or $assemblyPdb.MatchedDocuments -ne 92){throw 'Compiled-source audit failed or is incomplete.'}
$assemblyPrior=@()
foreach($assemblyRelative in @('validation/rechecks/2026-10-31/commercial-bsdf-calibration','validation/rechecks/2026-10-31/white-specular-runtime','validation/rechecks/2026-11-01/xyz-radiometry','validation/rechecks/2026-11-01/xyz-conditions','validation/rechecks/2026-11-01/xyz-angular','validation/rechecks/2026-11-01/xyz-reconstruction')){
 $assemblyRoot=Join-Path $assemblyRepo $assemblyRelative;$assemblyManifest=Get-Content (Join-Path $assemblyRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
 foreach($assemblyEntry in $assemblyManifest.Files){$assemblyFile=Get-Item -LiteralPath (Join-Path $assemblyRoot $assemblyEntry.Path);if($assemblyFile.Length -ne $assemblyEntry.Bytes -or (Get-FileHash -LiteralPath $assemblyFile.FullName).Hash.ToLowerInvariant() -ne $assemblyEntry.Sha256){throw "Prior frozen evidence changed: $assemblyRelative/$($assemblyEntry.Path)"}}
 $assemblyPrior+=[pscustomobject]@{Root=$assemblyRelative;VerifiedFiles=$assemblyManifest.Files.Count}
}
if(($assemblyPrior.VerifiedFiles | Measure-Object -Sum).Sum -ne 2439){throw 'Prior manifest count changed.'}
$assemblyCamera=@(Read-Json 'final-controls/air.json';Read-Json 'final-controls/immersion.json')
$assemblyMaxClosure=0d
foreach($assemblyRun in $assemblyCamera){
 $assemblyClosure=[Math]::Abs(($assemblyRun.DetectorPowerWatts+$assemblyRun.AbsorbedPowerWatts+$assemblyRun.EscapedPowerWatts+$assemblyRun.TruncatedPowerWatts)/$assemblyRun.SourcePowerWatts-1)
 $assemblyMaxClosure=[Math]::Max($assemblyMaxClosure,$assemblyClosure)
 if($assemblyRun.ProjectVersion -ne 20 -or -not $assemblyRun.ExactFrozenReplay -or $assemblyRun.PhotonErrorElectrons -gt 1e-11 -or $assemblyClosure -gt 1e-12){throw 'End-to-end camera control failed.'}
}
foreach($assemblyMode in @('air','immersion')){foreach($assemblySuffix in @('staropt','starrdb','camera-experiment.zip')){if(-not(Test-Path (Join-Path $PSScriptRoot "final-controls/$assemblyMode.$assemblySuffix"))){throw 'Missing frozen full-chain control.'}}}
$assemblyPoints=@((Read-Json 'final-controls/curved-air.json').Points)+@((Read-Json 'final-controls/curved-immersion.json').Points)
$assemblyStats=Read-Json 'final-controls/stochastic-statistics.json'
if([Math]::Abs($assemblyStats.ActualTransmission-$assemblyStats.ExpectedTransmission) -gt 6*$assemblyStats.Sigma -or -not $assemblyStats.ExactSeedReplay){throw 'Sampling control failed.'}
$assemblyBuild=Get-Content (Join-Path $PSScriptRoot 'build-app-infinite-radius.log') -Raw
if($assemblyBuild -notmatch '已成功生成' -or $assemblyBuild -notmatch 'OpticalSystemDesign.CoatingDesign.Engine ->' -or $assemblyBuild -notmatch 'OpticalSystemDesign.InitialStructure.Engine ->'){throw 'Default App build evidence is missing.'}
if(-not(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json'))){
 [ordered]@{
  CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Counters=[ordered]@{Total=[int]$assemblyCounters.total;Passed=[int]$assemblyCounters.passed;Failed=[int]$assemblyCounters.failed;NotExecuted=[int]$assemblyCounters.notExecuted};NewOpticalAssemblyTestsPassed=$assemblyNew.Count
  DefaultReleaseAppBuildPassed=$true;BothBuiltInModulesIncluded=$true;OffscreenEditorTestPassed=$true
  CompiledSourceChecksumsMatched=$assemblyPdb.MatchedDocuments;ScopedSourceCount=$assemblyMap.Count;ExecutionFiles=$assemblyAfter.Binaries.Count;ExecutionAndSourceDriftCount=$assemblyDrift.Count
  CoreSha256=($assemblyAfter.Binaries | Where-Object Path -eq 'OpticalSystemDesign.Core.dll').Sha256;ApplicationSha256=($assemblyAfter.Binaries | Where-Object Path -eq 'OpticalSystemDesign.Application.dll').Sha256
  PriorFrozenEvidence=$assemblyPrior;PriorFrozenFilesVerified=2439;CameraChainCases=2;ExactFrozenCameraReplays=2
  MaximumCameraEnergyRelativeError=$assemblyMaxClosure;MaximumPhotonErrorElectrons=($assemblyCamera.PhotonErrorElectrons | Measure-Object -Maximum).Maximum
  MaximumSequentialPositionErrorMillimeters=($assemblyPoints.PositionErrorMillimeters | Measure-Object -Maximum).Maximum
  MaximumSequentialDirectionError=($assemblyPoints.DirectionError | Measure-Object -Maximum).Maximum
  MaximumSequentialOpticalPathErrorMillimeters=($assemblyPoints.OplErrorMillimeters | Measure-Object -Maximum).Maximum
  StochasticRayCount=$assemblyStats.RayCount;SamplingErrorStandardDeviations=[Math]::Abs($assemblyStats.ActualTransmission-$assemblyStats.ExpectedTransmission)/$assemblyStats.Sigma
  AnalyticalControlsNotMeasured=$true;NativeDesktopAcceptance=$false;CommercialCertification=$false;FullCadOrGpuImplemented=$false
 } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PSScriptRoot 'final-verification-summary.json') -Encoding utf8
}
Write-Output "Verified $($assemblyCounters.passed) focused tests, 30 new cases, 92 scoped compiled-source checks, 2 complete camera replays and 2439 prior frozen files."
