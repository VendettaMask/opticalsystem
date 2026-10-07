$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskRoot=Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261007'
$taskEvidence=Join-Path $taskRepo 'artifacts/validation/zemax-ra256-single-ray-20261007'
function Read-TaskJson([string]$taskPath){Get-Content -LiteralPath $taskPath -Raw | ConvertFrom-Json -DateKind String -AsHashtable}
$taskV=Read-TaskJson (Join-Path $taskEvidence 'verification-final.json')
$taskReplays=@{}
foreach($taskName in @('old-snapshot-original132','fresh-import-original132','old-snapshot-controls17','fresh-import-controls17','old-snapshot-controls6','fresh-import-controls6')){
    $taskReplays[$taskName]=Read-TaskJson (Join-Path $taskRoot "ra256-single-ray-$taskName/summary.json")
}
$taskMain=$taskV.tests | Where-Object {$_.file.EndsWith('main-current-release.trx')}
$taskTool=$taskV.tests | Where-Object {$_.file.EndsWith('tool-current-release.trx')}
$taskPersistent=@{
    schemaVersion=1;date='2026-10-07';scope='Six official sources; original 132 captures, native arrays/requests/tolerances remain immutable. Old-snapshot and fresh original-source import paths are separately counted. Five-source independent controls are 17 prior plus 6 new RA256, not more official sources. Six complete edge-field RA256 ray audits do not certify full-field aperture acceptance or convergence.';
    oldSnapshot=$taskReplays['old-snapshot-original132'];freshImport=$taskReplays['fresh-import-original132'];
    independentControls=@{oldSnapshot17=$taskReplays['old-snapshot-controls17'];freshImport17=$taskReplays['fresh-import-controls17'];oldSnapshot6=$taskReplays['old-snapshot-controls6'];freshImport6=$taskReplays['fresh-import-controls6']};
    verification=$taskV;counts=$taskReplays['fresh-import-original132'].counts;attempted=132;comparedOfficialSources=6;plannedOfficialSources=27;releaseGatePassed=$false;
    previousStage='docs/validation/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.json';nextNumericalWork='Tessar bounded higher-density convergence and finite Relay aiming precision; Huygens PSF/cross-section, diffraction encircled energy, MTF and illumination; remaining 21 sources and unsupported comparison gates.'
}
$taskPersistent | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $taskRepo 'docs/validation/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.json') -Encoding utf8NoBOM
$taskPlanPath=Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json'
$taskPlan=Read-TaskJson $taskPlanPath
if($taskPlan.latestCoreRecalculation.report -ne 'docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md'){$taskPlan.previousCoreRecalculation=$taskPlan.latestCoreRecalculation}
$taskPlan.status='InitialSixComparedWithRa256ApertureAndSingleRayRepairsAndOpenIssues'
$taskPlan.updatedDate='2026-10-07'
$taskPlan.latestCoreRecalculation=@{selectedComparisons=132;pass=95;close=12;difference=16;incomparable=8;error=1;sourceHashesVerified=27;previous85PassRegressions=0;oldSnapshotAndFreshImportSeparatelyVerified=$true;nativeRecaptured=$false;report='docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md';independentRmsControls=23;previousRmsControls=17;newRa256Controls=6;controlPass=23;controlDifference=0;newRayAuditCaptures=6;newRayAuditInputs=308808;newRayAuditSurfaceResults=2521932;newRayAuditAcceptanceDifferences=0;newRayAuditFirstClipDifferences=0}
$taskPlan.rayAuditComparisons=13
$taskPlan.nextNumericalWork=@(
    @{priority=1;status='PlannedNotRun';scope='Tessar higher-density rectangular sampling and finite Relay aiming precision';acceptance='First extend the explicit bounded control contract beyond RA256, split larger batches within the shared budget, then measure non-monotonic RA64/128/256 convergence. The original native edge changes by 0.164328 micrometer from RA128 to RA256. Do not claim convergence from same-grid agreement.'},
    @{priority=2;status='PlannedNotRun';scope='Huygens PSF/cross-section, diffraction encircled energy, MTF and illumination on freshly imported corrected models';acceptance='Keep original requests/native arrays/tolerances; separate physical masks, phase, grid/window and integration residuals. Six-source current matrix retains 16 Difference/12 Close/8 Incomparable/1 Error.'},
    @{priority=3;status='PlannedNotRun';scope='Single-field Doublet applicability and remaining 21 official source files';acceptance='Retain invalid or unsupported results honestly; report each new source and configuration independently.'}
)
$taskPlan.completedNumericalWork=@(
    @{date='2026-10-07';scope='Five-source six-case RA256 curves and six full edge-pupil audits';controlPass=6;rayInputs=308808;raySurfaceResults=2521932;acceptanceDifferences=0;firstClipDifferences=0;automaticStopImporterBugFixed=$true;convergenceCertified=$false},
    @{date='2026-10-07';scope='Twelve original Single Ray Trace reports, all eleven columns; ten original Differences repaired';allNativeCasesPass=12;rawHistoryUnchanged=$true;zeroObjectStillFinite=$true}
)
foreach($taskSample in $taskPlan.samples | Where-Object {$_.ContainsKey('resultSummary')}){$taskSample.resultSummary='docs/validation/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.json'}
$taskPlan | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskPlanPath -Encoding utf8NoBOM
foreach($taskName in @('ZEMAX_STANDARD_SAMPLE_RESULTS_2026-10-06.json','ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.json','ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.json','ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json','ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.json')){
    $taskPath=Join-Path $taskRepo "docs/validation/$taskName"
    $taskOld=Read-TaskJson $taskPath
    $taskOld.latestCoreRecalculation='docs/validation/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.json'
    $taskOld | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $taskPath -Encoding utf8NoBOM
}
$taskHeader="2026-10-07 RA256 与单光线深入复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **$($taskMain.passed) 通过 / $($taskMain.failed) 失败 / 共 $($taskMain.total) 项**，比较工具完整 Release **$($taskTool.passed) 通过 / $($taskTool.failed) 个既有失败 / 共 $($taskTool.total) 项**，均零跳过；新增正式 **20/20**、工具 **16/16** 通过。正式导入/单光线/RMS/GRIN 定向两配置各 **153/153**，工具定向 Debug **54/54**。六份官方镜头原设置 132 项的旧快照与重新导入两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，已有 85 Pass 无回退；独立五文件 RMS 控制为旧 17 项加新 RA256 6 项，**23/23 Pass**，分开计数。六个完整边缘光瞳共 308808 条输入、2521932 个逐面结果，修复自动 STOP 后接纳/首次截断差异均归零；更高密度收敛、Relay 瞄准、衍射、其余 21 份镜头及发布门禁未完成，见[修复与证据](LINK)。完整 Debug 和实验室本轮未重跑，2026-10-06 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜两配置各 **47/47** 保留历史范围。此前阶段计数不相加。"
$taskPaths=Get-Content (Join-Path $taskRepo 'artifacts/validation/zemax-standard-sample-opd-20261006/current-baseline-documents.txt')
if($taskPaths.Count -ne 36){throw 'Current baseline document inventory changed'}
foreach($taskRelative in $taskPaths){
    $taskPath=Join-Path $taskRepo $taskRelative
    $taskText=[IO.File]::ReadAllText($taskPath)
    $taskLink=if($taskRelative -eq 'README.md'){'docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md'}else{'ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md'}
    $taskPattern='(?m)^2026-10-07 (?:Tessar RMS 深入复验|RA256 与单光线深入复验)：[^\r\n]+'
    if(![regex]::IsMatch($taskText,$taskPattern)){throw "Missing current baseline header: $taskRelative"}
    $taskText=[regex]::Replace($taskText,$taskPattern,[System.Text.RegularExpressions.MatchEvaluator]{param($taskMatch) $taskHeader.Replace('LINK',$taskLink)})
    [IO.File]::WriteAllText($taskPath,$taskText,[Text.UTF8Encoding]::new($false))
}
$taskOldReport=Join-Path $taskRepo 'docs/ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md'
$taskText=[IO.File]::ReadAllText($taskOldReport)
$taskNotice='本页保留 GQ 积分与连续渐晕阶段的历史结果（原矩阵 85 Pass、独立控制 17 Pass）。后续已完成 RA256 与单光线路径修复，并定位自动光阑导入误截光；当前结果及旧快照/重新导入两条路径见[最新报告](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文误差与测试计数仅描述本历史阶段。'
if(!$taskText.Contains($taskNotice)){$taskText=[regex]::Replace($taskText,'\A([^\r\n]+\r?\n)','$1'+"`n$taskNotice`n")}
[IO.File]::WriteAllText($taskOldReport,$taskText,[Text.UTF8Encoding]::new($false))
foreach($taskStage in @(
    @{name='ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.md';pattern='(?m)^本文保留 OPD 阶段 82 Pass 的修复和验证结果。[^\r\n]+';text='本文保留 OPD 阶段 82 Pass 的历史修复与验证。当前原矩阵 95 Pass、独立 RMS 17+6 项全部 Pass，旧快照和重新导入两条路径见[最新复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文剩余问题、测试计数与后续建议仅描述当时状态。'},
    @{name='ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md';pattern='(?m)^本报告为 2026-10-06 渐晕阶段的历史验证。[^\r\n]+';text='本报告保留 2026-10-06 渐晕阶段的历史验证。当前原矩阵 95 Pass、独立 RMS 17+6 项全部 Pass，双路径模型、误截光修复与最新测试见[当前复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文差异、计数与建议保留当时范围。'},
    @{name='ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md';pattern='(?m)^本页保留矩形单元中心采样阶段的历史结果[^\r\n]+';text='本页保留矩形单元中心采样阶段的历史结果（原 84 Pass；独立控制 14 Pass/3 Difference）。后续当前原矩阵为 95 Pass，独立 17+6 项全部 Pass，见[最新复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文误差和验证仅描述此历史阶段。'}
)){
    $taskPath=Join-Path $taskRepo "docs/$($taskStage.name)"
    $taskText=[IO.File]::ReadAllText($taskPath)
    if(![regex]::IsMatch($taskText,$taskStage.pattern)){throw "Missing historical scope notice: $($taskStage.name)"}
    $taskText=[regex]::Replace($taskText,$taskStage.pattern,$taskStage.text)
    [IO.File]::WriteAllText($taskPath,$taskText,[Text.UTF8Encoding]::new($false))
}
$taskIndex=Join-Path $taskRepo 'docs/README.md'
$taskText=[IO.File]::ReadAllText($taskIndex)
$taskText=[regex]::Replace($taskText,'(?m)^最新：[^\r\n]+','最新：[RA256、多文件光阑与单光线路径复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)，含双路径 132 项矩阵、23 项独立控制和六组完整光瞳诊断。此前报告保留各自历史范围。')
[IO.File]::WriteAllText($taskIndex,$taskText,[Text.UTF8Encoding]::new($false))
$taskParity=Join-Path $taskRepo 'docs/NUMERICAL_PARITY.md'
$taskText=[IO.File]::ReadAllText($taskParity)
$taskText=[regex]::Replace($taskText,'(?m)^本节保留第一次捕获、解析和原生诊断审计的历史结果（76 Pass）。[^\r\n]+','本节保留首批捕获/解析的历史结果（76 Pass）。后续当前原设置 132 项在旧快照与重新导入两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，已有 85 Pass 无回退。12 组 Single Ray Trace 的十一列全部通过；独立 17+6 项 RMS 控制均 Pass，六组边缘光瞳逐面诊断定位并修复自动 STOP 误截光。数值同网格一致与积分收敛分别验收，见[最新报告](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下述旧分类和修复顺序仅描述首批历史阶段。')
[IO.File]::WriteAllText($taskParity,$taskText,[Text.UTF8Encoding]::new($false))
$taskCommercial=Join-Path $taskRepo 'docs/COMMERCIAL_PHASE1_2026-10-06.md'
$taskText=[IO.File]::ReadAllText($taskCommercial)
$taskText=[regex]::Replace($taskText,'(?m)^本文保留商业质量第一阶段及其后续 OPD、渐晕阶段的历史范围。[^\r\n]+',"本文保留商业质量第一阶段及后续早期阶段的历史范围。当前六文件原矩阵 95 Pass，独立 RMS 17+6 项全部 Pass；正式完整 Release $($taskMain.passed)/$($taskMain.failed)/$($taskMain.total)，比较工具 $($taskTool.passed)/$($taskTool.failed)/$($taskTool.total)，详见[当前复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文阶段计数不代表当前全量结果。")
[IO.File]::WriteAllText($taskCommercial,$taskText,[Text.UTF8Encoding]::new($false))
function Format-TaskNumber($taskValue){([double]$taskValue).ToString('0.000000E+0',[Globalization.CultureInfo]::InvariantCulture)}
$taskRayTable="| 完整边缘光瞳 | 接纳差异（修复前→后） | 首次截断差异（前→后） | 原生传播错误 | 最大物理坐标差 mm |`n| --- | ---: | ---: | ---: | ---: |`n"
foreach($taskAudit in $taskV.rayAudits){$taskRayTable+="| $($taskAudit.case) | $($taskAudit.before.acceptanceDifferences) → $($taskAudit.after.acceptanceDifferences) | $($taskAudit.before.firstClipDifferences) → $($taskAudit.after.firstClipDifferences) | $($taskAudit.after.nativeErrors) | $(Format-TaskNumber $taskAudit.after.maximumPhysicalCoordinateMillimeters) |`n"}
$taskControlTable="| Tessar 控制 | 旧快照最大误差 µm | 同源重新导入最大误差 µm | 分类 |`n| --- | ---: | ---: | --- |`n"
foreach($taskPair in @(@('old-snapshot-controls17','fresh-import-controls17'),@('old-snapshot-controls6','fresh-import-controls6'))){
    foreach($taskRow in $taskReplays[$taskPair[1]].rows | Where-Object {$_.lens -like 'tessar*'}){
        $taskBefore=@($taskReplays[$taskPair[0]].rows | Where-Object {$_.lens -eq $taskRow.lens})[0]
        $taskControlTable+="| $($taskRow.lens) | $(Format-TaskNumber $taskBefore.currentWorstMaxAbsolute) | $(Format-TaskNumber $taskRow.currentWorstMaxAbsolute) | $($taskRow.currentConclusion) |`n"
    }
}
$taskResults='六文件原设置 **132 项**在两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，比上一阶段增加 10 项 Pass，已有 85 项 Pass 无回退。旧快照成功的 131 份分析 JSON 中 121 份逐字节不变，只有 10 份 Single Ray Trace 改变。重新导入修正了四个官方文件的自动 STOP 孔径表示，其他模型属性相同；在这批原分析设置下，131 份分析 JSON 与修复后的旧快照逐字节相同。独立 RMS 控制为此前 **17 项**加本次 **6 项**，两条路径均 **23/23 Pass**，逐点误差仍分别列出。'
if($taskV.freshImportRawChanges.Count -ne 0){throw 'Published original-matrix byte invariance assumption differs from actual result'}
$taskFinal="默认正式解决方案 Debug/Release 构建均为零警告、零错误。正式完整 Release **$($taskMain.passed)/$($taskMain.failed)/$($taskMain.total)**（通过/失败/总计），比较工具完整 Release **$($taskTool.passed)/$($taskTool.failed)/$($taskTool.total)**，均零跳过；新增正式 **20/20**、工具 **16/16** 在完整范围通过。正式导入/单光线/RMS/GRIN 定向 Debug/Release 各 **153/153**，工具定向 Debug **54/54**。正式唯一失败仍为冻结历史有限角度参考，工具两个既有失败仍为 Huygens PSF 截面和衍射包围能量，完整名称及精确消息见验证记录，与上一阶段相同。`n`n本轮未重跑完整 Debug 或实验室：2026-10-06 Debug 4336/1/4337、初始结构 Release 258/2/260、镀膜两配置各 47/47 保留历史范围。旧 153 个 RMS、56 个逐光线和新 54 个 RA256、120 个单光线夹具文件、27 份官方源文件均核验。原 132 项的 1769 个证据文件、旧独立控制的 295 个和新 RA256 的 108 个证据文件分别保留并核验；六个大型逐面捕获同样按流式 SHA-256 核验。36 份当前基线文档同步本轮实际结果。首次 OOM 与早期新测试失败保留历史记录，没有用最终通过覆盖它们。"
if($taskV.retainedEvidenceCounts['old-snapshot-controls6'] -ne 108){throw 'Unexpected new-control retained-file count'}
$taskReport=Join-Path $taskRepo 'docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md'
$taskText=[IO.File]::ReadAllText($taskReport)
foreach($taskMarker in @('<!-- FINAL_RESULTS -->','<!-- RAY_TABLE -->','<!-- CONTROL_TABLE -->','<!-- FINAL_VERIFICATION -->')){if(!$taskText.Contains($taskMarker)){throw "Report completion marker missing: $taskMarker"}}
$taskText=$taskText.Replace('<!-- FINAL_RESULTS -->',$taskResults).Replace('<!-- RAY_TABLE -->',$taskRayTable.TrimEnd()).Replace('<!-- CONTROL_TABLE -->',$taskControlTable.TrimEnd()).Replace('<!-- FINAL_VERIFICATION -->',$taskFinal).Replace('兩条','两条')
[IO.File]::WriteAllText($taskReport,$taskText,[Text.UTF8Encoding]::new($false))
Write-Output 'Published dual matrix, actual test counts, 36 current headers, historical pointers, source-import scope and updated plan.'
