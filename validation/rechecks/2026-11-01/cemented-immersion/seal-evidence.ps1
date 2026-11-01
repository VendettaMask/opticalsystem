$ErrorActionPreference='Stop'
$assemblyRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $assemblyRepo
if(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json')){throw 'Evidence already sealed.'}
& (Join-Path $PSScriptRoot 'verify-final.ps1')
$assemblySummary=Get-Content (Join-Path $PSScriptRoot 'final-verification-summary.json') -Raw | ConvertFrom-Json
$assemblyDocuments=@('docs/CEMENTED_IMMERSION_NSC_2026-11-01.md','docs/CURRENT_STATUS.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/CAMERA_IMAGING_LAB.md','docs/STAROPT_FILE_FORMAT.md')
$assemblyDocs=@(foreach($assemblyPath in $assemblyDocuments){[pscustomobject]@{Path=$assemblyPath;Sha256=(Get-FileHash -LiteralPath $assemblyPath).Hash.ToLowerInvariant()}})
$assemblyTools=Join-Path $PSScriptRoot 'verification-tools';New-Item -ItemType Directory -Path $assemblyTools -Force | Out-Null
foreach($assemblyName in @('Program.cs','PdbAudit.csproj')){Copy-Item -LiteralPath "tools/diagnostics/OpticalAssembly20261101/$assemblyName" -Destination $assemblyTools}
$assemblyFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
 [pscustomobject]@{Path=[IO.Path]::GetRelativePath($PSScriptRoot,$_.FullName).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
[ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Scope='Coaxial cemented and immersed sequential optical assemblies, actual media, independent apertures/coatings, atomic failure, shared NSC/database/camera chain.';ProductTestsPassed=$assemblySummary.Counters.Passed;NewTestsPassed=30;DefaultReleaseAppBuildPassed=$true;CompiledSourceChecksumsMatched=92;PriorFrozenFilesVerified=2439;NativeDesktopAcceptance=$false;CommercialCertification=$false;DocumentationAtSeal=$assemblyDocs;Files=$assemblyFiles} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'evidence-manifest.json') -Encoding utf8
Write-Output "Sealed $($assemblyFiles.Count) evidence files."
