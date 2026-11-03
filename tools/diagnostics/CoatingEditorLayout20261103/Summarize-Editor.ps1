$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $evidence = 'validation/rechecks/2026-11-03/coating-editor-layout'
    function Identity([string]$p) { [ordered]@{ path = $p.Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() } }
    function Documents([string]$pdb) {
        $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $pdb).Path)
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream,[System.Reflection.Metadata.MetadataStreamOptions]::Default,0)
            try {
                $reader = $provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default,$null)
                foreach ($handle in $reader.Documents) {
                    $document = $reader.GetDocument($handle)
                    if ($reader.GetGuid($document.HashAlgorithm) -ne [guid]'8829d00f-11b8-4213-878b-770e8597ac16') { throw 'Expected SHA-256 PDB document checksum' }
                    [pscustomobject]@{ path=$reader.GetString($document.Name).Replace('\','/'); checksum=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant() }
                }
            } finally { $provider.Dispose() }
        } finally { $stream.Dispose() }
    }
    $latest = @{}
    $runs = foreach ($relative in @('final/editor-final.trx')) {
        $p = Join-Path $evidence $relative
        $xml = [xml](Get-Content -Raw -LiteralPath $p)
        $cases = @($xml.SelectNodes("//*[local-name()='UnitTestResult']") | ForEach-Object { [ordered]@{name=$_.testName;outcome=$_.outcome;duration=$_.duration} })
        foreach ($case in $cases) { $latest[$case.name] = [ordered]@{name=$case.name;outcome=$case.outcome;run=$relative} }
        [ordered]@{path=$relative;sha256=(Identity $p).sha256;total=$cases.Count;passed=@($cases|Where-Object outcome -eq 'Passed').Count;cases=$cases}
    }
    $testDir = 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0'
    $before = Get-Content -Raw -LiteralPath (Join-Path $evidence 'runtime-before-final.json') | ConvertFrom-Json
    $assemblies = foreach ($name in @('OpticalSystemDesign.App.dll','OpticalSystemDesign.Core.dll','OpticalSystemDesign.CoatingDesign.Engine.dll')) {
        $current = Identity (Join-Path $testDir $name)
        $app = Identity (Join-Path 'src/OpticalSystemDesign.App/bin/Release/net10.0' $name)
        $prior = $before | Where-Object Name -eq $name
        [ordered]@{name=$name;test=$current;app=$app;before=$prior.Sha256.ToLowerInvariant();unchanged=$current.sha256 -eq $prior.Sha256.ToLowerInvariant();identical=$current.sha256 -eq $app.sha256}
    }
    $opticalChanges = @(git diff --name-only HEAD -- src/OpticalSystemDesign.Core/Coatings labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine)
    $testDocuments = @(Documents (Join-Path $testDir 'OpticalSystemDesign.App.pdb'))
    $appDocuments = @(Documents 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')
    $coatingDocuments = @($testDocuments | Where-Object { $_.path -match '/Laboratories/CoatingDesign/.*\.cs$' } | ForEach-Object {
        $appDocument = $appDocuments | Where-Object path -eq $_.path
        $disk = (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash.ToLowerInvariant()
        [ordered]@{path=[IO.Path]::GetRelativePath($repository,$_.path).Replace('\','/');tested=$_.checksum;normalApp=$appDocument.checksum;currentSource=$disk;matches=$_.checksum -eq $appDocument.checksum -and $_.checksum -eq $disk}
    })
    $appDifferences = @($testDocuments | Where-Object { $document=$_; $appDocument=$appDocuments|Where-Object path -eq $document.path; !$appDocument -or $appDocument.checksum -ne $document.checksum } | ForEach-Object { [IO.Path]::GetRelativePath($repository,$_.path).Replace('\','/') })
    $reference = Identity 'third_party/coating-reference/TFStudio-8b942b3.tar.gz'
    $sources = @('src/OpticalSystemDesign.App/Laboratories/CoatingDesign','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests') | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -File -Filter '*.cs' | ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository,$_.FullName)) }
    }
    $receipt = [ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceState='uncommitted UI changes';
        runs=$runs;distinctTests=$latest.Count;resolvedTests=@($latest.Values|Sort-Object name);allResolvedPassed=@($latest.Values|Where-Object outcome -ne 'Passed').Count -eq 0;
        history='Earlier reference-final run: 81 passed / 6 legacy UI expectations failed. Six adapted checks then passed on unchanged runtime. Final product adds menu-based multi-selection moves and runs the same 87-case scope afresh; only final/editor-final.trx determines the current count.';
        assemblies=$assemblies;productRuntimeUnchanged=@($assemblies|Where-Object {!$_.unchanged}).Count -eq 0;
        normalAppByteIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0;
        coatingCompiledSources=$coatingDocuments;coatingSourceSameInBothOutputs=$coatingDocuments.Count -gt 0 -and @($coatingDocuments|Where-Object {!$_.matches}).Count -eq 0;
        otherAppCompiledSourceDifferences=$appDifferences;
        sharedCoatingMathAndEngineUnchanged=$opticalChanges.Count -eq 0;unexpectedOpticalChanges=$opticalChanges;
        reference=$reference;sources=$sources;previews=@(Get-ChildItem -LiteralPath $evidence -File -Filter 'reference-*.png'|ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository,$_.FullName)) });
        currentTestAssembly=Identity (Join-Path $testDir 'OpticalSystemDesign.CoatingDesign.Tests.dll');
        documentationAudit=if (Test-Path -LiteralPath (Join-Path $evidence 'documentation-summary.json')) { Get-Content -LiteralPath (Join-Path $evidence 'documentation-summary.json') -Raw | ConvertFrom-Json } else { $null };
        scope='Background and headless only. No user desktop/clipboard control, app restart, release, full repository suite or remote synchronization.'
    }
    $receipt | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allResolvedPassed=$receipt.allResolvedPassed;productRuntimeUnchanged=$receipt.productRuntimeUnchanged;normalAppByteIdentical=$receipt.normalAppByteIdentical;coatingSourceSameInBothOutputs=$receipt.coatingSourceSameInBothOutputs;coatingMathUnchanged=$receipt.sharedCoatingMathAndEngineUnchanged}|ConvertTo-Json
    if ($receipt.distinctTests -ne 87 -or !$receipt.allResolvedPassed -or !$receipt.productRuntimeUnchanged -or !$receipt.coatingSourceSameInBothOutputs -or !$receipt.sharedCoatingMathAndEngineUnchanged -or $reference.sha256 -ne '055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6') { exit 1 }
}
finally { Pop-Location }
