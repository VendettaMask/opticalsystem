param([string]$OutputRoot='validation/rechecks/2026-11-01/spc-default-output/final')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path
$output=[IO.Path]::GetFullPath($OutputRoot,$repo)
if(Test-Path -LiteralPath $output){throw 'Final verification scope already exists.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$tests=Join-Path $repo 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
$hostDll=Join-Path $repo 'tools/diagnostics/SpcDefaultOutput20261101/bin/Release/net10.0/SpcDefaultOutput20261101.dll'
$scenario='D:/Projects/.codex-build/SPC-automatic-20261101-frozen/tools/diagnostics/SpcAutomatic20261101/bin/Release/net10.0/win-x64/OpticalSystemDesign.InitialStructure.Tests.dll'
$names=@('OpticalSystemDesign.Core','OpticalSystemDesign.InitialStructure.Contracts','OpticalSystemDesign.InitialStructure.Engine',
 'OpticalSystemDesign.InitialStructure.Persistence','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine',
 'OpticalSystemDesign.Modules','OpticalSystemDesign.App')
$handles=[Collections.Generic.List[IO.FileStream]]::new()
try {
    dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore -m:1 -nr:false *> (Join-Path $output 'default-app-build.log')
    if($LASTEXITCODE -ne 0){throw 'Final default App build failed.'}
    foreach($name in $names){foreach($extension in @('.dll','.pdb')){
        $file=Join-Path $app ($name+$extension)
        $handles.Add([IO.File]::Open($file,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
    }}
    $before=@(foreach($name in $names){$file=Join-Path $app ($name+'.dll');[pscustomobject]@{name=$name+'.dll';sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $before|ConvertTo-Json|Set-Content (Join-Path $output 'default-runtime-before.json')
    foreach($name in $names){foreach($extension in @('.dll','.pdb')){
        Copy-Item -LiteralPath (Join-Path $app ($name+$extension)) -Destination (Join-Path $tests ($name+$extension)) -Force
    }}
    $env:OPTICAL_SYSTEM_DESIGN_TEST_ROOT=$repo
    dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~Spc|FullyQualifiedName~CementedTopologyTests|FullyQualifiedName~AutonomousDesignReviewTests|FullyQualifiedName~ColdEntryControls|FullyQualifiedName~PreparationCheckpoint|FullyQualifiedName~GateCompensationTests|FullyQualifiedName~FixedTopologyTests' --logger 'trx;LogFileName=spc-default-final.trx' --results-directory (Join-Path $output 'tests') *> (Join-Path $output 'default-tests.log')
    if($LASTEXITCODE -ne 0){throw 'Final default-output tests failed.'}
    foreach($row in $before){if((Get-FileHash -LiteralPath (Join-Path $tests $row.name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Executed test copies differ from default output.'}}
    function Invoke-Scenario([string]$audit,[string[]]$scenarioArgs){
        & dotnet $hostDll $app $scenario (Join-Path $output ('runtime-audits/'+$audit+'.json')) @scenarioArgs
        if($LASTEXITCODE -ne 0){throw ('Default-output scenario failed: '+$audit)}
    }
    Invoke-Scenario 'freeze' @('freeze',(Join-Path $output 'inputs'))
    foreach($seed in 42..46){
        $inputPath=Join-Path $output ('inputs/seed'+$seed+'.json')
        Invoke-Scenario ('continuous-'+$seed) @('run',(Join-Path $output ('formal/continuous/seed'+$seed)),$inputPath,'none')
        foreach($stage in @('parent','hessian','minus','minus-physical','plus','plus-physical','none')){
            Invoke-Scenario ('resumed-'+$seed+'-'+$stage) @('run',(Join-Path $output ('formal/resumed/seed'+$seed)),$inputPath,$stage)
        }
    }
    Invoke-Scenario 'compare' @('compare',(Join-Path $output 'formal'))
    Invoke-Scenario 'reopen' @('reopen',(Join-Path $output 'formal'),(Join-Path $output 'new-process-reopen.json'))
    Invoke-Scenario 'guards' @('guards',(Join-Path $output 'original-tasks'))
    Invoke-Scenario 'product-start' @('product-start',(Join-Path $output 'product-session'),(Join-Path $output 'inputs/seed42.json'))
    Invoke-Scenario 'product-resume' @('product-resume',(Join-Path $output 'product-session'))
    Invoke-Scenario 'product-reopen' @('product-reopen',(Join-Path $output 'product-session'))
    & (Join-Path $repo 'tools/diagnostics/SpcDefaultOutput20261101/Archive-Default.ps1') -EvidenceRoot $output *> (Join-Path $output 'archive-default.log')
    $after=@(foreach($name in $names){$file=Join-Path $app ($name+'.dll');[pscustomobject]@{name=$name+'.dll';sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $after|ConvertTo-Json|Set-Content (Join-Path $output 'default-runtime-after.json')
    if(($before|ConvertTo-Json -Compress) -ne ($after|ConvertTo-Json -Compress)){throw 'Default runtime drifted under the read lease.'}
    [pscustomobject]@{defaultFullBuildPassed=$true;productAssemblies=8;defaultEqualsExecutedTestCopies=$true;unchangedBeforeAfter=$true;
        readOnlyLeaseHeldDuringVerification=$true;foregroundApplicationStarted=$false;nativeUserProcessesClosed=$false}|ConvertTo-Json|Set-Content (Join-Path $output 'verification-summary.json')
}finally{foreach($handle in $handles){$handle.Dispose()}}
