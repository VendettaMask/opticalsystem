[CmdletBinding()]
param([string]$WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/release.yml'))

$ErrorActionPreference = 'Stop'
$actionLines = Select-String -LiteralPath $WorkflowPath -Pattern '^\s*(?:-\s*)?uses:\s*(\S+)'
if ($actionLines.Count -eq 0) { throw 'No release actions found.' }
foreach ($line in $actionLines) {
    $action = $line.Matches[0].Groups[1].Value
    if ($action -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_./-]+)?@[0-9a-f]{40}$') {
        throw "Release action must use a complete commit SHA: $action (line $($line.LineNumber))."
    }
}
Write-Output "Verified $($actionLines.Count) release action references with full commit SHAs."
