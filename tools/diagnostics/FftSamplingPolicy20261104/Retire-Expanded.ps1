param([Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$owned=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation/fft-sampling-policy-20261104'))
$evidence=Join-Path $repo 'validation/rechecks/2026-11-04/fft-sampling-policy'
$manifest=@(Get-Content -LiteralPath (Join-Path $evidence 'archive-index.json') -Raw|ConvertFrom-Json)
$verification=Get-Content -LiteralPath (Join-Path $evidence 'archive-verification.json') -Raw|ConvertFrom-Json
$summary=Get-Content -LiteralPath (Join-Path $evidence 'summary.json') -Raw|ConvertFrom-Json
if(!$verification.allEntriesReadbackVerified-or$summary.preliminaryAndFinalArraysWithinOriginalPointTolerance-ne120){throw 'Missing full verification'}
if((Get-FileHash -LiteralPath (Join-Path $evidence $verification.archive)).Hash.ToLowerInvariant()-ne$verification.sha256){throw 'Archive changed'}
function Safe($path){$absolute=[IO.Path]::GetFullPath($path);if(!$absolute.StartsWith($owned+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside owned workspace'};if((Get-Item -LiteralPath $absolute).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Linked target'};return $absolute}
$retired=@()
foreach($name in 'final','afocal'){
 $target=Safe (Join-Path $owned $name);$lookup=@{};foreach($m in $manifest|Where-Object logicalPath -like "$name/*"){$lookup[$m.logicalPath]=$m}
 foreach($f in Get-ChildItem -LiteralPath $target -Recurse -File){$logical=$name+'/'+[IO.Path]::GetRelativePath($target,$f.FullName).Replace('\','/');if(!$lookup.ContainsKey($logical)-or(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()-ne$lookup[$logical].sha256){throw 'Unarchived expanded payload'}}
 $retired+=$target;Remove-Item -LiteralPath $target -Recurse -Force
}
# Preliminary arrays are superseded by the fully verified final scope; retain
# their unique phase/settings diagnostics and the one floating-roundoff receipt.
$arrays=0
foreach($name in 'convergence','convergence-rest','convergence-controls'){
 $target=Safe (Join-Path $owned $name)
 foreach($f in Get-ChildItem -LiteralPath $target -Recurse -File -Filter '*-data.json'){
  $file=Safe $f.FullName;$arrays++;Remove-Item -LiteralPath $file -Force
 }
}
if($arrays-ne120){throw 'Unexpected preliminary array count'}
foreach($name in 'runtime','phase-runtime'){$target=Safe (Join-Path $owned $name);$retired+=$target;Remove-Item -LiteralPath $target -Recurse -Force}
$verification.expandedEvidenceRetired=$true
[IO.File]::WriteAllText((Join-Path $evidence 'archive-verification.json'),($verification|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $evidence 'retirement.json'),([ordered]@{retiredDirectories=$retired;supersededPreliminaryArrays=$arrays;uniquePreliminaryDiagnosticsRetained=$true;currentRuntimeAndTestsRetained=$true;completeArchiveVerifiedBeforeRetirement=$true}|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Write-Output 'Retired verified expanded final evidence, 120 superseded arrays and two temporary build runtimes.'
