param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Fresh output required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$fixture = Join-Path $output 'repository'
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'validation/rechecks/2000-01-01/results') -Force
$tool = Join-Path $PSScriptRoot 'HistoricalEvidence.ps1'
$manifestPath = Join-Path $fixture 'validation/historical-compression.json'
$originalPath = 'validation/rechecks/2000-01-01/results/failed.json'
$payload = [Text.Encoding]::UTF8.GetBytes("{`r`n  `"verdict`": `"failed`", `"说明`": `"保留原始失败`"`r`n}`r`n")
$compressedPath = Join-Path $fixture ($originalPath + '.gz')
$stream = [IO.File]::Create($compressedPath)
$gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::SmallestSize)
$gzip.Write($payload); $gzip.Dispose(); $stream.Dispose()
$entry = @{path=$originalPath;storedPath=($originalPath + '.gz');bytes=$payload.Length;
    sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload)).ToLowerInvariant();
    storedBytes=(Get-Item -LiteralPath $compressedPath).Length;
    storedSha256=(Get-FileHash -LiteralPath $compressedPath).Hash.ToLowerInvariant()}
$manifest = @{schemaVersion=1;files=@($entry)}
function Save-Manifest { $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8 }
function Invoke-Check([string]$Mode = 'Verify', [string]$Destination, [string]$Prefix) {
    & $tool -RepositoryRoot $fixture -Mode $Mode -DestinationRoot $Destination -PathPrefix $Prefix
}
$results = [Collections.Generic.List[object]]::new()
function Require($condition, $message) { if (!$condition) { throw $message } }
function Reject($name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Require $rejected ('Missing rejection: ' + $name)
    $results.Add(@{name=$name;passed=$true})
}
Save-Manifest
$result = Invoke-Check | ConvertFrom-Json
Require ($result.valid -and $result.files -eq 1) 'Verification failed.'
$results.Add(@{name='Compressed historical failure verifies without changing its verdict or bytes';passed=$true})
$destination = Join-Path $output 'restored'
Invoke-Check 'Restore' $destination $originalPath | Out-Null
Require ((Get-FileHash -LiteralPath (Join-Path $destination $originalPath)).Hash.ToLowerInvariant() -ceq $entry.sha256) 'Restoration changed original bytes.'
$results.Add(@{name='Selective external restoration preserves Unicode, CRLF and original SHA256';passed=$true})
Reject 'Existing destination cannot be overwritten' { Invoke-Check 'Restore' $destination }
Reject 'Repository cannot be a restoration destination' { Invoke-Check 'Restore' (Join-Path $fixture 'generated') }
Reject 'Repository ancestor cannot be a restoration destination' { Invoke-Check 'Restore' (Split-Path -Parent $fixture) }
Reject 'Empty selection is rejected' { Invoke-Check 'Restore' (Join-Path $output 'empty') 'validation/rechecks/no-such-input' }
Reject 'Selection traversal is rejected' { Invoke-Check 'Verify' '' '../escape' }
$entry.path = '../escape'; Save-Manifest
Reject 'Manifest traversal is rejected' { Invoke-Check }
$entry.path = $originalPath; $manifest.files = @($entry,$entry); Save-Manifest
Reject 'Repeated original paths are rejected' { Invoke-Check }
$manifest.files = @($entry); $entry.storedPath = 'validation/unrelated.gz'; Save-Manifest
Reject 'Unexpected storage mapping is rejected' { Invoke-Check }
$entry.storedPath = $originalPath + '.gz'; $entry.storedSha256 = '0' * 64; Save-Manifest
Reject 'Corrupt compressed hash is rejected before creating output' { Invoke-Check 'Restore' (Join-Path $output 'corrupt') }
Require (!(Test-Path -LiteralPath (Join-Path $output 'corrupt'))) 'Invalid source created a destination.'
$entry.storedSha256 = (Get-FileHash -LiteralPath $compressedPath).Hash.ToLowerInvariant(); $entry.sha256 = '0' * 64; Save-Manifest
Reject 'Container hash alone cannot certify different original bytes' { Invoke-Check }
$entry.sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload)).ToLowerInvariant()
$entry.bytes--; Save-Manifest
Reject 'Expansion cannot exceed declared original length' { Invoke-Check }
$entry.bytes += 2; Save-Manifest
Reject 'Truncated original cannot pass verification' { Invoke-Check }
$entry.bytes--; Save-Manifest
[IO.File]::WriteAllBytes((Join-Path $fixture $originalPath), $payload)
Reject 'Uncompressed duplicates cannot silently return' { Invoke-Check }
@{passed=$results.Count;failed=0;opticalTests=$false;cases=$results.ToArray()} | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
[pscustomobject]@{passed=$results.Count;failed=0} | ConvertTo-Json
