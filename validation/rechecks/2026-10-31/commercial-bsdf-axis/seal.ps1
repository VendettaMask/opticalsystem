$ErrorActionPreference = 'Stop'
$bsdfDir = Join-Path (Get-Location).Path 'validation/rechecks/2026-10-31/commercial-bsdf-axis'
[xml]$bsdfTrx = Get-Content -Raw -LiteralPath (Join-Path $bsdfDir 'final-focused.trx')
$bsdfNs = New-Object System.Xml.XmlNamespaceManager($bsdfTrx.NameTable)
$bsdfNs.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$bsdfCounters = $bsdfTrx.SelectSingleNode('//t:Counters', $bsdfNs)
$bsdfNewResults = @($bsdfTrx.SelectNodes('//t:UnitTestResult', $bsdfNs) | Where-Object testName -Like '*CommercialBsdf*')
$bsdfBefore = Get-Content -Raw -LiteralPath (Join-Path $bsdfDir 'before-final-run.json') | ConvertFrom-Json
$bsdfAfter = Get-Content -Raw -LiteralPath (Join-Path $bsdfDir 'after-final-run.json') | ConvertFrom-Json
$bsdfSourceDrift = @($bsdfBefore.ScopedSources | Where-Object { $f = $_; ($bsdfAfter.ScopedSources | Where-Object Path -EQ $f.Path).Sha256 -ne $f.Sha256 })
$bsdfBinaryDrift = @($bsdfBefore.Binaries | Where-Object { $f = $_; ($bsdfAfter.Binaries | Where-Object Path -EQ $f.Path).Sha256 -ne $f.Sha256 })
$bsdfApplicationCopies = @('src/OpticalSystemDesign.Application/bin/Release/net10.0/OpticalSystemDesign.Application.dll', 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Application.dll', 'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Application.dll') | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } }
$bsdfArtifacts = @(Get-ChildItem -LiteralPath $bsdfDir -File | Where-Object Name -NE 'manifest.json' | Sort-Object Name | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
[ordered]@{
 Stage = 'Speos v8 text BSDF import and fixed material axes, NSC -> RAW/ISP'; SealedUtc = [DateTimeOffset]::UtcNow.ToString('o')
 BaseCommit = $bsdfBefore.BaseCommit; SharedDirtyWorkspace = $true; Scope = 'Focused regressions, no whole suite or native desktop acceptance'
 Supported = @('Speos v8.0 TEXT .brdf BSDF values', 'Speos v8.0 TEXT .anisotropicbsdf BSDF values', 'Native CSV v1/v2')
 Result = @{ Total = [int]$bsdfCounters.total; Passed = [int]$bsdfCounters.passed; Failed = [int]$bsdfCounters.failed; NewStageCases = $bsdfNewResults.Count }
 Fingerprints = @{ ScopedSources = $bsdfBefore.ScopedSources.Count; Binaries = $bsdfBefore.Binaries.Count; SourceDriftCount = $bsdfSourceDrift.Count; BinaryDriftCount = $bsdfBinaryDrift.Count; ApplicationCopiesAfterRun = @($bsdfApplicationCopies); ApplicationCopiesMatch = @($bsdfApplicationCopies.Sha256 | Select-Object -Unique).Count -eq 1 }
 Artifacts = $bsdfArtifacts
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $bsdfDir 'manifest.json') -Encoding utf8
$bsdfNewResults | Group-Object { ($_.testName -split '\.')[2] } | Select-Object Name,Count
$bsdfCounters | Select-Object total,passed,failed,notExecuted
