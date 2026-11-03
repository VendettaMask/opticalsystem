$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root = 'validation/rechecks/2026-11-03/camera-workspace'
    function Identity([string]$path) { [ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()} }
    $latest = @{}
    $runs = foreach ($relative in @('checked/camera-checked.trx','language/camera-language.trx')) {
        $file = Join-Path $root $relative; $xml=[xml](Get-Content -LiteralPath $file -Raw)
        $cases=@($xml.SelectNodes("//*[local-name()='UnitTestResult']") | ForEach-Object { [ordered]@{name=$_.testName;outcome=$_.outcome;duration=$_.duration} })
        foreach($case in $cases) { $latest[$case.name]=[ordered]@{name=$case.name;outcome=$case.outcome;run=$relative} }
        [ordered]@{path=$relative;sha256=(Identity $file).sha256;cases=$cases}
    }
    $testBin='tests/OpticalSystemDesign.Tests/bin/Release/net10.0'
    $before=Get-Content -LiteralPath (Join-Path $root 'product-before-test-adaptation.json') -Raw|ConvertFrom-Json
    $assemblies=foreach($item in $before) {
        $test=Identity (Join-Path $testBin $item.name);$app=Identity (Join-Path 'src/OpticalSystemDesign.App/bin/Release/net10.0' $item.name)
        [ordered]@{name=$item.name;test=$test;normalApp=$app;before=$item.sha256;unchanged=$test.sha256 -eq $item.sha256;identical=$test.sha256 -eq $app.sha256}
    }
    $paths=@('src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.cs','src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Presentation.cs','src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Actions.cs','src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Experiments.cs','src/OpticalSystemDesign.App/Services/UiLocalization.cs')
    $bindings=foreach($pdb in @((Join-Path $testBin 'OpticalSystemDesign.App.pdb'),'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')) {
        $stream=[IO.File]::OpenRead((Resolve-Path -LiteralPath $pdb).Path)
        try {
            $provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream,[System.Reflection.Metadata.MetadataStreamOptions]::Default,0)
            try {
                $reader=$provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default,$null)
                foreach($handle in $reader.Documents) {
                    $document=$reader.GetDocument($handle);$absolute=$reader.GetString($document.Name);$relative=[IO.Path]::GetRelativePath($repository,$absolute).Replace('\','/')
                    if($relative -notin $paths) { continue }
                    $compiled=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant();$source=(Identity $absolute).sha256
                    [ordered]@{pdb=$pdb;path=$relative;compiled=$compiled;source=$source;matches=$compiled -eq $source}
                }
            } finally { $provider.Dispose() }
        } finally { $stream.Dispose() }
    }
    $mathChanges=@(git diff --name-only HEAD -- src/OpticalSystemDesign.Core/Analysis/Imaging 'src/OpticalSystemDesign.Application/Services/CameraImaging*' 'src/OpticalSystemDesign.Application/Contracts/Camera*')
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceState='uncommitted shared working tree';
        runs=$runs;distinctTests=$latest.Count;resolvedTests=@($latest.Values|Sort-Object name);allResolvedPassed=@($latest.Values|Where-Object outcome -ne 'Passed').Count -eq 0;
        testAdaptation='The 27-case checked run passed 26 cases. The file-popover language test read labels before control Loaded events had run; it now opens the real sections/popover and pumps the dispatcher before changing language. This one case is rerun on unchanged App/Core/Application binaries; earlier failures remain visible.';
        assemblies=$assemblies;productRuntimeUnchanged=@($assemblies|Where-Object {!$_.unchanged}).Count -eq 0;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0;
        compiledSources=$bindings;sourceBindingPassed=$bindings.Count -eq 10 -and @($bindings|Where-Object {!$_.matches}).Count -eq 0;
        sharedCalculationUnchanged=$mathChanges.Count -eq 0;calculationChanges=$mathChanges;
        sources=@($paths+@('tests/OpticalSystemDesign.Tests/CameraWorkspaceTests.cs','tests/OpticalSystemDesign.Tests/CameraImagingWindowTests.cs','tests/OpticalSystemDesign.Tests/CameraReviewRegressionTests.cs')|ForEach-Object {Identity $_});
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png'|ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository,$_.FullName)) });
        scope='Background/headless UI only; actual source lens and service requests, no user desktop/clipboard input, installation, publication, hardware calibration, full-repository or native multi-screen acceptance.'
    }
    $receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allResolvedPassed=$receipt.allResolvedPassed;productRuntimeUnchanged=$receipt.productRuntimeUnchanged;normalAppIdentical=$receipt.normalAppIdentical;sourceBindingPassed=$receipt.sourceBindingPassed;sharedCalculationUnchanged=$receipt.sharedCalculationUnchanged}|ConvertTo-Json
    if($receipt.distinctTests -ne 27 -or !$receipt.allResolvedPassed -or !$receipt.productRuntimeUnchanged -or !$receipt.sourceBindingPassed -or !$receipt.sharedCalculationUnchanged) {exit 1}
} finally {Pop-Location}
