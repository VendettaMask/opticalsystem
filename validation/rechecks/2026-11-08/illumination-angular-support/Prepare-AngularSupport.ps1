param([Parameter(Mandatory)][string]$PreviousStudy,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$previous=(Resolve-Path -LiteralPath $PreviousStudy).Path
if (Test-Path -LiteralPath $Output) {throw 'Expected a fresh output directory'}
$root=(New-Item -ItemType Directory -Path $Output).FullName
$rows=Get-Content -LiteralPath "$previous/rows.json" -Raw|ConvertFrom-Json
$models=Get-Content -LiteralPath "$previous/models.json" -Raw|ConvertFrom-Json
$jobs=@(foreach($index in 1146,1168,2166,2210) {
    $row=$rows[$index-1]
    $original=Get-Content -LiteralPath "$($row.legacyPath)/native/captured-settings.json" -Raw|ConvertFrom-Json -AsHashtable
    foreach ($density in 9,17) {
        $request=$original.request.Clone()
        $request.inputSha256=(Get-FileHash -LiteralPath $request.input).Hash.ToLowerInvariant()
        if ($request.inputSha256 -ne $models[$row.lensIndex-1].sourceSha256 -or $request.configuration -ne $row.configuration) {
            throw 'Prescription or configuration differs from original'
        }
        $request.adapter='point-comparison'
        $request.pointKind='relative-illumination-angular-support'
        $request.originalKeys=@('Relative Illumination')
        $request.imageGeometryOnly=$true
        $request.captureScreenshots=$false
        $request.pointInputs=@(foreach($hy in @($row.run.request.definedFields | ForEach-Object {
            if ($row.run.request.maximumFieldRadius -eq 0) {0.0} else {$_[1] / $row.run.request.maximumFieldRadius}
        })) {
            for ($iy=-$density; $iy -le $density; $iy++) {
                for ($ix=-$density; $ix -le $density; $ix++) {
                    if ($ix*$ix+$iy*$iy -gt $density*$density) {continue}
                    @{hx=0.0;hy=$hy;px=$ix/$density;py=$iy/$density}
                }
            }
        })
        $id="$index-$density"
        $path=Join-Path $root "$id.json"
        $request|ConvertTo-Json -Depth 25|Set-Content -LiteralPath $path -Encoding utf8
        @{id=$id;request=$path;output=(Join-Path $root $id)}
    }
})
$jobs|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'native-jobs.json') -Encoding utf8
