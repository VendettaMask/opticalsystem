param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'verify-acceptance-scope.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('acceptance-scope-' + [guid]::NewGuid().ToString('N')) }
$fixture = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $fixture) { throw 'Control fixture output must be a fresh directory.' }
[IO.Directory]::CreateDirectory($fixture) | Out-Null

function Write-Fixture([string]$Relative, [string]$Text) {
    $path = Join-Path $fixture $Relative
    [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
}
function Hash-Text([string]$Text) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant() }
function Save-Policy { Write-Fixture 'validation/acceptance-policy.json' ($script:policy | ConvertTo-Json -Depth 20) }
function Save-Index {
    $text = @{ Files = @($script:lenses) } | ConvertTo-Json -Depth 8
    Write-Fixture 'assets/index.json' $text
    $script:policy.currentCorpus.indexLfSha256 = Hash-Text $text
    Save-Policy
}
function Assert-Rejected([string]$Label, [string]$Message) {
    $rejected = $false
    try { Test-NumericalAcceptanceScope -Root $fixture | Out-Null } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw "${Label}: unexpected rejection: $_" }
        $rejected = $true
    }
    if (-not $rejected) { throw "${Label}: invalid policy was accepted." }
    $script:passed.Add($Label)
}

$lenses = @(1..95 | ForEach-Object {
    $name = "lens-$_.zmx"
    Write-Fixture "assets/$name" "synthetic control $_"
    @{ RelativePath = $name; Sha256 = Hash-Text "synthetic control $_" }
})
$policy = @{
    schemaVersion = 1
    currentCorpus = @{ index = 'assets/index.json'; count = 95; indexLfSha256 = '' }
    testFilter = 'AcceptanceScope!=RetiredReference'
    retiredCaptureSources = @()
    retiredTests = @(@{ file = 'tests/Control.cs'; method = 'OldReference'; reason = 'Synthetic retired input control.' })
}
$source = '[Trait("AcceptanceScope", "RetiredReference")]' + "`npublic void OldReference() {}"
Write-Fixture 'tests/Control.cs' $source
Write-Fixture '.github/workflows/ci.yml' @'
CURRENT_TEST_FILTER: 'AcceptanceScope!=RetiredReference'
--filter "${{ env.CURRENT_TEST_FILTER }}"
--filter "${{ env.CURRENT_TEST_FILTER }}"
'@
Save-Index
$passed = [Collections.Generic.List[string]]::new()
Test-NumericalAcceptanceScope -Root $fixture | Out-Null
$passed.Add('valid-95-lens-policy')
Write-Fixture 'assets/lens-1.zmx' 'tampered'
Assert-Rejected 'changed-lens-bytes' 'Frozen lens bytes changed'
Write-Fixture 'assets/lens-1.zmx' 'synthetic control 1'
Write-Fixture 'assets/index.json' '{}'
Assert-Rejected 'changed-index' 'frozen 95-lens index changed'
Save-Index
$last = $lenses[-1]; $lenses = @($lenses | Select-Object -First 94); Save-Index
Assert-Rejected '94-lenses' 'exactly 95'
$lenses += $last; Save-Index
$first = $lenses[0].RelativePath; $lenses[0].RelativePath = '../escape.zmx'; Save-Index
Assert-Rejected 'path-traversal' 'Invalid scoped path'
$lenses[0].RelativePath = $first; Save-Index
Write-Fixture 'tests/Control.cs' 'public void OldReference() {}'
Assert-Rejected 'missing-retirement-trait' 'Missing or ambiguous'
Write-Fixture 'tests/Control.cs' ($source + "`n" + $source.Replace('OldReference', 'Unregistered'))
Assert-Rejected 'unregistered-retirement' 'Unregistered retirement'
Write-Fixture 'tests/Control.cs' $source
$policy.retiredCaptureSources = @(@{ sha256 = $lenses[0].Sha256; captures = @() }); Save-Policy
Assert-Rejected 'current-lens-cannot-be-retired' 'current lens source cannot be retired'
$policy.retiredCaptureSources = @(); Save-Policy
$archivePath = Join-Path $fixture 'retired.zip'
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $archive.CreateEntry('capture/source.zmx')
    $stream = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $stream.Write('retired control') } finally { $stream.Dispose() }
} finally { $archive.Dispose() }
$policy.retiredCaptureSources = @(@{ sha256 = Hash-Text 'retired control'; captures = @(@{ archive = 'retired.zip'; entry = 'capture/source.zmx' }) }); Save-Policy
Test-NumericalAcceptanceScope -Root $fixture | Out-Null
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Update)
try {
    $archive.GetEntry('capture/source.zmx').Delete()
    $entry = $archive.CreateEntry('capture/source.zmx')
    $stream = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $stream.Write('tampered archived source') } finally { $stream.Dispose() }
} finally { $archive.Dispose() }
Assert-Rejected 'changed-retired-archive-source' 'Archived retirement source changed'
$policy.retiredCaptureSources = @(); Save-Policy
$workflow = [IO.File]::ReadAllText((Join-Path $fixture '.github/workflows/ci.yml'))
Write-Fixture '.github/workflows/ci.yml' ($workflow.Replace('--filter "${{ env.CURRENT_TEST_FILTER }}"', 'unfiltered run'))
Assert-Rejected 'unfiltered-ci-test-runs' 'Both main and comparison CI'
Write-Fixture '.github/workflows/ci.yml' $workflow
Test-NumericalAcceptanceScope -Root $fixture | Out-Null
$receipt = @{ valid = $true; controls = @($passed); count = $passed.Count; fixture = $fixture } | ConvertTo-Json -Depth 8
Write-Fixture 'result.json' ($receipt + "`n")
$receipt
