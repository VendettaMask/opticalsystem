param([Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ReportDirectory).Path
$summary = Get-Content (Join-Path $root 'summary.json') -Raw | ConvertFrom-Json
$analyses = @(Import-Csv (Join-Path $root 'analysis-matrix.csv'))
$rays = @(Get-Content (Join-Path $root 'ray-results.json') -Raw | ConvertFrom-Json)
if (!$summary.completeExecutionAttempts -or $summary.sourceLenses -ne 95 -or $summary.lensRows.Count -ne 95 -or $analyses.Count -ne $summary.analysisRequests -or $rays.Count -ne $summary.rayJobs) { throw 'Complete 95-lens row accounting required.' }
$failures=@(foreach ($item in $analyses | Where-Object Conclusion -eq 'Error') {
    $workerPath=Join-Path $item.Evidence 'workbench/process.log'
    $nativePath=Join-Path $item.Evidence 'native/error.json'
    $worker=if(Test-Path -LiteralPath $workerPath){[IO.File]::ReadAllText($workerPath)}else{''}
    $native=if(Test-Path -LiteralPath $nativePath){[IO.File]::ReadAllText($nativePath)}else{''}
    $firstLine=($worker -split '\r?\n')[0]
    $category=if($native){'NativeCaptureFailure'}elseif($firstLine -match 'OpticCapabilityException'){'UnsupportedModelCapability'}elseif($firstLine -match 'RayAimingException'){'RayAimingFailure'}elseif($firstLine -match '\bchief ray\b'){'ChiefRayFailure'}elseif($worker){'WorkbenchCalculationFailure'}else{'PreparationOrComparison'}
    [pscustomobject]@{Lens=$item.Lens;Configuration=$item.Configuration;Scope=$item.Scope;Analysis=$item.Analysis;Conclusion=$item.Conclusion;Category=$category;Reason=$item.Reason;WorkerFirstLine=($worker -split '\r?\n')[0];NativeError=$native;Evidence=$item.Evidence}
})
if($failures.Count -ne [int]$summary.analysisCounts.Error) { throw 'Error classification count mismatch.' }
$failures | Export-Csv (Join-Path $root 'failure-matrix.csv') -NoTypeInformation -Encoding utf8BOM
function Count-Result($Counts, [string]$Name) { if ($null -eq $Counts.$Name) { 0 } else { [int]$Counts.$Name } }
function Counts-Text($Counts) { (@('Pass','Close','Difference','Incomparable','Error') | ForEach-Object { Count-Result $Counts $_ }) -join '/' }
function Escape-Cell([object]$Value) { ([string]$Value).Replace('|','\|').Replace("`r",'').Replace("`n",' ') }
function Lens-Status($Lens) {
    $a=$Lens.analysisCounts; $r=$Lens.rayCounts
    if (@($Lens.importErrors).Count) { return '导入/准备阻断' }
    if ((Count-Result $a 'Difference') -gt 0) { return '存在分析差异' }
    if ((Count-Result $r 'Difference') -gt 0) { return '存在逐面光线差异' }
    if ((Count-Result $a 'Error') + (Count-Result $r 'Error') -gt 0) { return '存在计算/采集错误' }
    if ((Count-Result $a 'Pass') -eq 0) { return '分析不可比' }
    if ((Count-Result $a 'Close') -gt 0) { return '存在 Close' }
    if ((Count-Result $a 'Incomparable') + (Count-Result $r 'Incomparable') -gt 0) { return '可比项通过，仍有不可比' }
    if ($Lens.coreConfigurations -ne $Lens.nativeConfigurations) { return '配置覆盖不完整' }
    return '本次请求全部通过'
}
$rows=@(foreach ($lens in $summary.lensRows) {
    $items=@($analyses | Where-Object Lens -eq $lens.lens)
    if ($items.Count -ne $lens.analysisRequests) { throw 'Per-lens analysis accounting mismatch.' }
    $issue=@($items | Where-Object Conclusion -in @('Close','Difference') | Group-Object Analysis | ForEach-Object { "$($_.Name)×$($_.Count)" })
    $issue+=@($lens.importErrors | ForEach-Object { ($_ -split '\r?\n')[0] })
    if ((Count-Result $lens.analysisCounts 'Error') -gt 0) { $issue+='计算/采集错误（见逐项原始原因）' }
    if ((Count-Result $lens.analysisCounts 'Incomparable') -gt 0) { $issue+='存在无共同有效参考或能力契约缺口' }
    [pscustomobject]@{ Lens=$lens.lens; Status=(Lens-Status $lens); Configurations="$($lens.coreConfigurations)/$($lens.nativeConfigurations)"; AnalysisCounts=(Counts-Text $lens.analysisCounts); RayCounts=(@('Pass','Difference','Incomparable','Error') | ForEach-Object { Count-Result $lens.rayCounts $_ }) -join '/'; Issues=$issue -join '；' }
})
$md=[Text.StringBuilder]::new()
[void]$md.AppendLine("# 全部 95 支镜头本轮结果`n`n日期：$($summary.date)。正式 Application/Core 重算，本机 Zemax OpticStudio 2026 R1 新捕获，原比较策略和容差保持。`n")
[void]$md.AppendLine("共 $($summary.analysisRequests) 项分析、$($summary.rayJobs) 组逐面光线。不同配置、波长和控制分别计数；不能将请求数量当作缺陷数。Close、不可比、错误、缺失配置及准备阻断均不计为全部通过。`n")
[void]$md.AppendLine("分析顺序为 **Pass / Close / Difference / 不可比 / Error**；光线为 **Pass / Difference / 不可比 / Error**。下表按首要问题互斥分类，详情保留同时存在的问题。`n")
foreach ($folder in $rows | Group-Object { ($_.Lens -split '/')[0] }) {
    [void]$md.AppendLine("## $($folder.Name)`n`n| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |`n|---|---|---:|---:|---:|---|")
    foreach ($row in $folder.Group) { [void]$md.AppendLine("| $([IO.Path]::GetFileName($row.Lens)) | $($row.Status) | $($row.Configurations) | $($row.AnalysisCounts) | $($row.RayCounts) | $(Escape-Cell $row.Issues) |") }
    [void]$md.AppendLine()
}
[void]$md.AppendLine("## 范围与明细`n`n95 个名称各算一支，不重复计入 ZOS 格式副本。本表主矩阵使用原 ZMX，ZOS 第一配置模型读回独立记录在 [机器汇总](summary.json)。本次请求限于 18 类主分析、各正权波长末视场四项控制与 GQ6 逐面光线，不涵盖任意设置的穷举。`n`n[逐项分析](analysis-matrix.csv)、[逐面光线](ray-results.json)、[错误分类](failure-matrix.csv)保留具体误差和原因。原生无效输出不能成为通过参考，反射/坐标约定和未支持面型不能直接归为算法缺陷。`n")
[IO.File]::WriteAllText((Join-Path $root 'BY_LENS.md'),$md.ToString(),[Text.UTF8Encoding]::new($false))
$rows | Export-Csv (Join-Path $root 'lens-results.csv') -NoTypeInformation -Encoding utf8BOM
$counts=@($rows | Group-Object Status | Select-Object Name,Count)
$counts | ConvertTo-Json | Set-Content (Join-Path $root 'lens-status-counts.json') -Encoding utf8NoBOM
if ((@($counts | Measure-Object -Property Count -Sum).Sum) -ne 95) { throw 'Lens status count mismatch.' }
$counts | ConvertTo-Json
