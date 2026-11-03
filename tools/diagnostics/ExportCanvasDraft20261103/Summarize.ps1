$ErrorActionPreference='Stop'
$repository=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root='validation/rechecks/2026-11-03/export-canvas-draft'
    function Identity([string]$path) { [ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()} }
    $runs=@('final/exports-final.trx','coating-final/coating-final.trx')
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
        'Controls/ExportCanvasPreview.cs','Services/ReportImageRenderer.cs','Controls/AnalysisImageFrame.cs','Controls/ResponsiveAnalysisPlotHost.cs','Controls/UniformDiagramPage.cs',
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
    $preRun=Get-Content -LiteralPath (Join-Path $root 'pre-final-identities.json') -Raw | ConvertFrom-Json
    $testedApp='9cc670ca1bd2de8791956d15f5ec97f6052c7bfe8cf2945130b2366f14f7417a'
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        sourceState='shared uncommitted working tree; unrelated UI/icon work preserved'
        runs=@($runs|ForEach-Object {Identity (Join-Path $root $_)});history=$history;cases=$cases
        distinctTests=$cases.Count;allPassed=@($cases|Where-Object outcome -ne 'Passed').Count -eq 0
        assemblies=$assemblies;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0
        preRunAppIdentical=$preRun.Count -eq 3 -and @($preRun|Where-Object Sha256 -ne $testedApp).Count -eq 0
        testedAppCopiesRetained=@($bins[0..1]|Where-Object {(Identity (Join-Path $_ 'OpticalSystemDesign.App.dll')).sha256 -ne $testedApp}).Count -eq 0
        buildBoundary='Both test outputs and the normal App were identical before the final run. Another shared-workspace task subsequently rebuilt the normal App; the recorded full binary equality may therefore be false at receipt time. All 25 scoped product sources are separately matched against all three PDBs. Do not promote the 172-case result to an untested later whole-App build.'
        mainRunAppSha256='9cc670ca1bd2de8791956d15f5ec97f6052c7bfe8cf2945130b2366f14f7417a'
        mainRunProductRetained=(Identity (Join-Path $bins[0] 'OpticalSystemDesign.App.dll')).sha256 -eq '9cc670ca1bd2de8791956d15f5ec97f6052c7bfe8cf2945130b2366f14f7417a'
        compiledSources=$bindings;sourceBindingPassed=$bindings.Count -eq (($appSources.Count+$applicationSources.Count)*$bins.Count) -and @($bindings|Where-Object {!$_.matches}).Count -eq 0
        computationUnchanged=$calculations.Count -eq 0;unexpectedCalculationChanges=$calculations
        sources=@($appSources+$applicationSources|ForEach-Object {Identity $_})
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png'|ForEach-Object {Identity ([IO.Path]::GetRelativePath($repository,$_.FullName))})
        scope='Adjustable resolution draft, not a finalized user specification. Focused complete-canvas PNG, independent DPI, source-aspect padding, reports retaining original figure geometry, viewport/camera/legend, SFR and coating checks. Actual PNG/BMP signatures and pixels; no full suite, external optical certification, foreground control, installation or publication.'
    }
    $receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allPassed=$receipt.allPassed;preRunAppIdentical=$receipt.preRunAppIdentical;normalAppIdenticalNow=$receipt.normalAppIdentical;testedAppCopiesRetained=$receipt.testedAppCopiesRetained;sourceBindingPassed=$receipt.sourceBindingPassed;computationUnchanged=$receipt.computationUnchanged}|ConvertTo-Json
    if($receipt.distinctTests -ne 172 -or !$receipt.allPassed -or !$receipt.preRunAppIdentical -or !$receipt.testedAppCopiesRetained -or !$receipt.sourceBindingPassed -or !$receipt.computationUnchanged){exit 1}
} finally {Pop-Location}
