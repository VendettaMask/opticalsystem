$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root = 'validation/rechecks/2026-11-03/assembly-workspace'
    function Identity([string]$path) { [ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()} }
    $latest = @{}
    $runs = foreach ($relative in @('checked/workspace-checked.trx','final-typography/workspace-real-final.trx')) {
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
    $paths=@('src/OpticalSystemDesign.App/Laboratories/OpticalAssemblyWindow.cs','src/OpticalSystemDesign.App/Laboratories/OpticalAssemblyWindow.Presentation.cs','src/OpticalSystemDesign.App/Laboratories/AssemblyReticleView.cs','src/OpticalSystemDesign.App/Services/UiLocalization.cs')
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
    $mathChanges=@(git diff --name-only HEAD -- src/OpticalSystemDesign.Core/Analysis/Assembly src/OpticalSystemDesign.Application/Services/OpticalAssemblyService.cs src/OpticalSystemDesign.Application/Contracts/OpticalAssemblyContracts.cs)
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceState='uncommitted shared working tree';
        runs=$runs;distinctTests=$latest.Count;resolvedTests=@($latest.Values|Sort-Object name);allResolvedPassed=@($latest.Values|Where-Object outcome -ne 'Passed').Count -eq 0;
        testAdaptation='Two real-lens setup failures used a serializer that rejected the legitimate infinite object distance. Test-only named-floating-point recording was enabled; final two cases also verify actual 22-point reticle rendering. Product binaries unchanged. Seven passing layout cases are not rerun or added twice.';
        assemblies=$assemblies;productRuntimeUnchanged=@($assemblies|Where-Object {!$_.unchanged}).Count -eq 0;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0;
        compiledSources=$bindings;sourceBindingPassed=$bindings.Count -eq 8 -and @($bindings|Where-Object {!$_.matches}).Count -eq 0;
        sharedCalculationUnchanged=$mathChanges.Count -eq 0;calculationChanges=$mathChanges;
        sources=@($paths+@('tests/OpticalSystemDesign.Tests/AssemblyWorkspaceTests.cs')|ForEach-Object {Identity $_});
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png'|ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository,$_.FullName)) });
        scope='Background/headless UI only; actual source lens and service requests, no user desktop/clipboard input, installation, publication, hardware calibration, full-repository or native multi-screen acceptance.'
    }
    $receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allResolvedPassed=$receipt.allResolvedPassed;productRuntimeUnchanged=$receipt.productRuntimeUnchanged;normalAppIdentical=$receipt.normalAppIdentical;sourceBindingPassed=$receipt.sourceBindingPassed;sharedCalculationUnchanged=$receipt.sharedCalculationUnchanged}|ConvertTo-Json
    if($receipt.distinctTests -ne 9 -or !$receipt.allResolvedPassed -or !$receipt.productRuntimeUnchanged -or !$receipt.sourceBindingPassed -or !$receipt.sharedCalculationUnchanged) {exit 1}
} finally {Pop-Location}
