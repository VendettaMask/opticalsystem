. './validation/rechecks/2026-10-31/binary-zemax-bsdf/inspect-v8.ps1' | Out-Null
$inventory=Get-Content -Raw validation/rechecks/2026-10-31/binary-zemax-bsdf/installed-speos-inventory.json|ConvertFrom-Json
$result=foreach($item in $inventory|Where-Object {$_.Header -match '^OPTIS.*brdf.*v8.0\n1'}) {
$bsdfRaw=[IO.File]::ReadAllBytes($item.Path);$bsdfStream=[IO.MemoryStream]::new($bsdfRaw);$bsdfStream.Position=[Array]::IndexOf($bsdfRaw,[byte]10)+11;$bsdfReader=[IO.BinaryReader]::new($bsdfStream)
try { Read-Text|Out-Null;Read-Text|Out-Null; [pscustomobject]@{Name=[IO.Path]::GetFileName($item.Path);Path=$item.Path;Flags=[BitConverter]::ToString($bsdfRaw,[int]$bsdfStream.Position,16);Offset=$bsdfStream.Position} } catch { [pscustomobject]@{Name=$item.Path;Error=$_.Exception.Message} }
}
$result|ConvertTo-Json -Depth 4|Set-Content validation/rechecks/2026-10-31/binary-zemax-bsdf/installed-v8-flags.json
$result|Select-Object -First 10|ConvertTo-Json -Depth 3
