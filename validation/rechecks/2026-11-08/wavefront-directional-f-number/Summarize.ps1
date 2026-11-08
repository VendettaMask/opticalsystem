
param([string]$Work='D:/Projects/.precision-mems-20261108')
$ErrorActionPreference='Stop'
$jobs=Get-Content "$Work/native-jobs.json" -Raw | ConvertFrom-Json
function NumA($chief,$ray) {
    if($ray.error -ne 0) { return $null }
    $dx=$chief.m*$ray.n-$chief.n*$ray.m
    $dy=$chief.n*$ray.l-$chief.l*$ray.n
    $dz=$chief.l*$ray.m-$chief.m*$ray.l
    [math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)
}
function Prediction($minus,$plus,$zone) {
    if($null -eq $minus -or $null -eq $plus) { return $null }
    function F($na) { if($na -eq 0) { 1e4 } else { [math]::Min(1e4, $zone/(2*$na)) } }
    $nonzero=@($minus,$plus | Where-Object {$_ -gt 1e-12})
    $meanNonzero=if($nonzero.Count) { ($nonzero | Measure-Object -Average).Average } else { 0 }
    [ordered]@{minus=(F $minus);plus=(F $plus);arithmetic=(F (($minus+$plus)/2))
        rms=(F ([math]::Sqrt(($minus*$minus+$plus*$plus)/2)));maximum=(F ([math]::Max($minus,$plus)))
        nonzeroArithmetic=(F $meanNonzero)}
}
$results=@(foreach($job in $jobs) {
    $out=$job.output
    if(!(Test-Path "$out/model-controls.json")) { continue }
    $status=Get-Content "$out/batch-result.json" -Raw | ConvertFrom-Json
    if($status.exitCode -ne 0) { throw "Host error for $($job.id)" }
    $controls=Get-Content "$out/model-controls.json" -Raw | ConvertFrom-Json
    $points=Get-Content "$out/point-reference.json" -Raw | ConvertFrom-Json
    $chief=$points.surfaces[-1].rays[0]
    $marginals=@(foreach($i in 1..12) {
        $ray=$points.surfaces[-1].rays[$i]
        [ordered]@{px=$points.inputs[$i].px;py=$points.inputs[$i].py;error=$ray.error;vignette=$ray.vignette;na=(NumA $chief $ray)}
    })
    $native=@{}
    foreach($row in $controls.rows){$native[$row.input.code]=$row.value}
    $predictions=@(foreach($i in 0,4,8) {
        $zone=[Math]::Abs($marginals[$i].py)
        [ordered]@{
            zone=$zone
            tangential=(Prediction $marginals[$i].na $marginals[$i+1].na $zone)
            sagittal=(Prediction $marginals[$i+2].na $marginals[$i+3].na $zone)
        }
    })
    [ordered]@{id=$job.id;native=$native;chiefError=$chief.error;chiefVignette=$chief.vignette;marginals=$marginals;predictions=$predictions}
})
ConvertTo-Json -InputObject $results -Depth 20 | Set-Content "$Work/predictions.json"
foreach($r in $results) {
    $axes=@('tangential','sagittal')
    for($i=0;$i -lt $axes.Count;$i++){
        $p=$r.predictions[0][$axes[$i]]
        $native=$r.native[@('TFNO','SFNO')[$i]]
        $matches=@(if($null -ne $p){foreach($k in $p.Keys){if([math]::Abs($native-$p[$k]) -lt 1e-7){$k}}})
        '{0} {1}: native={2:R}, minus={3:R}, plus={4:R}, average={5:R}, rms={6:R}, matches={7}' -f $r.id,$axes[$i],$native,$p.minus,$p.plus,$p.arithmetic,$p.rms,($matches -join ',')
    }
    '  working f={0:R}' -f $r.native.WFNO
}

