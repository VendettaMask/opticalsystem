param([string]$EvidenceDirectory, [string]$BeforePath, [string]$AfterPath)
$ErrorActionPreference='Stop'
$before=Get-Content $(if ($BeforePath) { $BeforePath } else { Join-Path $EvidenceDirectory 'before.json' }) -Raw | ConvertFrom-Json
$after=Get-Content $(if ($AfterPath) { $AfterPath } else { Join-Path $EvidenceDirectory 'after.json' }) -Raw | ConvertFrom-Json
$script:maximumNumericDelta=0.0
function Equivalent($a,$b,[double]$tolerance) {
    if ($null -eq $a -or $null -eq $b) { return $null -eq $a -and $null -eq $b }
    if ($a -is [ValueType] -and $b -is [ValueType] -and $a -isnot [bool] -and $b -isnot [bool]) {
        $delta=[Math]::Abs([double]$a-[double]$b)
        $script:maximumNumericDelta=[Math]::Max($script:maximumNumericDelta,$delta)
        return $delta -le $tolerance
    }
    if ($a -is [pscustomobject] -and $b -is [pscustomobject]) {
        foreach($p in $a.PSObject.Properties | Where-Object Name -notin @('Number','Length')) {
            if (!(Equivalent $p.Value $b.($p.Name) $tolerance)) { return $false }
        }
        return $true
    }
    return $a -ceq $b
}
$changes=[Collections.Generic.List[string]]::new()
$configurationCount=0; $restored=0; $validRays=0; $unchangedUnavailableRays=0
for($i=0;$i -lt $after.Count;$i++) {
    if($after[$i].Error -or $before[$i].Error) { $changes.Add("Import error: $($after[$i].Path)"); continue }
    if($after[$i].Sha256 -cne $before[$i].Sha256) { throw 'Source bytes changed.' }
    for($j=0;$j -lt $after[$i].Configurations.Count;$j++) {
        $configurationCount++
        $old=$before[$i].Configurations[$j]; $new=$after[$i].Configurations[$j]
        if ($new.Count -ne $after[$i].SourceCount) { $changes.Add("Row count: $($after[$i].Path), configuration $j") }
        $restored+=$new.Count-$old.Count
        foreach($s in $old.Surfaces) {
            $t=$new.Surfaces | Where-Object Source -eq $s.Source
            if (!(Equivalent $s $t 1e-8)) { $changes.Add("Surface: $($after[$i].Path), configuration $j, source $($s.Source)") }
        }
        for($k=0;$k -lt $old.Rays.Count;$k++) {
            $a=$old.Rays[$k]; $b=$new.Rays[$k]
            if($null -ne $a.Position) { $validRays++ } else { $unchangedUnavailableRays++ }
            $sameError=($a.Error -split ':',2)[0] -ceq ($b.Error -split ':',2)[0]
            if (!(Equivalent $a.Position $b.Position 1e-8) -or !(Equivalent $a.Direction $b.Direction 1e-10) -or !$sameError) {
                $changes.Add("Ray: $($after[$i].Path), configuration $j, pupil $($a.Py)")
            }
        }
    }
}
$result=[ordered]@{
    Scope='Internal import/trace invariants across the 95 menu examples; not new external optical certification.'
    Files=$after.Count; Configurations=$configurationCount; RestoredCoordinateRows=$restored
    ValidControlRays=$validRays; UnavailableControlRays=$unchangedUnavailableRays
    MaximumNumericDelta=$script:maximumNumericDelta
    PositionGuardMillimeters=1e-8; DirectionGuard=1e-10
    Changes=@($changes); Passed=$changes.Count -eq 0
}
$result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $EvidenceDirectory 'comparison.json') -Encoding utf8
$result | ConvertTo-Json -Depth 5
if ($changes.Count -ne 0) { exit 1 }
