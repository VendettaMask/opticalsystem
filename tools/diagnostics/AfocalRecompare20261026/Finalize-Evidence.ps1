param([string]$RunDirectory='artifacts/validation/afocal-recompare-20261026')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=(Resolve-Path -LiteralPath (Join-Path $repo $RunDirectory)).Path
function Read-Json([string]$path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function Write-Json([string]$path,$value){$value|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $path -Encoding utf8NoBOM}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Counts($rows){$c=[ordered]@{};foreach($g in $rows|Group-Object conclusion){$c[$g.Name]=$g.Count};return $c}
$final=Join-Path $root 'final'
if(Test-Path -LiteralPath $final){throw 'Fresh final evidence directory required'}
New-Item -ItemType Directory -Path $final|Out-Null
$base=@(Read-Json (Join-Path $root 'baseline-v4/analysis-results.json'))
$expanded=@(Read-Json (Join-Path $root 'expanded/compare-v3/summary.json'))
$accepted=@($base|Where-Object {$_.run.conclusion -eq 'Pass'})
if($accepted.Count -ne 30 -or $expanded.Count -ne 120){throw 'Analysis scope changed'}
$rows=@(foreach($b in $accepted){[pscustomobject]@{id=$b.run.directory;lens=$b.lens;configuration=$b.configuration;key=$b.run.key;conclusion=$b.run.conclusion;reason=$b.run.reason;metrics=$b.run.metrics;resultPath='baseline-v4/analyses/'+$b.run.directory}}
    foreach($e in $expanded){[pscustomobject]@{id=$e.id;lens=$e.lens;configuration=$e.configuration;key=$e.key;conclusion=$e.conclusion;reason=$e.reason;metrics=$e.metrics;resultPath='expanded/compare-v3/'+$e.id}})
if(@($rows.id|Sort-Object -Unique).Count -ne 150){throw 'Duplicate or omitted analysis requests'}
$counts=Counts $rows
if($counts.Pass -ne 73 -or $counts.Close -ne 4 -or $counts.Difference -ne 20 -or $counts.Incomparable -ne 53){throw 'Unexpected analysis counts'}
$rays=@(Read-Json (Join-Path $root 'ray-audit/summary.json'))
$controls=@(Read-Json (Join-Path $root 'controls/core-summary.json'))
$opd=@($controls|Where-Object key -eq 'Explicit OPD')
$mtf=@($controls|Where-Object key -eq 'MTF')
$fixed=@(Read-Json (Join-Path $root 'fixed-grid-controls/compare-v1/summary.json'))
$frozen=Read-Json (Join-Path $root 'frozen-integrity.json')
if(!$frozen.unchanged -or !$frozen.workingTree.unchanged -or $frozen.matched -ne 2668){throw 'Frozen evidence changed'}
$rayFailures=0
foreach($r in $rays){$rayFailures+=$r.coordinateFailures+$r.directionFailures+$r.acceptanceDifferences+$r.firstClipDifferences}
if($rays.Count -ne 15 -or $rayFailures -ne 0 -or ($opd|Where-Object conclusion -ne 'Pass').Count -ne 0){throw 'Physical controls changed'}
$sourceChecks=@(foreach($s in (Read-Json (Join-Path $root 'baseline-v4/sources.json'))){
    $id=$s.source.id;$product=Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$id)
    $paths=@($product,$s.source.path,(Join-Path $root ('baseline-v4/sources/'+$id)))
    $hs=@($paths|ForEach-Object {Hash $_});if(@($hs|Sort-Object -Unique).Count -ne 1){throw 'Source bytes changed'}
    [ordered]@{id=$id;sha256=$hs[0];allThreeCopiesMatch=$true}
})
$pobChecks=@(foreach($name in @('PORRO_PRISM-LH.POB','PORRO_PRISM-RH.POB')){
    $paths=@((Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/Objects/Polygon Objects/'+$name)),
        (Join-Path $root ('Objects/Polygon Objects/'+$name)),(Join-Path $env:USERPROFILE ('Documents/Zemax/Objects/Polygon Objects/'+$name)))
    $hs=@($paths|ForEach-Object {Hash $_});if(@($hs|Sort-Object -Unique).Count -ne 1){throw 'POB bytes changed'}
    [ordered]@{id=$name;sha256=$hs[0];productRunAndNativeCopiesMatch=$true}
})
$identity=Read-Json (Join-Path $root 'baseline-v4/identity.json')
$core=$identity.assemblies|Where-Object {$_.path -like '*Core.dll'}
$corePaths=@('runners-v4/SequentialCorpus20261019/OpticalSystemDesign.Core.dll','runners-v4/Afocal20261024/OpticalSystemDesign.Core.dll','expanded-runner-v4/OpticalSystemDesign.Core.dll','expanded-runner-v5/OpticalSystemDesign.Core.dll','expanded-runner-v6/OpticalSystemDesign.Core.dll')
foreach($path in $corePaths){if((Hash (Join-Path $root $path)) -ne $core.sha256){throw 'Optical run used mixed Core binaries'}}
$policyHash=Hash (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')
if($policyHash -ne '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5'){throw 'Global policy changed'}
$gridReadback=@(foreach($row in (Read-Json (Join-Path $root 'fixed-grid-controls/items.json'))|Where-Object key -eq 'Huygens PSF'){
    $data=Read-Json (Join-Path $root ('fixed-grid-controls/'+$row.id+'/workbench/data.json'))
    [ordered]@{lens=$row.lens;configuration=$row.configuration;requestedMilliradian=$row.request.imageDeltaMicrometers;actualMilliradian=$data.values.PixelPitchMilliradians;ratio=$data.values.PixelPitchMilliradians/$row.request.imageDeltaMicrometers}
})
$lensCounts=@(foreach($g in $rows|Group-Object lens){[ordered]@{lens=$g.Name;total=$g.Count;counts=(Counts $g.Group)}})
$ledger=[ordered]@{date='2026-10-26';status='Fresh four-file comparison completed; unresolved differences retained';generatedUtc=[DateTime]::UtcNow.ToString('o');
    baseCommit=(Get-Content (Join-Path $root 'base-commit.txt') -Raw).Trim();coreSha256=$core.sha256;globalPolicySha256=$policyHash;
    scope='Four original AF files, five configurations, 150 original scoped requests; not all analyses or a full 95-file rerun';
    native=[ordered]@{version='OpticStudio 2026 R1';build=260127;license='EnterpriseEdition';originalAnalysisAttempts=150;originalValidNumericalCaptures=145;nativeRelativeIlluminationWithoutData=5;allFreshJobAttemptsIncludingProbesAndControls=218};
    analysis=[ordered]@{total=150;counts=$counts;lenses=$lensCounts;results='analysis-results.json';expandedWorkbenchExitCodes=(Counts @((Read-Json (Join-Path $root 'expanded/workbench-results.json'))|ForEach-Object {[pscustomobject]@{conclusion=[string]$_.exitCode}}))};
    physicalRays=[ordered]@{groups=$rays.Count;counts=(Counts $rays);inputs=(($rays|ForEach-Object inputs)|Measure-Object -Sum).Sum;physicalSurfaceComparisons=(($rays|ForEach-Object physicalSurfaceComparisons)|Measure-Object -Sum).Sum;failures=$rayFailures};
    explicitOpd=[ordered]@{groups=$opd.Count;points=(($opd|ForEach-Object count)|Measure-Object -Sum).Sum;counts=(Counts $opd);maximumAbsoluteWaves=($opd.maxAbsolute|Measure-Object -Maximum).Maximum;scope='Native CurrentAndChief explicit points; does not certify full non-primary-wavelength OPD fan reference definition'};
    mtfControls=[ordered]@{total=$mtf.Count;counts=(Counts $mtf);physicalBand='5 Pass';wide55='3 Pass, 2 Close (both Binocular configurations)'};
    fixedGridControls=[ordered]@{total=$fixed.Count;counts=(Counts $fixed);fftPsf='5 Pass';huygensPsf='5 Incomparable; actual Core spacing is requested mrad / 1000';huygensMtf='1 Pass, 4 Difference';huygensThroughFocus='5 Difference';gridReadback=$gridReadback;interpretation='Same declared angular input does not yield the same Huygens grid. Modulation differences are not certified equal-grid algorithm residuals.'};
    sources=$sourceChecks;polygonObjects=$pobChecks;
    tests=[ordered]@{filter='RegistryCatalogSynchronizationTests or Afocal';configuration='Release';passed=52;failed=0;skipped=0;scope='Current comparison registry and existing afocal regressions; compiled separately from the frozen optical-run Core'};
    frozen=[ordered]@{count=2668;headAndWorkingTreeUnchanged=$true;aggregateSha256=$frozen.expectedAggregateSha256};
    limitations=@('No optical product fix in this comparison task','No rerun of historical focal 1159 + 52 requests','No full 95-file rerun, convergence certification, push, installer or installed-app acceptance','Concurrent unrelated workspace edits were preserved; all optical run outputs bind to the recorded Core DLL, not later default-build binaries')}
Write-Json (Join-Path $final 'analysis-results.json') $rows
Write-Json (Join-Path $final 'summary.json') $ledger
Write-Json (Join-Path $final 'fixed-grid-readback.json') $gridReadback
Write-Output ($ledger|ConvertTo-Json -Depth 2)
