param([string]$Root='artifacts/validation/afocal-sampling-20261027')
$ErrorActionPreference='Stop'
$out='validation/zemax/2026-r1/afocal-pupil-scale-2026-10-27'
if(Test-Path $out){throw 'Use fresh fixture output'}
New-Item -ItemType Directory -Path $out|Out-Null
$cases=@(foreach($r in (Get-Content "$Root/scaling-v3/items.json" -Raw|ConvertFrom-Json|Where-Object {$_.Lens -eq 'Miscellaneous/Apochromat_prism.zmx' -and $_.Key -in 'PSF','Huygens PSF'})){
    $path=Join-Path $r.Native 'data.json'
    $native=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    @{id=$r.Id;lens=$r.Lens;request=$r.Request;referencePath=$path;referenceSha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();grid=$native.dataGrids[0]}
})
@{contract='AfocalTangentialPupilScaleV1';nativeVersion='OpticStudio 2026 R1';originalReferenceBytesUnchanged=$true;cases=$cases}|ConvertTo-Json -Depth 50|Set-Content "$out/automatic-psf.json" -Encoding utf8
