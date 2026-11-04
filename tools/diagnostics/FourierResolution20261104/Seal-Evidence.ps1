param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$artifact=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/fourier-result-validation'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 90),[Text.UTF8Encoding]::new($false))}
$summary=Read "$root/summary.json";$core=$summary.coreAfter
if($summary.distinctTests -ne 707 -or $summary.priorityPass -ne 6 -or $summary.referencesConverged -ne 13 -or $summary.nativeSamplingCaptures -ne 33){throw 'Incomplete acceptance'}
$archive=Join-Path $root 'evidence.zip';if(Test-Path $archive){throw 'Already sealed'}
$files=[Collections.Generic.List[object]]::new()
function Add($path,$logical){$files.Add(@{path=[IO.Path]::GetFullPath($path);logical=$logical})}
function AddTree($path,$prefix){foreach($f in Get-ChildItem -LiteralPath $path -Recurse -File){$rel=[IO.Path]::GetRelativePath($path,$f.FullName).Replace('\','/');if($rel -match '(^|/)(bin|obj)/' -or $f.Extension -in @('.png','.svg','.pdf','.dll','.pdb','.exe')){continue};Add $f.FullName "$prefix/$rel"}}
$runtime=Join-Path $artifact 'fourier-resolution-final-runtime-20261104';$test=Join-Path $repo 'tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0';$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$bindings=[Collections.Generic.List[object]]::new()
$sourceVersions=@(Read "$root/concurrent-source-versions.json")
foreach($pdb in @("$runtime/OpticalSystemDesign.Core.pdb","$runtime/OpticalSystemDesign.Application.pdb","$runtime/OpticalSystemDesign.ZemaxComparison.pdb","$runtime/OpticalSystemDesign.Tests.pdb","$runtime/AnalysisDefinitionRepair20261104.pdb","$test/OpticalSystemDesign.Tests.pdb")){
 $stream=[IO.File]::OpenRead($pdb);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream);$documents=[Collections.Generic.List[object]]::new()
 try{$reader=$provider.GetMetadataReader();foreach($handle in $reader.Documents){$d=$reader.GetDocument($handle);$path=$reader.GetString($d.Name);if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue};$sha=[Convert]::ToHexString($reader.GetBlobBytes($d.Hash)).ToLowerInvariant();$rel=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');$kind='Current workspace';if((Hash $path) -ne $sha){$version=$sourceVersions|Where-Object {$_.path -eq $rel -and $_.compiledSha256 -eq $sha};if(!$version){throw "Compiled source changed: $path"};$path=Join-Path $repo $version.frozenPath;if((Hash $path) -ne $sha){throw 'Compiled archived source differs'};$kind='Verified archived pre-concurrent material source'};Add $path "source/$rel";$documents.Add(@{path=$rel;sha256=$sha;sourceVersion=$kind})}}finally{$provider.Dispose();$stream.Dispose()}
 $bindings.Add(@{pdb=[IO.Path]::GetRelativePath($repo,$pdb).Replace('\','/');sha256=(Hash $pdb);documents=$documents})
}
foreach($path in @($runtime,$test)){if((Hash "$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
Save 'source-verification.json' @{coreSha256=$core;allBoundCompiledSourcesVerified=$true;bindings=$bindings;sourceRevision=(Read "$root/source-revision.json");defaultAppBuildReceipt=(Read "$root/default-app-build-receipt.json");concurrentMaterialSourceVersions=$sourceVersions;currentCombinedWorkspaceCertified=$false;intermediateDiagnosticVersions='Retained as intermediate binaries and logs; not claimed to match the final diagnostic source'}
$studies=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'fourier-resolution-*' -and $_.Name -notmatch 'runtime'})
foreach($study in $studies){AddTree $study.FullName ('runs/'+$study.Name);foreach($binary in Get-ChildItem -LiteralPath $study.FullName -Recurse -File -Filter ZemaxHost.exe){Add $binary.FullName ('native-hosts/'+$study.Name+'/'+[IO.Path]::GetRelativePath($study.FullName,$binary.FullName).Replace('\','/'))}}
foreach($log in Get-ChildItem -LiteralPath $artifact -File|Where-Object {$_.Name -like 'fourier-resolution-*' -and $_.Extension -in @('.log','.json') -and $_.Name -notmatch 'seal'}){Add $log.FullName ('logs/'+$log.Name)}
foreach($pair in @(@($runtime,'final'),@($test,'scoped-tests'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -File|Where-Object {$_.Name -match '^(OpticalSystemDesign\.(Core|Application|ZemaxComparison|Tests)|AnalysisDefinitionRepair20261104)\.(dll|pdb|deps.json|runtimeconfig.json)$'}){Add $f.FullName ('runtime/'+$pair[1]+'/'+$f.Name)}}
$retireRuntimes=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'fourier-resolution-*runtime-20261104' -and $_.Name -ne 'fourier-resolution-final-runtime-20261104'})
foreach($directory in $retireRuntimes){foreach($f in Get-ChildItem -LiteralPath $directory.FullName -Recurse -File){Add $f.FullName ('intermediate-runtime/'+$directory.Name+'/'+[IO.Path]::GetRelativePath($directory.FullName,$f.FullName).Replace('\','/'))}}
foreach($f in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add $f.FullName ('source/tools/diagnostics/FourierResolution20261104/'+$f.Name)}
foreach($f in Get-ChildItem (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs'){Add $f.FullName ('source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$f.Name)}
Add "$repo/tests/OpticalSystemDesign.Tests/Fixtures/perfect-collimation-opd-native-v1.json" 'source/tests/OpticalSystemDesign.Tests/Fixtures/perfect-collimation-opd-native-v1.json'
Add "$repo/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json" 'source/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
Add "$repo/docs/FOURIER_RESULT_VALIDATION_REPAIR_2026-11-04.md" 'docs/FOURIER_RESULT_VALIDATION_REPAIR_2026-11-04.md'
Add "$artifact/fourier-resolution-report-script-used-20261104.ps1" 'source/tools/diagnostics/FourierResolution20261104/Write-Report-used-at-first-verification.ps1'
. "$PSScriptRoot/Seal-Reuse.ps1"
