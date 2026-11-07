param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Repo,
    [int[]]$Factors = @(2, 4),
    [int[]]$Ids = @(),
    [string]$JobFile = 'native-dense-jobs.json'
)
$ErrorActionPreference = 'Stop'
if ($Factors.Count -eq 0 -or @($Factors | Where-Object { $_ -notin 2,4,8,16,32 }).Count) { throw 'Use bounded dyadic pupil refinement' }
$summary = Get-Content "$repo/validation/rechecks/2026-11-07/fan-physical-coverage/summary.json" -Raw | ConvertFrom-Json
if ($Ids.Count -eq 0) { $Ids = $summary.opdScopeNotPassed }
if (@($Ids | Where-Object { $_ -notin $summary.opdScopeNotPassed }).Count) { throw 'Only original unresolved OPD fan requests are in scope' }
$jobs = foreach ($id in $Ids) {
    $source = "$Root/frozen/$id/request.json"
    $original = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json -AsHashtable
    if ($original.adapter -ne 'fan' -or $original.analysisType -ne 'OpticalPathFan' -or $original.wavelength -le 0) {
        throw "Original monochromatic native OPD request expected for $id"
    }
    foreach ($factor in $Factors) {
        $request = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json -AsHashtable
        $request.rayCount = [int]($original.rayCount * $factor)
        $request.workbenchSettings.NumberOfRays = $request.rayCount.ToString([Globalization.CultureInfo]::InvariantCulture)
        $requestPath = "$root/requests/opd-$id-x$factor.json"
        $request | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $requestPath -Encoding utf8
        @{id="opd-$id-x$factor";request=$requestPath;output="$root/opd-$id-x$factor"}
    }
}
ConvertTo-Json -InputObject @($jobs) -Depth 8 | Set-Content "$root/$JobFile" -Encoding utf8
