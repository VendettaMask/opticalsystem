param(
    [Parameter(Mandatory)][string]$PreviousStudy,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$previous = (Resolve-Path -LiteralPath $PreviousStudy).Path
if (Test-Path -LiteralPath $Output) { throw 'Expected a fresh output directory' }
$root = (New-Item -ItemType Directory -Path $Output).FullName
$rows = Get-Content -LiteralPath (Join-Path $previous 'rows.json') -Raw | ConvertFrom-Json
$models = Get-Content -LiteralPath (Join-Path $previous 'models.json') -Raw | ConvertFrom-Json
$jobs = [Collections.Generic.List[object]]::new()
function Read-Request([int]$index) {
    $row = $rows[$index - 1]
    if ($row.index -ne $index) { throw 'Unexpected request index' }
    $captured = Get-Content -LiteralPath (Join-Path $row.legacyPath 'native/captured-settings.json') -Raw | ConvertFrom-Json -AsHashtable
    $request = $captured.request
    $sourceHash = (Get-FileHash -LiteralPath $request.input).Hash.ToLowerInvariant()
    if ($sourceHash -ne $models[$row.lensIndex - 1].sourceSha256 -or $request.configuration -ne $row.configuration) {
        throw 'The native source identity or configuration differs from the replay'
    }
    $request.inputSha256 = $sourceHash
    $request.captureScreenshots = $false
    $request
}
function Save-Request([string]$id, $request) {
    $path = Join-Path $root "$id.json"
    $request | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8
    $jobs.Add(@{id = $id; request = $path; output = (Join-Path $root $id)})
}
function Operand([string]$code, [int]$int1, [int]$int2, [double]$data1 = 0) {
    @{code = $code; int1 = $int1; int2 = $int2; data1 = $data1; data2 = 0
      data3 = 0; data4 = 0; data5 = 0; data6 = 0}
}
foreach ($index in 2605,1146,1168,2166,2210,954) {
    $request = Read-Request $index
    $request.adapter = 'surface-model-controls'
    $request.modelOperands = @(if ($index -eq 2605) {
        foreach ($rings in 6,10,16) { Operand RSCE $rings $request.wavelength }
    } else {
        foreach ($density in 10,20,40) {
            for ($field = 1; $field -le $request.fieldCount; $field++) {
                Operand RELI $density $request.wavelength $field
                Operand EFNO $density $request.wavelength $field
            }
        }
    })
    Save-Request "$index-operands" $request
}
foreach ($index in 1070,1246) {
    foreach ($sampling in 64,128,256) {
        $request = Read-Request $index
        $request.pupilSampling = $sampling
        $request.useExitPupil = $false
        Save-Request "$index-entrance-$sampling" $request
    }
    $request = Read-Request $index
    $data = Get-Content -LiteralPath (Join-Path $previous "analyses/$index/data.json") -Raw | ConvertFrom-Json
    if (!$data.values.ProjectedExitPupilGrid -or $data.values.FieldHx -ne 0 -or $data.values.FieldHy -ne 0) {
        throw 'Expected the recorded on-axis projected wavefront'
    }
    $denominator = $request.pupilSampling / 2 - 1
    $request.adapter = 'point-comparison'
    $request.pointKind = 'wavefront-projected-meridian'
    $request.originalKeys = @('Wavefront')
    $request.pointInputs = @(for ($i = -$denominator; $i -le $denominator; $i++) {
        @{hx = 0; hy = 0; px = 0; py = $i / $denominator}
    })
    Save-Request "$index-meridian" $request
    $request = Read-Request $index
    $request.adapter = 'surface-model-controls'
    $request.modelOperands = @(
        Operand SFNO $request.field $request.wavelength
        Operand TFNO $request.field $request.wavelength
    )
    Save-Request "$index-working-f-number" $request
}
$jobs | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'native-jobs.json') -Encoding utf8
