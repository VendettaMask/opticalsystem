param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/gradient9-native-controls-20261031',
    [string]$Lens='GRINs/Selfoc lens.zmx',[string]$CasePrefix='selfoc',[int[]]$Surfaces=@(1,2),[double[]]$Fields=@(0),[int]$IndexSurface=1,[int[]]$Waves=@())
$ErrorActionPreference='Stop'
$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null
$original=Get-Content -LiteralPath (Join-Path $Repository "src/OpticalSystemDesign.App/Assets/SequentialExamples/$Lens") -Raw
$declaredWaves=[int](([regex]::Match($original,'(?m)^FTYP[^\r\n]*').Value.Trim() -split '\s+')[4])
if($Waves.Count -eq 0){$Waves=@(1..$declaredWaves)}
if($Waves|Where-Object {$_ -lt 1 -or $_ -gt $declaredWaves}){throw 'Native controls require declared wavelengths'}
$catalog=Get-Content -LiteralPath (Join-Path $Repository 'src/OpticalSystemDesign.Core/Materials/Data/gradient9-catalog.json') -Raw|ConvertFrom-Json
$jobs=@()
foreach($step in '.1','.05','.025')
{
    $id=$CasePrefix+'-step'+$step.Replace('.','')
    $source=$original -replace '(TYPE GRINSUR9[\s\S]*?PARM 1 )[^\r\n]+',('$1'+$step)
    $path=Join-Path $root ($id+'.ZMX');[IO.File]::WriteAllText($path,$source)
    $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    $operands=@()
    foreach($wave in $Waves)
    {
        $operands += [ordered]@{code='INDX';int1=$IndexSurface;int2=$wave;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}
        foreach($hy in $Fields){foreach($px in 0,.5){foreach($py in -.8,0,.8){foreach($surf in $Surfaces){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC','OPTH'){
            $operands += [ordered]@{code=$code;int1=$surf;int2=$wave;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
        }}}}}
    }
    $request=[ordered]@{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash
        configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false
        formalAcceptanceRequest=$false;gradient9CatalogSha256=$catalog.sourceSha256;modelOperands=$operands}
    $request|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $root ($id+'.request.json')) -Encoding utf8
    $jobs+=@{id=$id;request=$id+'.request.json';inputSha256=$hash;budgetSeconds=120}
}
$jobs|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $root 'jobs.json') -Encoding utf8
