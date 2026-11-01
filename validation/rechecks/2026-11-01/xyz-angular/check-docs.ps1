$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Set-Location -LiteralPath $bsdfRepo
$bsdfDocs=@('docs/XYZ_MEASURED_ANGULAR_2026-11-01.md','docs/CURRENT_STATUS.md','docs/STAROPT_FILE_FORMAT.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CAMERA_IMAGING_LAB.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/NUMERICAL_PARITY.md','docs/README.md','docs/XYZ_KNOWN_CONDITIONS_2026-11-01.md')
$bsdfMap=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-snapshot-map.json') -Raw | ConvertFrom-Json
$bsdfDiffPaths=@($bsdfDocs)+@($bsdfMap.OriginalPath)
& git diff --check -- $bsdfDiffPaths *> (Join-Path $PSScriptRoot 'scoped-diff-check.log')
$bsdfExit=$LASTEXITCODE
if($bsdfExit -ne 0){Get-Content -LiteralPath (Join-Path $PSScriptRoot 'scoped-diff-check.log');throw 'Scoped diff check failed'}
$bsdfMissing=@()
foreach($bsdfDoc in @('docs/XYZ_MEASURED_ANGULAR_2026-11-01.md','validation/rechecks/2026-11-01/xyz-angular/README.md')) {
    $bsdfDocPath=Join-Path $bsdfRepo $bsdfDoc
    foreach($bsdfMatch in [regex]::Matches([IO.File]::ReadAllText($bsdfDocPath),'\]\(([^)]+)\)')) {
        $bsdfTarget=$bsdfMatch.Groups[1].Value
        if($bsdfTarget -match '^(https?://|#)'){continue}
        $bsdfTarget=($bsdfTarget -split '#')[0]
        $bsdfResolved=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($bsdfDocPath)) $bsdfTarget))
        if(-not(Test-Path -LiteralPath $bsdfResolved)){$bsdfMissing += "$bsdfDoc : $bsdfTarget"}
    }
}
if($bsdfMissing.Count){$bsdfMissing;throw 'Broken report links'}
$bsdfChanged=@($bsdfMap | Where-Object {(Get-FileHash -LiteralPath $_.OriginalPath).Hash.ToLowerInvariant() -ne $_.Sha256} | Select-Object OriginalPath,Sha256)
[ordered]@{ScopedDiffCheckExitCode=$bsdfExit;ReportDocumentsChecked=2;BrokenLinks=0;OriginalScopedFilesChangedAfterFreeze=$bsdfChanged} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'documentation-check.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'documentation-check.json')
