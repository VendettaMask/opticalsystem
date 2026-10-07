$ErrorActionPreference = 'Stop'
$taskRepo = 'D:\Projects\opticalsystem'
$taskEvidence = Join-Path $taskRepo 'artifacts/validation/zemax-rms-field-20261007'
$taskVerification = Get-Content (Join-Path $taskEvidence 'verification-final.json') -Raw | ConvertFrom-Json
$taskRelease = $taskVerification.tests | Where-Object { $_.file.EndsWith('main-release.trx') }
$taskTool = $taskVerification.tests | Where-Object { $_.file.EndsWith('comparison-final-release.trx') }
$taskSummary = Get-Content $taskVerification.replaySummary -Raw | ConvertFrom-Json -AsHashtable
$taskControl = Get-Content $taskVerification.nativeControlSummary -Raw | ConvertFrom-Json -AsHashtable
$taskSummary['date']='2026-10-07'
$taskSummary['verification']=$taskVerification
$taskSummary['independentRmsControls']=$taskControl
$taskSummary['previous84PassRegressions']=0
$taskSummary['previousStageRawChanged']=0
$taskSummary['scopeNote']='Original six-lens 132-case GQ matrix unchanged. Independent five-lens 17-case controls: 14 Pass and 3 Difference after the RMS spot midpoint fix. No original reference/settings/tolerance replacement. 6 of 27 official lenses compared; 21 not run. RA wavefront and continuous vignetting rule remain unverified.'
$taskResult = Join-Path $taskRepo 'docs/validation/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json'
$taskSummary | ConvertTo-Json -Depth 100 | Set-Content $taskResult -Encoding utf8NoBOM
$taskPlanPath = Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json'
$taskPlan = Get-Content $taskPlanPath -Raw | ConvertFrom-Json -AsHashtable
if ($taskPlan.latestCoreRecalculation.report -ne 'docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md') { $taskPlan.previousCoreRecalculation=$taskPlan.latestCoreRecalculation }
$taskPlan.status='InitialSixComparedAndRmsSpotSamplingRepairedWithOpenIssues'
$taskPlan.updatedDate='2026-10-07'
$taskPlan.latestCoreRecalculation=@{selectedComparisons=132;pass=84;close=12;difference=27;incomparable=8;error=1;sourceHashesVerified=27;previous84PassRegressions=0;originalPassRegressions=0;nativeRecaptured=$false;report='docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md';independentRmsControls=17;controlPass=14;controlDifference=3}
$taskPlan.rmsNativeControlComparisons=17
foreach ($taskSample in $taskPlan.samples | Where-Object { $_.comparisonStatus -eq 'Compared' -or $_.ContainsKey('resultSummary') }) { $taskSample.resultSummary='docs/validation/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json' }
$taskPlan | ConvertTo-Json -Depth 100 | Set-Content $taskPlanPath -Encoding utf8NoBOM
foreach ($taskName in @('ZEMAX_STANDARD_SAMPLE_RESULTS_2026-10-06.json','ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.json','ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json')) {
    $taskPath=Join-Path $taskRepo "docs/validation/$taskName"
    $taskOld=Get-Content $taskPath -Raw | ConvertFrom-Json -AsHashtable
    $taskOld.scopeNote='Historical stage with its original counts and verification. Latest RMS spot sampling study retains original 132-case counts 84/12/27/8/1 and separately adds 17 controls (14 Pass/3 Difference); see latestCoreRecalculation.'
    $taskOld.latestCoreRecalculation='docs/validation/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json'
    $taskOld | ConvertTo-Json -Depth 100 | Set-Content $taskPath -Encoding utf8NoBOM
}
$taskDocs = Get-Content (Join-Path $taskRepo 'artifacts/validation/zemax-standard-sample-opd-20261006/current-baseline-documents.txt')
if ($taskDocs.Count -ne 36) { throw 'Current baseline document scope changed' }
foreach ($taskRelative in $taskDocs) {
    $taskPath = Join-Path $taskRepo $taskRelative
    $taskText = [IO.File]::ReadAllText($taskPath)
    $taskPattern='(?m)^2026-10-(06 Tessar 渐晕修复复验|07 RMS 光斑采样复验)：[^\r\n]*'
    if ([regex]::Matches($taskText,$taskPattern).Count -ne 1) { throw "Current paragraph missing: $taskRelative" }
    $taskLink = if ($taskRelative -eq 'README.md') {'docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md'} else {'ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md'}
    $taskParagraph="2026-10-07 RMS 光斑采样复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **$($taskRelease.passed) 通过 / $($taskRelease.failed) 失败 / 共 $($taskRelease.total) 项**，零跳过：一项既有历史参考差异、一项本次 UI 清理异常；后者隔离 **16/16** 通过，原全量失败保留、原因未完全定责。当前 RMS 定向 Debug/Release 各 **22/22**，含新增正式回归 **14/14**。比较工具完整 Release **$($taskTool.passed) 通过 / $($taskTool.failed) 个既有失败 / 共 $($taskTool.total) 项**，零跳过，含新增 **30/30**。本轮未重跑正式完整 Debug 和实验室：2026-10-06 完整 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜 Debug/Release 各 **47/47** 保留历史范围。六份官方镜头原设置 132 项仍为 **84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error**，84 项通过无回退，131 份成功原始结果逐字节不变。新增五文件 RMS 控制另计 **14 Pass / 3 Difference / 共 17 项**；共享 Core 已修正 RMS 光斑 RA 单元中心采样，Tessar GQ、连续渐晕及 RA 波前仍有未认证范围。其余 21 份官方镜头未运行，发布门禁未通过，见[修复与完整证据]($taskLink)。此前阶段计数保留历史范围，不相加。"
    [IO.File]::WriteAllText($taskPath,[regex]::Replace($taskText,$taskPattern,{param($taskMatch) $taskParagraph}),[Text.UTF8Encoding]::new($false))
}
$taskReportPath=Join-Path $taskRepo 'docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md'
$taskReport=[IO.File]::ReadAllText($taskReportPath)
$taskFinal="默认正式解决方案 Debug/Release 均构建成功，零警告、零错误。正式完整 Release **$($taskRelease.passed) 通过 / $($taskRelease.failed) 失败 / $($taskRelease.total) 项**；比较工具完整 Release **$($taskTool.passed) 通过 / $($taskTool.failed) 个既有失败 / $($taskTool.total) 项**，均零跳过。本批新增正式 **14/14**、工具 **30/30** 在对应全量通过，正式 RMS 定向 Debug/Release 各 **22/22**，工具 RMS 定向 **30/30**。`n`n正式失败包含既有冻结历史有限角度参考：预期 0.058589983242791625，实际 0.060191701479492175；另有本次 `PanelContentLayoutTests` 的 GbT13323_1991/cemented true 在 `SafeHeadlessUnitTestSession.Dispose` 出现 NullReferenceException。相关 PanelContentLayout 与 WavefrontSurfaceRender 隔离复验 **16/16** 通过，原全量失败保留；原因未完全定责，本次未修改测试清理辅助类或屏蔽异常。工具两个既有失败仍为 Huygens PSF 截面和衍射包围能量，NRMSE 分别 0.0037256670590166724、0.011467280942848471，与上一阶段相同。没有删除断言或放宽误差门槛。`n`n本轮未重跑正式完整 Debug 和实验室。2026-10-06 正式完整 Debug 4336/1/4337、初始结构 Release 258/2/260、镀膜 Debug/Release 各 47/47 为历史验证，不作为本轮执行。当前基线同步到 36 份文档。"
$taskReport=$taskReport.Replace('<!-- FINAL_VERIFICATION -->',$taskFinal)
[IO.File]::WriteAllText($taskReportPath,$taskReport,[Text.UTF8Encoding]::new($false))
$taskOldReportPath=Join-Path $taskRepo 'docs/ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md'
$taskOldReport=[IO.File]::ReadAllText($taskOldReportPath)
if (-not $taskOldReport.Contains('本报告为 2026-10-06 渐晕阶段的历史验证')) {
    $taskOldReport=[regex]::Replace($taskOldReport,'(?m)^(# Tessar 渐晕与相位复验[^\r\n]*)(\r?\n)',"`$1`n`n本报告为 2026-10-06 渐晕阶段的历史验证。随后完成 RMS 光斑采样修复，原 132 项分类保持 84/12/27/8/1，新增 17 项独立控制及当前测试结果见[RMS 采样复验](ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md)。下文测试计数和下一批建议保留当时范围。`n")
    [IO.File]::WriteAllText($taskOldReportPath,$taskOldReport,[Text.UTF8Encoding]::new($false))
}
Write-Output 'Published current 132-case matrix, 17 controls, plan, historical pointers, and all 36 baseline paragraphs.'
