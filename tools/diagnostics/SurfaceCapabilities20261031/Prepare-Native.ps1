param([string]$Repository = 'D:/Projects/opticalsystem', [string]$Study = 'artifacts/validation/surface-model-controls-20261031')
$ErrorActionPreference = 'Stop'
$folder = Join-Path $Repository $Study
if (Test-Path -LiteralPath $folder) { throw 'Fresh study required' }
New-Item -ItemType Directory -Path $folder | Out-Null
$jobs = @()
function Add-Job([string]$id, [string]$source, [object[]]$operands) {
    $path = Join-Path $folder "$id.ZMX"
    [IO.File]::WriteAllText($path, $source)
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $request = [ordered]@{ zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio'; input=$path; inputSha256=$hash
        configuration=1; wavelength=1; zemaxVersion='2026 R1'; adapter='surface-model-controls'
        captureScreenshots=$false; formalAcceptanceRequest=$false; modelOperands=$operands }
    $request | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder "$id.request.json") -Encoding utf8
    $script:jobs += [ordered]@{id=$id; request="$id.request.json"; inputSha256=$hash; budgetSeconds=120}
}
foreach ($mode in 0..3) {
    $source = @"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
NAME Paraxial phase definition control
ENPD 20
FTYP 0 0 2 1 0 0 0
XFLN 0 0
YFLN 0 5
WAVM 1 0.55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ INFINITY
SURF 1
 STOP
 TYPE PARAXIAL
 CURV 0
 PARM 1 100
 PARM 2 $mode
 DIAM 20 1 0 0 1 ""
 DISZ 100
SURF 2
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
"@
    $operands = foreach ($hy in 0,1) { foreach ($py in -.8,-.4,0,.4,.8) { foreach ($surf in 1,2) {
        [ordered]@{code='OPTH';int1=$surf;int2=1;data1=0;data2=$hy;data3=0;data4=$py;data5=0;data6=0}
    } } }
    Add-Job "paraxial-mode$mode" $source $operands
}
$source = @'
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 2
FTYP 0 0 1 1 0 0 0
XFLN 0
YFLN 0
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ INFINITY
SURF 1
 STOP
 TYPE FZERNSAG
 CURV .01
 CONI -.5
 PARM 0 1
 PARM 1 .00002
 PARM 9 .1
 PARM 10 -.2
 XDAT 1 8
 XDAT 2 2
 XDAT 3 .01
 XDAT 4 .02
 XDAT 5 -.03
 XDAT 6 .04
 XDAT 7 -.05
 XDAT 8 .06
 XDAT 9 -.07
 XDAT 10 .08
 DISZ 10
 DIAM 3 1 0 0 1 ""
SURF 2
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
'@
$operands = foreach($x in -.6,0,.5){foreach($y in -.8,0,.25){
    [ordered]@{code='SSAG';int1=1;int2=1;data1=$x;data2=$y;data3=0;data4=0;data5=0;data6=0}
    foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC') {
        [ordered]@{code=$code;int1=1;int2=1;data1=0;data2=0;data3=$x;data4=$y;data5=0;data6=0}
    }
}}
Add-Job 'fringe-nonzero' $source $operands
foreach ($entry in @(
    @{id='chebyshev-oap'; lens='Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx';surface=3;positions=@(0,-60,-80)},
    @{id='fringe-machzender'; lens='Optical testing/Mach Zender Interferometer.zmx';surface=11;positions=@(0,.5,2)}
)) {
    $source = Get-Content -LiteralPath (Join-Path $Repository "src/OpticalSystemDesign.App/Assets/SequentialExamples/$($entry.lens)") -Raw
    $operands = foreach ($x in 0,1,2) { foreach ($y in $entry.positions) {
        [ordered]@{code='SSAG';int1=$entry.surface;int2=1;data1=$x;data2=$y;data3=0;data4=0;data5=0;data6=0}
    } }
    Add-Job $entry.id $source $operands
}
$jobs | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder 'jobs.json') -Encoding utf8
