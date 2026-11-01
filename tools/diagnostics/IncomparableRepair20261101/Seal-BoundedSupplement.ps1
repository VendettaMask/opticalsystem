param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=Join-Path $repo 'artifacts/validation/incomparable-bounded-knot-core-20261101'
$assessment=Join-Path $repo 'artifacts/validation/incomparable-current-accepted-assessment-20261101'
$destination=Join-Path $repo 'validation/rechecks/2026-11-01/example-incomparable-repair'
$archive=Join-Path $destination 'bounded-supplement.zip'
foreach($name in 'bounded-supplement.zip','bounded-manifest.json','bounded-verification.json','bounded-scope.json'){
 if(Test-Path -LiteralPath (Join-Path $destination $name)){throw 'A sealed supplement must never be overwritten'}
}
$scope=Get-Content -LiteralPath "$assessment/scope.json" -Raw|ConvertFrom-Json
if($scope.closedRequestCount -ne 24 -or $scope.all27Closed -or $scope.gatesChanged){throw 'Unexpected acceptance scope'}
$results=Get-Content -LiteralPath "$assessment/compare-results.json" -Raw|ConvertFrom-Json
foreach($index in $scope.closedIndices){
 if(!@($results|Where-Object{$_.index -eq $index -and $_.conclusion -eq 'Pass' -and
  $_.workbenchCoreSha256 -eq $scope.coreSha256}).Count){throw "Missing same-Core acceptance: $index"}
}
$execution=Get-Content -LiteralPath "$root/execution.json" -Raw|ConvertFrom-Json
if(@($execution).Count -ne 53 -or @($execution|Where-Object{!$_.complete -or $_.exitCode -ne 0 -or
 $_.coreSha256 -ne $scope.coreSha256}).Count){throw 'Incomplete or mixed Core execution'}
$test=Join-Path $repo 'tests/OpticalSystemDesign.Tests/TestResults/incomparable-bounded-knot-final-20261101.trx'
[xml]$trx=Get-Content -LiteralPath $test -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '77' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0'){throw 'Focused tests not passed'}
Copy-Item -LiteralPath "$assessment/scope.json" -Destination "$destination/bounded-scope.json"
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-File([string]$path){
 $full=(Resolve-Path -LiteralPath $path).Path
 if(!$full.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence outside repository'}
 [void]$files.Add($full)
}
foreach($folder in @($root,$assessment,(Join-Path $repo 'artifacts/validation/incomparable-final-mtf-current-assessment-20261101'),
 (Join-Path $repo 'artifacts/validation/incomparable-bounded-energy-quadrature-20261101'))){
 foreach($file in Get-ChildItem -LiteralPath $folder -File -Filter '*.json'){Add-File $file.FullName}
}
foreach($entry in $execution){
 $job=Get-Content -LiteralPath $entry.request -Raw|ConvertFrom-Json
 Add-File $entry.request; Add-File "$($job.outputDirectory)/data.json"; Add-File "$($job.outputDirectory)/provenance.json"
 Add-File $job.snapshotPath
}
foreach($row in $results|Where-Object{$_.index -in $scope.closedIndices}){
 foreach($name in 'data.json','data.txt','captured-settings.json','environment.json','model.json','result-diagnostics.json'){
  $path=Join-Path $row.native $name; if(Test-Path -LiteralPath $path){Add-File $path}
 }
}
$quadrature=Join-Path $repo 'artifacts/validation/incomparable-bounded-energy-quadrature-20261101'
foreach($folder in Get-ChildItem -LiteralPath $quadrature -Directory|Where-Object Name -ne 'runtime'){
 foreach($name in 'data.json','provenance.json'){
  $path=Join-Path $folder.FullName $name;if(Test-Path -LiteralPath $path){Add-File $path}
 }
}
foreach($name in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','IncomparableRepair20261101.dll',
 'OpticalSystemDesign.ZemaxComparison.dll'){Add-File "$root/runtime/$name"}
Add-File $test; Add-File "$destination/bounded-scope.json"
foreach($file in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add-File $file.FullName}
foreach($path in @('docs/EXAMPLE_INCOMPARABLE_REPAIR_2026-11-01.md',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.AxisProfiles.cs',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.FourierWindow.cs',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.CumulativeEnergy.Window.cs',
 'src/OpticalSystemDesign.Core/Analysis/Radiometry/FftCumulativeKnotGrid.cs',
 'src/OpticalSystemDesign.Core/Analysis/Radiometry/FftCumulativeEnergyGrid.cs',
 'src/OpticalSystemDesign.Core/Analysis/Radiometry/EncircledEnergyVariants.cs',
 'src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfProfileAnalyses.cs',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs',
 'src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs')){Add-File (Join-Path $repo $path)}
$manifest=@(foreach($file in @($files|Sort-Object)){
 [pscustomobject]@{entry=[IO.Path]::GetRelativePath($repo,$file).Replace('\','/');source=$file
  length=(Get-Item -LiteralPath $file).Length;sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()}
})
Add-Type -AssemblyName System.IO.Compression
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $manifest){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.source,$file.entry,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$readback=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($file in $manifest){$stream=$readback.GetEntry($file.entry).Open();try{
 $actual=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))
 if($actual -ne $file.sha256){throw "Archive hash mismatch: $($file.entry)"}
}finally{$stream.Dispose()}}}finally{$readback.Dispose()}
$manifest|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$destination/bounded-manifest.json" -Encoding utf8
@{archive=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();bytes=(Get-Item $archive).Length
 entries=$manifest.Count;allEntriesReadBackAndMatched=$true;closedRequests=24;openRequests=3;all27Closed=$false
 coreSha256=$scope.coreSha256;sealedUtc=[DateTimeOffset]::UtcNow}|ConvertTo-Json -Depth 6|
 Set-Content -LiteralPath "$destination/bounded-verification.json" -Encoding utf8
Get-Content -LiteralPath "$destination/bounded-verification.json"
