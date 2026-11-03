param([string]$RepositoryRoot=(Resolve-Path "$PSScriptRoot/../../..").Path,[string]$StudyRoot)
$ErrorActionPreference='Stop'
$root=if($StudyRoot){[IO.Path]::GetFullPath($StudyRoot)}else{Join-Path $RepositoryRoot 'artifacts/validation/all-example-accuracy-20261031'}
$prior=Join-Path $RepositoryRoot 'artifacts/zemax-sequential-corpus/20261021/full-v1'
$archive=Join-Path $RepositoryRoot 'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
$expectedArchive='1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
if((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $expectedArchive){throw 'Frozen native evidence archive changed'}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{$reader=[IO.StreamReader]::new($zip.GetEntry('payload-manifest.json').Open());try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json -AsHashtable}finally{$reader.Dispose()}}finally{$zip.Dispose()}
$lookup=@{};foreach($entry in $manifest){$lookup[$entry.path]=$entry}
$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($name in @('items.json','analysis-results.json','comparison-settings.json','inventory.json','models.json','sources.json')){[void]$paths.Add((Join-Path $prior $name))}
$rows=Get-Content "$root/rows.json" -Raw|ConvertFrom-Json
foreach($row in $rows){foreach($name in @('data.json','model.json','captured-settings.json','request.json','batch-result.json','error.json')){$p=Join-Path $row.legacyPath "native/$name";if(Test-Path -LiteralPath $p){[void]$paths.Add($p)}}}
foreach($dir in Get-ChildItem "$prior/rays" -Directory){foreach($name in @('job.json','inputs.json','native/ray-audit.json','native/model.json','native/request.json','native/batch-result.json','native/error.json')){$p=Join-Path $dir.FullName $name;if(Test-Path -LiteralPath $p){[void]$paths.Add($p)}}}
$models=Get-Content "$root/models.json" -Raw|ConvertFrom-Json
foreach($m in $models){[void]$paths.Add((Join-Path $prior "sources/$($m.lens)"));if((Get-FileHash -LiteralPath $m.source).Hash.ToLowerInvariant() -ne $m.sourceSha256){throw 'Product example changed'}}
$checks=foreach($p in $paths){$key='run/'+[IO.Path]::GetRelativePath($prior,$p).Replace('\','/');if(!$lookup.ContainsKey($key)){throw "Native file absent from immutable manifest: $key"};$r=$lookup[$key];$actual=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant();if($actual -ne $r.sha256 -or (Get-Item -LiteralPath $p).Length -ne $r.bytes){throw "Native reference changed: $key"};@{path=$key;sha256=$actual;bytes=$r.bytes;matched=$true}}
@{archive=$archive;archiveSha256=$expectedArchive;allMatched=$true;verifiedFiles=$checks.Count;productSources=95;files=$checks}|ConvertTo-Json -Depth 6|Set-Content "$root/reference-integrity.json" -Encoding utf8NoBOM
Write-Output "Verified $($checks.Count) original source/reference payloads against immutable native evidence."
