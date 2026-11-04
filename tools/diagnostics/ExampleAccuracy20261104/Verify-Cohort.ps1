param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$primary=Join-Path $repo 'artifacts/validation/all-example-accuracy-20261104/runtime'
$afocal=Join-Path $repo 'artifacts/validation/all-example-afocal-20261104-final/runner'
$evidence=Join-Path $repo 'validation/rechecks/2026-11-04/all-example-accuracy'
function Hash($p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
$bindings=[Collections.Generic.List[object]]::new()
foreach($pdb in @((Join-Path $primary 'OpticalSystemDesign.Core.pdb'),(Join-Path $primary 'OpticalSystemDesign.Application.pdb'),(Join-Path $primary 'OpticalSystemDesign.ZemaxComparison.pdb'),(Join-Path $primary 'ExampleAccuracy20261031.pdb'),(Join-Path $afocal 'FinalAfocalRecheck20261029.pdb'))){
 $stream=[IO.File]::OpenRead($pdb)
 $provider=[Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
 $documents=[Collections.Generic.List[object]]::new()
 try {
  $reader=$provider.GetMetadataReader()
  foreach($handle in $reader.Documents){
   $document=$reader.GetDocument($handle);$path=$reader.GetString($document.Name)
   if($path -match '[\\/]obj[\\/]'){continue}
   if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Compiled source unavailable: $path"}
   $expected=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant()
   if((Hash $path) -ne $expected){throw "Compiled source drift: $path"}
   $documents.Add(@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=$expected})
  }
 } finally {$provider.Dispose();$stream.Dispose()}
 $dll=[IO.Path]::ChangeExtension($pdb,'.dll')
 $bindings.Add(@{assembly=[IO.Path]::GetFileName($dll);assemblySha256=(Hash $dll);pdbSha256=(Hash $pdb);documents=$documents})
}
foreach($name in @('OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','OpticalSystemDesign.ZemaxComparison.dll')){
 if((Hash (Join-Path $primary $name)) -ne (Hash (Join-Path $afocal $name))){throw "Main/afocal runtime mismatch: $name"}
}
@{bindings=$bindings;mainAndAfocalProductAssembliesMatch=$true;compiledSourceMatches=$true}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $evidence 'source-verification.json') -Encoding utf8NoBOM
Write-Output "Verified $($bindings.Count) compiled-source bindings and matching main/afocal product assemblies."
