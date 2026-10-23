param(
    [Parameter(Mandatory)][string]$RunDirectory,
    [Parameter(Mandatory)][string]$OutputArchive,
    [Parameter(Mandatory)][string[]]$Groups,
    [switch]$MetadataOnly
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RunDirectory).Path
$archive=[IO.Path]::GetFullPath($OutputArchive)
if(Test-Path -LiteralPath $archive){throw 'Fresh archive required'}
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
$files=@()
foreach($group in $Groups){
    $path=[IO.Path]::GetFullPath((Join-Path $root $group))
    if(-not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Group escaped run directory'}
    if(-not(Test-Path -LiteralPath $path)){throw "Missing evidence group: $group"}
    $item=Get-Item -LiteralPath $path
    $files+=@(if($item.PSIsContainer){Get-ChildItem -LiteralPath $path -Recurse -File}else{$item})
}
$files=@($files|Sort-Object FullName -Unique|Where-Object {
    $_.Extension -notin '.exe','.dll','.pdb','.png','.svg' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
    (-not $MetadataOnly -or $_.Name -in 'summary.json','identity.json','interrupted.json','error.txt','comparison.json')
})
$manifest=@()
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
    foreach($file in $files){
        $name=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
        $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::SmallestSize)
        $input=[IO.File]::OpenRead($file.FullName);$target=$entry.Open()
        try{$input.CopyTo($target)}finally{$target.Dispose();$input.Dispose()}
        $manifest+=@{path=$name;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
    }
}finally{$zip.Dispose();$stream.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    foreach($item in $manifest){
        $entry=$verify.GetEntry($item.path)
        if($null -eq $entry -or $entry.Length -ne $item.bytes){throw "Archive shape differs: $($item.path)"}
        $input=$entry.Open();$hash=[Security.Cryptography.SHA256]::Create()
        try{$actual=[Convert]::ToHexString($hash.ComputeHash($input)).ToLowerInvariant()}finally{$hash.Dispose();$input.Dispose()}
        if($actual -cne $item.sha256){throw "Archive bytes differ: $($item.path)"}
    }
}finally{$verify.Dispose()}
$record=@{archive=(Split-Path -Leaf $archive);archiveSha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();
    fileCount=$manifest.Count;uncompressedBytes=($manifest|Measure-Object bytes -Sum).Sum;files=$manifest;
    excluded='Executable/SDK binaries, debug symbols, derived PNG/SVG plots and build bin/obj folders.'}
ConvertTo-Json -InputObject $record -Depth 10|Set-Content -LiteralPath ($archive+'.manifest.json') -Encoding utf8
Get-Item -LiteralPath $archive|Select-Object Name,Length
