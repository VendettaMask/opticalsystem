param([Parameter(Mandatory)][string]$TaskDirectory,[Parameter(Mandatory)][string]$ReportDirectory,[Parameter(Mandatory)][string]$ArchivePath)
$ErrorActionPreference='Stop'
$repository=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$task=(Resolve-Path -LiteralPath $TaskDirectory).Path
$report=(Resolve-Path -LiteralPath $ReportDirectory).Path
$archive=[IO.Path]::GetFullPath($ArchivePath)
if(-not $archive.StartsWith($repository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $archive)){throw 'Use a new repository-local archive.'}
$summary=Get-Content -LiteralPath (Join-Path $report 'summary.json') -Raw|ConvertFrom-Json
if($summary.sourceLenses -ne 14 -or -not $summary.completeExecutionAttempts -or @($summary.lensRows|Where-Object {$_.importErrors.Count -gt 0}).Count){throw 'Complete 14-lens import repair required.'}
$files=[Collections.Generic.List[object]]::new()
function Add-File([string]$Path,[string]$Name) {
    $file=Get-Item -LiteralPath $Path
    $files.Add([pscustomobject]@{path=$Name;fullPath=$file.FullName;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
}
function Add-Tree([string]$Path,[string]$Prefix,[switch]$SourcesOnly) {
    foreach($file in Get-ChildItem -LiteralPath $Path -Recurse -File){
        $relative=[IO.Path]::GetRelativePath($Path,$file.FullName).Replace('\','/')
        if($relative -match '(^|/)(bin|obj)/' -or $file.Extension -in @('.exe','.dll','.pdb','.lock','.zip') -or $file.Name.StartsWith('.')){continue}
        if($SourcesOnly -and $file.Extension -notin @('.cs','.csproj','.ps1','.md','.json')){continue}
        Add-File $file.FullName "$Prefix/$relative"
    }
}
Add-Tree (Join-Path $task 'matrix-v2') 'run'
Add-Tree $report 'report'
Add-Tree (Join-Path $task 'reference-control') 'controls'
Add-Tree $PSScriptRoot 'diagnostic-source' -SourcesOnly
Add-Tree (Join-Path $repository 'src/OpticalSystemDesign.Core') 'source/Core' -SourcesOnly
Add-File (Join-Path $repository 'tests/OpticalSystemDesign.Tests/ZemaxImportParameterAndSurfaceMappingTests.cs') 'source/ZemaxImportParameterAndSurfaceMappingTests.cs'
foreach($group in Get-ChildItem -LiteralPath (Join-Path $task 'formal-local') -Directory){Add-File (Join-Path $group.FullName 'summary.json') ('formal/'+$group.Name+'.json')}
foreach($file in Get-ChildItem -LiteralPath $task -File){if($file.Extension -in @('.json','.log') -and $file.Name -ne 'archive.log'){Add-File $file.FullName ('verification/'+$file.Name)}}
Add-Tree (Join-Path $task 'tests') 'tests'
foreach($name in @('ZEMAX_IMPORT_PARAMETER_SURFACE_REPAIR_2026-10-19.md','CURRENT_STATUS.md','NUMERICAL_PARITY.md','OPEN_ISSUES_2026-10-08.md')){Add-File (Join-Path $repository ('docs/'+$name)) ('documentation/'+$name)}
if(@($files|Group-Object path|Where-Object Count -ne 1).Count){throw 'Duplicate archive entry.'}
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach($file in $files){
            $entry=$zip.CreateEntry($file.path,[IO.Compression.CompressionLevel]::Optimal)
            $target=$entry.Open();$source=[IO.File]::OpenRead($file.fullPath)
            try{$source.CopyTo($target)}finally{$source.Dispose();$target.Dispose()}
        }
        $writer=[IO.StreamWriter]::new($zip.CreateEntry('payload-manifest.json').Open(),[Text.UTF8Encoding]::new($false))
        try{$writer.Write((@($files|Select-Object path,bytes,sha256)|ConvertTo-Json -Depth 4))}finally{$writer.Dispose()}
    }finally{$zip.Dispose()}
}finally{$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach($file in $files){
        $entry=$zip.GetEntry($file.path)
        if($null -eq $entry -or $entry.Length -ne $file.bytes){throw 'Archive payload size mismatch.'}
        $stream=$entry.Open()
        try{$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        if($hash -cne $file.sha256){throw 'Archive payload hash mismatch.'}
    }
    if($zip.Entries.Count -ne $files.Count+1){throw 'Archive entry count mismatch.'}
}finally{$zip.Dispose()}
[ordered]@{path=[IO.Path]::GetRelativePath($repository,$archive).Replace('\','/');bytes=(Get-Item -LiteralPath $archive).Length;
    payloadFiles=$files.Count;verifiedReadback=$true;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $report 'archive.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $report 'archive.json')
