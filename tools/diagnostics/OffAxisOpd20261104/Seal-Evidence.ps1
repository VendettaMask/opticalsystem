param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$artifact=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/offaxis-opd-sampling'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 90),[Text.UTF8Encoding]::new($false))}
$summary=Read "$root/summary.json";$core=$summary.coreAfter
if($summary.distinctTests -ne 691 -or $summary.equivalentOpdPass -ne 6 -or $summary.samplingRequestsAudited -ne 19 -or $summary.nativeSamplingCaptures -ne 38 -or $summary.crossAnalysisPriorPassLost -ne 0){throw 'Incomplete acceptance'}
$archive=Join-Path $root 'evidence.zip';if(Test-Path $archive){throw 'Already sealed'}
$files=[Collections.Generic.List[object]]::new()
function Add($path,$logical){$files.Add(@{path=[IO.Path]::GetFullPath($path);logical=$logical})}
function AddTree($path,$prefix){foreach($f in Get-ChildItem -LiteralPath $path -Recurse -File){$rel=[IO.Path]::GetRelativePath($path,$f.FullName).Replace('\','/');if($rel -match '(^|/)(bin|obj)/' -or $f.Extension -in @('.png','.svg','.pdf','.dll','.pdb','.exe')){continue};Add $f.FullName "$prefix/$rel"}}
$runtime=Join-Path $artifact 'offaxis-opd-final-runtime-20261104';$test=Join-Path $repo 'tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0';$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$bindings=[Collections.Generic.List[object]]::new()
foreach($pdb in @("$runtime/OpticalSystemDesign.Core.pdb","$runtime/OpticalSystemDesign.Application.pdb","$runtime/OpticalSystemDesign.ZemaxComparison.pdb","$runtime/OpticalSystemDesign.Tests.pdb","$test/OpticalSystemDesign.Tests.pdb")){
 $stream=[IO.File]::OpenRead($pdb);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream);$documents=[Collections.Generic.List[object]]::new()
 try{$reader=$provider.GetMetadataReader();foreach($handle in $reader.Documents){$d=$reader.GetDocument($handle);$path=$reader.GetString($d.Name);if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue};$sha=[Convert]::ToHexString($reader.GetBlobBytes($d.Hash)).ToLowerInvariant();if((Hash $path) -ne $sha){throw "Compiled source changed: $path"};$rel=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');Add $path "source/$rel";$documents.Add(@{path=$rel;sha256=$sha})}}finally{$provider.Dispose();$stream.Dispose()}
 $bindings.Add(@{pdb=[IO.Path]::GetRelativePath($repo,$pdb).Replace('\','/');sha256=(Hash $pdb);documents=$documents})
}
foreach($path in @($runtime,$test,$app)){if((Hash "$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
Save 'source-verification.json' @{coreSha256=$core;allListedFinalSourcesMatchCompiled=$true;bindings=$bindings;sourceRevision=(Read "$root/source-revision.json");defaultAppCoreMatches=$true;appSha256=(Hash "$app/OpticalSystemDesign.App.dll");intermediateDiagnosticVersions='Retained as intermediate binaries and logs; not claimed to match the final diagnostic source'}
$studies=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'offaxis-opd-*' -and $_.Name -notmatch 'runtime'})
foreach($study in $studies){AddTree $study.FullName ('runs/'+$study.Name);foreach($binary in Get-ChildItem -LiteralPath $study.FullName -Recurse -File -Filter ZemaxHost.exe){Add $binary.FullName ('native-hosts/'+$study.Name+'/'+[IO.Path]::GetRelativePath($study.FullName,$binary.FullName).Replace('\','/'))}}
foreach($log in Get-ChildItem -LiteralPath $artifact -File|Where-Object {$_.Name -like 'offaxis-opd-*' -and $_.Extension -in @('.log','.json') -and $_.Name -notmatch 'seal'}){Add $log.FullName ('logs/'+$log.Name)}
foreach($pair in @(@($runtime,'final'),@($test,'scoped-tests'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -File|Where-Object {$_.Name -match '^OpticalSystemDesign\.(Core|Application|ZemaxComparison|Tests)\.(dll|pdb|deps.json|runtimeconfig.json)$'}){Add $f.FullName ('runtime/'+$pair[1]+'/'+$f.Name)}}
$retireRuntimes=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'offaxis-opd-*runtime-20261104' -and $_.Name -ne 'offaxis-opd-final-runtime-20261104'})
foreach($directory in $retireRuntimes){foreach($f in Get-ChildItem -LiteralPath $directory.FullName -Recurse -File){Add $f.FullName ('intermediate-runtime/'+$directory.Name+'/'+[IO.Path]::GetRelativePath($directory.FullName,$f.FullName).Replace('\','/'))}}
foreach($f in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add $f.FullName ('source/tools/diagnostics/OffAxisOpd20261104/'+$f.Name)}
foreach($f in Get-ChildItem (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs'){Add $f.FullName ('source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$f.Name)}
Add "$repo/tests/OpticalSystemDesign.Tests/Fixtures/perfect-collimation-opd-native-v1.json" 'source/tests/OpticalSystemDesign.Tests/Fixtures/perfect-collimation-opd-native-v1.json'
Add "$repo/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json" 'source/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
Add "$repo/docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md" 'docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md'
. "$PSScriptRoot/Seal-Reuse.ps1"
