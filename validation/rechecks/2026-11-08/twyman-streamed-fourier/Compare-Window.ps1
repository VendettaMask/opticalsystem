[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Folder,
    [Parameter(Mandatory)] [string] $Archive,
    [string] $FormalResults
)
$ErrorActionPreference='Stop'
function Get-Native([int] $Sampling) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $entry = $zip.GetEntry("2157-$Sampling/data.json")
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $data = $reader.ReadToEnd() | ConvertFrom-Json -Depth 20 }
        finally { $reader.Dispose() }
        $grid = $data.dataGrids[0]
        if ($grid.nx -ne 128 -or $grid.ny -ne 128 -or $grid.dx -ne .25 -or $grid.dy -ne .25 -or
            $grid.minX -ne -15.75 -or $grid.minY -ne -15.75 -or @($data.messages).Count -ne 0) {
            throw 'Native grid settings, bounds or diagnostics differ'
        }
        return ,$grid.values
    }
    finally { $zip.Dispose() }
}
function Compare-Grid([string] $Label, $Sample, $Reference) {
    $sum = 0.0
    $max = 0.0
    $peak = 0.0
    $pointwise = $true
    for ($row = 0; $row -lt 128; $row++) {
        for ($column = 0; $column -lt 128; $column++) {
            $a = [double]$Sample[$row][$column]
            $b = [double]$Reference[$row][$column]
            if (-not [double]::IsFinite($a) -or -not [double]::IsFinite($b)) { throw 'Nonfinite grid' }
            $difference = [Math]::Abs($a-$b)
            $peak = [Math]::Max($peak,[Math]::Abs($b))
            $sum += $difference*$difference
            $max = [Math]::Max($max,$difference)
            $pointwise = $pointwise -and ($difference -le 1e-5 + .001 * [Math]::Abs($b))
        }
    }
    $nrmse = [Math]::Sqrt($sum / 16384) / [Math]::Max($peak,1e-5)
    [ordered]@{label=$Label; maxAbsolute=$max; nrmse=$nrmse; fullWindow=$true; pointwise=$pointwise; pass=$pointwise -or $nrmse -le .01}
}
$native256 = Get-Native 256
$native512 = Get-Native 512
$results=@(Compare-Grid 'Native 256 vs 512' $native256 $native512)
$previous = $null
foreach ($path in Get-ChildItem -LiteralPath $Folder -Filter 'psf-*.json' | Sort-Object { [int]($_.BaseName -replace 'psf-','') }) {
    $data = Get-Content -LiteralPath $path.FullName -Raw | ConvertFrom-Json -Depth 12
    $case = Compare-Grid "Core $($data.count) vs native 512" $data.image $native512
    $case['corePhaseStepWaves'] = $data.maximumStep
    $case['corePhaseGate'] = $data.maximumStep -le .5
    $results += $case
    if ($previous) { $results += Compare-Grid "Core $($previous.count) vs Core $($data.count)" $previous.image $data.image }
    $previous = $data
}
if ($FormalResults) {
    $formal = Get-Content -LiteralPath $FormalResults -Raw | ConvertFrom-Json -Depth 30
    $report = $formal.Values.FourierSamplingValidation
    $streamed = @($report.Steps | Where-Object UsesStreamedPhysicalPupil)
    if ($formal.Outcome -cne 'Success' -or !$report.Validated -or $streamed.Count -ne 2 -or
        @($streamed | Where-Object { -not $_.PhaseResolved }).Count -ne 0 -or $formal.axes.Count -ne 128 -or
        $formal.image.Count -ne 128 -or $formal.Values.ImageDeltaMicrometers -ne .25) {
        throw 'Formal replay did not preserve the verified physical window and two resolved tiers'
    }
    for ($i = 0; $i -lt 128; $i++) {
        if ($formal.axes[$i] -ne ($i - 63) * .25 -or $formal.image[$i].Count -ne 128) {
            throw 'Formal physical coordinates or grid dimensions differ'
        }
    }
    $metric = Compare-Grid "Formal adaptive $($report.ReturnedPupilSampling) vs native 512" $formal.image $native512
    if (!$metric.pass) { throw 'Formal replay differs from the converged native reference' }
    $results += $metric
    if ($previous -and $previous.count -eq $report.ReturnedPupilSampling) {
        $results += Compare-Grid "Formal adaptive $($report.ReturnedPupilSampling) vs diagnostic $($previous.count)" $formal.image $previous.image
    }
}
$results | ConvertTo-Json -Depth 8
