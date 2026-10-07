$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskEvidence=Join-Path $taskRepo 'artifacts/validation/zemax-tessar-rms-20261007'
$taskV=Get-Content (Join-Path $taskEvidence 'verification-final.json') -Raw | ConvertFrom-Json -DateKind String -AsHashtable
$taskSummary=Get-Content $taskV.originalMatrixSummary -Raw | ConvertFrom-Json -DateKind String -AsHashtable
$taskControl=Get-Content $taskV.rmsControlSummary -Raw | ConvertFrom-Json -DateKind String -AsHashtable
$taskMain=$taskV.tests | Where-Object {$_.file.EndsWith('main-current-release.trx')}
$taskTool=$taskV.tests | Where-Object {$_.file.EndsWith('tool-final-current-release.trx')}
$taskSummary.date='2026-10-07'
$taskSummary.verification=$taskV
$taskSummary.independentRmsControls=$taskControl
$taskSummary.previous84PassRegressions=0
$taskSummary.previousStageConclusionChanges=1
$taskSummary.controlPreviousStageConclusionChanges=3
$taskSummary.rayAuditFixtureManifest='validation/zemax/2026-r1/tessar-ray-audit-2026-10-07/manifest.json'
$taskSummary.scopeNote='Six official sources, original 132 native captures/settings/tolerances: 85 Pass/12 Close/26 Difference/8 Incomparable/1 Error. One improvement since prior 84-Pass stage; zero previous passes regress. The replay change counter compares against the initial 76-Pass capture, not the preceding stage. Five-source 17 independent RMS controls all Pass, separately counted. Seven frozen explicit-ray batches: 3384 rays/28656 surface results. RA physical-aperture and GQ6 integration residuals remain measured; 21 of 27 sources not run. No universal wavefront, polarization, convergence or commercial release certification.'
$taskResult=Join-Path $taskRepo 'docs/validation/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.json'
$taskSummary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskResult -Encoding utf8NoBOM
$taskPlanPath=Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json'
$taskPlan=Get-Content $taskPlanPath -Raw | ConvertFrom-Json -DateKind String -AsHashtable
if($taskPlan.latestCoreRecalculation.report -ne 'docs/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md'){$taskPlan.previousCoreRecalculation=$taskPlan.latestCoreRecalculation}
$taskPlan.status='InitialSixComparedAndTessarRmsIntegralRepairedWithOpenIssues'
$taskPlan.updatedDate='2026-10-07'
$taskPlan.latestCoreRecalculation=@{selectedComparisons=132;pass=85;close=12;difference=26;incomparable=8;error=1;sourceHashesVerified=27;previous84PassRegressions=0;originalPassRegressions=0;nativeRecaptured=$false;report='docs/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md';independentRmsControls=17;controlPass=17;controlDifference=0;rayAuditCaptures=7;rayAuditInputs=3384}
$taskPlan.rayAuditComparisons=7
$taskPlan.nextNumericalWork=@(
  @{priority=1;status='PlannedNotRun';scope='Five-source RA256 with Tessar retain/remove controls; split per-surface batches within the shared limit';acceptance='Measure per-surface aperture acceptance, first clipping surface and RA64/128/256 convergence separately; retain original settings/tolerances'},
  @{priority=2;status='PlannedNotRun';scope='Ten original Single Ray Trace differences and finite Relay aiming residual';acceptance='Locate coordinate/direction/normal/optical-path column differences against the five-source native surface controls'},
  @{priority=3;status='PlannedNotRun';scope='Huygens PSF, diffraction encircled energy, MTF, illumination, single-field Doublet applicability, then remaining 21 official sources';acceptance='Replay the unchanged original matrix with no prior-pass regressions and publish actual unresolved differences'}
)
foreach($taskSample in $taskPlan.samples | Where-Object {$_.comparisonStatus -eq 'Compared' -or $_.ContainsKey('resultSummary')}){$taskSample.resultSummary='docs/validation/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.json'}
$taskPlan | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskPlanPath -Encoding utf8NoBOM
foreach($taskName in @('ZEMAX_STANDARD_SAMPLE_RESULTS_2026-10-06.json','ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.json','ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json','ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json')){
    $taskPath=Join-Path $taskRepo "docs/validation/$taskName"
    $taskOld=Get-Content $taskPath -Raw | ConvertFrom-Json -DateKind String -AsHashtable
    $taskOld.latestCoreRecalculation='docs/validation/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.json'
    if($taskName -eq 'ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json'){$taskOld.scopeNote='Historical midpoint-sampling stage: original 84 Pass and independent 14 Pass/3 Difference. Current results are in the latestCoreRecalculation pointer; this stage data and verification remain unchanged.'}
    $taskOld | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskPath -Encoding utf8NoBOM
}
$taskHeader="2026-10-07 Tessar RMS 深入复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **$($taskMain.passed) 通过 / $($taskMain.failed) 失败 / 共 $($taskMain.total) 项**，零跳过；失败见本批报告。RMS/GRIN 定向 Debug/Release 各 **57/57**，新增正式回归 **13/13**；并发读访问定向各 **5/5**，保持原两秒门槛。比较工具完整 Release **$($taskTool.passed) 通过 / $($taskTool.failed) 个既有失败 / 共 $($taskTool.total) 项**，新增 **8/8**，RMS/诊断定向 **38/38**。完整 Debug 和实验室本轮未重跑：2026-10-06 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜 Debug/Release 各 **47/47** 为历史范围。六份官方镜头原设置 132 项为 **85 Pass / 12 Close / 26 Difference / 8 Incomparable / 1 Error**，已有 84 Pass 无回退；独立五文件 RMS 控制 **17/17 Pass**。GQ6 和 RA 孔径边界仍有残差，RA 波前、其余 21 份镜头及发布门禁未完成，见[修复与完整证据](LINK)。此前阶段计数保留历史范围，不相加。"
$taskPaths=Get-Content (Join-Path $taskRepo 'artifacts/validation/zemax-standard-sample-opd-20261006/current-baseline-documents.txt')
if($taskPaths.Count -ne 36){throw 'Current document list changed'}
foreach($taskRelative in $taskPaths){
    $taskPath=Join-Path $taskRepo $taskRelative
    $taskText=[IO.File]::ReadAllText($taskPath)
    $taskLink=if($taskRelative -eq 'README.md'){'docs/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md'}else{'ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md'}
    $taskPattern='(?m)^2026-10-07 (?:RMS 光斑采样复验|Tessar RMS 深入复验)：[^\r\n]+'
    if(![regex]::IsMatch($taskText,$taskPattern)){throw "Missing current baseline header: $taskRelative"}
    $taskText=[regex]::Replace($taskText,$taskPattern,[System.Text.RegularExpressions.MatchEvaluator]{param($taskMatch) $taskHeader.Replace('LINK',$taskLink)})
    [IO.File]::WriteAllText($taskPath,$taskText,[Text.UTF8Encoding]::new($false))
}
$taskOldReport=Join-Path $taskRepo 'docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md'
$taskText=[IO.File]::ReadAllText($taskOldReport)
$taskNotice='本页保留矩形单元中心采样阶段的历史结果（原 84 Pass；独立控制 14 Pass/3 Difference）。后续已修复 Tessar GQ 积分与连续渐晕，当前原矩阵 85 Pass、独立控制 17 Pass，见[最新报告](ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md)。下文差异和验证仅描述此历史阶段。'
if(!$taskText.Contains($taskNotice)){$taskText=[regex]::Replace($taskText,'\A([^\r\n]+\r?\n)','$1'+"`n$taskNotice`n")}
[IO.File]::WriteAllText($taskOldReport,$taskText,[Text.UTF8Encoding]::new($false))
$taskIndex=Join-Path $taskRepo 'docs/README.md'
$taskText=[IO.File]::ReadAllText($taskIndex)
$taskText=[regex]::Replace($taskText,'(?m)^最新：[^\r\n]+','最新：[Tessar RMS 积分与连续渐晕修复](ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md)，含五文件逐光线诊断和原 132 项复算。此前矩形采样、Tessar 渐晕、OPD 阶段报告保留历史范围。')
$taskText=$taskText.Replace('同步日期：2026-10-05。','同步日期：2026-10-07。')
[IO.File]::WriteAllText($taskIndex,$taskText,[Text.UTF8Encoding]::new($false))
$taskParity=Join-Path $taskRepo 'docs/NUMERICAL_PARITY.md'
$taskText=[IO.File]::ReadAllText($taskParity)
$taskText=[regex]::Replace($taskText,'(?m)^本节保留第一次捕获、解析和原生诊断审计的历史结果（76 Pass）。[^\r\n]+','本节保留第一次捕获、解析和原生诊断审计的历史结果（76 Pass）。随后 OPD、渐晕、RMS 采样与 Tessar 积分依次修复；当前原设置 132 项为 **85 Pass / 12 Close / 26 Difference / 8 Incomparable / 1 Error**，已有 84 Pass 无回退；12/12 组 OPD 与光线扇图通过。另有五文件 17 项 RMS 原生控制全部 Pass，不与原矩阵合并。误差、边界和原生逐光线证据见[Tessar RMS 复验](ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md)。下述旧分类和修复顺序仅描述首批捕获阶段。')
[IO.File]::WriteAllText($taskParity,$taskText,[Text.UTF8Encoding]::new($false))
$taskStatus=Join-Path $taskRepo 'docs/CURRENT_STATUS.md'
$taskText=[IO.File]::ReadAllText($taskStatus).Replace('同步日期：2026-10-05。','同步日期：2026-10-07。')
[IO.File]::WriteAllText($taskStatus,$taskText,[Text.UTF8Encoding]::new($false))
$taskFinal="默认正式解决方案 Debug/Release 构建均为零警告、零错误。正式完整 Release **$($taskMain.passed)/$($taskMain.failed)/$($taskMain.total)**（通过/失败/总计），比较工具完整 Release **$($taskTool.passed)/$($taskTool.failed)/$($taskTool.total)**，均零跳过。RMS/GRIN 定向两配置各 **57/57**，并发读取定向两配置各 **5/5**，工具 RMS/诊断定向 **38/38**；新增正式 **13/13**、工具 **8/8** 在完整范围通过。正式唯一失败仍是冻结历史有限角度参考：期望 0.058589983242791625，实际 0.060191701479492175。工具两个既有失败仍为 Huygens PSF 截面和衍射包围能量，误差与上阶段相同；完整失败名称和消息见验证记录。`n`n本轮未重跑完整 Debug 或实验室，2026-10-06 Debug 4336/1/4337、初始结构 Release 258/2/260、镀膜两配置各 47/47 保留历史范围。上一阶段 UI 清理异常和隔离 16/16 记录也保留历史范围，未修改清理辅助类。36 份当前基线文档同步本批结果。"
$taskReport=Join-Path $taskRepo 'docs/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md'
$taskText=[IO.File]::ReadAllText($taskReport).Replace('<!-- FINAL_VERIFICATION -->',$taskFinal)
[IO.File]::WriteAllText($taskReport,$taskText,[Text.UTF8Encoding]::new($false))
Write-Output 'Published the full matrix, plan, stage boundaries and 36 current baseline headers.'
