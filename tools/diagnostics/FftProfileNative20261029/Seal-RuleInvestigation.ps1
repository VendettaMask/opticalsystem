param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$IsolatedRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$isolated=(Resolve-Path -LiteralPath $IsolatedRoot).Path
$artifacts=Join-Path $root 'artifacts/validation'
$destination=Join-Path $root 'validation/rechecks/2026-10-29/fft-rule-investigation'
function Json([string]$p) { Get-Content -LiteralPath $p -Raw|ConvertFrom-Json }
function Require([bool]$condition,[string]$message) { if(-not $condition){throw $message} }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
Push-Location $isolated
try {
    $head=git -c core.fsmonitor=false rev-parse HEAD
    $coreDiff=@(git -c core.fsmonitor=false status --porcelain -- src/OpticalSystemDesign.Core)
    Require ($LASTEXITCODE -eq 0 -and $coreDiff.Count -eq 0) 'Experimental Core changes remain.'
} finally { Pop-Location }
$final=@(Json (Join-Path $artifacts 'fft-rule-20261029-restored-final/summary.json'))
$previous=@(Json (Join-Path $artifacts 'fft-profile-grid-20261029-restored-after-moller/summary.json'))
Require (($final|ConvertTo-Json -Depth 10 -Compress) -ceq ($previous|ConvertTo-Json -Depth 10 -Compress)) 'Restored baseline differs.'
Require ($final[3].violations -eq 335 -and $final[3].maximum -eq 0.0011946610381988604) 'Unexpected precision status.'
$sourceArchive='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip'
Require ((Hash (Join-Path $root $sourceArchive)) -eq '235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1') 'Original reference archive differs.'
$executions=@()
$fieldChanges=@()
foreach($e in (Json (Join-Path $artifacts 'fft-rule-20261029-native-field2/execution.json'))) {
    Require ($e.result.exitCode -eq 0 -and $e.sourceUnchanged) 'Invalid field control process.'
    $folder=Join-Path $artifacts ('fft-rule-20261029-native-field2/'+$e.id)
    $settings=Json (Join-Path $folder 'captured-settings.json')
    Require ($settings.selectors.Field -eq 2 -and $settings.selectors.Wavelength -eq 2) 'Wrong native selector.'
    $data=Json (Join-Path $folder 'data.json')
    Require ($data.dataGrids.Count -eq 1 -and $data.dataGrids[0].values.Count -eq 64) 'Incomplete field control.'
    $executions+=[pscustomobject]@{cohort='field2';id=$e.id;exitCode=0;sourceUnchanged=$true;validity='valid';nativeField=2;canonicalParentField=$settings.request.field;formalAcceptanceRequest=$false}
    if($e.id -match '^binocular-c([12])-gaussian-(Real|Imaginary)$') {
        $old=Json (Join-Path $artifacts ('fft-profile-gaussian-20261029/'+$e.id+'/data.json'))
        $maximum=0.0
        for($y=0;$y -lt 64;$y++){for($x=0;$x -lt 64;$x++){$maximum=[Math]::Max($maximum,[Math]::Abs($data.dataGrids[0].values[$y][$x]-$old.dataGrids[0].values[$y][$x]))}}
        Require ($maximum -gt .1) 'Nonzero-field control did not change the complex field.'
        $fieldChanges+=[pscustomobject]@{id=$e.id;maximumComplexChannelChange=$maximum}
    }
}
foreach($cohort in @('fft-rule-20261029-native-predicate','fft-rule-20261029-native-predicate-02','fft-rule-20261029-native-predicate-03','fft-rule-20261029-native-predicate-04')) {
    foreach($e in (Json (Join-Path $artifacts ($cohort+'/execution.json')))) {
        Require $e.sourceUnchanged 'A diagnostic changed its source input.'
        $executions+=[pscustomobject]@{cohort=$cohort;id=$e.id;exitCode=$e.result.exitCode;sourceUnchanged=$true;validity=$(if($e.result.exitCode -eq 0){'invalid'}else{'failed'});formalAcceptanceRequest=$false}
    }
}
Require (@($executions|Where-Object validity -eq 'valid').Count -eq 8 -and @($executions|Where-Object validity -eq 'invalid').Count -eq 5 -and @($executions|Where-Object validity -eq 'failed').Count -eq 2) 'Unexpected native execution accounting.'
$normalResults=@(Json (Join-Path $artifacts 'fft-rule-20261029/normal-formulas.json'))
$normalMatches=@()
foreach($c in $normalResults){foreach($m in $c.matches){
    $exact=@($m.exact|Where-Object formula -eq 'edge/reciprocal')
    Require ($exact.Count -gt 0) 'Reciprocal-normal form does not reconstruct a native normal.'
    $normalMatches+=[pscustomobject]@{configuration=$c.config;segment=$m.segment;matchingImportedFaceNumbers=@($exact.face);apiInternalTriangleIdentity=$false}
}}
$field1=@(Json (Join-Path $artifacts 'fft-profile-grid-20261029/gaussian-coordinates-reference-corrected.json'))
$field2=@(Json (Join-Path $artifacts 'fft-rule-20261029/field2-gaussian-coordinates.json'))
$axes=@()
foreach($c in @(1,2)) { foreach($p in @(@{x=13;y=0},@{x=0;y=13})) {
    $a=$field1[$c-1].axis|Where-Object {$_.x -eq $p.x -and $_.y -eq $p.y}
    $b=$field2[$c-1].axis|Where-Object {$_.x -eq $p.x -and $_.y -eq $p.y}
    $axes+=[pscustomobject]@{configuration=$c;x=$p.x;y=$p.y;field1EnvelopeStretch=$a.inferredCoordinateStretch;field2EnvelopeStretch=$b.inferredCoordinateStretch;difference=($b.inferredCoordinateStretch-$a.inferredCoordinateStretch);physicalLaunchMapProven=$false}
}}
$experiments=@()
foreach($name in @('quad-tie','plane-equation','reciprocal','source-reciprocal','stable-source','coplanar-source','coplanar-frozen','tangential-source')) {
    $s=@(Json (Join-Path $artifacts ('fft-rule-20261029-'+$name+'/summary.json')))
    Require ($s[3].violations -eq 335) 'Unexpected counterfactual result.'
    $experiments+=[pscustomobject]@{id=$name;usesSourceReimport=($name -in @('source-reciprocal','stable-source','coplanar-source','tangential-source'));c1Violations=$s[2].violations;c2Violations=$s[3].violations;c2Maximum=$s[3].maximum;accepted=$false}
}
Require ((Json (Join-Path $artifacts 'fft-rule-20261029-verification/host-build.json')).success) 'Final host does not compile.'
$installed='C:\Users\19851\Documents\Zemax\OBJECTS\Polygon Objects'
$assets=@()
foreach($p in @(@{name='PORRO_PRISM-LH.POB';sha='8ba3b4bfe0092319a97e6169de6987132c0f9e2cda99f0b6a19a70eecc3a74f4'},@{name='PORRO_PRISM-RH.POB';sha='c247ee42b9ec39fed6d0411e5f8b7c780ad3a6ff938bd6a642450d1e40dcef68'})) {
    $sha=Hash (Join-Path $installed $p.name)
    Require ($sha -eq $p.sha) 'Installed original POB changed.'
    $assets+=[pscustomobject]@{name=$p.name;sha256=$sha;unchanged=$true}
}
Require (@(Get-ChildItem -LiteralPath $installed -Filter 'codex_afocal_*.POB').Count -eq 0) 'Owned temporary POB remains.'
$assessment=[ordered]@{
    date='2026-10-29';status='rules_partially_characterized_precision_issue_open';objectiveAchieved=$false
    sourceCommit=$head;isolatedCoreSourceMatchesCommit=$true;isolatedRoot=$isolated
    coreAssemblySha256=(Hash (Join-Path $isolated 'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll'))
    originalReferenceSamplingToleranceChanged=$false;foregroundDesktopUsed=$false;productCoreChangesAccepted=0
    sourceArchive=$sourceArchive;sourceArchiveSha256=(Hash (Join-Path $root $sourceArchive));installedAssets=$assets
    nativeValidCaptures=8;nativeInvalidOutputs=5;nativeFailedControls=2;executions=$executions
    rejectedCoreHypotheses=7;counterfactualRuns=$experiments;normalMatches=$normalMatches
    fieldChanges=$fieldChanges;gaussianEnvelopeAxes=$axes;gaussianEnvelopeDoesNotProvePhysicalLaunchMap=$true
    restoredProfilesExactlyEqualPreviousBaseline=$true;restoredProfiles=$final;remainingViolations=335
    rejectedPredicateAdapterRemoved=$true;broadTestSuiteRun=$false;newUnitTestRun=$false
    finalBuildLog='artifacts/validation/fft-rule-20261029-restored-build-02.log'
    interruptedBuildLog='artifacts/validation/fft-rule-20261029-restored-build.log'
    verificationScope='Isolated HEAD Core and recorded diagnostics only; concurrent main-workspace changes are not certified'
}
New-Item -ItemType Directory -Path $destination -Force|Out-Null
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not (Test-Path -LiteralPath $archivePath)) 'Preserve existing evidence archive.'
$assessmentPath=Join-Path $destination 'assessment.json'
$assessment|ConvertTo-Json -Depth 40|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new()
$ext=@('.json','.txt','.log','.cfg','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj')
foreach($d in Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'fft-rule-20261029*') {foreach($f in Get-ChildItem -LiteralPath $d.FullName -Recurse -File){if($f.Extension.ToLowerInvariant() -in $ext){$files.Add($f)}}}
foreach($f in Get-ChildItem -LiteralPath $artifacts -File -Filter 'fft-rule-20261029*.log'){$files.Add($f)}
foreach($folder in @('tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost')){foreach($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File){if($f.Extension.ToLowerInvariant() -in $ext){$files.Add($f)}}}
foreach($p in @('docs/AFOCAL_FFT_RULES_2026-10-29.md','validation/rechecks/2026-10-29/fft-rule-investigation/assessment.json',
    'artifacts/validation/fft-profile-grid-20261029/gaussian-coordinates-reference-corrected.json')){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$canonical=@{};$entries=[Collections.Generic.List[object]]::new()
try { foreach($f in $files|Sort-Object FullName -Unique) {
    $logical=[IO.Path]::GetRelativePath($root,$f.FullName).Replace('\','/');Require (-not $logical.StartsWith('../')) 'Evidence path escaped repository.'
    $sha=Hash $f.FullName
    if(-not $canonical.ContainsKey($sha)){$canonical[$sha]=$logical;$entry=$zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal);$input=[IO.File]::OpenRead($f.FullName);$output=$entry.Open();try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}}
    $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})
};$entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {foreach($e in $entries){$member=$zip.GetEntry($e.member);$input=$member.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($input))}finally{$input.Dispose()};Require ($sha -eq $e.sha256 -and $member.Length -eq $e.length) ('Evidence byte mismatch: '+$e.path)}}finally{$zip.Dispose()}
$seal=[ordered]@{date='2026-10-29';status=$assessment.status;objectiveAchieved=$false;remainingViolations=335;maximumIntensityDifference=$final[3].maximum;sourceCommit=$head;coreAssemblySha256=$assessment.coreAssemblySha256;productCoreChangesAccepted=0;foregroundDesktopUsed=$false;nativeValidCaptures=8;nativeInvalidOutputs=5;nativeFailedControls=2;counterfactualRuns=8;archiveBytes=(Get-Item -LiteralPath $archivePath).Length;archiveSha256=(Hash $archivePath);logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false}
$seal|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal|ConvertTo-Json -Depth 10
