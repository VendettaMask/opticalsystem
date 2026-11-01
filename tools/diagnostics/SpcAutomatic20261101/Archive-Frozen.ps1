param([string]$SnapshotRoot,[string]$EvidenceRoot,[string]$Prefix,[switch]$RuntimeOnly,[string]$RuntimeSuffix='')
$ErrorActionPreference='Stop'
$snapshot=(Resolve-Path -LiteralPath $SnapshotRoot).Path
$evidence=(Resolve-Path -LiteralPath $EvidenceRoot).Path
$runtime=Join-Path $snapshot 'tools/diagnostics/SpcAutomatic20261101/bin/Release/net10.0'
$tests=Join-Path $snapshot 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
if($RuntimeSuffix){$runtime=Join-Path $runtime $RuntimeSuffix;$tests=Join-Path $tests $RuntimeSuffix}
$packageRoot=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'
$null=[Reflection.Assembly]::Load('System.Reflection.Metadata')
$files=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$documents=[Collections.Generic.List[object]]::new()
foreach($scope in @(@{name='driver';path=$runtime},@{name='tests';path=$tests})) {
    foreach($pdb in Get-ChildItem -LiteralPath $scope.path -Filter 'OpticalSystemDesign*.pdb' -File) {
        $stream=[IO.File]::OpenRead($pdb.FullName);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
        try {
            $reader=$provider.GetMetadataReader()
            foreach($handle in $reader.Documents) {
                $doc=$reader.GetDocument($handle);$name=$reader.GetString($doc.Name)
                $inside=$name.StartsWith($snapshot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
                $package=$name.StartsWith([IO.Path]::GetFullPath($packageRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
                if(!$inside -and !$package){throw ('Source outside snapshot/packages '+$name)}
                $expected=[Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant()
                $hash=(Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant()
                if($hash -ne $expected){throw ('PDB/source mismatch '+$name)}
                $relative=if($inside){[IO.Path]::GetRelativePath($snapshot,$name).Replace([char]92,[char]47)}else{'__external/nuget/'+[IO.Path]::GetRelativePath($packageRoot,$name).Replace([char]92,[char]47)}
                $files[$relative]=$name
                $documents.Add([pscustomobject]@{scope=$scope.name;pdb=$pdb.Name;path=$relative;sha256=$hash;matched=$true})
            }
        } finally {$provider.Dispose();$stream.Dispose()}
    }
}
$documents | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence ($Prefix+'-pdb-sources.json'))
foreach($file in Get-ChildItem -LiteralPath $snapshot -File -Recurse | Where-Object { $_.FullName.Replace([char]92,[char]47) -notmatch '/(bin|obj|.avalonia-build-tasks)/' }) {
    $files[[IO.Path]::GetRelativePath($snapshot,$file.FullName).Replace([char]92,[char]47)]=$file.FullName
}
$sourceRows=@()
if(!$RuntimeOnly){
$source=[IO.Compression.ZipFile]::Open((Join-Path $evidence ($Prefix+'-source.zip')),[IO.Compression.ZipArchiveMode]::Create)
try { foreach($key in $files.Keys | Sort-Object) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($source,$files[$key],$key,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $sourceRows += [pscustomobject]@{path=$key;sha256=(Get-FileHash -LiteralPath $files[$key] -Algorithm SHA256).Hash.ToLowerInvariant()}
} } finally {$source.Dispose()}
$sourceRows | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence ($Prefix+'-source-sha256.json'))
}else{$sourceRows=@(Get-Content -LiteralPath (Join-Path $evidence ($Prefix+'-source-sha256.json')) -Raw | ConvertFrom-Json)}
$runtimeZip=[IO.Compression.ZipFile]::Open((Join-Path $evidence ($Prefix+'-runtime.zip')),[IO.Compression.ZipArchiveMode]::Create)
$runtimeRows=@()
try { foreach($scope in @(@{name='driver';path=$runtime},@{name='tests';path=$tests})) { foreach($file in Get-ChildItem -LiteralPath $scope.path -File -Recurse) {
    $key=$scope.name+'/'+[IO.Path]::GetRelativePath($scope.path,$file.FullName).Replace([char]92,[char]47)
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($runtimeZip,$file.FullName,$key,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $runtimeRows += [pscustomobject]@{path=$key;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
} } } finally {$runtimeZip.Dispose()}
$runtimeRows | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence ($Prefix+'-runtime-sha256.json'))
Write-Output ('Archived {0} sources/resources, {1} matching PDB documents, {2} runtime files.' -f $sourceRows.Count,$documents.Count,$runtimeRows.Count)
