param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/paraxial-phase-native-20261031')
$ErrorActionPreference='Stop'
$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null
$jobs=@()
foreach($kind in 'infinite','finite','aberrated'){
 foreach($mode in 0..3){
    $id="$kind-mode$mode"
    $distance=if($kind -eq 'finite'){'200'}else{'INFINITY'}
    $fieldType=if($kind -eq 'finite'){1}else{0}
    $curvature=if($kind -eq 'aberrated'){'.02'}else{'0'}
    $glassLine=if($kind -eq 'aberrated'){'GLAS N-BK7'}else{''}
    $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
GCAT SCHOTT
ENPD 10
FTYP $fieldType 0 2 1 0 0 0
XFLN 0 0
YFLN 0 5
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ $distance
SURF 1
 STOP
 TYPE STANDARD
 CURV $curvature
 $glassLine
 DISZ 5
 DIAM 20 1 0 0 1 ""
SURF 2
 TYPE PARAXIAL
 CURV 0
 $glassLine
 PARM 1 100
 PARM 2 $mode
 DISZ 100
 DIAM 20 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
"@
    $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source)
    $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    $ops=foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
        [ordered]@{code=$code;int1=$surf;int2=1;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
    }}}}}
    $request=[ordered]@{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash
        configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false
        formalAcceptanceRequest=$false;modelOperands=@($ops)}
    $request|ConvertTo-Json -Depth 16|Set-Content -LiteralPath "$root/$id.request.json" -Encoding utf8
    $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
 }
}
$jobs|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$root/jobs.json" -Encoding utf8
