$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\Projects\opticalsystem'
$taskData = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$taskRelease = $taskData.suites | Where-Object name -eq 'product-release'
$taskDebug = $taskData.suites | Where-Object name -eq 'product-debug'
if ($taskRelease.failed -ne 1 -or $taskDebug.failed -ne 1 -or $taskRelease.addedPassed -ne 31 -or $taskDebug.addedPassed -ne 31) {
    throw 'Inspect unexpected formal-suite results before synchronizing documentation.'
}
$taskSummary = "2026-10-06 商业质量第一阶段复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **$($taskRelease.passed) 通过 / $($taskRelease.failed) 个既有历史参考失败 / 共 $($taskRelease.total) 项**，正式完整 Debug **$($taskDebug.passed) 通过 / $($taskDebug.failed) 个相同失败 / 共 $($taskDebug.total) 项**，两者均零跳过，含新增回归 **31/31**，不重复累加。比较工具 Release **102 通过 / 2 个既有失败 / 共 104 项**，误差值与前阶段相同；初始结构 Release **258 通过 / 2 个既有失败 / 共 260 项**；镀膜 Debug/Release 各 **47/47**。当前完整发布门禁仍未通过，见[第一阶段修复与证据](COMMERCIAL_PHASE1_2026-10-06.md)。此前阶段计数保留历史范围，不与本轮全量相加。"
$taskDocuments = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -Filter '*.md' -File) + @(Get-Item -LiteralPath (Join-Path $taskRoot 'README.md'))
$taskUpdated = 0
foreach ($taskDocument in $taskDocuments) {
    $taskText = [System.IO.File]::ReadAllText($taskDocument.FullName)
    $taskNewText = [regex]::Replace($taskText, '(?m)^2026-10-05 可靠性修复复验：[^\r\n]+', $taskSummary)
    if ($taskNewText -ne $taskText) {
        if ($taskDocument.Name -eq 'README.md' -and $taskDocument.DirectoryName -eq $taskRoot) {
            $taskNewText = $taskNewText.Replace('](COMMERCIAL_PHASE1_2026-10-06.md)', '](docs/COMMERCIAL_PHASE1_2026-10-06.md)')
        }
        [System.IO.File]::WriteAllText($taskDocument.FullName, $taskNewText, [System.Text.UTF8Encoding]::new($false))
        $taskUpdated++
    }
}
$taskReportPath = Join-Path $taskRoot 'docs/COMMERCIAL_PHASE1_2026-10-06.md'
$taskReport = [System.IO.File]::ReadAllText($taskReportPath)
$taskVerification = @"
## 验证

使用仓库默认输出目录，正式完整 Debug/Release 构建均零警告、零错误。源码格式验证和 ``git diff --check`` 通过。最终正式全量包含导出保护及新增 31 项回归；没有用首次局部通过代替最终全量。

| 实际范围 | 结果 | 证据 |
| --- | --- | --- |
| 正式完整 Release | $($taskRelease.passed) 通过 / 1 个既有失败 / $($taskRelease.total) 项，零跳过 | product-release.trx |
| 正式完整 Debug | $($taskDebug.passed) 通过 / 1 个相同失败 / $($taskDebug.total) 项，零跳过 | product-debug.trx |
| 新增回归 | 两配置各 31/31，从全量提取，不重复累加 | product-release.trx / product-debug.trx |
| 比较工具完整 Release | 102 通过 / 2 个既有失败 / 104 项 | comparison-release.trx |
| 初始结构完整 Release | 258 通过 / 2 个既有失败 / 260 项 | initial-structure-release.trx |
| 镀膜完整 Debug/Release | 各 47/47 | coating-debug.trx / coating-release.trx |

正式剩余失败是冻结 Optiland 辅助历史的有限角度光线参考：预期 0.058589983242791625，实际 0.060191701479492175。不得将该辅助数据当作商业精度的主标准，也未修改历史文件来消除差异。

比较工具仍是 Huygens PSF Cross Section 和 Diffraction Encircled Energy 的误差非退化门槛失败，实际 NRMSE 分别为 0.0037256670590166724 与 0.011467280942848471，和前阶段完全相同。初始结构仍为 ``trial-0078`` 重启可行性和 Secant ``index: 9`` 独立重算目标失败；前阶段使用原始远端 Core 在相同断言复现，这里不重复运行原始对照。

255² 合成双点 PSF 的非二次幂 MTF 探针保留 127 个频率点，首个切向值 0.9951469164070644 与解析值相同；计算项数 33,162,750。本机探针计时仅代表该样本和当次运行，不承诺所有镜头的时长。

TRX、源文件 SHA-256、机器清单及隔离探针输出保存在 [验证目录](../artifacts/validation/commercial-phase1-20261006/verification.json)。旧中间结果以 ``before-export-guard`` 后缀保留；发布判断以最终结果为准。

"@
$taskReport = [regex]::Replace($taskReport, '(?s)## 验证\r?\n.*?(?=## 剩余范围)', $taskVerification)
[System.IO.File]::WriteAllText($taskReportPath, $taskReport, [System.Text.UTF8Encoding]::new($false))
$taskPreviousPath = Join-Path $taskRoot 'docs/AUDIT_RELIABILITY_FIXES_2026-10-05.md'
$taskPrevious = [System.IO.File]::ReadAllText($taskPreviousPath)
if (!$taskPrevious.Contains('本页保留 2026-10-05 的阶段验证计数')) {
    $taskPrevious = [regex]::Replace($taskPrevious, '(?m)^(# 软件可靠性问题修复[^\r\n]+)', '$1' + "`r`n`r`n本页保留 2026-10-05 的阶段验证计数。当前完整 Debug/Release 与剩余失败见[商业质量第一阶段](COMMERCIAL_PHASE1_2026-10-06.md)。")
    [System.IO.File]::WriteAllText($taskPreviousPath, $taskPrevious, [System.Text.UTF8Encoding]::new($false))
}
Write-Output "Synchronized $taskUpdated baseline paragraphs and phase reports."
