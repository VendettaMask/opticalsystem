param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$ManifestPath,
      [ValidateSet('HEAD', 'Index')][string]$Source = 'HEAD',
      [ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedAggregateSha256,
      [string]$OutputPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')

# Validate the manifest, paths and published aggregate using the existing byte
# checker. A correct working copy alone does not prove that Git carries it.
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$working = Get-FrozenIntegrityReport $repository $ManifestPath
if ($ExpectedAggregateSha256 -and $working.expectedAggregateSha256 -cne $ExpectedAggregateSha256) {
    throw 'Frozen manifest does not carry the pinned aggregate identity.'
}
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$revision = (& git -C $repository rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the repository commit.' }
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
try {
    foreach ($entry in $manifest.files) {
        if ($entry.path.Contains("`n") -or $entry.path.Contains("`r")) { throw 'Git evidence path contains a newline.' }
        $spec = if ($Source -eq 'Index') { ':0:' + $entry.path } else { $revision + ':' + $entry.path }
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
        $rows.Add([pscustomobject]@{path=$entry.path; expectedSha256=$entry.sha256;
            actualSha256=$actual; gitBlob=$blob; bytes=$length; matches=($actual -ceq $entry.sha256)})
    }
    $process.StandardInput.Close()
    if (!$process.WaitForExit(10000)) { throw 'Git batch did not exit.' }
    if ($process.ExitCode -ne 0) { throw ('Git batch failed: ' + $errors.GetAwaiter().GetResult()) }
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
$ordered = $rows.ToArray()
[Array]::Sort($ordered, [Collections.Generic.Comparer[object]]::Create({param($a,$b) [StringComparer]::Ordinal.Compare($a.path,$b.path)}))
$lines = [string[]]@($ordered | ForEach-Object { $_.path + '=' + $_.actualSha256 })
$aggregate = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(
    [string]::Join("`r`n", $lines) + "`r`n"))).ToLowerInvariant()
$mismatches = @($rows | Where-Object { !$_.matches })
$report = [ordered]@{source=$Source; commit=$revision; count=$rows.Count;
    matched=($rows.Count-$mismatches.Count); expectedAggregateSha256=$working.expectedAggregateSha256;
    actualAggregateSha256=$aggregate;
    unchanged=($mismatches.Count -eq 0 -and $aggregate -ceq $working.expectedAggregateSha256);
    mismatches=$mismatches; workingTree=$working}
$json = $report | ConvertTo-Json -Depth 8
if ($OutputPath) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
    [pscustomobject]$report | Select-Object source,commit,count,matched,unchanged,
        @{Name='workingMatched';Expression={$working.matched}} | ConvertTo-Json
} else { $json }
if (!$report.unchanged -or !$working.unchanged) { exit 1 }
