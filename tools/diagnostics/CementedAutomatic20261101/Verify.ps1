param([string]$OutputRoot='validation/rechecks/2026-11-01/cemented-automatic/final')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path;$output=[IO.Path]::GetFullPath($OutputRoot,$repo)
if(Test-Path -LiteralPath $output){throw 'Verification scope already exists.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$tests=Join-Path $repo 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
$hostDll=Join-Path $repo 'tools/diagnostics/SpcDefaultOutput20261101/bin/Release/net10.0/SpcDefaultOutput20261101.dll'
$driver=Join-Path $repo 'tools/diagnostics/CementedAutomatic20261101/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll'
$names=@('OpticalSystemDesign.Core','OpticalSystemDesign.InitialStructure.Contracts','OpticalSystemDesign.InitialStructure.Engine',
 'OpticalSystemDesign.InitialStructure.Persistence','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine','OpticalSystemDesign.Modules','OpticalSystemDesign.App')
$handles=[Collections.Generic.List[IO.FileStream]]::new()
try {
    dotnet build labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests -c Release --no-restore -m:1 -nr:false *> (Join-Path $output 'test-build.log')
    if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
    dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore -m:1 -nr:false *> (Join-Path $output 'default-app-build.log')
    if($LASTEXITCODE -ne 0){throw 'Normal default App full build failed.'}
    foreach($name in $names){foreach($ext in @('.dll','.pdb')){$handles.Add([IO.File]::Open((Join-Path $app ($name+$ext)),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))}}
    $before=@(foreach($name in $names){[pscustomobject]@{name=$name+'.dll';sha256=(Get-FileHash -LiteralPath (Join-Path $app ($name+'.dll'))).Hash.ToLowerInvariant()}})
    $before|ConvertTo-Json|Set-Content (Join-Path $output 'default-runtime-before.json')
    foreach($name in $names){foreach($ext in @('.dll','.pdb')){Copy-Item -LiteralPath (Join-Path $app ($name+$ext)) -Destination (Join-Path $tests ($name+$ext)) -Force}}
    & tools/diagnostics/SpcDefaultOutput20261101/Archive-Default.ps1 -EvidenceRoot $output *> (Join-Path $output 'archive.log')
    dotnet build tools/diagnostics/CementedAutomatic20261101/CementedAutomatic20261101.csproj -c Release --no-restore -m:1 -nr:false *> (Join-Path $output 'driver-build.log')
    if($LASTEXITCODE -ne 0){throw 'Diagnostic driver build failed.'}
    Copy-Item -LiteralPath $driver -Destination (Join-Path $output 'scenario-driver.dll')
    Copy-Item -LiteralPath ($driver -replace '\.dll$','.pdb') -Destination (Join-Path $output 'scenario-driver.pdb')
    $env:OPTICAL_SYSTEM_DESIGN_TEST_ROOT=$repo
    dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~Cemented|FullyQualifiedName~AutonomousSearchTests|FullyQualifiedName~TopologyExpansionTests|FullyQualifiedName~AutonomousAxialCompensationTests|FullyQualifiedName~AutonomousDesignReviewTests|FullyQualifiedName~Spc|FullyQualifiedName~PreparationCheckpoint|FullyQualifiedName~GateCompensationTests|FullyQualifiedName~FixedTopologyTests' --logger 'trx;LogFileName=cemented-final.trx' --results-directory (Join-Path $output 'tests') *> (Join-Path $output 'tests.log')
    if($LASTEXITCODE -ne 0){throw 'Focused regression failed.'}
    foreach($row in $before){if((Get-FileHash -LiteralPath (Join-Path $tests $row.name)).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Test runtime differs from actual default output.'}}
    function Scenario([string]$audit,[string[]]$arguments){
        dotnet $hostDll $app (Join-Path $output 'scenario-driver.dll') (Join-Path $output ('runtime-audits/'+$audit+'.json')) @arguments
        if($LASTEXITCODE -ne 0){throw ('Scenario failed: '+$audit)}
    }
    Scenario 'freeze' @('freeze',(Join-Path $output 'inputs'))
    foreach($seed in 42..46){
        $inputPath=Join-Path $output ('inputs/seed'+$seed+'.json')
        Scenario ('continuous-'+$seed) @('run',(Join-Path $output ('formal/continuous/seed'+$seed)),$inputPath,'none')
        foreach($stage in @('dispatch','local','full','pool','none')){
            Scenario ('resumed-'+$seed+'-'+$stage) @('run',(Join-Path $output ('formal/resumed/seed'+$seed)),$inputPath,$stage)
        }
        Scenario ('control-'+$seed) @('run',(Join-Path $output ('formal/control/seed'+$seed)),(Join-Path $output ('inputs/control'+$seed+'.json')),'none')
    }
    Scenario 'compare' @('compare',(Join-Path $output 'formal'))
    Scenario 'reopen' @('reopen',(Join-Path $output 'formal'),(Join-Path $output 'new-process-reopen.json'))
    Scenario 'guards' @('guards',(Join-Path $output 'original-tasks'))
    Scenario 'product-start' @('product-start',(Join-Path $output 'product-session'),(Join-Path $output 'inputs/seed42.json'))
    Scenario 'product-resume' @('product-resume',(Join-Path $output 'product-session'))
    Scenario 'product-reopen' @('product-reopen',(Join-Path $output 'product-session'))
    $completed=(Get-Content -LiteralPath (Join-Path $output 'formal/continuous/seed42/run.autonomous.json') -Raw|ConvertFrom-Json).checkpoint
    $chain=$completed.plan
    $chain.seeds=@(($completed.candidates|Where-Object{$null -ne $_.topology.cementedInterfaces}|Select-Object -First 1).topology)
    $chain.options.expansion.cemented.maximumGroupElements=3;$chain.options.maximumTrials=1
    $chain|ConvertTo-Json -Depth 100|Set-Content (Join-Path $output 'chain-input.json')
    Scenario 'chain-continuous' @('run',(Join-Path $output 'chain/continuous'),(Join-Path $output 'chain-input.json'),'none')
    foreach($stage in @('dispatch','full','none')){
        Scenario ('chain-resumed-'+$stage) @('run',(Join-Path $output 'chain/resumed'),(Join-Path $output 'chain-input.json'),$stage)
    }
    Scenario 'chain-reopen' @('reopen',(Join-Path $output 'chain'),(Join-Path $output 'chain-reopen.json'))
    $after=@(foreach($name in $names){[pscustomobject]@{name=$name+'.dll';sha256=(Get-FileHash -LiteralPath (Join-Path $app ($name+'.dll'))).Hash.ToLowerInvariant()}})
    $after|ConvertTo-Json|Set-Content (Join-Path $output 'default-runtime-after.json')
    if(($before|ConvertTo-Json -Compress) -ne ($after|ConvertTo-Json -Compress)){throw 'Default runtime changed during verification.'}
    [pscustomobject]@{defaultFullBuildPassed=$true;readLeaseHeld=$true;defaultEqualsExecutedTestCopies=$true;unchangedBeforeAfter=$true;
        foregroundApplicationStarted=$false;userProcessesClosed=$false}|ConvertTo-Json|Set-Content (Join-Path $output 'verification-summary.json')
}finally{foreach($handle in $handles){$handle.Dispose()}}
