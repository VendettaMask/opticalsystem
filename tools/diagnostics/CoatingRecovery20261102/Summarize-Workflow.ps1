param([string]$Evidence = 'validation/rechecks/2026-11-02/coating-workflow-analytic')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $repository
try {
    function Identity([string]$file) { [ordered]@{ path = $file.Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() } }
    $tests = foreach ($file in @('final/final-coating.trx','final/final-shared.trx')) {
        $path = Join-Path $Evidence $file
        $xml = [xml](Get-Content -Raw -LiteralPath $path)
        $cases = @($xml.SelectNodes("//*[local-name()='UnitTestResult']") | ForEach-Object { [ordered]@{ name = $_.testName; outcome = $_.outcome; duration = $_.duration } })
        [ordered]@{ path = $file; total = $cases.Count; passed = @($cases | Where-Object outcome -eq 'Passed').Count; cases = $cases; sha256 = (Identity $path).sha256 }
    }
    $names = @($tests.cases.name | Sort-Object -Unique)
    $assemblies = foreach ($name in @('OpticalSystemDesign.App.dll','OpticalSystemDesign.Core.dll','OpticalSystemDesign.CoatingDesign.Engine.dll')) {
        $copies = @(@('src/OpticalSystemDesign.App/bin/Release/net10.0','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0') | ForEach-Object { Identity (Join-Path $_ $name) })
        [ordered]@{ name = $name; identical = @($copies.sha256 | Sort-Object -Unique).Count -eq 1; copies = $copies }
    }
    $prior = Get-Content -Raw -LiteralPath 'validation/rechecks/2026-11-02/coating-editor-duplex/receipt.json' | ConvertFrom-Json
    $legacy = @($prior.legacyNumericalImplementations.sources | ForEach-Object {
        $now = Identity $_.path
        [ordered]@{ path = $_.path; sha256 = $now.sha256; previousSha256 = $_.sha256; unchanged = $now.sha256 -eq $_.sha256 }
    })
    $sourceFiles = @(@('src/OpticalSystemDesign.Core/Coatings','src/OpticalSystemDesign.App/Laboratories/CoatingDesign','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests') | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -File | Where-Object Extension -in @('.cs','.csproj') | ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName) }
    }) + @('tools/diagnostics/CoatingRecovery20261102/Program.cs','tools/diagnostics/CoatingRecovery20261102/CoatingRecovery20261102.csproj','tools/diagnostics/CoatingRecovery20261102/packages.lock.json','tools/diagnostics/CoatingRecovery20261102/Summarize-Workflow.ps1')
    $comparisons = @(Get-ChildItem -LiteralPath $Evidence -Filter 'duplex-budget-*.json' | ForEach-Object {
        $d = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
        [ordered]@{ path = $_.Name; inputFingerprint = $d.baseline.Fingerprint; iterationLimit = $d.input.Settings.MaximumIterations;
            baseline = $d.baseline.Metrics; v1 = $d.v1.Metrics; v2 = $d.v2.Metrics;
            v1Passed = $d.v1.Passed; v2Passed = $d.v2.Passed; v1Work = $d.v1.Run.Duplex; v2Work = $d.v2.Run.Duplex;
            sha256 = (Identity $_.FullName).sha256 }
    })
    $receipt = [ordered]@{
        baseCommit = (& git rev-parse HEAD).Trim(); sourceState = 'uncommitted working tree; scoped coating validation'; capturedUtc = [DateTimeOffset]::UtcNow.ToString('o');
        tests = $tests; distinctTests = $names.Count; allPassed = @($tests | Where-Object { $_.total -eq 0 -or $_.total -ne $_.passed }).Count -eq 0;
        assemblies = $assemblies; binaryCopiesMatch = @($assemblies | Where-Object { !$_.identical }).Count -eq 0;
        testAssemblies = @(Identity 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.CoatingDesign.Tests.dll'; Identity 'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll'; Identity 'tools/diagnostics/CoatingRecovery20261102/bin/Release/net10.0/CoatingRecovery20261102.dll');
        legacyNumericalSources = $legacy; sources = @($sourceFiles | Sort-Object -Unique | ForEach-Object { Identity $_ });
        independentInputs = @(Identity 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/duplex-substrate-20261102.json'; Identity 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/incoherent-substrate-20261102.json'; Identity 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/tmmcore-sensitivity-0.4.2.json'; Identity 'validation/coating/tmmcore-0.4.2.json');
        buildLogs = @(Identity (Join-Path $Evidence 'build-final.log'); Identity (Join-Path $Evidence 'build-shared.log'));
        documentation = @(Identity 'docs/COATING_WORKFLOW_ANALYTIC_2026-11-02.md'; Identity 'docs/COATING_WORKFLOW_ANALYTIC_PLAN_2026-11-02.md'; Identity 'docs/CURRENT_STATUS.md'; Identity (Join-Path $Evidence 'README.md'); Identity (Join-Path $Evidence 'documentation.log'));
        comparisons = $comparisons; previews = @(Get-ChildItem -LiteralPath $Evidence -Filter '*.png' | ForEach-Object { Identity ([IO.Path]::GetRelativePath($repository, $_.FullName)) });
        scope = 'Headless and background only; no native user clipboard or desktop interaction. No installation, release, full repository suite or remote synchronization.'
    }
    $receipt | ConvertTo-Json -Depth 35 | Set-Content -LiteralPath (Join-Path $Evidence 'receipt.json') -Encoding utf8NoBOM
    [ordered]@{ distinctTests = $receipt.distinctTests; allPassed = $receipt.allPassed; binaryCopiesMatch = $receipt.binaryCopiesMatch; originalScalarSourcesUnchanged = @($legacy | Where-Object { !$_.unchanged }).Count -eq 0 } | ConvertTo-Json
    if (!$receipt.allPassed -or !$receipt.binaryCopiesMatch -or @($legacy | Where-Object { !$_.unchanged }).Count -ne 0) { exit 1 }
}
finally { Pop-Location }
