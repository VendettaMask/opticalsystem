param([switch]$Original132)
$ErrorActionPreference='Stop'
$taskRoot='D:\Projects\opticalsystem\artifacts\zemax-standard-samples\20261007\ra256-ray-audit'
$taskRecords=@()
$taskCases=if($Original132){
    $taskSummary=Get-Content 'D:\Projects\opticalsystem\artifacts\zemax-standard-samples\20261007\ra256-single-ray-fresh-import-original132\summary.json' -Raw | ConvertFrom-Json
    @($taskSummary.rows | Group-Object sourceSha256 | ForEach-Object {$taskRow=$_.Group[0]; @{case=$taskRow.lens;before=$taskRow.snapshot;after=$taskRow.executionSnapshot}})
} else {
    @(foreach($taskCase in @('tessar-ra256-remove-streamed','tessar-ra256-retain','cooke-40-degree-field-ra256-remove','double-gauss-28-degree-field-ra256-remove','relay-lens-ra256-remove','even-asphere-ra256-remove')){
        $taskDirectory=Join-Path $taskRoot $taskCase
        @{case=$taskCase;before=(Join-Path $taskDirectory 'snapshot.json');after=(Join-Path $taskDirectory 'current-import-snapshot.json')}
    })
}
foreach($taskModel in $taskCases){
    $taskCase=$taskModel.case
    $taskBefore=Get-Content -LiteralPath $taskModel.before -Raw | ConvertFrom-Json -AsHashtable
    $taskAfter=Get-Content -LiteralPath $taskModel.after -Raw | ConvertFrom-Json -AsHashtable
    $taskDifferences=[Collections.Generic.List[object]]::new()
    function Compare-TaskValues($taskA,$taskB,[string]$taskPath){
        if($null -eq $taskA -and $null -eq $taskB){return}
        if($taskA -is [Collections.IDictionary] -and $taskB -is [Collections.IDictionary]){
            $taskKeys=@($taskA.Keys)+@($taskB.Keys) | Sort-Object -Unique
            foreach($taskKey in $taskKeys){Compare-TaskValues $taskA[$taskKey] $taskB[$taskKey] "$taskPath/$taskKey"}
            return
        }
        if($taskA -is [Collections.IList] -and $taskB -is [Collections.IList]){
            if($taskA.Count -eq $taskB.Count){for($taskIndex=0;$taskIndex -lt $taskA.Count;$taskIndex++){Compare-TaskValues $taskA[$taskIndex] $taskB[$taskIndex] "$taskPath/$taskIndex"};return}
        }
        if(($taskA | ConvertTo-Json -Depth 100 -Compress) -ne ($taskB | ConvertTo-Json -Depth 100 -Compress)){
            $taskDifferences.Add(@{path=$taskPath;before=$taskA;after=$taskB})
        }
    }
    Compare-TaskValues $taskBefore $taskAfter ''
    foreach($taskDifference in $taskDifferences){
        if($taskDifference.path -notmatch '^/surfaces/(\d+)/components/physicalAperture(Kind)?$'){throw "Unexpected imported-model difference: $taskCase/$($taskDifference.path)"}
        $taskSurface=$taskBefore.surfaces[[int]$Matches[1]]
        if(!$taskSurface.isStop -or $taskSurface.semiDiameterFixed -or $null -ne $taskDifference.after){throw 'Importer corrected more than automatic stop aperture'}
        if($taskSurface.components.physicalAperture.kind -ne 'circular' -or $taskSurface.components.physicalAperture.numbers.radius -ne $taskSurface.semiDiameter){throw 'Unexpected former stop aperture type/radius'}
    }
    $taskRecords+=@{case=$taskCase;differences=@($taskDifferences);capturedSnapshotSha256=(Get-FileHash -LiteralPath $taskModel.before -Algorithm SHA256).Hash.ToLowerInvariant();currentSnapshotSha256=(Get-FileHash -LiteralPath $taskModel.after -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$taskOutput=if($Original132){'D:\Projects\opticalsystem\artifacts\validation\zemax-ra256-single-ray-20261007\model-original132-import-differences.json'}else{'D:\Projects\opticalsystem\artifacts\validation\zemax-ra256-single-ray-20261007\model-import-differences.json'}
if(Test-Path -LiteralPath $taskOutput){throw 'Fresh model difference record required'}
@{scope='Same source bytes and all snapshot properties; only automatic-stop physicalAperture removal allowed';models=$taskRecords;allOtherPropertiesUnchanged=$true} | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskOutput -Encoding utf8NoBOM
$taskRecords | ForEach-Object {Write-Output "$($_.case): $($_.differences.Count) model differences"}
