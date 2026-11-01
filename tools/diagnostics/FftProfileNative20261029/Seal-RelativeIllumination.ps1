param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation'
$destination=Join-Path $root 'validation/rechecks/2026-10-30/afocal-ri'
function ReadJson([string]$p){Get-Content -Raw -LiteralPath $p|ConvertFrom-Json}
function Require([bool]$v,[string]$message){if(-not $v){throw $message}}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function Relative([string]$p){[IO.Path]::GetRelativePath($root,$p).Replace('\','/')}
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not(Test-Path -LiteralPath $archivePath)) 'Never overwrite sealed evidence.'
$dependencies=@(
    @{path='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip';sha256='235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1'},
    @{path='validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip';sha256='8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1'},
    @{path='validation/rechecks/2026-10-30/afocal-auto-grid/evidence.zip';sha256='b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f'},
    @{path='validation/rechecks/2026-10-30/riflescope-pupil/evidence.zip';sha256='f4e6fef82110d8b6bde01a6d85ea72dbf7613e480991751e79b1f5a423bace4a'},
    @{path='validation/rechecks/2026-10-30/centroid-energy/evidence.zip';sha256='a7c6cded1d3d98546a1aad0aa2bc42290d3fcd5f0c3688a8382f107e6d82d997'})
foreach($d in $dependencies){Require ((Hash (Join-Path $root $d.path)) -ceq $d.sha256) 'Frozen dependency changed.'}
$before=@(ReadJson (Join-Path $artifacts 'afocal-ri-20261030-before/summary.json'))
$after=@(ReadJson (Join-Path $artifacts 'afocal-ri-20261030-final-02/summary.json'))
$manifest=ReadJson (Join-Path $destination 'original-requests.json')
Require ($before.Count -eq 5 -and $after.Count -eq 5 -and $manifest.cases.Count -eq 5) 'Wrong request count.'
Require (@($before|Where-Object {$_.result.outcome -ceq 'Exception'}).Count -eq 2) 'Wrong before exception count.'
Require (@($before|Where-Object {$_.result.outcome -ceq 'Success'}).Count -eq 3) 'Wrong before numeric count.'
$native=@()
for($i=0;$i -lt 5;$i++){
    $a=$after[$i];$b=$before[$i];$m=$manifest.cases[$i]
    foreach($property in 'id','fingerprint','sourceSha256','snapshotSha256'){
        Require ($a.$property -ceq $b.$property -and $a.$property -ceq $m.$property) ('Original identity changed: '+$property)
    }
    Require ($a.result.outcome -ceq 'NotApplicable' -and $a.result.curves -eq 0 -and -not $a.result.numericRiPresent -and -not $a.formalNumericalPass) 'Incorrect applicability result.'
    Require (@($a.originalEffectiveTolerances.PSObject.Properties).Count -eq 0 -and @($b.originalEffectiveTolerances.PSObject.Properties).Count -eq 0) 'Original empty tolerance record changed.'
    $folder=Join-Path $artifacts ('afocal-ri-20261030-native-01/'+$a.id)
    $r=ReadJson (Join-Path $artifacts ('afocal-ri-20261030-native-01/'+$a.id+'.request.json'));$s=ReadJson (Join-Path $folder 'captured-settings.json')
    $d=ReadJson (Join-Path $folder 'relative-illumination-diagnostic.json');$buffers=ReadJson (Join-Path $folder 'result-diagnostics.json')
    Require ($r.originalRequestFingerprint -ceq $a.fingerprint -and $r.inputSha256 -ceq $a.sourceSha256 -and (Hash $r.input) -ceq $a.sourceSha256) 'Original native input or request differs.'
    Require ($r.configuration -eq $a.request.configuration -and $r.wavelength -eq $a.request.wavelength -and $r.imageSpaceAfocal -and -not $r.captureScreenshots -and -not $r.formalAcceptanceRequest) 'Original selectors differ.'
    foreach($p in $a.request.zemaxSettings.PSObject.Properties){
        $readback=if($p.Name -eq 'Wavelength'){$s.selectors.Wavelength}else{$s.properties.($p.Name)}
        Require ([string]$r.zemaxSettings.($p.Name) -ceq [string]$p.Value -and [string]$readback -ceq [string]$p.Value) ('Native setting differs: '+$p.Name)
    }
    Require ($d.afocalImageSpace -and -not $d.nativeNumericalBuffersAvailable -and -not $d.formalOpticalPass -and -not $d.savedInput) 'Invalid native diagnostic scope.'
    Require ($buffers.seriesCount -eq 1 -and $buffers.buffers.Count -eq 1 -and -not $buffers.buffers[0].xBuffer -and -not $buffers.buffers[0].yBuffer) 'Unexpected native buffers.'
    Require ((Get-Content -Raw -LiteralPath (Join-Path $folder 'data.txt')) -match '无法计算相对照度') 'Missing native rejection text.'
    Require (-not(Test-Path -LiteralPath (Join-Path $folder 'data.json'))) 'Fabricated numerical reference.'
    $old=ReadJson (Join-Path $artifacts ('afocal-ri-20261030-before/'+$a.id+'/original-native-error.json'))
    Require ($old.errorCode -ceq 'NativeInvalidResult') 'Wrong historical native failure.'
    $native+=[pscustomobject]@{id=$a.id;inputSha256=$r.inputSha256;fingerprint=$r.originalRequestFingerprint;afocal=$true;configuration=$r.configuration;wavelength=$s.selectors.Wavelength;buffers=$buffers;numericReferenceAvailable=$false;formalOpticalPass=$false}
}
$execution=@(ReadJson (Join-Path $artifacts 'afocal-ri-20261030-native-01/execution.json'))
Require ($execution.Count -eq 6 -and @($execution|Where-Object {$_.result.exitCode -ne 0 -or -not $_.sourceUnchanged}).Count -eq 0) 'Original native diagnostic execution failed.'
$positiveFolder=Join-Path $artifacts 'afocal-ri-20261030-native-positive-03/focal-positive'
$positive=ReadJson (Join-Path $positiveFolder 'relative-illumination-diagnostic.json')
$positiveBuffers=ReadJson (Join-Path $positiveFolder 'result-diagnostics.json')
$positiveData=ReadJson (Join-Path $positiveFolder 'data.json')
$positiveRequest=ReadJson (Join-Path $artifacts 'afocal-ri-20261030-native-positive-03/focal-positive.request.json')
Require ($positive.nativeNumericalBuffersAvailable -and -not $positive.afocalImageSpace -and -not $positive.formalOpticalPass -and $positiveBuffers.messages.Count -eq 0) 'Focal buffer positive failed.'
Require ($positiveData.dataSeries.Count -eq 1 -and $positiveData.dataSeries[0].x.Count -eq 21 -and $positiveData.dataSeries[0].y.Count -eq 21 -and $positiveData.dataSeries[0].y[0].Count -eq 2) 'Wrong focal control array shape.'
Require ((Hash $positiveRequest.input) -ceq $positiveRequest.inputSha256) 'Focal positive source changed.'
$positiveExecution=@(ReadJson (Join-Path $artifacts 'afocal-ri-20261030-native-positive-03/execution.json'))
Require ($positiveExecution.Count -eq 1 -and $positiveExecution[0].result.exitCode -eq 0 -and $positiveExecution[0].sourceUnchanged) 'Focal positive execution failed.'
$tests=@();$distinct=[Collections.Generic.HashSet[string]]::new()
foreach($path in 'afocal-ri-20261030-tests-core-final/core.trx','afocal-ri-20261030-tests-comparison-final/comparison.trx'){
    [xml]$doc=Get-Content -Raw -LiteralPath (Join-Path $artifacts $path)
    $counts=$doc.TestRun.ResultSummary.Counters
    Require ($counts.failed -eq 0 -and $counts.notExecuted -eq 0 -and $counts.total -eq $counts.passed) 'Focused tests failed or skipped.'
    $definitions=@{};foreach($t in $doc.TestRun.TestDefinitions.UnitTest){$definitions[[string]$t.id]=[string]$t.TestMethod.className}
    foreach($t in $doc.TestRun.Results.UnitTestResult){Require ($t.outcome -ceq 'Passed') 'Nonpassing test result.';Require ($distinct.Add($definitions[[string]$t.testId]+':'+$t.testName)) 'Duplicate test identity.'}
    $tests+=[pscustomobject]@{path=('artifacts/validation/'+$path);sha256=(Hash (Join-Path $artifacts $path));passed=[int]$counts.passed;failed=[int]$counts.failed;skipped=[int]$counts.notExecuted}
}
Require ($distinct.Count -eq 298 -and $tests[0].passed -eq 237 -and $tests[1].passed -eq 61) 'Wrong distinct focused scope.'
$copies=@()
foreach($p in 'src/OpticalSystemDesign.Core/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0','tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0','tools/OpticalSystemDesign.ZemaxComparison/bin/Release/net10.0','tools/diagnostics/FftProfileNative20261029/bin/Release/net10.0'){
    $relativePath=$p+'/OpticalSystemDesign.Core.dll';$copies+=[pscustomobject]@{path=$relativePath;sha256=(Hash (Join-Path $root $relativePath))}
}
Require (@($copies.sha256|Sort-Object -Unique).Count -eq 1) 'Shared Core copies differ.'
$identity=ReadJson (Join-Path $artifacts 'afocal-ri-20261030-final-02/identity.json')
Require ($identity.coreSha256 -ceq $copies[0].sha256) 'Final replay uses a different Core.'
foreach($path in 'afocal-ri-20261030-app-build-final.log','afocal-ri-20261030-tool-build-final.log'){
    $log=Get-Content -Raw -LiteralPath (Join-Path $artifacts $path)
    Require ($log -match '0 个警告' -and $log -match '0 个错误') 'Final default build failed or warned.'
}
$sources=@();$sourcePaths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@(git -C $root -c core.fsmonitor=false ls-files --others --exclude-standard -- src/OpticalSystemDesign.Core)+@('src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs')
foreach($p in $sourcePaths|Sort-Object -Unique){$sources+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}}
$assessment=[ordered]@{date='2026-10-30';status='five_afocal_ri_requests_resolved_as_not_applicable';applicabilityResolutionComplete=$true;numericalParityAchieved=$false;numericalPassesAdded=0;allAfocalIssuesClosed=$false;originalInputsSettingsGatesAndReferencesChanged=$false;historical170LedgerRewritten=$false;dependencies=$dependencies;sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);coreCopies=$copies;workingTreeSources=$sources;before=$before;after=$after;nativeRequests=$native;nativeExecution=$execution;focalPositive=$positive;focalPositiveBuffers=$positiveBuffers;focalPositiveSourceSha256=$positiveRequest.inputSha256;focalPositiveScope='Native buffer availability only, not formal optical parity';testRuns=$tests;latestDistinctTestsPassed=$distinct.Count;earlierRunsSupersededNotAdded=$true;repeatedAfterConcurrentCoatingCoreUpdate=$true;foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json';$assessment|ConvertTo-Json -Depth 60|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new();$extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj','.trx','.csv','.tsv')
foreach($item in Get-ChildItem -LiteralPath $artifacts -Filter 'afocal-ri-20261030*'){
    $candidates=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $item.FullName -Recurse -File)}else{@($item)}
    foreach($f in $candidates){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
}
foreach($folder in 'tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost'){
    foreach($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
}
foreach($p in @('docs/AFOCAL_RELATIVE_ILLUMINATION_APPLICABILITY_2026-10-30.md','tests/OpticalSystemDesign.Tests/AfocalRelativeIlluminationTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalRelativeIlluminationContractTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj','validation/rechecks/2026-10-30/afocal-ri/original-requests.json')+$sources.path){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
$files.Add((Get-Item -LiteralPath $assessmentPath))
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create);$canonical=@{};$entries=[Collections.Generic.List[object]]::new()
try{
    foreach($f in $files|Sort-Object FullName -Unique){
        $logical=Relative $f.FullName;Require (-not $logical.StartsWith('../')) 'Evidence escaped repository.';$sha=Hash $f.FullName
        if(-not $canonical.ContainsKey($sha)){
            $canonical[$sha]=$logical;$entry=$zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal)
            $inputStream=[IO.File]::OpenRead($f.FullName);$outputStream=$entry.Open();try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}
        }
        $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})
    }
    $entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{foreach($e in $entries){$member=$zip.GetEntry($e.member);$inputStream=$member.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream))}finally{$inputStream.Dispose()};Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence mismatch: '+$e.path)}}finally{$zip.Dispose()}
$seal=[ordered]@{date='2026-10-30';status=$assessment.status;applicabilityResolutionComplete=$true;afocalRequestsNotApplicable=5;nativeAfocalRequestsWithoutBuffers=5;numericalPassesAdded=0;latestDistinctTestsPassed=$distinct.Count;coreAssemblySha256=$copies[0].sha256;coreCopiesIdentical=6;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal|ConvertTo-Json -Depth 12
