param([Parameter(Mandatory)][string]$Study)
$ErrorActionPreference='Stop'
$study=(Resolve-Path -LiteralPath $Study).Path
$jobs=@(Get-Content -LiteralPath "$study/native-jobs.json" -Raw | ConvertFrom-Json)
$measurements=@(foreach($job in $jobs) {
    $request=Get-Content -LiteralPath $job.request -Raw|ConvertFrom-Json
    $receipt=Get-Content -LiteralPath "$($job.output)/batch-result.json" -Raw|ConvertFrom-Json
    $model=Get-Content -LiteralPath "$($job.output)/model.json" -Raw|ConvertFrom-Json
    $capture=Get-Content -LiteralPath "$($job.output)/point-reference.json" -Raw|ConvertFrom-Json
    $ray=@(Get-Content -LiteralPath "$($job.output)/image-geometry.json" -Raw|ConvertFrom-Json)
    if ($receipt.exitCode -ne 0 -or $capture.inputSha256 -ne $request.inputSha256 -or
        $capture.wavelength -ne $request.wavelength -or !$capture.imageGeometryOnly -or
        $ray.Count -ne $capture.inputs.Count -or $model.units.LensUnits -ne 'Millimeters') {throw "Invalid native capture $($job.id)"}
    if ($model.fields.data | Where-Object { $_.VDX -ne 0 -or $_.VDY -ne 0 -or $_.VCX -ne 0 -or $_.VCY -ne 0 -or $_.VAN -ne 0 }) {
        throw "Nonzero native field vignetting factors on $($job.id)"
    }
    $density=[int]($job.id.Split('-')[-1])
    foreach ($field in @($capture.inputs.hy | Sort-Object -Unique)) {
        $accepted=[Collections.Generic.List[object]]::new()
        $byGrid=@{}
        $count=0
        for ($i=0; $i -lt $ray.Count; $i++) {
            if ($ray[$i].number -ne $i+1) {throw 'Native ray order differs'}
            $p=$capture.inputs[$i]
            if ($p.hy -ne $field) {continue}
            $count++
            $r=$ray[$i]
            if ($r.error -ne 0 -or $r.vignette -ne 0 -or $r.intensity -le 0) {continue}
            if ([math]::Abs($r.l*$r.l+$r.m*$r.m+$r.n*$r.n-1) -gt 1e-10) {throw 'Direction is not normalized'}
            $accepted.Add($r)
            $x=[int][math]::Round($p.px*$density)
            $y=[int][math]::Round($p.py*$density)
            $byGrid["$x,$y"]=$r
        }
        if ($accepted.Count -lt 3) {throw 'Insufficient accepted pupil nodes'}
        $chief=$byGrid['0,0']
        if (!$chief) {throw 'No accepted reference chief'}
        $maxDirectionDifference=0.0
        foreach ($item in $accepted) {
            $distance=[math]::Sqrt([math]::Pow($item.l-$chief.l,2)+[math]::Pow($item.m-$chief.m,2)+[math]::Pow($item.n-$chief.n,2))
            $maxDirectionDifference=[math]::Max($maxDirectionDifference,$distance)
        }
        $largestCellArea=0.0
        for($y=-$density;$y -lt $density;$y++){
            for($x=-$density;$x -lt $density;$x++){
                $a=$byGrid["$x,$y"]
                $b=$byGrid["$($x+1),$y"]
                $c=$byGrid["$x,$($y+1)"]
                if (!$a -or !$b -or !$c) {continue}
                $area=0.5*[math]::Abs(($b.l-$a.l)*($c.m-$a.m)-($b.m-$a.m)*($c.l-$a.l))
                $largestCellArea=[math]::Max($largestCellArea,$area)
            }
        }
        [pscustomobject]@{
            index=[int]($job.id.Split('-')[0]);configuration=$request.configuration;normalizedFieldY=$field;density=$density;
            pupilNodes=$count;acceptedRays=$accepted.Count;chiefCosines=@($chief.l,$chief.m,$chief.n);
            maxDirectionDifference=$maxDirectionDifference;largestAcceptedProjectedTriangle=$largestCellArea;
            inputSha256=$capture.inputSha256;nativeRayPayloadSha256=(Get-FileHash -LiteralPath "$($job.output)/image-geometry.json").Hash.ToLowerInvariant()
        }
    }
})
$measurements|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$study/angular-support.json" -Encoding utf8
