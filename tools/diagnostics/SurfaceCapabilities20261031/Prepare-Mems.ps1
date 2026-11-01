param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/mems-native-controls-20261031')
$ErrorActionPreference='Stop'
$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null
$source=@'
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 10
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
 TYPE USERSURF US_MEMS.DLL
 CURV 0
 PARM 1 4
 PARM 2 4
 PARM 3 10
 PARM 4 10
 PARM 5 0
 PARM 6 -10
 PARM 7 10
 PARM 8 45
 XDAT 1 0
 XDAT 2 5
 GLAS MIRROR
 DISZ -10
 DIAM 10 1 0 0 1 ""
SURF 2
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
'@
$path=Join-Path $root 'mems-rows.ZMX';[IO.File]::WriteAllText($path,$source)
$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
$operands=foreach($px in 0,.5){foreach($py in -.8,-.25,0,.8){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC'){
    [ordered]@{code=$code;int1=1;int2=1;data1=0;data2=0;data3=$px;data4=$py;data5=0;data6=0}
}}}
$request=[ordered]@{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash
    configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false
    formalAcceptanceRequest=$false;memsDllSha256=(Get-FileHash -LiteralPath 'C:/Users/19851/Documents/Zemax/DLL/Surfaces/US_MEMS.DLL').Hash.ToLowerInvariant();modelOperands=@($operands)}
$request|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $root 'mems-rows.request.json') -Encoding utf8
@(@{id='mems-rows';request='mems-rows.request.json';inputSha256=$hash;budgetSeconds=120})|ConvertTo-Json -Depth 12 -AsArray|Set-Content -LiteralPath (Join-Path $root 'jobs.json') -Encoding utf8
