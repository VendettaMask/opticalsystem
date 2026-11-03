$ErrorActionPreference='Stop'
$repository=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root='validation/rechecks/2026-11-03/analysis-frame-dpi'
    function Identity([string]$path) { [ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()} }
    $runs=@('final/frame-export-final.trx','retry/frame-retry.trx','legend-final/legend-final.trx',
        'coating-checked/coating-frame-checked.trx','coating-verified/coating-spectrum-verified.trx')
    $history=@();$latest=@{}
    foreach($run in $runs) {
        $xml=[xml](Get-Content -LiteralPath (Join-Path $root $run) -Raw)
        foreach($case in $xml.SelectNodes("//*[local-name()='UnitTestResult']")) {
            $item=[ordered]@{run=$run;name=$case.testName;outcome=$case.outcome;duration=$case.duration}
            $history+=$item;$latest[$case.testName]=$item
        }
    }
    $bins=@('tests/OpticalSystemDesign.Tests/bin/Release/net10.0',
        'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0',
        'src/OpticalSystemDesign.App/bin/Release/net10.0')
    $assemblies=foreach($name in @('OpticalSystemDesign.App','OpticalSystemDesign.Application','OpticalSystemDesign.Core')) {
        $copies=@($bins|ForEach-Object { Identity (Join-Path $_ ($name+'.dll')) })
        [ordered]@{name=$name;copies=$copies;identical=@($copies.sha256|Select-Object -Unique).Count -eq 1}
    }
    $appSources=@(
        'Controls/AnalysisImageFrame.cs','Controls/ResponsiveAnalysisPlotHost.cs','Controls/UniformDiagramPage.cs',
        'Services/ExportImageFrame.cs','Services/ImageExportResolution.cs','Services/PngImageWriter.cs','Services/AppSettings.cs','Services/UiLocalization.cs',
        'DisplaySettingsWindow.cs','Shell/MainWindow.Workspace.cs','Panels/Analysis/AnalysisPanel.Export.cs','Panels/Analysis/AnalysisPanel.Parameters.cs',
        'Panels/ViewerPanel.Export.cs','Panels/ViewerPanel.cs','Panels/NonSequentialStrayLightWindows.cs',
        'Laboratories/CameraImagingWindow.Sfr.cs','Laboratories/CameraImagingWindow.SceneSfr.cs',
        'Laboratories/CoatingDesign/CoatingStackAnalysisView.cs','Laboratories/CoatingDesign/SpectrumPlot.cs','Laboratories/CoatingDesign/MainWindow.cs'
    )|ForEach-Object {'src/OpticalSystemDesign.App/'+$_}
    $applicationSources=@('Services/LosslessImageOutput.cs','Services/ImageFileLoader.cs','Runtime/WorkbenchRuntime.Analysis.cs')|
        ForEach-Object {'src/OpticalSystemDesign.Application/'+$_}
    $bindings=foreach($bin in $bins) {
        foreach($assembly in @('OpticalSystemDesign.App','OpticalSystemDesign.Application')) {
            $selected=if($assembly -eq 'OpticalSystemDesign.App'){$appSources}else{$applicationSources}
            $pdb=Join-Path $bin ($assembly+'.pdb');$stream=[IO.File]::OpenRead((Resolve-Path -LiteralPath $pdb).Path)
            try {
                $provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream,[System.Reflection.Metadata.MetadataStreamOptions]::Default,0)
                try {
                    $reader=$provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default,$null)
                    foreach($handle in $reader.Documents) {
                        $document=$reader.GetDocument($handle);$absolute=$reader.GetString($document.Name)
                        $relative=[IO.Path]::GetRelativePath($repository,$absolute).Replace('\','/')
                        if($relative -notin $selected){continue}
                        $compiled=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant()
                        $source=(Identity $absolute).sha256
                        [ordered]@{pdb=$pdb;path=$relative;compiled=$compiled;source=$source;matches=$compiled -eq $source}
                    }
                } finally {$provider.Dispose()}
            } finally {$stream.Dispose()}
        }
    }
    $calculations=@(git diff --name-only HEAD -- src/OpticalSystemDesign.Core labs/CoatingDesign/src labs/InitialStructure/src)
    $cases=@($latest.Values|Sort-Object name)
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        sourceState='shared uncommitted working tree; unrelated UI/icon work preserved'
        runs=@($runs|ForEach-Object {Identity (Join-Path $root $_)});history=$history;cases=$cases
        distinctTests=$cases.Count;allPassed=@($cases|Where-Object outcome -ne 'Passed').Count -eq 0
        assemblies=$assemblies;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0
        mainRunAppSha256='8da4fad54d1babe10a30a7edda8219686c212d32822989238561a1a9d8af6764'
        mainRunProductRetained=(Identity (Join-Path $bins[0] 'OpticalSystemDesign.App.dll')).sha256 -eq '8da4fad54d1babe10a30a7edda8219686c212d32822989238561a1a9d8af6764'
        compiledSources=$bindings;sourceBindingPassed=$bindings.Count -eq 69 -and @($bindings|Where-Object {!$_.matches}).Count -eq 0
        computationUnchanged=$calculations.Count -eq 0;unexpectedCalculationChanges=$calculations
        sources=@($appSources+$applicationSources|ForEach-Object {Identity $_})
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png'|ForEach-Object {Identity ([IO.Path]::GetRelativePath($repository,$_.FullName))})
        scope='154 distinct focused cases after test-only precondition repairs: 147 main-app cases and seven coating cases. Actual PNG/BMP signatures/pixels, independent DPI, complete live/export/report frames, viewport/camera/legend state, SFR and Word/PDF/HTML/Markdown drawing retention. No full suite, external optical certification, foreground desktop control, installation or publishing.'
    }
    $receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allPassed=$receipt.allPassed;normalAppIdentical=$receipt.normalAppIdentical;mainRunProductRetained=$receipt.mainRunProductRetained;sourceBindingPassed=$receipt.sourceBindingPassed;computationUnchanged=$receipt.computationUnchanged}|ConvertTo-Json
    if($receipt.distinctTests -ne 154 -or !$receipt.allPassed -or !$receipt.normalAppIdentical -or !$receipt.mainRunProductRetained -or !$receipt.sourceBindingPassed -or !$receipt.computationUnchanged){exit 1}
} finally {Pop-Location}
