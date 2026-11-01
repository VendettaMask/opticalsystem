param([Parameter(Mandatory)][string]$Output,[string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
if(Test-Path -LiteralPath $Output){throw 'Fresh concurrent audit required'}
Add-Type -AssemblyName System.Reflection.Metadata
function Read-Documents([string]$path){
 $s=[IO.File]::OpenRead($path);try{$p=[Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($s);try{
  $r=$p.GetMetadataReader();foreach($h in $r.Documents){$d=$r.GetDocument($h);$name=$r.GetString($d.Name)
   if($name.StartsWith((Join-Path $repo 'src/OpticalSystemDesign.Core'),[StringComparison]::OrdinalIgnoreCase)){
    [pscustomobject]@{path=$name;hash=[Convert]::ToHexStringLower($r.GetBlobBytes($d.Hash))}
   }
  }
 }finally{$p.Dispose()}}finally{$s.Dispose()}
}
function Read-Resources([string]$path){
 $s=[IO.File]::OpenRead($path);try{$p=[Reflection.PortableExecutable.PEReader]::new($s);try{
  $r=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($p);foreach($h in $r.ManifestResources){$m=$r.GetManifestResource($h)
   if(!$m.Implementation.IsNil){throw 'External resource not expected'}
   $b=$p.GetSectionData($p.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress+[int]$m.Offset).GetReader();$length=$b.ReadInt32()
   [pscustomobject]@{name=$r.GetString($m.Name);hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($b.ReadBytes($length)))}
  }
 }finally{$p.Dispose()}}finally{$s.Dispose()}
}
function Unrelated([string]$path,[string]$hash){
 $relative=[IO.Path]::GetRelativePath((Join-Path $repo 'src/OpticalSystemDesign.Core'),$path).Replace('\','/')
 # The reviewed serializer delta only gates the new NSC optical-assembly
 # payload as STAROPT v20. The sequential JSON snapshot path is unchanged.
 $reviewedStore=$relative -eq 'Serialization/StarOptProjectStore.cs' -and $hash -eq 'ce59ff1bf623842d8a0e32f08f29459a6e26b9f492fdd3fa24551c2f34c1ff6e'
 return $relative -eq 'Optimization/StationaryPointSearch.cs' -or $relative.StartsWith('NonSequential/') -or $reviewedStore
}
$frozen=Join-Path $repo 'artifacts/validation/grid-mtf-final-core-20261101/runtime/OpticalSystemDesign.Core'
$current=Join-Path $repo 'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core'
$before=(Get-FileHash -LiteralPath "$current.dll").Hash.ToLowerInvariant()
$old=@(Read-Documents "$frozen.pdb");$now=@(Read-Documents "$current.pdb");$map=@{};foreach($d in $old){$map[$d.path]=$d.hash}
$compiledChanges=@(foreach($d in $now){if(!$map.ContainsKey($d.path) -or $map[$d.path] -ne $d.hash){[pscustomobject]@{path=$d.path;unrelated=Unrelated $d.path $d.hash;frozen=$map[$d.path];current=$d.hash}}})
$newMap=@{};foreach($d in $now){$newMap[$d.path]=$d.hash};$compiledChanges+=@(foreach($d in $old){if(!$newMap.ContainsKey($d.path)){[pscustomobject]@{path=$d.path;unrelated=Unrelated $d.path;frozen=$d.hash;current='removed'}}})
$sourceChanges=@(foreach($d in $old){$hash=if(Test-Path -LiteralPath $d.path){(Get-FileHash -LiteralPath $d.path).Hash.ToLowerInvariant()}else{'missing'};if($hash -ne $d.hash){[pscustomobject]@{path=$d.path;unrelated=Unrelated $d.path $hash;frozen=$d.hash;current=$hash}}})
$resourcesOld=@(Read-Resources "$frozen.dll");$resourcesNow=@(Read-Resources "$current.dll")
$resourceDelta=@(Compare-Object @($resourcesOld|ForEach-Object {$_.name+':'+$_.hash}) @($resourcesNow|ForEach-Object {$_.name+':'+$_.hash}))
$after=(Get-FileHash -LiteralPath "$current.dll").Hash.ToLowerInvariant();if($before -ne $after){throw 'Default binary changed during audit'}
@{acceptanceCore=(Get-FileHash -LiteralPath "$frozen.dll").Hash.ToLowerInvariant();defaultCore=$after;documents=$old.Count
 compiledChanges=$compiledChanges;sourceChanges=$sourceChanges;onlyUnrelatedChanges=(@(@($compiledChanges+$sourceChanges)|Where-Object {!$_.unrelated}).Count -eq 0)
 resourcesUnchanged=($resourceDelta.Count -eq 0);resourceCount=$resourcesOld.Count;recordedUtc=[DateTimeOffset]::UtcNow}|
 ConvertTo-Json -Depth 12|Set-Content -LiteralPath $Output
Get-Content -LiteralPath $Output
