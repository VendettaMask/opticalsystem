param([string]$Root = 'artifacts/validation/issue-repair-20261008/vignetting-native')
$ErrorActionPreference = 'Stop'
$taskRows = foreach ($taskControl in @('negative-02','mixed','asymmetric')) {
    $taskNative = Get-Content -LiteralPath "$Root/$taskControl/native/ray-audit.json" -Raw | ConvertFrom-Json
    $taskCore = Get-Content -LiteralPath "$Root/$taskControl/core.json" -Raw | ConvertFrom-Json
    foreach ($taskField in 0..15) {
        $taskOld = $taskCore[$taskField].vignetting
        $taskRays = $taskCore[$taskField].physical
        $taskProjected = foreach ($taskRay in $taskRays) {
            $taskOrigin = $taskRay.launch.origin; $taskDirection = $taskRay.launch.direction
            [pscustomobject]@{ x=$taskOrigin.x-$taskOrigin.z*$taskDirection.x/$taskDirection.z; y=$taskOrigin.y-$taskOrigin.z*$taskDirection.y/$taskDirection.z }
        }
        $taskNativeRays = @($taskNative.launches | Where-Object fieldIndex -eq $taskField)
        $taskInputs = @($taskNative.inputs | Where-Object fieldIndex -eq $taskField)
        $taskP = $taskInputs[0].px
        $taskRadius = [Math]::Sqrt([Math]::Pow($taskProjected[0].x-$taskProjected[2].x,2)+[Math]::Pow($taskProjected[0].y-$taskProjected[2].y,2))/(2*$taskP*(1-$taskOld.compressionX))
        $taskSin=[Math]::Sin($taskOld.angleDegrees*[Math]::PI/180); $taskCos=[Math]::Cos($taskOld.angleDegrees*[Math]::PI/180)
        $taskTx=($taskOld.decenterX+$taskP*(1-$taskOld.compressionX))*$taskCos-$taskOld.decenterY*$taskSin
        $taskTy=($taskOld.decenterX+$taskP*(1-$taskOld.compressionX))*$taskSin+$taskOld.decenterY*$taskCos
        $taskBaseX=$taskProjected[0].x-$taskRadius*$taskTx; $taskBaseY=$taskProjected[0].y-$taskRadius*$taskTy
        $taskXx=($taskNativeRays[0].x-$taskNativeRays[2].x)/(2*$taskP*$taskRadius)
        $taskXy=($taskNativeRays[0].y-$taskNativeRays[2].y)/(2*$taskP*$taskRadius)
        $taskYx=($taskNativeRays[1].x-$taskNativeRays[3].x)/(2*$taskP*$taskRadius)
        $taskYy=($taskNativeRays[1].y-$taskNativeRays[3].y)/(2*$taskP*$taskRadius)
        $taskAngle=[Math]::Atan2($taskXy,$taskXx)
        $taskCenterX=(($taskNativeRays[0].x+$taskNativeRays[2].x)/2-$taskBaseX)/$taskRadius
        $taskCenterY=(($taskNativeRays[0].y+$taskNativeRays[2].y)/2-$taskBaseY)/$taskRadius
        [pscustomobject]@{control=$taskControl;hy=$taskCore[$taskField].hy;radius=$taskRadius;
            decenterX=$taskCenterX*[Math]::Cos($taskAngle)+$taskCenterY*[Math]::Sin($taskAngle);
            decenterY=-$taskCenterX*[Math]::Sin($taskAngle)+$taskCenterY*[Math]::Cos($taskAngle);
            compressionX=1-[Math]::Sqrt($taskXx*$taskXx+$taskXy*$taskXy);
            compressionY=1-[Math]::Sqrt($taskYx*$taskYx+$taskYy*$taskYy);
            angleDegrees=$taskAngle*180/[Math]::PI}
    }
}
$taskRows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$Root/observed-factors.json" -Encoding utf8
$taskRows | Format-Table control,hy,decenterX,decenterY,compressionX,compressionY,angleDegrees
