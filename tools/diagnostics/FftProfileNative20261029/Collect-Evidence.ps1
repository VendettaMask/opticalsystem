param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts = Join-Path $root 'artifacts/validation'
$destination = Join-Path $root 'validation/rechecks/2026-10-29/afocal-native-resume'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archivePath = Join-Path $destination 'evidence.zip'
if (Test-Path -LiteralPath $archivePath) { throw 'Evidence archive already exists; preserve the existing capture.' }
$assessmentPath = Join-Path $artifacts 'afocal-native-resume-20261029-assessment.json'
$assessment = Get-Content -LiteralPath $assessmentPath -Raw | ConvertFrom-Json
if ($assessment.status -ne 'native_capture_completed_precision_issue_open' -or $assessment.executions.Count -ne 20 -or $assessment.allProfilePointsPassed) {
    throw 'Unexpected assessment; do not seal an incomplete or falsely passing result.'
}
$cohorts = @('', '-02', '-paths', '-paths-02', '-seam-sweep', '-glue-controls', '-reference-controls')
$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($suffix in $cohorts) {
    $directory = Join-Path $artifacts ('afocal-native-resume-20261029' + $suffix)
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        if ($file.Extension.ToLowerInvariant() -in @('.json', '.txt', '.cfg', '.log')) { $files.Add($file) }
    }
}
foreach ($suffix in @('-compare-before', '-compare-normalized', '-compare-final')) {
    $directory = Join-Path $artifacts ('afocal-native-resume-20261029' + $suffix)
    foreach ($file in Get-ChildItem -LiteralPath $directory -File -Filter '*.json') { $files.Add($file) }
}
# Full baseline diagnostics once only: final diagnostics reproduce the same values.
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $artifacts 'afocal-native-resume-20261029-compare-final/diagnostics') -File -Filter '*.json') { $files.Add($file) }
foreach ($file in Get-ChildItem -LiteralPath $artifacts -File -Filter 'afocal-native-resume-20261029-*.log') { $files.Add($file) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs') { $files.Add($file) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'tools/diagnostics/FftProfileNative20261029') -File) { $files.Add($file) }
$files.Add((Get-Item -LiteralPath $assessmentPath))
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
$canonical = @{}
$entries = [Collections.Generic.List[object]]::new()
try {
    foreach ($file in $files | Sort-Object FullName -Unique) {
        $logicalPath = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $sha = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $canonical.ContainsKey($sha)) {
            $canonical[$sha] = $logicalPath
            $entry = $zip.CreateEntry($logicalPath, [IO.Compression.CompressionLevel]::Optimal)
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
        $entries.Add([pscustomobject]@{path=$logicalPath; member=$canonical[$sha]; length=$file.Length; sha256=$sha})
    }
    $entry = $zip.CreateEntry('entry-map.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
# Re-open and independently verify every logical entry before writing the seal.
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $entries) {
        $input = $zip.GetEntry($entry.member).Open()
        try { $sha = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($input)) } finally { $input.Dispose() }
        if ($sha -ne $entry.sha256 -or $zip.GetEntry($entry.member).Length -ne $entry.length) { throw "Archive verification failed: $($entry.path)" }
    }
} finally { $zip.Dispose() }
Copy-Item -LiteralPath $assessmentPath -Destination (Join-Path $destination 'assessment.json')
$seal = [ordered]@{
    date='2026-10-29'; status='native_capture_completed_precision_issue_open'; completedNativeCaptures=20
    productCoreSourceChanged=$false; originalReferenceSamplingToleranceChanged=$false
    originalFftProfiles=5; originalFftProfilesPointwisePassed=4; remainingPointwiseFailures=1; remainingViolations=335
    archiveSha256=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    archiveBytes=(Get-Item -LiteralPath $archivePath).Length; logicalEntries=$entries.Count; uniqueMembers=$canonical.Count
    allEntryHashesVerified=$true; proprietaryAssembliesIncluded=$false; foregroundDesktopUsed=$false
    coreAssemblySha256=$assessment.coreAssemblySha256; polygonSourceSha256=$assessment.polygonSourceSha256
}
$seal | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal | ConvertTo-Json -Depth 10
