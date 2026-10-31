param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation'
$destination=Join-Path $root 'validation/rechecks/2026-10-30/centroid-energy'
function ReadJson([string]$p) { Get-Content -Raw -LiteralPath $p | ConvertFrom-Json }
function Require([bool]$v,[string]$m) { if(-not $v){throw $m} }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Relative([string]$p) { [IO.Path]::GetRelativePath($root,$p).Replace('\','/') }
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not(Test-Path -LiteralPath $archivePath)) 'Never overwrite sealed evidence.'
$dependencies=@(
    @{path='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip';sha256='235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1'},
    @{path='validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip';sha256='8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1'},
    @{path='validation/rechecks/2026-10-30/afocal-auto-grid/evidence.zip';sha256='b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f'},
    @{path='validation/rechecks/2026-10-30/riflescope-pupil/evidence.zip';sha256='f4e6fef82110d8b6bde01a6d85ea72dbf7613e480991751e79b1f5a423bace4a'})
foreach($d in $dependencies){Require ((Hash (Join-Path $root $d.path)) -ceq $d.sha256) 'Frozen dependency changed.'}
$manifestPath=Join-Path $destination 'original-requests.json'; $manifest=ReadJson $manifestPath
$before=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-before/summary.json'))
$after=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-final-02/summary.json'))
Require ($before.Count -eq 5 -and $after.Count -eq 5 -and $manifest.cases.Count -eq 5) 'Wrong original request set.'
$beforeViolations=0; $values=0; $targetValues=0
for($i=0;$i -lt 5;$i++){
    $a=$after[$i];$b=$before[$i];$m=$manifest.cases[$i]
    Require ($a.id -ceq $b.id -and $a.id -ceq $m.id -and $a.fingerprint -ceq $b.fingerprint -and $a.fingerprint -ceq $m.fingerprint -and $a.snapshotSha256 -ceq $b.snapshotSha256 -and $a.nativeDataSha256 -ceq $b.nativeDataSha256) 'Original request, snapshot or native bytes changed.'
    Require ($a.currentConclusion -ceq 'Pass' -and $a.curves.Count -eq $b.curves.Count) 'An original case remains unresolved.'
    foreach($c in $a.curves){
        Require ($c.violations -eq 0 -and $c.metric.conclusion -ceq 'Pass') 'A claimed closure fails pointwise or overall gates.'
        foreach($key in 'Absolute','Relative','Nrmse','CloseNrmse','MinimumCoverage'){Require ($c.gate.$key -eq $m.effectiveTolerances.EnergyFraction.$key) 'Original energy gate changed.'}
        $values+=$c.count
        if($m.originalConclusion -eq 'Difference'){$targetValues+=$c.count}
    }
    $beforeViolations+=($b.curves | Measure-Object violations -Sum).Sum
}
Require ($beforeViolations -eq 2129 -and $values -eq 9169 -and $targetValues -eq 7971) 'Wrong original pointwise scope.'
$rejected=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-trial/summary.json'))
Require (($rejected.curves | Measure-Object violations -Sum).Sum -eq 13) 'Early overall-Pass/pointwise-failure candidate was lost.'
$controlSummary=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-controls-final-02/summary.json'))
Require ($controlSummary.Count -eq 19 -and ($controlSummary | Measure-Object violations -Sum).Sum -eq 0 -and @($controlSummary | Where-Object {$_.metric.conclusion -ne 'Pass'}).Count -eq 0 -and ($controlSummary.metric | Measure-Object count -Sum).Sum -eq 11309) 'Independent reference controls fail.'
$grids=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-grids-final-02/summary.json'))
Require ($grids.Count -eq 20 -and @($grids | Where-Object {$_.failure -or $null -eq $_.violations -or $_.violations -ne 0 -or $_.metric.conclusion -ne 'Pass'}).Count -eq 0 -and ($grids.metric | Measure-Object count -Sum).Sum -eq 250880) 'An existing original PSF grid regressed.'
$profiles=@(ReadJson (Join-Path $artifacts 'centroid-energy-20261030-profiles-final-02/summary.json'))
Require ($profiles.Count -eq 5 -and @($profiles | Where-Object violations -NE 0).Count -eq 0) 'An original profile regressed.'
$tests=@();$identities=[Collections.Generic.HashSet[string]]::new();$physicalCdfTests=0;$strictReferenceTests=0
foreach($spec in @(@{file='centroid-final-02.trx';count=133;assembly='Comparison'},@{file='centroid-core-02.trx';count=36;assembly='Core'})){
    $p=Join-Path $artifacts ('centroid-energy-20261030-tests/'+$spec.file);[xml]$trx=Get-Content -Raw -LiteralPath $p;$results=@($trx.TestRun.Results.UnitTestResult)
    Require ($results.Count -eq $spec.count -and @($results | Where-Object outcome -NE 'Passed').Count -eq 0) 'Final tests are incomplete or failed.'
    foreach($r in $results){[void]$identities.Add($spec.assembly+':'+$r.testName);if($r.testName -like '*AngularCentroidEnergyMatchesIndependentNativeRays*'){$physicalCdfTests++};if($r.testName -like '*ABlockedReferenceRayCannotAcquireACentroidDirection*'){$strictReferenceTests++}}
    $tests+=[pscustomobject]@{path=(Relative $p);passed=$results.Count;failed=0;skipped=0;sha256=(Hash $p)}
}
Require ($identities.Count -eq 169 -and $physicalCdfTests -eq 3 -and $strictReferenceTests -eq 1) 'Wrong latest distinct test scope.'
$copies=@()
foreach($prefix in 'src/OpticalSystemDesign.Core','src/OpticalSystemDesign.App','tests/OpticalSystemDesign.Tests','tests/OpticalSystemDesign.ZemaxComparison.Tests','tools/OpticalSystemDesign.ZemaxComparison','tools/diagnostics/FftProfileNative20261029'){
    $p="$prefix/bin/Release/net10.0/OpticalSystemDesign.Core.dll";$copies+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}
}
Require (@($copies.sha256|Select-Object -Unique).Count -eq 1) 'Six Core copies differ.'
foreach($folder in 'centroid-energy-20261030-final-02','centroid-energy-20261030-controls-final-02','centroid-energy-20261030-grids-final-02'){
    $identity=ReadJson (Join-Path $artifacts ($folder+'/identity.json'));Require ($identity.coreSha256 -ceq $copies[0].sha256) 'Final diagnostic used a different Core.'
}
$unchanged=@('tools/OpticalSystemDesign.ZemaxComparison/Metrics/ComparisonMetrics.cs','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json','validation/zemax/2026-r1/afocal-reference-focus-2026-10-27/angular-centroid-energy.json','src/OpticalSystemDesign.Core/Services/GeometricEnergyMetrics.cs','src/OpticalSystemDesign.Core/Analysis/Rays/SpotAnalysisSupport.cs')
git -C $root -c core.fsmonitor=false diff --exit-code -- @unchanged | Out-Null
Require ($LASTEXITCODE -eq 0) 'Physical spot/operand, original ray-CDF fixture or comparison strategy changed.'
$energySource='src/OpticalSystemDesign.Core/Analysis/Radiometry/EncircledEnergyVariants.cs'
$oldText=(git -C $root -c core.fsmonitor=false show ('HEAD:'+$energySource)|Out-String).Replace("`r",'').TrimEnd()
$newText=(Get-Content -Raw -LiteralPath (Join-Path $root $energySource)).Replace("`r",'').TrimEnd()
$mean='    public static (double X, double Y) Centroid('; $reference='    public static (double X, double Y) ReferencePoint('; $cdf='    public static AnalysisData CreateCurve('
Require ($oldText.Substring($oldText.IndexOf($mean),$oldText.IndexOf($reference)-$oldText.IndexOf($mean)) -ceq $newText.Substring($newText.IndexOf($mean),$newText.IndexOf($reference)-$newText.IndexOf($mean))) 'Physical angular mean formula changed.'
Require ($oldText.Substring($oldText.IndexOf($cdf)) -ceq $newText.Substring($newText.IndexOf($cdf))) 'Physical closed CDF formula changed.'
$enums=ReadJson (Join-Path $artifacts 'centroid-energy-20261030-reference-enum.json')
Require (($enums.values -join ',') -ceq 'ChiefRay,Centroid,Vertex') 'Installed native energy enum differs.'
$native=@();$nativeGroups=@{};$failed=0
foreach($cohort in '01','02','03','04'){
    $capture=Join-Path $artifacts ('centroid-energy-20261030-native-'+$cohort)
    foreach($e in @(ReadJson (Join-Path $capture 'execution.json'))){
        Require ($e.sourceUnchanged -and -not $e.result.TimedOut) 'Native source changed or timed out.'
        $request=ReadJson (Join-Path $capture ($e.id+'.request.json'))
        Require (-not $request.captureScreenshots -and -not $request.formalAcceptanceRequest -and $request.diagnosticEnergyReference) 'Background diagnostic contract differs.'
        Require ((Hash $request.input) -ceq $request.inputSha256) 'Native source file changed.'
        if($e.result.ExitCode -ne 0){
            $errorRecord=ReadJson (Join-Path $capture ($e.id+'/error.json'))
            Require ($cohort -eq '01' -and $e.id -ceq 'afocal-Middle' -and $errorRecord.error.Contains('Middle')) 'Unexpected native failure.'
            $failed++;continue
        }
        $folder=Join-Path $capture $e.id
        $model=ReadJson (Join-Path $folder 'model.json');$settings=ReadJson (Join-Path $folder 'captured-settings.json');$control=ReadJson (Join-Path $folder 'diagnostic-energy-reference.json')
        $environment=ReadJson (Join-Path $folder 'environment.json')
        Require ($environment.validLicense -and $environment.major -eq 26 -and $environment.minor -eq 1 -and $model.aperture.AFocalImageSpace -eq $request.imageSpaceAfocal -and $settings.properties.ReferTo -ceq $request.zemaxSettings.ReferTo) 'Native readback differs.'
        foreach($s in $control.frozenThicknesses){
            # Native runtime equality is checked in the host. Its general model
            # exporter maps nonfinite values through Host.Finite to JSON null.
            if([double]::IsInfinity([double]$s.thickness)){Require ($null -eq $model.surfaces[$s.surface].Thickness) 'Infinite thickness sentinel encoding differs.'}
            else{Require ($model.surfaces[$s.surface].Thickness -eq $s.thickness) 'Working physical thickness changed.'}
        }
        $data=ReadJson (Join-Path $folder 'data.json');$series=$data.dataSeries[0]
        Require ($data.dataSeries.Count -eq 1 -and $series.x[0] -eq 0 -and $series.y[0][0] -eq 0) 'Native zero-radius plot origin differs.'
        $text=Get-Content -Raw -LiteralPath (Join-Path $folder 'data.txt')
        Require ($text -match '参考坐标单位是\s*毫米') 'Native spatial-reference unit is missing.'
        $key=$request.inputSha256+':'+$request.configuration+':'+$request.imageSpaceAfocal+':'+$request.field+':'+$request.wavelength+':'+$settings.properties.SampleSize
        $row=[pscustomobject]@{cohort=$cohort;id=$e.id;reference=$settings.properties.ReferTo;afocal=$request.imageSpaceAfocal;sourceUnchanged=$true;formalOpticalPass=$false;series=$series}
        if(-not $nativeGroups.ContainsKey($key)){$nativeGroups[$key]=[Collections.Generic.List[object]]::new()};$nativeGroups[$key].Add($row)
        $native+=[pscustomobject]@{cohort=$cohort;id=$e.id;reference=$row.reference;afocal=$row.afocal;sourceUnchanged=$true;formalOpticalPass=$false}
    }
}
Require ($native.Count -eq 19 -and $failed -eq 1 -and $nativeGroups.Count -eq 7) 'Wrong native control scope.'
$characterization=@()
foreach($group in $nativeGroups.Values){
    $chief=@($group|Where-Object reference -CEQ 'ChiefRay');Require ($chief.Count -eq 1) 'Missing independent chief reference.';$maximum=0d
    foreach($r in $group){Require ($r.series.x.Count -eq $chief[0].series.x.Count) 'Native axes differ within a control group.';for($i=0;$i -lt $r.series.x.Count;$i++){Require ($r.series.x[$i] -eq $chief[0].series.x[$i]) 'Native control axes differ.';$maximum=[Math]::Max($maximum,[Math]::Abs($r.series.y[$i][0]-$chief[0].series.y[$i][0]))}}
    if($chief[0].afocal){Require ($maximum -eq 0) 'Afocal reference-direction observation differs.'}else{Require ($maximum -gt 1e-4) 'Focal reference positive control does not distinguish settings.'}
    $characterization+=[pscustomobject]@{cohort=$chief[0].cohort;chiefId=$chief[0].id;afocal=$chief[0].afocal;references=@($group.reference);maximumFractionDifferenceToChief=$maximum}
}
$sources=@();$sourcePaths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@(git -C $root -c core.fsmonitor=false ls-files --others --exclude-standard -- src/OpticalSystemDesign.Core)+@('src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs')
foreach($p in $sourcePaths|Sort-Object -Unique){$sources+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}}
$assessment=[ordered]@{date='2026-10-30';status='three_original_native_centroid_energy_plot_differences_closed';requestedIssueClosed=$true;allAfocalIssuesClosed=$false;originalViolationsBefore=2129;originalViolationsAfter=0;targetComparedValues=7971;allFiveComparedValues=9169;originalInputsReferencesSamplingAndGatesChanged=$false;dependencies=$dependencies;sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);coreCopies=$copies;workingTreeSources=$sources;before=$before;after=$after;nativeControlComparison=$controlSummary;nativeExecutions=$native;nativeCharacterization=$characterization;nativeValidDiagnosticControls=19;nativeUnsupportedReferenceAttempts=1;nativeControlsCountedAsOpticalPasses=0;unexecutedPreparedFirstCohortRequests=4;physicalAngularMeanAndClosedCdfUnchanged=$true;physicalNativeRayCdfTests=3;blockedReferenceCannotSubstituteCentroid=$true;earlyOverallPassBut13PointViolationsRejected=$true;originalPsfGridsPassed=20;original335ProfileViolationsRemaining=0;latestDistinctTestsPassed=169;testRuns=$tests;earlierRunsSupersededNotAdded=$true;foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;otherOpenIssuesClosed=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json';$assessment|ConvertTo-Json -Depth 55|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new();$extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj','.trx','.csv','.tsv')
foreach($item in Get-ChildItem -LiteralPath $artifacts -Filter 'centroid-energy-20261030*'){$candidates=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $item.FullName -Recurse -File)}else{@($item)};foreach($f in $candidates){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}}
foreach($folder in 'tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost'){foreach($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}}
foreach($p in @('docs/AFOCAL_CENTROID_ENERGY_REPAIR_2026-10-30.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalCentroidEnergyPointwiseTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalReferenceFocusTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj')+$sources.path+$unchanged){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
foreach($p in $assessmentPath,$manifestPath){$files.Add((Get-Item -LiteralPath $p))}
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create);$canonical=@{};$entries=[Collections.Generic.List[object]]::new()
try{
    foreach($f in $files|Sort-Object FullName -Unique){$logical=Relative $f.FullName;Require (-not $logical.StartsWith('../')) 'Evidence escaped repository.';$sha=Hash $f.FullName
        if(-not $canonical.ContainsKey($sha)){$canonical[$sha]=$logical;$entry=$zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal);$inputStream=[IO.File]::OpenRead($f.FullName);$outputStream=$entry.Open();try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}}
        $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})
    }
    $entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{foreach($e in $entries){$member=$zip.GetEntry($e.member);$inputStream=$member.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream))}finally{$inputStream.Dispose()};Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence byte mismatch: '+$e.path)}}finally{$zip.Dispose()}
$seal=[ordered]@{date='2026-10-30';status=$assessment.status;requestedIssueClosed=$true;allAfocalIssuesClosed=$false;originalCentroidPlotViolationsRemaining=0;originalFiveEnergyRequestsPassed=5;originalPsfGridsPassed=20;original335ProfileViolationsRemaining=0;latestDistinctTestsPassed=169;nativeValidDiagnosticControls=19;nativeUnsupportedReferenceAttempts=1;coreAssemblySha256=$copies[0].sha256;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal|ConvertTo-Json -Depth 12
