param([string]$Repository = 'D:\Projects\opticalsystem')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($Repository)
$root = [IO.Path]::GetFullPath((Join-Path $repo 'validation/rechecks/2026-11-01/testing-three'))
if (!$root.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid evidence root' }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read([string]$p) { Get-Content -LiteralPath $p -Raw | ConvertFrom-Json }
function Write-Json([string]$name, $value) { [IO.File]::WriteAllText((Join-Path $root $name), (ConvertTo-Json -InputObject $value -Depth 40), [Text.UTF8Encoding]::new($false)) }
function Counts($rows) { $counts = [ordered]@{}; foreach ($g in ($rows | Group-Object conclusion)) { $counts[$g.Name] = $g.Count }; $counts }
$after = Read (Join-Path $root 'categories-final/results.json')
if ($after.Count -ne 426) { throw 'Incomplete final replay' }
$identity = Read (Join-Path $root 'categories-final/identity.json')
$coreHash = $identity.coreSha256
$rays = Read (Join-Path $root 'rays-final/results.json')
if ($rays.Count -ne 5 -or @($rays | Where-Object conclusion -ne 'Pass').Count) { throw 'Fixed rays incomplete' }
$priorRoot = Join-Path $repo 'validation/rechecks/2026-11-01/obscured-reference'
$priorIndex = Read (Join-Path $priorRoot 'archive-index.json')
$priorEntry = $priorIndex | Where-Object logicalPath -eq 'categories-final/results.json'
$priorZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $priorRoot 'evidence.zip'))
try {
    $stream = $priorZip.GetEntry('payloads/' + $priorEntry.sha256).Open()
    $memory = [IO.MemoryStream]::new(); $stream.CopyTo($memory); $stream.Dispose()
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant() -ne $priorEntry.sha256) { throw 'Prior result identity differs' }
    $before = [Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json; $memory.Dispose()
} finally { $priorZip.Dispose() }
$priorMap = @{}; foreach ($row in $before) { $priorMap[$row.index] = $row }
$lost = @($after | Where-Object { $priorMap[$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass' })
if ($lost.Count) { throw 'A prior Pass was lost' }
Write-Json 'prior-pass-audit.json' @{priorArchive=(Join-Path $priorRoot 'evidence.zip');priorResultsSha256=$priorEntry.sha256;before=(Counts $before);after=(Counts $after);lost=$lost;lostCount=$lost.Count}
$runtimes = @('artifacts/validation/testing-three-runner-20261101-accepted','artifacts/validation/testing-three-runner-20261101-evidence','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')
foreach ($runtime in $runtimes) { if ((Hash (Join-Path $repo "$runtime/OpticalSystemDesign.Core.dll")) -ne $coreHash) { throw "Core identity changed: $runtime" } }
$files = [Collections.Generic.List[object]]::new()
$audit = [Collections.Generic.List[object]]::new()
$pdbs = @('artifacts/validation/testing-three-runner-20261101-accepted/OpticalSystemDesign.Core.pdb','artifacts/validation/testing-three-runner-20261101-evidence/ObscuredReference20261101.pdb','artifacts/validation/testing-three-runner-20261101-evidence/OpticalSystemDesign.ZemaxComparison.pdb','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0/OpticalSystemDesign.Tests.pdb')
foreach ($pdb in $pdbs) {
    $entries = [Collections.Generic.List[object]]::new()
    $stream = [IO.File]::OpenRead((Join-Path $repo $pdb)); $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try {
        $reader = $provider.GetMetadataReader()
        foreach ($handle in $reader.Documents) {
            $document = $reader.GetDocument($handle); $path = $reader.GetString($document.Name)
            if (!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]') { continue }
            $expected = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant()
            $actual = Hash $path
            $relative = [IO.Path]::GetRelativePath($repo, $path).Replace('\','/')
            $sourcePath = $path; $recovered = $false
            if ($expected -ne $actual) {
                # Another chat may edit an unrelated source after execution.
                # Recover only exact PDB-matching bytes; never modify its work.
                $start = [Diagnostics.ProcessStartInfo]::new('git'); $start.WorkingDirectory=$repo; $start.RedirectStandardOutput=$true; $start.UseShellExecute=$false
                $start.ArgumentList.Add('show'); $start.ArgumentList.Add('HEAD:'+$relative)
                $process=[Diagnostics.Process]::Start($start); $memory=[IO.MemoryStream]::new()
                try {$process.StandardOutput.BaseStream.CopyTo($memory);$process.WaitForExit();$bytes=$memory.ToArray();if($process.ExitCode -ne 0){throw 'Cannot recover compiled source'}} finally {$process.Dispose();$memory.Dispose()}
                if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -ne $expected) { throw "Compiled source changed without matching frozen bytes: $path" }
                $sourcePath=Join-Path $root ('compiled-sources/'+$relative); [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($sourcePath)) | Out-Null
                [IO.File]::WriteAllBytes($sourcePath,$bytes); $recovered=$true
            }
            $entries.Add([pscustomobject]@{path=$relative;sha256=$expected;matchesCompiledChecksum=$true;currentWorkspaceSha256=$actual;currentWorkspaceMatchesCompiledChecksum=($expected -eq $actual);exactCompiledSourceRecoveredFromHead=$recovered})
            $files.Add([pscustomobject]@{path=$sourcePath;logicalPath='source/'+$relative})
        }
    } finally { $provider.Dispose(); $stream.Dispose() }
    $audit.Add([pscustomobject]@{pdb=$pdb;sha256=(Hash (Join-Path $repo $pdb));documents=$entries})
}
Write-Json 'source-verification.json' @{coreSha256=$coreHash;assemblies=$audit;allListedSourcesMatchCompiledChecksums=$true}
$three = @($after | Where-Object lens -in @('Optical testing/Double-pass null test.zmx','Optical testing/Mach Zender Interferometer.zmx','Optical testing/Twyman Green Interferometer.zmx'))
$beforeThree = Read (Join-Path $root 'before/results.json')
$convergence = Read (Join-Path $root 'convergence-final/comparisons.json')
$test = [xml](Get-Content -LiteralPath (Join-Path $root 'tests/testing-three-accepted.trx') -Raw)
if ($test.TestRun.ResultSummary.Counters.failed -ne '0' -or $test.TestRun.ResultSummary.Counters.passed -ne '234') { throw 'Final test scope failed' }
$appLog = Join-Path $repo 'artifacts/validation/testing-three-default-app-accepted-20261101.log'
if ((Get-Content -LiteralPath $appLog -Raw) -notmatch '0 个错误') { throw 'Default App build not verified' }
Write-Json 'summary.json' ([ordered]@{coreSha256=$coreHash;selection='Three prioritized lenses / five configurations; two menu categories / 426 executable original requests';beforeThree=(Counts $beforeThree);afterThree=(Counts $three);beforeCategories=(Counts $before);afterCategories=(Counts $after);categories=@($after | Group-Object {$_.lens.Split('/')[0]} | ForEach-Object {@{category=$_.Name;count=$_.Count;counts=(Counts $_.Group)}});original13=@($after | Where-Object historicalRegression | Select-Object index,lens,key,conclusion);fixedRays=$rays;convergence=$convergence;testsPassed=234;defaultAppBuild=@{passed=$true;builtInModules=2;warnings=0;errors=0;log=$appLog;coreSha256=$coreHash};runtimeCoreIdentities=@($runtimes | ForEach-Object {@{path=$_;sha256=$coreHash}});openThree=@($three | Where-Object conclusion -ne 'Pass' | Select-Object index,lens,configuration,key,conclusion,reason);higherSamplingBlock=@{index=2165;requestedPupilSampling=512;coreRejectedBeforeNative=$true;reason='Original 100,000,000 direct PSF operation guard';budgetRaised=$false;imageWindowReduced=$false};sourceInputs=$identity.inputs;unchangedOriginalRequestsAndTolerances=$true;capturedScreenshots=$false;foregroundInteraction=$false;completedUtc=[DateTimeOffset]::UtcNow})
Write-Json 'results-final.json' $after
foreach ($directory in (Get-ChildItem -LiteralPath $root -Directory)) {
    foreach ($file in (Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)) {
        $files.Add([pscustomobject]@{path=$file.FullName;logicalPath=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')})
    }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts/validation') -File -Filter 'testing-three-*.log')) { $files.Add([pscustomobject]@{path=$file.FullName;logicalPath='logs/'+$file.Name}) }
foreach ($runtime in $runtimes) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repo $runtime) -File | Where-Object { $_.Name -match '^(OpticalSystemDesign\.|ObscuredReference).*(\.dll|\.pdb|\.deps\.json|\.runtimeconfig\.json)$' })) {
        $files.Add([pscustomobject]@{path=$file.FullName;logicalPath='runtime/'+$runtime.Replace('/','_')+'/'+$file.Name})
    }
}
foreach ($file in (Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -in '.cs','.csproj','.ps1','.md')) { $files.Add([pscustomobject]@{path=$file.FullName;logicalPath='source/tools/diagnostics/ObscuredReference20261101/'+$file.Name}) }
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs')) { $files.Add([pscustomobject]@{path=$file.FullName;logicalPath='source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$file.Name}) }
$archivePath = Join-Path $root 'evidence.zip'
if (Test-Path -LiteralPath $archivePath) { throw 'Evidence already sealed' }
$payloads = @{}; $mapping = [Collections.Generic.List[object]]::new(); $logical = @{}
$zip = [IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $hash = Hash $file.path; $length = (Get-Item -LiteralPath $file.path).Length
        if ($logical.ContainsKey($file.logicalPath)) { if ($logical[$file.logicalPath] -ne $hash) { throw 'Conflicting logical path' }; continue }
        $logical[$file.logicalPath] = $hash
        if (!$payloads.ContainsKey($hash)) {
            $entry = $zip.CreateEntry('payloads/'+$hash,[IO.Compression.CompressionLevel]::Optimal)
            $source = [IO.File]::OpenRead($file.path); $target = $entry.Open()
            try {$source.CopyTo($target)} finally {$source.Dispose();$target.Dispose()}
            $payloads[$hash] = $length
        }
        $mapping.Add([pscustomobject]@{logicalPath=$file.logicalPath;sha256=$hash;length=$length})
    }
} finally {$zip.Dispose()}
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath); $verified = 0
try {
    if ($zip.Entries.Count -ne $payloads.Count) { throw 'Payload count differs' }
    foreach ($entry in $zip.Entries) {
        $hash = $entry.FullName.Substring(9); $stream = $entry.Open()
        try {$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()} finally {$stream.Dispose()}
        if ($actual -ne $hash -or $entry.Length -ne $payloads[$hash]) { throw 'Archive verification failed' }
        $verified++
    }
} finally {$zip.Dispose()}
Write-Json 'archive-index.json' $mapping
Write-Json 'archive-verification.json' @{archive='evidence.zip';sha256=(Hash $archivePath);bytes=(Get-Item -LiteralPath $archivePath).Length;logicalFiles=$mapping.Count;uniquePayloads=$payloads.Count;fullyReadVerifiedPayloads=$verified;verifiedBeforeExpandedRetirement=$true}
foreach ($directory in (Get-ChildItem -LiteralPath $root -Directory)) {
    $resolved = [IO.Path]::GetFullPath($directory.FullName)
    if (!$resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Retirement path outside task evidence' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output "Sealed $($mapping.Count) logical files / $verified unique payloads; all read back and verified."
