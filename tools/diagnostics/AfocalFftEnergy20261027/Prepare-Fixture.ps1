param([string]$Root='artifacts/validation/afocal-fft-energy-20261027')
$ErrorActionPreference='Stop'
$out='validation/zemax/2026-r1/afocal-fft-energy-2026-10-27'
if(Test-Path $out){throw 'Preserve existing fixtures'}
New-Item -ItemType Directory -Path $out|Out-Null
$cases=@(foreach($r in (Get-Content "$Root/target-items.json" -Raw|ConvertFrom-Json)){
    $curves=@()
    if($r.key -eq 'FFT PSF Cross Section'){
        $path=Join-Path $r.native 'data.txt'
        $rows=@(foreach($line in [IO.File]::ReadAllLines($path)){
            $m=[regex]::Match($line,'^\s*(\d+)\s+([-+\d.eE]+)\s+([-+\d.eE]+)\s*$')
            if($m.Success){@{index=[int]$m.Groups[1].Value;x=[double]::Parse($m.Groups[2].Value,[cultureinfo]::InvariantCulture);y=[double]::Parse($m.Groups[3].Value,[cultureinfo]::InvariantCulture)}}
        })
        if($rows.Count -ne 201){throw 'Unexpected frozen profile grid'}
        for($i=0;$i -lt $rows.Count;$i++){if($rows[$i].index -ne $i){throw 'Profile index mismatch'}}
        $curves=@(@{x=@($rows.x);y=@($rows.y)})
    }else{
        $path=Join-Path $r.native 'data.json'
        $native=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $curves=@(foreach($s in $native.dataSeries){@{x=$s.x;y=@($s.y|ForEach-Object {$_[0]})}})
        if($curves.Count -ne 2){throw 'Expected actual and ideal energy curves'}
    }
    @{id=$r.id;lens=$r.lens;configuration=$r.configuration;key=$r.key;request=$r.request;nativePath=$path;nativeSha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();curves=$curves}
})
if($cases.Count -ne 10){throw 'Expected five configurations and two analyses'}
@{contract='AfocalFftAspectAndEnergyIntervalV1';nativeReferenceBytesUnchanged=$true;cases=$cases}|ConvertTo-Json -Depth 50|Set-Content "$out/curves.json" -Encoding utf8
