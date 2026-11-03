$ErrorActionPreference='Stop'
$repository=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root='validation/rechecks/2026-11-03/scientific-colormaps'
    function Identity([string]$path) {
        [ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $trx=Join-Path $root 'verified/colormaps-verified.trx'
    $xml=[xml](Get-Content -LiteralPath $trx -Raw)
    $cases=@($xml.SelectNodes("//*[local-name()='UnitTestResult']") | ForEach-Object {
        [ordered]@{name=$_.testName;outcome=$_.outcome;duration=$_.duration}
    })
    $testBin='tests/OpticalSystemDesign.Tests/bin/Release/net10.0'
    $assemblies=foreach($name in @('OpticalSystemDesign.App','OpticalSystemDesign.Core','OpticalSystemDesign.Application')) {
        $test=Identity (Join-Path $testBin ($name+'.dll'))
        $app=Identity (Join-Path 'src/OpticalSystemDesign.App/bin/Release/net10.0' ($name+'.dll'))
        [ordered]@{name=$name;test=$test;normalApp=$app;identical=$test.sha256 -eq $app.sha256}
    }
    $sources=@(
        'src/OpticalSystemDesign.App/Services/ScientificColorMaps.cs',
        'src/OpticalSystemDesign.App/Services/AppSettings.cs',
        'src/OpticalSystemDesign.App/Services/UiLocalization.cs',
        'src/OpticalSystemDesign.App/DisplaySettingsWindow.cs',
        'src/OpticalSystemDesign.App/Controls/AnalysisPlotControl.cs',
        'src/OpticalSystemDesign.App/Controls/WavefrontSurfaceControl.cs',
        'src/OpticalSystemDesign.App/Controls/FoucaultPlotControl.cs',
        'src/OpticalSystemDesign.App/Controls/FullFieldAberrationControl.cs',
        'src/OpticalSystemDesign.App/Shell/MainWindow.Workspace.cs',
        'src/OpticalSystemDesign.App/Panels/NonSequentialStrayLightWindows.cs'
    )
    $bindings=foreach($pdb in @((Join-Path $testBin 'OpticalSystemDesign.App.pdb'),
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')) {
        $stream=[IO.File]::OpenRead((Resolve-Path -LiteralPath $pdb).Path)
        try {
            $provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream,[System.Reflection.Metadata.MetadataStreamOptions]::Default,0)
            try {
                $reader=$provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default,$null)
                foreach($handle in $reader.Documents) {
                    $document=$reader.GetDocument($handle);$absolute=$reader.GetString($document.Name)
                    $relative=[IO.Path]::GetRelativePath($repository,$absolute).Replace('\','/')
                    if($relative -notin $sources){continue}
                    $compiled=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant()
                    $source=(Identity $absolute).sha256
                    [ordered]@{pdb=$pdb;path=$relative;compiled=$compiled;source=$source;matches=$compiled -eq $source}
                }
            } finally {$provider.Dispose()}
        } finally {$stream.Dispose()}
    }
    $calculationChanges=@(git diff --name-only HEAD -- src/OpticalSystemDesign.Core src/OpticalSystemDesign.Application/Runtime)
    $tests=@('tests/OpticalSystemDesign.Tests/ScientificColorMapTests.cs','tests/OpticalSystemDesign.Tests/InterferogramRenderingTests.cs')
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        sourceState='uncommitted shared working tree; unrelated presentation/catalog changes retained'
        testResult=Identity $trx;cases=$cases;distinctTests=$cases.Count
        allPassed=@($cases|Where-Object outcome -ne 'Passed').Count -eq 0
        assemblies=$assemblies;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0
        compiledSources=$bindings
        sourceBindingPassed=$bindings.Count -eq 20 -and @($bindings|Where-Object {!$_.matches}).Count -eq 0
        coreAndRuntimeUnchanged=$calculationChanges.Count -eq 0;unexpectedCalculationChanges=$calculationChanges
        sources=@($sources+$tests | ForEach-Object { Identity $_ })
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png' | ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository,$_.FullName)) })
        scope='29 focused off-screen cases: 18 new colormap checks; 11 existing interferogram, wavefront and curve-width checks. Default build, shared live/PNG/report drawing and real Double Gauss wavefront checked. No full-suite, external numerical certification, foreground app control or deployment.'
    }
    $receipt | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allPassed=$receipt.allPassed;normalAppIdentical=$receipt.normalAppIdentical;sourceBindingPassed=$receipt.sourceBindingPassed;coreAndRuntimeUnchanged=$receipt.coreAndRuntimeUnchanged} | ConvertTo-Json
    if($receipt.distinctTests -ne 29 -or !$receipt.allPassed -or !$receipt.normalAppIdentical -or !$receipt.sourceBindingPassed -or !$receipt.coreAndRuntimeUnchanged){exit 1}
} finally {Pop-Location}
