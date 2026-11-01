param([string]$OutputRoot='validation/rechecks/2026-11-01/cemented-automatic/verified',
 [string]$ProductEvidence='validation/rechecks/2026-11-01/cemented-automatic/final')
$ErrorActionPreference='Stop';$repo=(Get-Location).Path
$output=[IO.Path]::GetFullPath($OutputRoot,$repo);$product=(Resolve-Path -LiteralPath $ProductEvidence).Path
if(Test-Path -LiteralPath $output){throw 'New verification scope already exists.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$tests=Join-Path $repo 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
$hostDll=Join-Path $repo 'tools/diagnostics/SpcDefaultOutput20261101/bin/Release/net10.0/SpcDefaultOutput20261101.dll'
$driver=Join-Path $repo 'tools/diagnostics/CementedAutomatic20261101/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll'
$before=@(Get-Content -LiteralPath (Join-Path $product 'default-runtime-before.json') -Raw|ConvertFrom-Json)
$handles=[Collections.Generic.List[IO.FileStream]]::new()
try{
    foreach($row in $before){foreach($ext in @('.dll','.pdb')){$handles.Add([IO.File]::Open((Join-Path $app ([IO.Path]::GetFileNameWithoutExtension($row.name)+$ext)),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))}
        if((Get-FileHash -LiteralPath (Join-Path $app $row.name)).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Default runtime changed; a fresh full build/test scope is required.'}
        if((Get-FileHash -LiteralPath (Join-Path $tests $row.name)).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Prior executed test copies no longer match.'}
    }
    [xml]$trx=Get-Content -LiteralPath (Join-Path $product 'tests/cemented-final.trx') -Raw
    if($trx.TestRun.ResultSummary.Counters.passed -ne '146' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0'){throw 'Prior regressions did not pass.'}
    $build=Get-Content -LiteralPath (Join-Path $product 'default-app-build.log') -Raw
    if(!$build.Contains('0 个警告') -or !$build.Contains('0 个错误')){throw 'Prior normal default build did not pass.'}
    dotnet build tools/diagnostics/CementedAutomatic20261101/CementedAutomatic20261101.csproj -c Release --no-restore -m:1 -nr:false *> (Join-Path $output 'driver-build.log')
    if($LASTEXITCODE -ne 0){throw 'Diagnostic build failed.'}
    Copy-Item -LiteralPath $driver -Destination (Join-Path $output 'scenario-driver.dll')
    Copy-Item -LiteralPath ($driver -replace '\.dll$','.pdb') -Destination (Join-Path $output 'scenario-driver.pdb')
    [pscustomobject]@{productEvidence=[IO.Path]::GetRelativePath($repo,$product);buildAndTestsReusedOnlyAfterExactEightAssemblyMatch=$true;
        productSourceAndRuntimeArchivesReused=$true;reason='Diagnostic native-readback assertion correction only; no product change since 146 passed tests.'}|ConvertTo-Json|Set-Content (Join-Path $output 'evidence-references.json')
    function Scenario([string]$audit,[string[]]$arguments){
        dotnet $hostDll $app (Join-Path $output 'scenario-driver.dll') (Join-Path $output ('runtime-audits/'+$audit+'.json')) @arguments
        if($LASTEXITCODE -ne 0){throw ('Scenario failed: '+$audit)}
    }
    foreach($seed in 42..46){
        $inputPath=Join-Path $product ('inputs/seed'+$seed+'.json')
        Scenario ('continuous-'+$seed) @('run',(Join-Path $output ('formal/continuous/seed'+$seed)),$inputPath,'none')
        foreach($stage in @('dispatch','local','full','pool','none')){
            Scenario ('resumed-'+$seed+'-'+$stage) @('run',(Join-Path $output ('formal/resumed/seed'+$seed)),$inputPath,$stage)
        }
        Scenario ('control-'+$seed) @('run',(Join-Path $output ('formal/control/seed'+$seed)),(Join-Path $product ('inputs/control'+$seed+'.json')),'none')
    }
    Scenario 'compare' @('compare',(Join-Path $output 'formal'))
    Scenario 'reopen' @('reopen',(Join-Path $output 'formal'),(Join-Path $output 'new-process-reopen.json'))
    Scenario 'guards' @('guards',(Join-Path $output 'original-tasks'))
    Scenario 'product-start' @('product-start',(Join-Path $output 'product-session'),(Join-Path $product 'inputs/seed42.json'))
    Scenario 'product-resume' @('product-resume',(Join-Path $output 'product-session'))
    Scenario 'product-reopen' @('product-reopen',(Join-Path $output 'product-session'))
    $after=@(foreach($row in $before){[pscustomobject]@{name=$row.name;sha256=(Get-FileHash -LiteralPath (Join-Path $app $row.name)).Hash.ToLowerInvariant()}})
    $after|ConvertTo-Json|Set-Content (Join-Path $output 'default-runtime-after.json')
    if(($before|ConvertTo-Json -Compress) -ne ($after|ConvertTo-Json -Compress)){throw 'Runtime drifted.'}
    [pscustomobject]@{defaultFullBuildPassed=$true;passedTests=146;defaultEqualsExecutedTestCopies=$true;
        readLeaseHeldDuringBothTestAndFinalScenarioScopes=$true;exactRuntimeMatchBetweenScopes=$true;unchangedBeforeAfter=$true;
        foregroundApplicationStarted=$false;userProcessesClosed=$false}|ConvertTo-Json|Set-Content (Join-Path $output 'verification-summary.json')
}finally{foreach($handle in $handles){$handle.Dispose()}}
