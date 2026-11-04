param([Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$owned=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation/image-simulation-propagation-20261104'))
$out=Join-Path $repo 'validation/rechecks/2026-11-04/image-simulation-pickup'
$manifest=@(Get-Content -LiteralPath "$out/archive-index.json" -Raw|ConvertFrom-Json)
$verified=Get-Content -LiteralPath "$out/archive-verification.json" -Raw|ConvertFrom-Json
if(!$verified.completeReadback-or(Get-FileHash -LiteralPath (Join-Path $out $verified.archive)).Hash.ToLowerInvariant()-ne$verified.sha256){throw 'Unverified archive'}
function Safe($path){$absolute=[IO.Path]::GetFullPath($path);if(!$absolute.StartsWith($owned+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside owned directory'};if((Get-Item -LiteralPath $absolute).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Linked target'};return $absolute}
$retired=@()
foreach($name in 'before-full','after','import-audit','phase'){
 $target=Safe (Join-Path $owned $name);$lookup=@{};foreach($m in $manifest|Where-Object logicalPath -like "$name/*"){$lookup[$m.logicalPath]=$m}
 foreach($f in Get-ChildItem -LiteralPath $target -Recurse -File){
  $logical=$name+'/'+[IO.Path]::GetRelativePath($target,$f.FullName).Replace('\','/')
  if(!$lookup.ContainsKey($logical)-or(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()-ne$lookup[$logical].sha256){throw 'Unarchived expanded payload'}
 }
 $retired+=$target;Remove-Item -LiteralPath $target -Recurse -Force
}
$hashes=[Collections.Generic.HashSet[string]]::new();foreach($m in $manifest){[void]$hashes.Add($m.sha256)}
$duplicateFiles=0;$retainedUnique=@()
foreach($name in 'before','after-initial'){
 $target=Safe (Join-Path $owned $name)
 foreach($f in Get-ChildItem -LiteralPath $target -Recurse -File){
  $file=Safe $f.FullName;$hash=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
  if($hashes.Contains($hash)){$duplicateFiles++;Remove-Item -LiteralPath $file -Force}else{$retainedUnique+=[pscustomobject]@{path=$file;sha256=$hash}}
 }
}
$oldRuntime=Safe (Join-Path $owned 'runtime');$retired+=$oldRuntime;Remove-Item -LiteralPath $oldRuntime -Recurse -Force
$verified.expandedEvidenceRetired=$true
[IO.File]::WriteAllText("$out/archive-verification.json",($verified|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$out/retirement.json",([ordered]@{retiredDirectories=$retired;verifiedPartialDuplicateFilesRemoved=$duplicateFiles;retainedUniquePreliminaryFiles=$retainedUnique;currentRuntimeAndTestsRetained=$true;completeReadbackBeforeRetirement=$true}|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Write-Output "Retired verified expanded evidence and $duplicateFiles identical partial-probe files; unique preliminary metadata retained."
