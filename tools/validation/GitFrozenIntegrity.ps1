param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$ManifestPath,
      [ValidateSet('HEAD', 'Index')][string]$Source = 'HEAD',
      [ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedAggregateSha256,
      [string]$RelocationMapPath,
      [string]$ExpectedRelocationMapSha256,
      [string]$OutputPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')

# Validate the manifest, paths and published aggregate using the existing byte
# checker. A correct working copy alone does not prove that Git carries it.
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$plan = Get-FrozenIntegrityPlan -RepositoryRoot $repository -ManifestPath $ManifestPath `
    -RelocationMapPath $RelocationMapPath -ExpectedRelocationMapSha256 $ExpectedRelocationMapSha256
$working = Get-FrozenWorkingIntegrityReport $plan
if ($ExpectedAggregateSha256 -and $working.expectedAggregateSha256 -cne $ExpectedAggregateSha256) {
    throw 'Frozen manifest does not carry the pinned aggregate identity.'
}
$revision = (& git -C $repository rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the repository commit.' }
# A normal working file must not conceal a staged/committed symlink or gitlink.
# NUL-delimited paths avoid Git quoting and platform-specific filename parsing.
$modeRecords = if ($Source -eq 'Index') { & git -C $repository ls-files --stage -z }
    else { & git -C $repository ls-tree -r -z --full-tree $revision }
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git evidence modes.' }
$gitLinks = @((($modeRecords -join "`n").Split([char]0)) | ForEach-Object {
    $parts = $_.Split("`t", 2)
    if ($parts.Count -eq 2 -and $parts[0] -match '^(120000|160000) ') { $parts[1] }
})
function Get-GitFrozenLink([string]$RelativePath) {
    foreach ($link in $gitLinks) {
        if ($RelativePath -ceq $link -or $RelativePath.StartsWith($link+'/', [StringComparison]::Ordinal)) { return $link }
    }
    return $null
}
$info = [Diagnostics.ProcessStartInfo]::new('git')
$info.ArgumentList.Add('-C'); $info.ArgumentList.Add($repository)
$info.ArgumentList.Add('cat-file'); $info.ArgumentList.Add('--batch')
$info.UseShellExecute = $false
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
$errors = $process.StandardError.ReadToEndAsync()
$stream = $process.StandardOutput.BaseStream
$rows = [Collections.Generic.List[object]]::new()
$buffer = [byte[]]::new(65536)
function Read-GitFrozenBlob([string]$RelativePath) {
    $spec = if ($Source -eq 'Index') { ':0:' + $RelativePath } else { $revision + ':' + $RelativePath }
    $process.StandardInput.WriteLine($spec)
    $process.StandardInput.Flush()
    # Read headers as bytes too: a StreamReader can buffer the binary body.
    $headerBytes = [Collections.Generic.List[byte]]::new()
    while (($next = $stream.ReadByte()) -ne 10) {
        if ($next -lt 0 -or $headerBytes.Count -gt 32768) { throw 'Invalid Git batch header.' }
        $headerBytes.Add([byte]$next)
    }
    $header = [Text.Encoding]::UTF8.GetString($headerBytes.ToArray())
    $actual = $null; $blob = $null; $length = $null
    if ($header -match '^([0-9a-f]{40,64}) blob ([0-9]+)$') {
        $blob = $Matches[1]; $length = [long]$Matches[2]; $remaining = $length
        $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
        try {
            while ($remaining -gt 0) {
                $read = $stream.Read($buffer, 0, [int][Math]::Min($remaining, $buffer.Length))
                if ($read -le 0) { throw 'Truncated Git blob.' }
                $hash.AppendData($buffer, 0, $read); $remaining -= $read
            }
            $actual = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        } finally { $hash.Dispose() }
        if ($stream.ReadByte() -ne 10) { throw 'Invalid Git blob terminator.' }
    } elseif (!$header.EndsWith(' missing', [StringComparison]::Ordinal)) {
        throw "Unexpected Git object: $header"
    }
    [pscustomobject]@{sha256=$actual; blob=$blob; bytes=$length}
}
$mapReport = $null
try {
    foreach ($entry in $plan.entries) {
        $content = Read-GitFrozenBlob $entry.resolvedPath
        $gitLink = Get-GitFrozenLink $entry.resolvedPath
        $originalPresent = $false
        if ($entry.path -cne $entry.resolvedPath) {
            $originalPresent = $null -ne (Read-GitFrozenBlob $entry.path).blob
        }
        $rows.Add([pscustomobject]@{path=$entry.path; resolvedPath=$entry.resolvedPath; expectedSha256=$entry.sha256;
            actualSha256=$content.sha256; gitBlob=$content.blob; bytes=$content.bytes; originalPathPresent=$originalPresent;
            gitLinkPath=$gitLink; matches=(!$originalPresent -and !$gitLink -and $content.sha256 -ceq $entry.sha256)})
    }
    if ($plan.relocationMap) {
        # The map must be pinned AND carried by the same Git source as the
        # evidence. An untracked/unstaged working map cannot certify HEAD/Index.
        $content = Read-GitFrozenBlob $plan.relocationMap.path
        $gitLink = Get-GitFrozenLink $plan.relocationMap.path
        $mapReport = [pscustomobject]@{path=$plan.relocationMap.path; count=$plan.relocationMap.count;
            expectedSha256=$plan.relocationMap.sha256; actualSha256=$content.sha256;
            gitBlob=$content.blob; gitLinkPath=$gitLink; matches=(!$gitLink -and $content.sha256 -ceq $plan.relocationMap.sha256)}
    }
    $process.StandardInput.Close()
    if (!$process.WaitForExit(10000)) { throw 'Git batch did not exit.' }
    if ($process.ExitCode -ne 0) { throw ('Git batch failed: ' + $errors.GetAwaiter().GetResult()) }
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
$aggregate = Get-FrozenAggregate $rows.ToArray() 'actualSha256'
$mismatches = @($rows | Where-Object { !$_.matches })
$report = [ordered]@{source=$Source; commit=$revision; count=$rows.Count;
    matched=($rows.Count-$mismatches.Count); expectedAggregateSha256=$working.expectedAggregateSha256;
    actualAggregateSha256=$aggregate;
    unchanged=($mismatches.Count -eq 0 -and $aggregate -ceq $working.expectedAggregateSha256 -and
        (!$mapReport -or $mapReport.matches));
    mismatches=$mismatches; relocationMap=$mapReport;
    relocations=@($rows | Where-Object { $_.path -cne $_.resolvedPath }); workingTree=$working}
$json = $report | ConvertTo-Json -Depth 8
if ($OutputPath) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
    [pscustomobject]$report | Select-Object source,commit,count,matched,unchanged,
        @{Name='workingMatched';Expression={$working.matched}} | ConvertTo-Json
} else { $json }
if (!$report.unchanged -or !$working.unchanged) { exit 1 }
