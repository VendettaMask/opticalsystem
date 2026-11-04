param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$archive=Join-Path $repo 'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'){throw 'Original native archive identity changed'}
$target=Join-Path $repo 'artifacts/zemax-sequential-corpus/20261021/full-v1'
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
$restored=0
try {
 $reader=[IO.StreamReader]::new($zip.GetEntry('payload-manifest.json').Open())
 try {$manifest=$reader.ReadToEnd()|ConvertFrom-Json} finally {$reader.Dispose()}
 $lookup=@{};foreach($entry in $manifest){$lookup[$entry.path]=$entry}
 foreach($entry in $zip.Entries){
  $name=$entry.FullName
  $take=$name -match '^run/(items|analysis-results|comparison-settings|inventory|models|sources)\.json$' -or
        $name.StartsWith('run/sources/') -or
        ($name -match '^run/analyses/[^/]+/native/' -and $name -notmatch '\.(png|jpe?g|bmp|svg|pdf)$') -or
        $name -match '^run/rays/[^/]+/(job|inputs)\.json$' -or
        ($name -match '^run/rays/[^/]+/native/' -and $name -notmatch '\.(png|jpe?g|bmp|svg|pdf)$')
  if(!$take -or $name.EndsWith('/')){continue}
  if(!$lookup.ContainsKey($name)){throw "Missing original manifest entry: $name"}
  $destination=[IO.Path]::GetFullPath((Join-Path $target $name.Substring(4)))
  if(!$destination.StartsWith($target+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe original entry path'}
  if(!(Test-Path -LiteralPath $destination)){
   [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
   $inputStream=$entry.Open();$outputStream=[IO.File]::Create($destination)
   try {$inputStream.CopyTo($outputStream)} finally {$inputStream.Dispose();$outputStream.Dispose()}
  }
  $expected=$lookup[$name]
  if((Get-Item -LiteralPath $destination).Length -ne $expected.bytes -or
     (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected.sha256){throw "Restored native identity differs: $name"}
  $restored++
 }
} finally {$zip.Dispose()}
Write-Output "Restored and hash-verified $restored original input payloads."
