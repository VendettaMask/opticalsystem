$bsdfInventory=Get-Content -Raw validation/rechecks/2026-10-31/binary-zemax-bsdf/installed-speos-inventory.json | ConvertFrom-Json
$bsdfP=($bsdfInventory | Where-Object { $_.Header -match '^OPTIS.*brdf surface file v8.0\n1' } | Sort-Object Length | Select-Object -First 1).Path
$bsdfRaw=[IO.File]::ReadAllBytes($bsdfP); $bsdfStart=[Array]::IndexOf($bsdfRaw,[byte]10)+2
"File: $bsdfP; payload at $bsdfStart"
for($bsdfOffset=$bsdfStart; $bsdfOffset -lt $bsdfStart+175; $bsdfOffset++){ if($bsdfOffset % 16 -eq 0){""}; '{0}: {1:X2}' -f $bsdfOffset,$bsdfRaw[$bsdfOffset] }
