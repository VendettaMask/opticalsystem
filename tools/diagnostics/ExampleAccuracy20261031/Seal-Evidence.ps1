param([string]$RepositoryRoot=(Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference='Stop'
$out=Join-Path $RepositoryRoot 'validation/rechecks/2026-10-31/all-example-accuracy'
$archive=Join-Path $out 'evidence-current.zip'
if(Test-Path -LiteralPath $archive){throw 'Immutable evidence already exists'}
$summary=Get-Content "$out/summary.json" -Raw|ConvertFrom-Json
if($summary.mainRequests -ne 3024 -or $summary.productExamples -ne 95 -or $summary.afocal.requests -ne 170){throw 'Incomplete report'}
$files=[Collections.Generic.List[object]]::new()
function AddFile($path,$name){$item=Get-Item -LiteralPath $path;$files.Add(@{path=$name;source=$item.FullName;bytes=$item.Length;sha256=(Get-FileHash -LiteralPath $item.FullName).Hash.ToLowerInvariant()})}
function AddDirectory($directory,$prefix){foreach($file in Get-ChildItem -LiteralPath $directory -File -Recurse){$relative=[IO.Path]::GetRelativePath($directory,$file.FullName).Replace('\','/');if($file.Extension -in @('.png','.svg','.pdf','.pdb','.exe','.zip') -or $relative -match '(^|/)(bin|obj)/'){continue};AddFile $file.FullName "$prefix/$relative"}}
AddDirectory (Join-Path $RepositoryRoot 'artifacts/validation/all-example-accuracy-20261031') 'study'
AddDirectory (Join-Path $RepositoryRoot 'artifacts/validation/all-example-afocal-20261031') 'afocal-study'
foreach($file in Get-ChildItem $out -File){if($file.Extension -in @('.json','.csv','.md') -and $file.Name -notin @('manifest.json','verification.json')){AddFile $file.FullName "report/$($file.Name)"}}
$asset=Join-Path $RepositoryRoot 'src/OpticalSystemDesign.App/Assets/SequentialExamples'
$index=Get-Content "$asset/index.json" -Raw|ConvertFrom-Json
AddFile "$asset/index.json" 'inputs/index.json'
foreach($entry in $index.Files){AddFile (Join-Path $asset $entry.RelativePath) "inputs/Sequential/$($entry.RelativePath)"}
foreach($name in @('all-example-accuracy-runner-20261031','all-example-accuracy-audit-runner-20261031-v3','all-example-accuracy-final-runner-20261031','all-example-accuracy-probe-runner-20261031','all-example-afocal-runner-20261031')){
 $runtime=Join-Path $RepositoryRoot "artifacts/validation/$name"
 foreach($file in Get-ChildItem $runtime -File){if($file.Extension -in @('.dll','.json')){AddFile $file.FullName "runtime/$name/$($file.Name)"}}
 $native=Join-Path $runtime 'runtimes/win-x64/native/libSkiaSharp.dll';if(Test-Path $native){AddFile $native "runtime/$name/runtimes/win-x64/native/libSkiaSharp.dll"}
}
foreach($file in Get-ChildItem (Join-Path $RepositoryRoot 'artifacts/validation') -File -Filter 'all-example-*-20261031.log'|Where-Object {$_.Name -notlike '*-seal*'}){AddFile $file.FullName "logs/$($file.Name)"}
AddDirectory $PSScriptRoot 'diagnostic-source'
foreach($name in @('Program.cs','CurrentInputs.cs','FinalAfocalRecheck20261029.csproj')){AddFile (Join-Path $RepositoryRoot "tools/diagnostics/FinalAfocalRecheck20261029/$name") "afocal-diagnostic-source/$name"}
AddFile (Join-Path $RepositoryRoot 'tools/diagnostics/AfocalReferenceFocus20261027/ScopedAfocalNormalizer.cs') 'afocal-diagnostic-source/ScopedAfocalNormalizer.cs'
AddFile (Join-Path $RepositoryRoot 'docs/ALL_EXAMPLE_ACCURACY_2026-10-31.md') 'documentation/ALL_EXAMPLE_ACCURACY_2026-10-31.md'
AddFile (Join-Path $RepositoryRoot 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json') 'policy/comparison-settings.json'
if(@($files|Group-Object path|Where-Object Count -gt 1).Count){throw 'Duplicate evidence entry'}
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
 try{foreach($file in $files){$e=$zip.CreateEntry($file.path,[IO.Compression.CompressionLevel]::Optimal);$destination=$e.Open();$source=[IO.File]::OpenRead($file.source);try{$source.CopyTo($destination)}finally{$source.Dispose();$destination.Dispose()}}}finally{$zip.Dispose()}
}finally{$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($file in $files){$e=$zip.GetEntry($file.path);if($e.Length -ne $file.bytes){throw 'Truncated payload'};$s=$e.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($sha -ne $file.sha256){throw "Payload mismatch: $($file.path)"}}}finally{$zip.Dispose()}
$hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
@{sha256=$hash;bytes=(Get-Item -LiteralPath $archive).Length;entries=@($files|ForEach-Object{@{path=$_.path;sha256=$_.sha256;bytes=$_.bytes}})}|ConvertTo-Json -Depth 6|Set-Content "$out/manifest.json" -Encoding utf8NoBOM
@{sha256=$hash;verifiedEntries=$files.Count;allMatched=$true;coreSha256=$summary.coreSha256}|ConvertTo-Json|Set-Content "$out/verification.json" -Encoding utf8NoBOM
Write-Output "Evidence sealed: $hash; $($files.Count) entries; $((Get-Item -LiteralPath $archive).Length) bytes"
