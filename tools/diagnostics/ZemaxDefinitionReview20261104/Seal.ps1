param([Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$work=Join-Path $repo 'artifacts/validation/zemax-definition-review-20261104-final'
$out=Join-Path $repo 'validation/rechecks/2026-11-04/zemax-definition-review';[IO.Directory]::CreateDirectory($out)|Out-Null
function Read($path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function Save($name,$value){[IO.File]::WriteAllText((Join-Path $out $name),($value|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))}
function Hash($path){(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
$identity=Read "$work/identity.json";$modes=@(Read "$work/reference-modes.json");$surfaces=@(Read "$work/surfaces.json")
if($modes.Count-ne4-or$surfaces.Count-ne16-or@($modes.curveHash|Sort-Object -Unique).Count-ne1){throw 'Incomplete or changed reproduction'}
$absolute=$modes|Where-Object mode -eq 'Absolute';$maximum=($absolute.rows|ForEach-Object{[Math]::Abs($_.errorAgainstAbsolute)}|Measure-Object -Maximum).Maximum
foreach($key in 'Optical Path Difference','Wavefront'){
 $rows=@($surfaces|Where-Object analysis -eq $key)
 if($rows.Count-ne4-or@($rows.curveHash|Sort-Object -Unique).Count-ne1-or($rows|Where-Object requestedSurface -eq 999).outcome-ne'Success'){throw 'Surface reproduction changed'}
}
$oldImports=@(Read (Join-Path $repo 'validation/rechecks/2026-11-04/image-simulation-pickup/import-audit.json'))
$inventory=@();$examples=Join-Path $repo 'src/OpticalSystemDesign.App/Assets/SequentialExamples'
foreach($file in Get-ChildItem -LiteralPath $examples -Recurse -File|Where-Object Extension -ieq '.zmx'){
 $path=[IO.Path]::GetRelativePath($examples,$file.FullName).Replace('\','/');$hash=Hash $file.FullName
 if(($oldImports|Where-Object lens -eq $path).sourceHash-ne$hash){throw 'Example source changed'}
 $line=Select-String -LiteralPath $file.FullName -Pattern '^ROPD\s+'|Select-Object -First 1
 $inventory+=[pscustomobject]@{path=$path;sha256=$hash;referenceOpd=$line.Line.Trim()}
}
if($inventory.Count-ne95-or@($inventory|Where-Object referenceOpd -ne 'ROPD 2').Count){throw 'Source mode inventory changed'}
Save 'source-inventory.json' $inventory
foreach($name in 'identity.json','reference-modes.json','surfaces.json'){Copy-Item -LiteralPath "$work/$name" -Destination $out}
Save 'summary.json' ([ordered]@{scope='Documentation review and independent diagnostic controls; not native numerical certification';
 workbenchRequests=20;sharedWavefrontProbeGroups=4;focalReferenceModeCurveVariants=1;requestedReferenceModes=4;
 absoluteProbeMaximumDefinitionMismatchWaves=$maximum;opdAndWavefrontIgnoreSurfaceSelection=$true;nonexistentSurface999ReportedSuccess=$true;
 psfAndMtfIntermediatePreparation='Truncation without documented temporary focal plane; before-stop selection loses stop';
 originalExamples=95;originalExamplesUsingExitPupil=95;productCodeChanged=$false;nativeCaptured=$false;
 existingSixOpdResidualCauseEstablished=$false;coreSha256=$identity.coreSha256;
 sourceHashes=@('src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs','src/OpticalSystemDesign.Core/Analysis/Wavefront/OpticalPathDifferenceAnalysis.cs',
 'src/OpticalSystemDesign.Core/Analysis/Wavefront/WavefrontAnalyses.cs','src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs',
 'tools/diagnostics/ZemaxDefinitionReview20261104/Program.cs')|ForEach-Object{[pscustomobject]@{path=$_;sha256=Hash (Join-Path $repo $_)}}})
$file=[IO.File]::Open("$out/evidence.zip",[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
$manifest=@();$written=@{}
try{
 foreach($source in Get-ChildItem -LiteralPath $work -File){
  $hash=Hash $source.FullName;$entry="payloads/$hash"
  if(!$written.ContainsKey($hash)){$item=$zip.CreateEntry($entry,[IO.Compression.CompressionLevel]::Optimal);$input=$source.OpenRead();$output=$item.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()};$written[$hash]=$true}
  $manifest+=[pscustomobject]@{logicalPath=$source.Name;entry=$entry;sha256=$hash;length=$source.Length}
 }
}finally{$zip.Dispose();$file.Dispose()}
Save 'archive-index.json' $manifest
$zip=[IO.Compression.ZipFile]::OpenRead("$out/evidence.zip")
try{foreach($item in $manifest){$input=$zip.GetEntry($item.entry).Open();$memory=[IO.MemoryStream]::new();try{$input.CopyTo($memory);$bytes=$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()};if($bytes.Length-ne$item.length-or[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))-ne$item.sha256){throw 'Archive mismatch'}}}finally{$zip.Dispose()}
Save 'archive-verification.json' ([ordered]@{archive='evidence.zip';sha256=Hash "$out/evidence.zip";logicalFiles=$manifest.Count;uniquePayloads=$written.Count;completeReadback=$true;expandedEvidenceRetired=$false})
Write-Output "Confirmed two definition gaps; $($manifest.Count) payloads verified. No product change or native capture."
