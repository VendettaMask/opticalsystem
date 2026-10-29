param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$IsolatedRoot
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$isolated = (Resolve-Path -LiteralPath $IsolatedRoot).Path
$artifacts = Join-Path $root 'artifacts/validation'
$output = Join-Path $artifacts 'fft-profile-grid-20261029-assessment.json'
if (Test-Path -LiteralPath $output) { throw 'Preserve the existing assessment.' }
function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
$cohorts = @(
    @{ name='fft-profile-gaussian-20261029'; validity='valid'; reason='Gaussian factor 1 readback and complex grids' },
    @{ name='fft-profile-triangle-control-20261029'; validity='invalid'; reason='Native filename truncated; polygon not intersected' },
    @{ name='fft-profile-triangle-control-20261029-02'; validity='valid'; reason='Exact filename readback and complete physical paths' },
    @{ name='fft-profile-normal-control-20261029'; validity='failed'; reason='NSC single-ray analysis unavailable in mixed mode' },
    @{ name='fft-profile-normal-control-20261029-02'; validity='invalid'; reason='SaveRaysFile interpreted as input; result IsValid=false' },
    @{ name='fft-profile-normal-control-20261029-03'; validity='valid'; reason='Extracted group; valid direct normal readback' }
)
$executions = @()
$triangleControls = @()
$normalControls = @()
foreach ($cohort in $cohorts) {
    $directory = Join-Path $artifacts $cohort.name
    foreach ($execution in (Read-Json (Join-Path $directory 'execution.json'))) {
        Require $execution.sourceUnchanged 'Native capture changed its source.'
        $job = Join-Path $directory $execution.id
        if ($cohort.validity -eq 'valid') {
            Require ($execution.result.exitCode -eq 0) 'A valid capture must exit successfully.'
            if ($cohort.name -eq 'fft-profile-gaussian-20261029') {
                $apodization = Read-Json (Join-Path $job 'diagnostic-apodization.json')
                Require ($apodization.actual.ApodizationType -eq 'Gaussian' -and $apodization.actual.ApodizationFactor -eq 1) 'Wrong Gaussian control.'
                $grids = (Read-Json (Join-Path $job 'data.json')).dataGrids
                Require ($grids.Count -eq 1 -and $grids[0].values.Count -eq 64 -and $grids[0].values[0].Count -eq 64) 'Incomplete complex grid.'
            }
            if ($cohort.name -eq 'fft-profile-triangle-control-20261029-02') {
                $polygon = Read-Json (Join-Path $job 'diagnostic-polygon.json')
                Require ($polygon.actualComment -ceq $polygon.temporaryName) 'Truncated polygon name.'
                $paths = Read-Json (Join-Path $job 'nsc-paths.json')
                Require ($paths.rays.Count -eq 33) 'Incomplete triangle probes.'
                Require (@($paths.rays | Where-Object { $_.error -ne 0 -or $_.segmentCount -lt 8 }).Count -eq 0) 'Missing physical polygon interactions.'
                $originalPath = Join-Path $artifacts "afocal-native-resume-20261029-paths-02/binocular-c$($paths.configuration)-nsc-paths-pupil/nsc-paths.json"
                $original = Read-Json $originalPath
                $same = ($paths.rays | ConvertTo-Json -Depth 30 -Compress) -ceq ($original.rays | ConvertTo-Json -Depth 30 -Compress)
                $maxPosition = 0.0
                $maxDirection = 0.0
                $maxLength = 0.0
                $sameSegments = $true
                for ($rayIndex=0; $rayIndex -lt $paths.rays.Count; $rayIndex++) {
                    $a=$paths.rays[$rayIndex]; $b=$original.rays[$rayIndex]
                    if ($a.segmentCount -ne $b.segmentCount -or $a.segments.Count -ne $b.segments.Count) { $sameSegments=$false }
                    for ($segment=0; $segment -lt [Math]::Min($a.segments.Count,$b.segments.Count); $segment++) {
                        $p=$a.segments[$segment]; $q=$b.segments[$segment]
                        if ($p.hitObject -ne $q.hitObject -or $p.insideOf -ne $q.insideOf) { $sameSegments=$false }
                        foreach ($key in @('x','y','z')) { $maxPosition=[Math]::Max($maxPosition,[Math]::Abs($p.$key-$q.$key)) }
                        foreach ($key in @('l','m','n')) { $maxDirection=[Math]::Max($maxDirection,[Math]::Abs($p.$key-$q.$key)) }
                        $maxLength=[Math]::Max($maxLength,[Math]::Abs($p.pathLength-$q.pathLength))
                    }
                }
                $triangleControls += [pscustomobject]@{id=$execution.id;configuration=$paths.configuration;originalPath=[IO.Path]::GetRelativePath($root,$originalPath);all33PathsExactlyEqual=$same;segmentObjectSequencesEqual=$sameSegments;maximumCoordinateDifferenceMm=$maxPosition;maximumDirectionComponentDifference=$maxDirection;maximumSegmentLengthDifferenceMm=$maxLength}
            }
            if ($cohort.name -eq 'fft-profile-normal-control-20261029-03') {
                $result = Read-Json (Join-Path $job 'normal-result.json')
                $normals = @(Read-Json (Join-Path $job 'native-normals.json'))
                Require ($result.saved -and $result.data.IsValid -and $result.data.NumberOfSegments -ge 8 -and $normals.Count -ge 8) 'Invalid native normal result.'
                $normalControls += [pscustomobject]@{id=$execution.id;valid=$true;segments=$normals.Count;originalFullSystemRequest=$false;internalTriangleIdsAvailable=$false}
            }
        } elseif ($cohort.validity -eq 'invalid') {
            if ($cohort.name -eq 'fft-profile-triangle-control-20261029') {
                $paths = Read-Json (Join-Path $job 'nsc-paths.json')
                Require (@($paths.rays | Where-Object { $_.segmentCount -lt 8 }).Count -gt 0) 'Expected invalid no-hit diagnostic.'
            } else {
                Require (-not (Read-Json (Join-Path $job 'normal-result.json')).data.IsValid) 'Expected invalid normal result.'
            }
        } else { Require ($execution.result.exitCode -ne 0) 'Expected failed analysis launch.' }
        $executions += [pscustomobject]@{cohort=$cohort.name;id=$execution.id;exitCode=$execution.result.exitCode;sourceUnchanged=$execution.sourceUnchanged;validity=$cohort.validity;reason=$cohort.reason}
    }
}
$final = @(Read-Json (Join-Path $artifacts 'fft-profile-grid-20261029-restored-after-moller/summary.json'))
$before = @(Read-Json (Join-Path $artifacts 'afocal-native-resume-20261029-compare-final/summary.json'))
Require (($final | ConvertTo-Json -Depth 10 -Compress) -ceq ($before | ConvertTo-Json -Depth 10 -Compress)) 'Restored original results differ.'
Require ($final.Count -eq 5 -and $final[3].violations -eq 335 -and $final[3].maximum -eq 0.0011946610381988604) 'Unexpected final pointwise status.'
$experiments = @(Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'fft-profile-grid-20261029-*' | Sort-Object Name | ForEach-Object {
    $summary = Join-Path $_.FullName 'summary.json'
    if ((Test-Path -LiteralPath $summary) -and $_.Name -notin @('fft-profile-grid-20261029-restored-final','fft-profile-grid-20261029-restored-after-moller')) {
        $rows = @(Read-Json $summary)
        [pscustomobject]@{id=$_.Name;c1Violations=$rows[2].violations;c2Violations=$rows[3].violations;c2Maximum=$rows[3].maximum;accepted=$false}
    }
})
Require ($experiments.Count -eq 11 -and @($experiments | Where-Object { $_.c2Violations -eq 0 }).Count -eq 0) 'Unexpected experiment coverage.'
Push-Location $isolated
try {
    $head = git rev-parse HEAD
    $coreDiff = @(git status --porcelain -- src/OpticalSystemDesign.Core)
    Require ($LASTEXITCODE -eq 0 -and $coreDiff.Count -eq 0) 'Experimental Core changes remain.'
} finally { Pop-Location }
$originalArchive = Join-Path $root 'validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip'
$referenceSha = (Get-FileHash -LiteralPath $originalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Require ($referenceSha -eq '235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1') 'Original frozen reference differs.'
$assets = @()
$installedObjects = 'C:\Users\19851\Documents\Zemax\OBJECTS\Polygon Objects'
foreach ($asset in @(
    @{name='PORRO_PRISM-LH.POB';sha='8ba3b4bfe0092319a97e6169de6987132c0f9e2cda99f0b6a19a70eecc3a74f4'},
    @{name='PORRO_PRISM-RH.POB';sha='c247ee42b9ec39fed6d0411e5f8b7c780ad3a6ff938bd6a642450d1e40dcef68'}
)) {
    $sha = (Get-FileHash -LiteralPath (Join-Path $installedObjects $asset.name) -Algorithm SHA256).Hash.ToLowerInvariant()
    Require ($sha -eq $asset.sha) 'Installed original polygon differs.'
    $assets += [pscustomobject]@{name=$asset.name;sha256=$sha;unchanged=$true}
}
Require (@(Get-ChildItem -LiteralPath $installedObjects -Filter 'codex_afocal_*.POB').Count -eq 0) 'Owned temporary polygon remains.'
$gaussian = @(Read-Json (Join-Path $artifacts 'fft-profile-grid-20261029/gaussian-coordinates-reference-corrected.json'))
$gaussianAxes = @($gaussian | ForEach-Object {
    $configuration=$_.configuration
    $_.axis | Where-Object { $_.x -eq 13 -or $_.y -eq 13 } | ForEach-Object {
        Require ($null -ne $_.normalizationReference) 'Stale Gaussian analyzer output.'
        [pscustomobject]@{configuration=$configuration;x=$_.x;y=$_.y;inferredCoordinateStretch=$_.inferredCoordinateStretch}
    }
})
$verification = Join-Path $artifacts 'fft-profile-grid-20261029-verification'
[xml]$trx = Get-Content -LiteralPath (Join-Path $verification 'native-guards.trx') -Raw
$counters=$trx.TestRun.ResultSummary.Counters
Require ($counters.total -eq 12 -and $counters.passed -eq 12 -and $counters.failed -eq 0) 'Focused guard tests did not pass.'
Require ((Read-Json (Join-Path $verification 'host-build.json')).success) 'Final Framework host does not compile.'
$assessment = [ordered]@{
    date='2026-10-29';status='investigation_completed_precision_issue_open';objectiveAchieved=$false
    originalReferenceSamplingToleranceChanged=$false;foregroundDesktopUsed=$false;productCoreChangesAccepted=0
    verificationScope='Isolated HEAD Core only; concurrent main-workspace changes are not certified'
    isolatedRoot=$isolated;sourceCommit=$head;coreSourceMatchesCommit=$true
    coreAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $isolated 'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    originalArchiveSha256=$referenceSha;installedPolygonAssets=$assets;temporaryPolygonFilesRemaining=0
    validNativeCaptures=@($executions|Where-Object validity -eq 'valid').Count
    invalidNativeCaptures=@($executions|Where-Object validity -eq 'invalid').Count
    failedNativeCaptures=@($executions|Where-Object validity -eq 'failed').Count
    executions=$executions;triangleControls=$triangleControls;normalControls=$normalControls
    gaussianInferenceAxes=$gaussianAxes;gaussianInferenceIsRuntimeInput=$false
    rejectedExperiments=$experiments;restoredProfilesExactlyEqualPreviousBaseline=$true
    restoredProfiles=$final;allProfilePointsPassed=$false;remainingViolations=335
    focusedGuardTests=@{total=12;passed=12;failed=0};broadTestSuiteRun=$false
    staleGaussianOutput='gaussian-coordinates.json and gaussian-coordinates-corrected.json use the old analyzer and are rejected; the latter ran before its rebuild completed'
}
Require ($assessment.validNativeCaptures -eq 10 -and $assessment.invalidNativeCaptures -eq 6 -and $assessment.failedNativeCaptures -eq 1) 'Unexpected native execution totals.'
$assessment | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $output -Encoding utf8
[pscustomobject]$assessment | Select-Object status,objectiveAchieved,validNativeCaptures,invalidNativeCaptures,failedNativeCaptures,remainingViolations,coreSourceMatchesCommit | ConvertTo-Json
