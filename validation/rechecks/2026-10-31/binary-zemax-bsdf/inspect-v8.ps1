$ErrorActionPreference='Stop'
$bsdfInventory=Get-Content -Raw validation/rechecks/2026-10-31/binary-zemax-bsdf/installed-speos-inventory.json | ConvertFrom-Json
$bsdfP=($bsdfInventory | Where-Object { $_.Header -match '^OPTIS.*brdf surface file v8.0\n1' } | Sort-Object Length | Select-Object -First 1).Path
$bsdfRaw=[IO.File]::ReadAllBytes($bsdfP); $bsdfStream=[IO.MemoryStream]::new($bsdfRaw); $bsdfStream.Position=[Array]::IndexOf($bsdfRaw,[byte]10)+2; $bsdfReader=[IO.BinaryReader]::new($bsdfStream)
function Read-Packed { $rawLen=$bsdfReader.ReadInt32(); $packedLen=$bsdfReader.ReadInt32(); $zbytes=$bsdfReader.ReadBytes($packedLen); $zs=[IO.Compression.ZLibStream]::new([IO.MemoryStream]::new($zbytes),[IO.Compression.CompressionMode]::Decompress); $dst=[IO.MemoryStream]::new(); $zs.CopyTo($dst); $zs.Dispose(); $out=$dst.ToArray(); if($out.Length -ne $rawLen){throw 'size'}; return ,$out }
function Read-Text { $n=$bsdfReader.ReadInt32(); $b=Read-Packed; "text($n)="+[Text.Encoding]::UTF8.GetString($b) }
"prefix: $($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()),$($bsdfReader.ReadByte())"
Read-Text; Read-Text
"Flags: $($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32())"
"dimensions $($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32())"
$b=Read-Packed; 'inc='+(@(for($i=0;$i -lt $b.Length;$i+=8){[BitConverter]::ToDouble($b,$i)}) -join ',')
$b=Read-Packed; 'wave='+(@(for($i=0;$i -lt $b.Length;$i+=8){[BitConverter]::ToDouble($b,$i)}) -join ',')
"pos=$($bsdfStream.Position); nextDouble=$($bsdfReader.ReadDouble()); thenDims=$($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()); pos=$($bsdfStream.Position)"
"nextInts: $($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32()),$($bsdfReader.ReadInt32())"
