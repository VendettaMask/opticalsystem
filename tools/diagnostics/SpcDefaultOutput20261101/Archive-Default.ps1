param([string]$EvidenceRoot='validation/rechecks/2026-11-01/spc-default-output')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path
$evidence=(Resolve-Path -LiteralPath $EvidenceRoot).Path
$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$tests=Join-Path $repo 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
$names=@('OpticalSystemDesign.Core','OpticalSystemDesign.InitialStructure.Contracts','OpticalSystemDesign.InitialStructure.Engine',
 'OpticalSystemDesign.InitialStructure.Persistence','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine',
 'OpticalSystemDesign.Modules','OpticalSystemDesign.App')
$null=[Reflection.Assembly]::Load('System.Reflection.Metadata')
$sourceFiles=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$documents=@();$binaries=@();$drift=@()
$previousSource=[IO.Compression.ZipFile]::OpenRead((Join-Path $repo 'validation/rechecks/2026-11-01/spc-automatic-resume/v2-source.zip'))
foreach($name in $names){foreach($extension in @('.dll','.pdb')){$binaries += Join-Path $app ($name+$extension)}}
foreach($extension in @('.dll','.pdb','.deps.json','.runtimeconfig.json')){$binaries += Join-Path $tests ('OpticalSystemDesign.InitialStructure.Tests'+$extension)}
foreach($file in $binaries | Where-Object { $_.EndsWith('.pdb') }) {
    $stream=[IO.File]::OpenRead($file);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try {
        $reader=$provider.GetMetadataReader()
        foreach($handle in $reader.Documents) {
            $doc=$reader.GetDocument($handle);$name=$reader.GetString($doc.Name)
            $expected=[Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant()
            $actual=(Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant()
            if($name.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){$relative=[IO.Path]::GetRelativePath($repo,$name).Replace([char]92,[char]47)}
            elseif($name -match '\.nuget[\\/]packages[\\/](.+)$'){$relative='__external/nuget/'+$Matches[1].Replace([char]92,[char]47)}
            else{throw ('Unexpected source outside checkout: '+$name)}
            $archivedPath=$name;$matched=$expected -eq $actual;$recovered=$false
            if(!$matched){
                $entry=$previousSource.GetEntry($relative)
                if($entry){$input=$entry.Open();$buffer=[IO.MemoryStream]::new();try{$input.CopyTo($buffer)}finally{$input.Dispose()};$bytes=$buffer.ToArray();$buffer.Dispose()
                    if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $expected){
                        $archivedPath=Join-Path $evidence ('recovered-source/'+$relative);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($archivedPath))|Out-Null
                        [IO.File]::WriteAllBytes($archivedPath,$bytes);$matched=$true;$recovered=$true
                    }
                }
                $drift += [pscustomobject]@{path=$relative;compiledSha256=$expected;currentSha256=$actual;recoveredFromPriorSource=$recovered}
            }
            if($matched){$sourceFiles[$relative]=$archivedPath}
            $documents += [pscustomobject]@{pdb=[IO.Path]::GetFileName($file);path=$relative;sha256=$expected;matched=$matched;recovered=$recovered}
        }
    }finally{$provider.Dispose();$stream.Dispose()}
}
$previousSource.Dispose()
$documents | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'default-pdb-sources.json')
$drift | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'source-drift.json')
$runtime=[IO.Compression.ZipFile]::Open((Join-Path $evidence 'default-runtime.zip'),[IO.Compression.ZipArchiveMode]::Create)
$runtimeRows=@()
try {foreach($file in $binaries){$prefix=if($file.StartsWith($app,[StringComparison]::OrdinalIgnoreCase)){'app/'}else{'tests/'};$key=$prefix+[IO.Path]::GetFileName($file)
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($runtime,$file,$key,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    $runtimeRows += [pscustomobject]@{path=$key;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
}}finally{$runtime.Dispose()}
$runtimeRows | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'default-runtime-sha256.json')
foreach($file in @('AGENTS.md','global.json','Directory.Build.props','Directory.Build.targets','ProductVersion.props',
 'labs/InitialStructure/Directory.Build.props','labs/CoatingDesign/Directory.Build.props')){$sourceFiles[$file]=Join-Path $repo $file}
foreach($directory in @('src','labs/InitialStructure/src','labs/CoatingDesign/src')){foreach($file in Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object{
    $_.FullName.Replace([char]92,[char]47) -notmatch '/(bin|obj)/' -and $_.Extension -in @('.csproj','.props','.targets','.json','.ggd','.ogdb')}){
    $sourceFiles[[IO.Path]::GetRelativePath($repo,$file.FullName).Replace([char]92,[char]47)]=$file.FullName
}}
$source=[IO.Compression.ZipFile]::Open((Join-Path $evidence 'default-source.zip'),[IO.Compression.ZipArchiveMode]::Create)
$sourceRows=@()
try{foreach($key in $sourceFiles.Keys | Sort-Object){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($source,$sourceFiles[$key],$key,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    $sourceRows += [pscustomobject]@{path=$key;sha256=(Get-FileHash -LiteralPath $sourceFiles[$key] -Algorithm SHA256).Hash.ToLowerInvariant()}
}}finally{$source.Dispose()}
$sourceRows | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'default-source-sha256.json')
Write-Output ('Default output archived: {0} PDB documents matched, {1} source/build/resource files, {2} binary/test-host files.' -f $documents.Count,$sourceRows.Count,$runtimeRows.Count)
