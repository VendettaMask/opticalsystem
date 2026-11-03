$ErrorActionPreference='Stop'
$repository=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    $root='validation/rechecks/2026-11-03/initial-workspace'
    function Identity([string]$path){[ordered]@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
    $trx=Join-Path $root 'checked/initial-checked.trx';$xml=[xml](Get-Content -LiteralPath $trx -Raw)
    $cases=@($xml.SelectNodes("//*[local-name()='UnitTestResult']")|ForEach-Object{[ordered]@{name=$_.testName;outcome=$_.outcome;duration=$_.duration}})
    $testBin='labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0'
    $assemblies=foreach($name in @('OpticalSystemDesign.App','OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.InitialStructure.Engine','OpticalSystemDesign.InitialStructure.Contracts','OpticalSystemDesign.InitialStructure.Persistence')) {
        $test=Identity (Join-Path $testBin ($name+'.dll'));$app=Identity (Join-Path 'src/OpticalSystemDesign.App/bin/Release/net10.0' ($name+'.dll'))
        [ordered]@{name=$name;test=$test;normalApp=$app;identical=$test.sha256 -eq $app.sha256}
    }
    $sources=@('src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.cs','src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Chrome.cs','src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Presentation.cs','src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Workflow.cs','src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Inputs.cs','src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Examples.cs','src/OpticalSystemDesign.App/Services/UiLocalization.cs')
    $bindings=foreach($pdb in @((Join-Path $testBin 'OpticalSystemDesign.App.pdb'),'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')) {
        $stream=[IO.File]::OpenRead((Resolve-Path -LiteralPath $pdb).Path)
        try {
            $provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream,[System.Reflection.Metadata.MetadataStreamOptions]::Default,0)
            try {
                $reader=$provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default,$null)
                foreach($handle in $reader.Documents) {
                    $document=$reader.GetDocument($handle);$absolute=$reader.GetString($document.Name);$relative=[IO.Path]::GetRelativePath($repository,$absolute).Replace('\','/')
                    if($relative -notin $sources){continue}
                    $compiled=[Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant();$source=(Identity $absolute).sha256
                    [ordered]@{pdb=$pdb;path=$relative;compiled=$compiled;source=$source;matches=$compiled -eq $source}
                }
            } finally {$provider.Dispose()}
        } finally {$stream.Dispose()}
    }
    $calculationChanges=@(git diff --name-only HEAD -- labs/InitialStructure/src src/OpticalSystemDesign.Core src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Workflow.cs src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Inputs.cs src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Examples.cs)
    $tests=@('labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/FlatStartDesktopTests.cs','labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/FlatStartWorkspaceTests.cs','labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.cs')
    $receipt=[ordered]@{
        baseCommit=(& git rev-parse HEAD).Trim();capturedUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceState='uncommitted shared working tree';
        testResult=Identity $trx;cases=$cases;distinctTests=$cases.Count;allPassed=@($cases|Where-Object outcome -ne 'Passed').Count -eq 0;
        assemblies=$assemblies;normalAppIdentical=@($assemblies|Where-Object {!$_.identical}).Count -eq 0;
        compiledSources=$bindings;sourceBindingPassed=$bindings.Count -eq 14 -and @($bindings|Where-Object {!$_.matches}).Count -eq 0;
        calculationAndContractsUnchanged=$calculationChanges.Count -eq 0;unexpectedCalculationChanges=$calculationChanges;
        sources=@($sources+$tests|ForEach-Object {Identity $_});
        previews=@(Get-ChildItem -LiteralPath $root -File -Filter '*.png'|ForEach-Object {Identity ([IO.Path]::GetRelativePath($repository,$_.FullName))});
        scope='24 scoped headless cases: 19 existing non-CUDA desktop cases, four new compact-layout cases and the autonomous entry case. Dedicated CUDA hardware test excluded; automatic device behavior and numerical/search acceptance unchanged. No user desktop control, deployment, full suite or external numerical certification.'
    }
    $receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{distinctTests=$receipt.distinctTests;allPassed=$receipt.allPassed;normalAppIdentical=$receipt.normalAppIdentical;sourceBindingPassed=$receipt.sourceBindingPassed;calculationUnchanged=$receipt.calculationAndContractsUnchanged}|ConvertTo-Json
    if($receipt.distinctTests -ne 24 -or !$receipt.allPassed -or !$receipt.normalAppIdentical -or !$receipt.sourceBindingPassed -or !$receipt.calculationAndContractsUnchanged){exit 1}
} finally {Pop-Location}