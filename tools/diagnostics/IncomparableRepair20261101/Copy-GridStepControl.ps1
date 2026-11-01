param(
 [Parameter(Mandatory)][string]$Source,
 [Parameter(Mandatory)][string]$Destination,
 [Parameter(Mandatory)][double]$MaximumStep
)
$ErrorActionPreference='Stop'
if(![double]::IsFinite($MaximumStep) -or $MaximumStep -le 0){throw 'A positive finite integration step is required'}
$sourcePath=(Resolve-Path -LiteralPath $Source).Path
if(Test-Path -LiteralPath $Destination){throw 'A numerical control must have a fresh destination'}
$lines=[IO.File]::ReadAllLines($sourcePath)
$gridSurface=$false;$indices=@()
for($i=0;$i -lt $lines.Length;$i++){
 if($lines[$i] -match '^\s*SURF\s+'){ $gridSurface=$false }
 if($lines[$i] -match '^\s*TYPE\s+GRINSU11\s+'){ $gridSurface=$true }
 if($gridSurface -and $lines[$i] -match '^\s*PARM\s+1\s+\S+\s*$'){ $indices+=$i }
}
if($indices.Count -ne 1){throw 'This control requires exactly one explicit Grid GRIN maximum-step parameter'}
$index=$indices[0];$changed=[string[]]$lines.Clone()
$changed[$index]='  PARM 1 '+$MaximumStep.ToString('R',[Globalization.CultureInfo]::InvariantCulture)
if($changed[$index] -eq $lines[$index]){throw 'The requested numerical control would not change the source'}
$destinationPath=[IO.Path]::GetFullPath($Destination)
[IO.File]::WriteAllLines($destinationPath,$changed,[Text.Encoding]::Unicode)
$readback=[IO.File]::ReadAllLines($destinationPath)
$differences=@(for($i=0;$i -lt $lines.Length;$i++){if($readback[$i] -cne $lines[$i]){$i}})
if($readback.Length -ne $lines.Length -or $differences.Count -ne 1 -or $differences[0] -ne $index -or
 $readback[$index] -cne $changed[$index]){throw 'Serialized control must change only the verified maximum-step line'}
[pscustomobject]@{source=$sourcePath;sourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash.ToLowerInvariant()
 input=$destinationPath;inputSha256=(Get-FileHash -LiteralPath $destinationPath).Hash.ToLowerInvariant()
 changedLineCount=1;line=$index+1;before=$lines[$index];after=$readback[$index];maximumStep=$MaximumStep}
