$inv=Get-Content -Raw validation/rechecks/2026-10-31/binary-zemax-bsdf/installed-speos-inventory.json|ConvertFrom-Json
foreach($item in @($inv|Where-Object {$_.Header -match '^OPTIS.*Anisotropic.*v(7.0|9.0|11|11.0)\n1'}|Group-Object {($_.Header-split'\n')[0]}|ForEach-Object {$_.Group|Sort-Object Length|Select-Object -First 1})) {
$b=[IO.File]::ReadAllBytes($item.Path);$s=[Array]::IndexOf($b,[byte]10)+2;$st=[IO.MemoryStream]::new($b);$r=[IO.BinaryReader]::new($st);$v=if($item.Header -match 'v11'){11}elseif($item.Header -match 'v9'){9}else{7};$st.Position=$s+9+([int]($v -eq 11))
for($j=0;$j -lt 2;$j++){ $count=$r.ReadInt32(); if($count -lt 0 -or $count -gt 4096){throw "bad text $count"}; if($count -gt 0){$rawSize=$r.ReadInt64();$raw=$r.ReadInt32();$packed=$r.ReadInt32();$st.Position+=$packed} }
[pscustomobject]@{Version=$v;Path=$item.Path;Vector=@($r.ReadDouble(),$r.ReadDouble(),$r.ReadDouble());R=$r.ReadByte();T=$r.ReadByte();N1=$(if($v-ge9){$r.ReadDouble()}else{1});N2=$(if($v-ge9){$r.ReadDouble()}else{1});Proportional=$r.ReadByte();Enhance=$r.ReadByte();NRotation=$r.ReadInt32();NextBytes=[BitConverter]::ToString($b,[int]$st.Position,24)}|ConvertTo-Json -Depth 4
}
