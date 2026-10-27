param([string]$Root='artifacts/validation/afocal-reference-focus-20261027')
$ErrorActionPreference='Stop'
$rows=Get-Content -LiteralPath "$Root/chief-controls-v2/items.json" -Raw|ConvertFrom-Json -AsHashtable
$cases=@()
foreach($row in $rows|Where-Object {$_.lens -like '*Rifle*' -or $_.lens -like '*Binoc*'}){
    $directory="$Root/native-centroid-rays/$($row.id)"
    $request=Get-Content -LiteralPath "$directory/request.json" -Raw|ConvertFrom-Json
    $rays=Get-Content -LiteralPath "$directory/image-geometry.json" -Raw|ConvertFrom-Json
    if($rays.Count -ne $request.pointInputs.Count){throw 'Incomplete explicit native trace'}
    $scale=switch($row.request.nativeAfocalAngleUnit){'ArcMinute'{[Math]::PI/10800*1000};'Milliradian'{1.};default{throw 'Undeclared angular unit'}}
    $radius=5*$scale
    $fields=@()
    for($field=1;$field -le $row.request.fieldCount;$field++){
        $points=@()
        for($i=0;$i -lt $rays.Count;$i++){
            if($request.pointInputs[$i].fieldNumber -ne $field){continue}
            $ray=$rays[$i]
            if($ray.error -ne 0 -or $ray.vignette -ne 0){continue}
            if([Math]::Abs($ray.intensity-1) -gt 1e-12){throw 'This independent uniform-pupil control requires unit native weights'}
            $points+=@{x=[Math]::Atan2($ray.l,$ray.n)*1000;y=[Math]::Atan2($ray.m,$ray.n)*1000}
        }
        if($points.Count -ne 797){throw 'Captured uniform pupil differs from the declared 32-interval disk'}
        $cx=($points|ForEach-Object {$_.x}|Measure-Object -Average).Average
        $cy=($points|ForEach-Object {$_.y}|Measure-Object -Average).Average
        $radii=@($points|ForEach-Object {[Math]::Sqrt(($_.x-$cx)*($_.x-$cx)+($_.y-$cy)*($_.y-$cy))})
        # Independent empirical CDF, before the product's display spline.
        # No product analysis or interpolation code supplies native expected values.
        $knots=@(for($i=0;$i -lt 99;$i++){
            $x=$radius*$i/99
            @{x=$x;y=@($radii|Where-Object {$_ -le $x}).Count/[double]$points.Count}
        })
        $fields+=@{field=$field;rayCount=$points.Count;centroidMilliradian=@($cx,$cy);knots=$knots}
    }
    $row.request.reference='Centroid';$row.request.workbenchSettings.Reference='centroid';$row.request.zemaxSettings.ReferTo='Centroid'
    $cases+=@{id=$row.id;lens=$row.lens;configuration=$row.configuration;request=$row.request;fields=$fields;
        nativeImageGeometrySha256=(Get-FileHash -LiteralPath "$directory/image-geometry.json").Hash.ToLowerInvariant();
        nativeRequestSha256=(Get-FileHash -LiteralPath "$directory/request.json").Hash.ToLowerInvariant();
        sourceSha256=$request.inputSha256}
}
$out='validation/zemax/2026-r1/afocal-reference-focus-2026-10-27/angular-centroid-energy.json'
if(Test-Path -LiteralPath $out){throw 'Never overwrite a native-derived fixture'}
@{contract='NativeExplicitRayAngularCentroidV1';zemaxBuild=260127;angleUnit='Milliradian';pupilIntervals=32;builtInPlotReplaced=$false;cases=$cases}|ConvertTo-Json -Depth 90|Set-Content -LiteralPath $out -Encoding utf8
