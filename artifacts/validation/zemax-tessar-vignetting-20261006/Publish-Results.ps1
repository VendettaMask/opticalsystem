$ErrorActionPreference = 'Stop'
$taskRepo = 'D:\Projects\opticalsystem'
$taskEvidence = Join-Path $taskRepo 'artifacts\validation\zemax-tessar-vignetting-20261006'
$taskVerificationPath = Join-Path $taskEvidence 'verification-final.json'
$taskVerification = Get-Content -LiteralPath $taskVerificationPath -Raw | ConvertFrom-Json
$taskSummary = Get-Content -LiteralPath $taskVerification.replaySummary -Raw | ConvertFrom-Json -AsHashtable
$taskSummary['previousStageCounts'] = $taskVerification.previousStageCounts
$taskSummary['previousStageChanges'] = $taskVerification.previousStageChanges
$taskSummary['previous82PassRegressions'] = 0
$taskSummary['additionalNativeControls'] = @{total=8;counts=$taskVerification.nativeControlCounts;summary=$taskVerification.nativeControlSummary;countedInOriginal132=$false}
$taskSummary['verification'] = @{manifest=$taskVerificationPath;sha256=(Get-FileHash -LiteralPath $taskVerificationPath).Hash.ToLowerInvariant(); tests=$taskVerification.tests}
$taskSummary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $taskRepo 'docs\validation\ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json') -Encoding utf8NoBOM
$taskSummary.rows | Select-Object batch,lens,key,originalConclusion,currentConclusion,currentWorstNrmse,currentWorstMaxAbsolute,currentMetricCount,reason | Export-Csv -LiteralPath (Join-Path (Split-Path $taskVerification.replaySummary) 'matrix.csv') -Encoding utf8NoBOM -NoTypeInformation
$taskPlanPath = Join-Path $taskRepo 'docs\validation\ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json'
$taskPlan = Get-Content -LiteralPath $taskPlanPath -Raw | ConvertFrom-Json -AsHashtable
$taskPlan['previousCoreRecalculation'] = $taskPlan.latestCoreRecalculation
$taskPlan['status'] = 'InitialSixComparedAndVignettingRepairedWithOpenIssues'
$taskPlan['latestCoreRecalculation'] = @{selectedComparisons=132;pass=84;close=12;difference=27;incomparable=8;error=1;originalPassRegressions=0;previous82PassRegressions=0;nativeRecaptured=$false;sourceHashesVerified=27;report='docs/ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md'}
$taskPlan['vignettingNativeControlComparisons'] = 8
foreach ($taskSample in $taskPlan.samples | Where-Object comparisonStatus -ne 'PlannedNotRun') { $taskSample.resultSummary = 'docs/validation/ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json' }
$taskPlan | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskPlanPath -Encoding utf8NoBOM
foreach ($taskOldName in @('ZEMAX_STANDARD_SAMPLE_RESULTS_2026-10-06.json','ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.json')) {
    $taskOldPath = Join-Path $taskRepo ('docs\validation\' + $taskOldName)
    $taskOld = Get-Content -LiteralPath $taskOldPath -Raw | ConvertFrom-Json -AsHashtable
    $taskOld['scopeNote'] = 'Historical stage retained with its original counts. Latest original-132 comparison is Tessar vignetting repair: 84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error; see latestCoreRecalculation.'
    $taskOld['latestCoreRecalculation'] = 'docs/validation/ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json'
    $taskOld | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskOldPath -Encoding utf8NoBOM
}
$taskRelease = $taskVerification.tests | Where-Object { $_.file.EndsWith('main-release.trx') }
$taskDebug = $taskVerification.tests | Where-Object { $_.file.EndsWith('main-debug.trx') }
$taskTool = $taskVerification.tests | Where-Object { $_.file.EndsWith('comparison-release.trx') }
$taskDocs = Get-Content -LiteralPath (Join-Path $taskRepo 'artifacts\validation\zemax-standard-sample-opd-20261006\current-baseline-documents.txt')
if ($taskDocs.Count -ne 36) { throw 'Current-baseline document scope changed' }
foreach ($taskRelative in $taskDocs) {
    $taskPath = Join-Path $taskRepo $taskRelative
    $taskText = [IO.File]::ReadAllText($taskPath)
    $taskPattern = '(?m)^2026-10-06 标准镜头 OPD 修复复验：[^\r\n]*'
    if ([regex]::Matches($taskText,$taskPattern).Count -ne 1) { throw ('Current baseline paragraph missing: ' + $taskRelative) }
    $taskLink = if ($taskRelative -eq 'README.md') {'docs/ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md'} else {'ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md'}
    $taskParagraph = "2026-10-06 Tessar 渐晕修复复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **$($taskRelease.passed) 通过 / $($taskRelease.failed) 个既有失败 / 共 $($taskRelease.total) 项**；正式完整 Debug **$($taskDebug.passed) 通过 / $($taskDebug.failed) 个失败 / 共 $($taskDebug.total) 项**，均零跳过。本批新增正式回归 **30/30** 在两配置全量均通过，上一批 OPD **12/12** 保持通过；比较工具完整 Release **$($taskTool.passed) 通过 / $($taskTool.failed) 个既有失败 / 共 $($taskTool.total) 项**，含本批新增 **6/6**。实验室沿用上一阶段完整结果：初始结构 Release **258 通过 / 2 个既有失败 / 共 260 项**；镀膜 Debug/Release 各 **47/47**，本批未重跑实验室。六份官方标准镜头原设置 132 项为 **84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error**，上一阶段 82 项 Pass 全部保持通过，12/12 组 OPD 与光线扇图通过；新增 8 项原生控制另计，4 Pass / 4 Incomparable。发布门禁仍未通过，见[渐晕修复与完整证据]($taskLink)。此前阶段计数保留历史范围，不与本轮全量相加。"
    [IO.File]::WriteAllText($taskPath, [regex]::Replace($taskText,$taskPattern,{param($taskMatch) $taskParagraph}), [Text.UTF8Encoding]::new($false))
}
$taskNumericalPath = Join-Path $taskRepo 'docs\NUMERICAL_PARITY.md'
$taskNumerical = [IO.File]::ReadAllText($taskNumericalPath)
$taskHistoryPattern = '(?m)^本节保留第一次捕获、解析和原生诊断审计的历史结果（76 Pass）。[^\r\n]*'
$taskHistory = '本节保留第一次捕获、解析和原生诊断审计的历史结果（76 Pass）。随后 OPD 修复达到 82 Pass，再完成渐晕修复；当前原设置 132 项为 **84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error**，上一阶段 82 项通过全部保留；12/12 组 OPD 与光线扇图通过。当前结论、逐项误差与验收边界见[渐晕修复复验](ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md)。下述“未修改 Core”、旧分类和修复顺序仅描述首批捕获阶段。'
[IO.File]::WriteAllText($taskNumericalPath, [regex]::Replace($taskNumerical,$taskHistoryPattern,{param($taskMatch) $taskHistory}), [Text.UTF8Encoding]::new($false))
$taskOldReportPath = Join-Path $taskRepo 'docs\ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.md'
$taskOldReport = [IO.File]::ReadAllText($taskOldReportPath)
$taskOldNotice = '本文保留前一阶段 82 Pass 的修复和验证结果。随后已完成 Tessar 渐晕修复，当前为 84 Pass，原 12 组 OPD 均通过；本文剩余问题与“下一批工作”按当时状态记录。最新结论见[渐晕修复复验](ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md)。'
$taskOldReport = $taskOldReport.Replace("# 标准镜头 OPD 修复与复验 · 2026-10-06`n", "# 标准镜头 OPD 修复与复验 · 2026-10-06`n`n$taskOldNotice`n")
[IO.File]::WriteAllText($taskOldReportPath,$taskOldReport,[Text.UTF8Encoding]::new($false))
$taskCommercialPath = Join-Path $taskRepo 'docs\COMMERCIAL_PHASE1_2026-10-06.md'
$taskCommercial = [IO.File]::ReadAllText($taskCommercialPath)
$taskCommercial += "`n随后完成[渐晕修复复验](ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md)：原 132 项为 84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error，新增正式回归 30 项与工具回归 6 项均通过。当前正式 Release 为 $($taskRelease.passed)/$($taskRelease.failed)/$($taskRelease.total)，Debug 为 $($taskDebug.passed)/$($taskDebug.failed)/$($taskDebug.total)；上文 OPD 阶段的 82 Pass、4307 项及其超时记录保留历史范围。`n"
[IO.File]::WriteAllText($taskCommercialPath,$taskCommercial,[Text.UTF8Encoding]::new($false))
Write-Output 'Published final numerical matrix, historical pointers, and all 36 current-baseline documents.'
