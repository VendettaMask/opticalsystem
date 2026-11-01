param([string]$Repository = 'D:\Projects\opticalsystem')
$ErrorActionPreference = 'Stop'
$repositoryPath = [IO.Path]::GetFullPath($Repository)
$root = Join-Path $repositoryPath 'validation/rechecks/2026-11-01/obscured-reference'
$root = [IO.Path]::GetFullPath($root)
if (-not $root.StartsWith($repositoryPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence root outside repository' }
function Hash-File([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Json([string]$name, $value) { [IO.File]::WriteAllText((Join-Path $root $name), ($value | ConvertTo-Json -Depth 35), [Text.UTF8Encoding]::new($false)) }
function Counts($rows) { $c = [ordered]@{}; foreach ($g in ($rows | Group-Object conclusion)) { $c[$g.Name] = $g.Count }; $c }
$before = Get-Content -LiteralPath (Join-Path $root 'categories-before/results.json') -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath (Join-Path $root 'categories-final/results.json') -Raw | ConvertFrom-Json
$rays = Get-Content -LiteralPath (Join-Path $root 'rays-final/results.json') -Raw | ConvertFrom-Json
$energy = Get-Content -LiteralPath (Join-Path $root 'energy-convergence-native/comparisons.json') -Raw | ConvertFrom-Json
$beforeMap = @{}; foreach ($row in $before) { $beforeMap[$row.index] = $row }
$lost = @($after | Where-Object { $beforeMap[$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass' })
$coreHash = (Get-Content -LiteralPath (Join-Path $root 'categories-final/identity.json') -Raw | ConvertFrom-Json).coreSha256
$runtimeCore = @('obscured-reference-runner-20261101-final','obscured-reference-tests-20261101-final','obscured-reference-tests-afocal-20261101') | ForEach-Object {
    $path = Join-Path $repositoryPath "artifacts/validation/$_/OpticalSystemDesign.Core.dll"
    if ((Hash-File $path) -ne $coreHash) { throw "Runtime Core mismatch: $path" }
    [pscustomobject]@{ path = $path; sha256 = $coreHash }
}
$currentDefaultCoreHash = Hash-File (Join-Path $repositoryPath 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll')
Write-Json 'summary.json' ([ordered]@{
    coreSha256 = $coreHash; selection = '16 menu lenses in Telescopes and Optical testing; 426 executable canonical requests; 22 non-executable ledger rows excluded'
    before = (Counts $before); after = (Counts $after)
    categories = @($after | Group-Object { $_.lens.Split('/')[0] } | ForEach-Object { [pscustomobject]@{category=$_.Name;total=$_.Count;counts=(Counts $_.Group)} })
    historicalRegressions = @($after | Where-Object historicalRegression | Select-Object index,lens,key,conclusion)
    oldPassRetained = 150 - $lost.Count; oldPassNotRetained = $lost
    fixedRayControls = (Counts $rays); fixedRaySurfaceComparisons = ($rays | Measure-Object compared -Sum).Sum
    tests = @(@{passed=101;failed=0;skipped=0;logicalPath='tests-final/obscured-reference-final.trx'},@{passed=66;failed=0;skipped=0;logicalPath='tests-afocal/obscured-reference-afocal.trx'})
    runtimeCore = $runtimeCore; defaultAppBuild = 'Succeeded; 0 warnings, 0 errors; both built-in modules included. Core was verified equal before subsequent concurrent builds replaced the shared output.'
    defaultOutputAtSealingSha256 = $currentDefaultCoreHash; defaultOutputReplacedSinceVerification = $currentDefaultCoreHash -ne $coreHash
    nativeConvergence = @($energy | Where-Object sampling -eq 512)
    limits = @('Original 32-sampling row 2278 remains Difference; the new 512-sampling comparison and 256-to-512 convergence are separate controls.','No full 95-example recertification or native foreground interaction, installation, commit or publication.')
})
Write-Json 'category-results.json' $after
Write-Json 'regression-results.json' @($after | Where-Object historicalRegression)
Write-Json 'ray-results.json' $rays
Write-Json 'energy-convergence.json' $energy

# Verify the reused primary arrays against the one existing authoritative archive.
$authorityPath = Join-Path $repositoryPath 'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
$authority = [IO.Compression.ZipFile]::OpenRead($authorityPath)
$references = [Collections.Generic.List[object]]::new()
try {
    foreach ($row in $after) {
        $path = Join-Path $row.nativeReference 'data.json'
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $relative = [IO.Path]::GetRelativePath((Join-Path $repositoryPath 'artifacts/zemax-sequential-corpus/20261021/full-v1'), $path).Replace('\','/')
        $entry = $authority.GetEntry('run/' + $relative)
        if ($null -eq $entry) { throw "Missing primary reference: $relative" }
        $stream = $entry.Open(); try { $sha = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ((Hash-File $path) -ne $sha -or $sha -ne $row.nativeDataSha256) { throw "Primary reference changed: $relative" }
        $references.Add([pscustomobject]@{index=$row.index;entry=$entry.FullName;sha256=$sha})
    }
} finally { $authority.Dispose() }
$index = Get-Content -LiteralPath (Join-Path $repositoryPath 'src/OpticalSystemDesign.App/Assets/SequentialExamples/index.json') -Raw | ConvertFrom-Json
foreach ($inputRecord in (Get-Content -LiteralPath (Join-Path $root 'categories-final/identity.json') -Raw | ConvertFrom-Json).inputs) {
    $expected = @($index.Files | Where-Object RelativePath -eq $inputRecord.lens)[0].Sha256
    if ($expected -ne $inputRecord.sha256) { throw "Menu source identity changed: $($inputRecord.lens)" }
}
Write-Json 'reference-verification.json' @{authority=$authorityPath;authoritySha256=(Hash-File $authorityPath);verifiedNativeArrays=$references.Count;references=$references;originalPolicySha256=(Hash-File (Join-Path $repositoryPath 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'))}

# Each byte-identical payload is stored once, with logical paths in the index.
$stages = @('before','categories-before','categories-first-after','categories-v2','categories-final','first-after','obscuration-after','spot-reference-after','rays-final','energy-convergence','energy-convergence-native','tests','tests-final','tests-afocal')
$files = [Collections.Generic.List[object]]::new()
foreach ($stage in $stages) {
    $directory = [IO.Path]::GetFullPath((Join-Path $root $stage))
    if ([IO.Path]::GetDirectoryName($directory) -ne $root) { throw 'Stage must be a direct evidence child' }
    if (Test-Path -LiteralPath $directory) { foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse) {
        $files.Add([pscustomobject]@{path=$file.FullName;logicalPath=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')})
    } }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryPath 'artifacts/validation') -File -Filter 'obscured-reference-*.log' | Where-Object {$_.Name -ne 'obscured-reference-seal-v2-20261101.log'}) {
    $files.Add([pscustomobject]@{path=$file.FullName;logicalPath='logs/'+$file.Name})
}
foreach ($runtime in @('obscured-reference-runner-20261101-before','obscured-reference-runner-20261101-final','obscured-reference-tests-20261101-final','obscured-reference-tests-afocal-20261101')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryPath "artifacts/validation/$runtime") -File | Where-Object {$_.Name -match '^(OpticalSystemDesign\.(Core|Application|ZemaxComparison|Tests)|ObscuredReference20261101)\.'}) {
        $files.Add([pscustomobject]@{path=$file.FullName;logicalPath="runtime/$runtime/"+$file.Name})
    }
}
$changed = @(git -C $repositoryPath diff --name-only -- src/OpticalSystemDesign.Core) + @(git -C $repositoryPath ls-files --others --exclude-standard 'tests/OpticalSystemDesign.Tests/ObscuredReferenceTests.cs' 'tools/diagnostics/ObscuredReference20261101' 'tools/diagnostics/ObscuredReferenceTests20261101')
foreach ($path in $changed | Sort-Object -Unique | Where-Object {$_ -match '\.(cs|csproj|ps1)$'}) {
    $files.Add([pscustomobject]@{path=(Join-Path $repositoryPath $path);logicalPath='source/'+$path.Replace('\','/')})
}
$archivePath = Join-Path $root 'evidence.zip'
if (Test-Path -LiteralPath $archivePath) { throw 'Archive already exists' }
$payloads = @{}; $mapping = [Collections.Generic.List[object]]::new()
$archive = [IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $hash = Hash-File $file.path
        $length = (Get-Item -LiteralPath $file.path).Length
        if (-not $payloads.ContainsKey($hash)) {
            $entry = $archive.CreateEntry('payloads/'+$hash,[IO.Compression.CompressionLevel]::Optimal)
            $inputStream=[IO.File]::OpenRead($file.path); $outputStream=$entry.Open()
            try {$inputStream.CopyTo($outputStream)} finally {$inputStream.Dispose();$outputStream.Dispose()}
            $payloads[$hash]=$length
        }
        $mapping.Add([pscustomobject]@{logicalPath=$file.logicalPath;sha256=$hash;length=$length})
    }
    $entry=$archive.CreateEntry('index.json');$stream=$entry.Open()
    try { $bytes=[Text.Encoding]::UTF8.GetBytes(($mapping|ConvertTo-Json -Depth 5));$stream.Write($bytes) } finally {$stream.Dispose()}
} finally {$archive.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries | Where-Object FullName -like 'payloads/*') {
        $hash=$entry.Name; $stream=$entry.Open()
        try {$actual=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
        if ($actual -ne $hash -or $entry.Length -ne $payloads[$hash]) {throw "Archive verification failed: $hash"}
    }
} finally {$archive.Dispose()}
Write-Json 'archive-index.json' $mapping
Write-Json 'archive-verification.json' @{sha256=(Hash-File $archivePath);logicalFiles=$mapping.Count;uniquePayloads=$payloads.Count;archiveBytes=(Get-Item -LiteralPath $archivePath).Length;allPayloadHashesVerified=$true;completedUtc=[DateTimeOffset]::UtcNow}
# Only verified copies in this task's explicit evidence directories are retired.
foreach ($stage in $stages) {
    $directory=[IO.Path]::GetFullPath((Join-Path $root $stage))
    if ([IO.Path]::GetDirectoryName($directory) -ne $root -or -not $directory.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe retirement path'}
    if (Test-Path -LiteralPath $directory) {Remove-Item -LiteralPath $directory -Recurse -Force}
}
Get-Content -LiteralPath (Join-Path $root 'archive-verification.json')
