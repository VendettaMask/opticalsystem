#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($EvidenceRoot)
$runs=@(Get-Content -Raw -LiteralPath (Join-Path $root 'formal-runs.json')|ConvertFrom-Json)
$finals=@(Get-Content -Raw -LiteralPath (Join-Path $root 'formal-finalists.json')|ConvertFrom-Json)
$trials=@(Get-Content -Raw -LiteralPath (Join-Path $root 'formal-trials.json')|ConvertFrom-Json)
$archive=@(Get-Content -Raw -LiteralPath (Join-Path $root 'formal-qualified-archive.json')|ConvertFrom-Json)
if($runs.Count -ne 90){throw 'Expected all ninety formal runs'}
function Median([double[]]$numbers){
    if($numbers.Length -eq 0){return $null}; $ordered=@($numbers|Sort-Object)
    if($ordered.Length%2){return $ordered[[int][Math]::Floor($ordered.Length/2)]}
    return ($ordered[$ordered.Length/2-1]+$ordered[$ordered.Length/2])/2
}
$summary=foreach($case in @('competition-8-F8-full25','competition-8-F4-full25','original-half21')){
    foreach($arm in @('fixed','direct','recover','compete','power','combined')){
        $group=@($runs|Where-Object{$_.case -eq $case -and $_.arm -eq $arm})
        $assessments=@($finals|Where-Object{$_.case -eq $case -and $_.arm -eq $arm -and -not $_.inheritedParent})
        $branches=@($trials|Where-Object{$_.case -eq $case -and $_.arm -eq $arm})
        $added=@($archive|Where-Object{$_.case -eq $case -and $_.arm -eq $arm -and $null -ne $_.parentId})
        $complete=@($assessments|Where-Object completed)
        $allMetrics=@($complete|Where-Object allFieldsHaveMetrics)
        [pscustomobject]@{
            Case=$case;Arm=$arm;Runs=$group.Count;InheritedQualifiedRuns=@($group|Where-Object{$_.inheritedQualified -gt 0}).Count
            NewQualifiedRuns=@($group|Where-Object{$_.newQualified -gt 0}).Count;NewQualifiedDesigns=($group.newQualified|Measure-Object -Sum).Sum
            StructuralQualifiedDesigns=($group.newStructuralQualified|Measure-Object -Sum).Sum
            CompletedFinals=$complete.Count;IncompleteFinals=@($assessments|Where-Object{-not $_.completed}).Count
            MedianCurrentSeconds=Median @($group|ForEach-Object{[TimeSpan]::Parse($_.current.elapsed).TotalSeconds})
            MedianEndToEndSeconds=Median @($group|ForEach-Object{[TimeSpan]::Parse($_.endToEnd.elapsed).TotalSeconds})
            MedianEndToEndRays=Median @($group.endToEnd.rays)
            MedianFunctions=Median @($group.endToEnd.functions);MedianGradients=Median @($group.endToEnd.gradients)
            FirstNewQualifiedMedianSeconds=if($added.Count){Median @($group|Where-Object{$null -ne $_.firstNewQualifiedElapsed}|ForEach-Object{[TimeSpan]::Parse($_.firstNewQualifiedElapsed).TotalSeconds})}else{$null}
            MinimumNewQualifiedElements=if($added.Count){($added.complexity.elements|Measure-Object -Minimum).Minimum}else{$null}
            BestNewQualifiedRmsMillimeters=if($added.Count){($added.worstRmsMillimeters|Measure-Object -Minimum).Minimum}else{$null}
            CompletedWithAllFieldMetrics=$allMetrics.Count
            BestCompleteWorstRmsMillimeters=if($allMetrics.Count){($allMetrics.worstRmsMillimeters|Measure-Object -Minimum).Minimum}else{$null}
            BestCompleteMinimumValidFraction=if($complete.Count){($complete.minimumValidFraction|Measure-Object -Maximum).Maximum}else{$null}
            Branches=$branches.Count;Recovered=@($branches|Where-Object recovery -eq Recovered).Count
            PowerAttempts=@($branches|Where-Object{$null -ne $_.power}).Count
            PowerClosures=@($branches|Where-Object{$_.power.outcome -eq 'AllocatedPowersAndOriginalTargetsClosed'}).Count
        }
    }
}
$outcomes=@($trials|Group-Object case,arm,operation,outcome|ForEach-Object{[pscustomobject]@{Group=$_.Name;Count=$_.Count}})
$summary|ConvertTo-Json -Depth 12|Set-Content (Join-Path $root 'formal-summary.json') -Encoding utf8
$outcomes|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'formal-outcomes.json') -Encoding utf8
$summary|Format-Table Case,Arm,InheritedQualifiedRuns,NewQualifiedRuns,StructuralQualifiedDesigns,CompletedFinals,IncompleteFinals,MedianEndToEndRays,MedianCurrentSeconds -AutoSize
