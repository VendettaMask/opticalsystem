param([string]$EvidenceRoot = 'validation/rechecks/2026-11-01/spc-branches', [string]$SourceRoot = '.', [string]$RuntimeName = 'formal-runtime')
$ErrorActionPreference = 'Stop'
$null = [Reflection.Assembly]::Load('System.Reflection.Metadata')
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$repo = (Resolve-Path -LiteralPath $SourceRoot).Path
Push-Location -LiteralPath $repo
$files = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$documents = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
foreach ($pdb in Get-ChildItem -LiteralPath (Join-Path $evidence $RuntimeName) -Filter '*.pdb' -File) {
    $stream = [IO.File]::OpenRead($pdb.FullName)
    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try {
        $reader = $provider.GetMetadataReader()
        foreach ($handle in $reader.Documents) {
            $doc = $reader.GetDocument($handle)
            $name = $reader.GetString($doc.Name)
            $expected = [Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant()
            if (!$name.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { $errors.Add('Outside repository: ' + $name); continue }
            $relative = [IO.Path]::GetRelativePath($repo, $name).Replace('\','/')
            $actual = if (Test-Path -LiteralPath $name) { (Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }
            $documents.Add([pscustomobject]@{pdb=$pdb.Name;path=$relative;expectedSha256=$expected;actualSha256=$actual;matches=($expected -eq $actual)})
            if ($expected -ne $actual) { $errors.Add('PDB source mismatch: ' + $relative) } else { $files[$relative] = $name }
        }
    } finally { $provider.Dispose(); $stream.Dispose() }
}
$documents | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'pdb-source-audit-final.json')
if ($errors.Count -gt 0) { $errors | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'pdb-source-errors.json'); throw ($errors -join "`n") }
foreach ($name in @('Directory.Build.props','Directory.Build.targets','ProductVersion.props','NuGet.Config','global.json')) {
    if (Test-Path -LiteralPath $name) { $files[$name] = Join-Path $repo $name }
}
foreach ($directory in @('src/OpticalSystemDesign.Core','labs/InitialStructure/src','tools/diagnostics/SpcBranch20261101')) {
    foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object { $_.FullName.Replace([char]92,[char]47) -notmatch '/(bin|obj)/' -and $_.Extension -in @('.csproj','.json','.props','.targets','.ps1','.ggd','.ogdb') }) {
        $files[[IO.Path]::GetRelativePath($repo,$file.FullName).Replace('\','/')] = $file.FullName
    }
}
$zipPath = Join-Path $evidence 'executed-source.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Source archive exists.' }
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
$manifest = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($key in $files.Keys | Sort-Object) {
        $bytes = [IO.File]::ReadAllBytes($files[$key])
        $entry = $archive.CreateEntry($key, [IO.Compression.CompressionLevel]::Optimal)
        $destination = $entry.Open()
        try { $destination.Write($bytes) } finally { $destination.Dispose() }
        $manifest.Add([pscustomobject]@{path=$key;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()})
    }
} finally { $archive.Dispose() }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'executed-source-sha256.json')
Write-Output ('Archived {0} files; {1} PDB document checks matched.' -f $files.Count,$documents.Count)
Pop-Location
