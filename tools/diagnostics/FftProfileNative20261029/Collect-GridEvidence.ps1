param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts = Join-Path $root 'artifacts/validation'
$destination = Join-Path $root 'validation/rechecks/2026-10-29/fft-profile-grid-investigation'
$assessmentPath = Join-Path $artifacts 'fft-profile-grid-20261029-assessment.json'
$assessment = Get-Content -LiteralPath $assessmentPath -Raw | ConvertFrom-Json
if ($assessment.objectiveAchieved -or $assessment.remainingViolations -ne 335 -or $assessment.validNativeCaptures -ne 10 -or $assessment.rejectedExperiments.Count -ne 11) {
    throw 'Unexpected assessment: this stage did not close the precision issue.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archivePath = Join-Path $destination 'evidence.zip'
if (Test-Path -LiteralPath $archivePath) { throw 'Preserve the existing evidence archive.' }
$extensions = @('.json','.txt','.log','.cfg','.pob','.zmx','.cs','.patch','.ps1','.md','.csproj','.trx')
$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($pattern in @('fft-profile-grid-20261029*','fft-profile-gaussian-20261029','fft-profile-triangle-control-20261029*','fft-profile-normal-control-20261029*')) {
    foreach ($directory in Get-ChildItem -Path (Join-Path $artifacts $pattern) -Directory) {
        foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -Recurse -File) {
            if ($file.Extension.ToLowerInvariant() -in $extensions) { $files.Add($file) }
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $artifacts -File -Filter 'fft-profile-*-20261029*.log') { $files.Add($file) }
foreach ($file in Get-ChildItem -LiteralPath $artifacts -File -Filter 'fft-profile-grid-20261029-assessment*.json') { $files.Add($file) }
foreach ($control in $assessment.triangleControls) { $files.Add((Get-Item -LiteralPath (Join-Path $root $control.originalPath))) }
# The original frozen archive remains a separate, SHA-identified prerequisite.
# Retain the precise native 33-probe/operand readouts used by the scale diagnostic.
$previous = Join-Path $artifacts 'afocal-native-resume-20261029-02'
foreach ($name in @('binocular-c1-boundary-and-seam33','binocular-c2-boundary-and-seam33',
    '0065-003-binocular_system-c1-primary-f1-w2-encircled-energy-pupil','0095-003-binocular_system-c2-primary-f1-w2-encircled-energy-pupil')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $previous $name) -File) {
        if ($file.Extension.ToLowerInvariant() -in $extensions) { $files.Add($file) }
    }
    $request = Join-Path $previous ($name + '.request.json')
    if (Test-Path -LiteralPath $request) { $files.Add((Get-Item -LiteralPath $request)) }
}
foreach ($folder in @('tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost','tools/diagnostics/FftProfileNative20261029')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File) {
        if ($file.Extension.ToLowerInvariant() -in $extensions) { $files.Add($file) }
    }
}
$files.Add((Get-Item -LiteralPath (Join-Path $root 'docs/AFOCAL_FFT_GRID_INVESTIGATION_2026-10-29.md')))
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$canonical = @{}
$entries = [Collections.Generic.List[object]]::new()
try {
    foreach ($file in $files | Sort-Object FullName -Unique) {
        $logical = [IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
        if ($logical.StartsWith('../')) { throw 'Evidence file escaped repository.' }
        $sha = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $canonical.ContainsKey($sha)) {
            $canonical[$sha]=$logical
            $entry=$zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal)
            $input=[IO.File]::OpenRead($file.FullName); $output=$entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
        $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$file.Length;sha256=$sha})
    }
    $entry=$zip.CreateEntry('entry-map.json')
    $writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
    try { $writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($logical in $entries) {
        $member=$zip.GetEntry($logical.member); $input=$member.Open()
        try { $sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($input)) } finally { $input.Dispose() }
        if ($sha -ne $logical.sha256 -or $member.Length -ne $logical.length) { throw "Archive byte mismatch: $($logical.path)" }
    }
} finally { $zip.Dispose() }
Copy-Item -LiteralPath $assessmentPath -Destination (Join-Path $destination 'assessment.json')
$seal=[ordered]@{
    date='2026-10-29';status=$assessment.status;objectiveAchieved=$false
    validNativeCaptures=10;invalidNativeCaptures=6;failedNativeCaptures=1;rejectedCoreExperiments=11
    originalReferenceSamplingToleranceChanged=$false;foregroundDesktopUsed=$false
    productCoreChangesAccepted=0;sourceCommit=$assessment.sourceCommit;coreSourceMatchesCommit=$true
    remainingViolations=335;maximumIntensityDifference=0.0011946610381988604
    restoredProfilesExactlyEqualPreviousBaseline=$true;focusedGuardTests=$assessment.focusedGuardTests
    archiveBytes=(Get-Item -LiteralPath $archivePath).Length
    archiveSha256=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true
    proprietaryAssembliesIncluded=$false;originalFrozenArchiveSha256=$assessment.originalArchiveSha256
    coreAssemblySha256=$assessment.coreAssemblySha256
}
$seal | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal | ConvertTo-Json -Depth 10
