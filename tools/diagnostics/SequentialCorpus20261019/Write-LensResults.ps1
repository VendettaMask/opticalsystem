param([Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ReportDirectory).Path
$summary = Get-Content -LiteralPath (Join-Path $root 'summary.json') -Raw | ConvertFrom-Json
$analyses = @(Import-Csv -LiteralPath (Join-Path $root 'analysis-matrix.csv'))
$failures = @(Import-Csv -LiteralPath (Join-Path $root 'failure-matrix.csv'))
$supplement = Get-Content -LiteralPath (Join-Path $root 'zos-variants/summary.json') -Raw | ConvertFrom-Json
$supplementAnalyses = @(Import-Csv -LiteralPath (Join-Path $root 'zos-variants/analysis-matrix.csv'))
if ($summary.sourceLenses -ne 95 -or $summary.lensRows.Count -ne 95 -or $analyses.Count -ne 2526 -or $failures.Count -ne 836 -or $supplementAnalyses.Count -ne 356) {
    throw 'Final per-lens input row accounting failed.'
}
$names = @{
    'First Order'='一阶参数'; 'Single Ray Trace'='单光线'; 'Spot Diagram'='点列图';
    'Optical Path Difference'='光程差'; 'Wavefront'='波前'; 'Ray Fan'='光线扇形图';
    'MTF'='FFT MTF'; 'Huygens MTF'='Huygens MTF'; 'Huygens PSF'='Huygens PSF';
    'FFT PSF Cross Section'='FFT PSF 截面'; 'Huygens PSF Cross Section'='Huygens PSF 截面';
    'Encircled Energy'='几何包围能量'; 'Diffraction Encircled Energy'='衍射包围能量';
    'Huygens Through Focus MTF'='Huygens 离焦 MTF'; 'RMS vs Field'='RMS 随视场';
    'Relative Illumination'='相对照度'; 'Field Curvature and Distortion'='场曲与畸变'
}
$categories = @{
    'UnsupportedModelCapability'='模型面型/能力未支持'; 'ChiefRayFailure'='主光线未到达像面';
    'WorkbenchCalculationFailure'='软件计算失败'; 'NativeCaptureFailure'='原生采集适配器失败';
    'ComparisonOrPreparation'='导入/准备失败'
}
function Escape-Cell([object]$Value) { ([string]$Value).Replace('|','\|').Replace("`r",'').Replace("`n",' ') }
function Count-Result($Counts, [string]$Name) { if ($null -eq $Counts.$Name) { 0 } else { [int]$Counts.$Name } }
function Counts-Text($Counts) {
    @('Pass','Close','Difference','Incomparable','Error') | ForEach-Object { Count-Result $Counts $_ } | Join-String -Separator '/'
}
function Analysis-Name([string]$Name) { if ($names.ContainsKey($Name)) { $names[$Name] } else { $Name } }
function Describe-Analyses($Rows) {
    @($Rows | Group-Object Analysis | ForEach-Object { "$(Analysis-Name $_.Name)×$($_.Count)" }) -join '、'
}
function Lens-Status($Lens) {
    $a=$Lens.analysisCounts; $r=$Lens.rayCounts
    if (@($Lens.importErrors).Count -gt 0) { return '导入/准备失败' }
    if ((Count-Result $a 'Difference') -gt 0) { return '存在分析差异' }
    if ((Count-Result $r 'Difference') -gt 0) { return '存在光线差异，待核坐标约定' }
    if ((Count-Result $a 'Error') + (Count-Result $r 'Error') -gt 0) { return '存在计算/采集错误' }
    if ((Count-Result $a 'Pass') -eq 0) { return '分析不可比，精度未认证' }
    if ((Count-Result $a 'Close') -gt 0) { return '部分接近，仍需核对' }
    if ((Count-Result $a 'Incomparable') + (Count-Result $r 'Incomparable') -gt 0) { return '已比较项通过，存在未覆盖项' }
    return '本次请求全部通过'
}
function Lens-Issues($Lens, $Rows) {
    $parts=[Collections.Generic.List[string]]::new()
    foreach ($errorText in $Lens.importErrors) { $parts.Add(($errorText -split '\r?\n')[0]) }
    $different=@($Rows | Where-Object Conclusion -eq 'Difference')
    if ($different.Count) { $parts.Add((Describe-Analyses $different)) }
    $errors=@($failures | Where-Object Lens -eq $Lens.lens | Group-Object Category)
    foreach ($group in $errors) { $parts.Add("$($categories[$group.Name])×$($group.Count)") }
    $incomparable=@($Rows | Where-Object Conclusion -eq 'Incomparable' | Select-Object -ExpandProperty Reason -Unique)
    if ($incomparable -match 'requires MM focal image space') { $parts.Add('现有分析适配器限制毫米有焦像空间') }
    if ($incomparable.Count -and -not ($incomparable -match 'requires MM focal image space')) { $parts.Add('部分原生/软件输出缺少可比数据，见下方逐项原因') }
    if ((Count-Result $Lens.rayCounts 'Difference') -gt 0) { $parts.Add('逐面光线有差异；反射方向符号需另核') }
    if ((Count-Result $Lens.rayCounts 'Incomparable') -gt 0) { $parts.Add('部分逐面光线受坐标断点折叠或能力边界限制') }
    if ($parts.Count -eq 0) { $parts.Add('本次已比较项未见超差；范围限于本次设置') }
    $parts -join '；'
}
$md=[Text.StringBuilder]::new()
[void]$md.AppendLine(@'
# 按镜头对比结果（95 个镜头）

日期：2026-10-19。参考为本机 Zemax OpticStudio 2026 R1 新采集；软件由正式 Application / Core 重算，原容差不变。以下按源目录和镜头逐一列出，点击对应详情可查看具体配置、视场、波长和误差。

**分析计数顺序：通过 / 接近 / 差异 / 不可比 / 错误。光线计数顺序：通过 / 差异 / 不可比 / 错误。** 接近单列，不算严格通过；导入失败即使没有分析请求，也不能算通过。分析行是请求数，可能包含不同配置、波长及控制；并非独立缺陷数。光线行是控制组数。

“本次请求全部通过”只适用于本次明确设置；“已比较项通过”保留未覆盖项。分析差异和原始反射光线方向差异仍需排除坐标/适配器约定，不能直接全部归为产品算法缺陷。最大 NRMSE 只在对应分析内解释，不用于跨镜头排名。

95 个名称对应 95 对 ZMX/ZOS，不把格式副本重复算作镜头。13 个格式存在差异的镜头另做 ZOS 补充，结果列于同一镜头详情中；Core 读取的是原生导出的 ZMX，不代表产品支持直接导入 ZOS。

'@)
$rows=[Collections.Generic.List[object]]::new()
$index=0
foreach ($folder in $summary.lensRows | Group-Object { ($_.lens -split '/')[0] }) {
    [void]$md.AppendLine("## $($folder.Name)`n`n| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |`n|---|---|---:|---:|---|")
    foreach ($lens in $folder.Group) {
        $index++
        $lensItems=@($analyses | Where-Object Lens -eq $lens.lens)
        $status=Lens-Status $lens
        $issue=Lens-Issues $lens $lensItems
        $rayText=@('Pass','Difference','Incomparable','Error') | ForEach-Object { Count-Result $lens.rayCounts $_ } | Join-String -Separator '/'
        $row=[pscustomobject]@{Number=$index; Lens=$lens.lens; Status=$status; AnalysisCounts=(Counts-Text $lens.analysisCounts); RayCounts=$rayText; Issues=$issue}
        $rows.Add($row)
        [void]$md.AppendLine("| [$([IO.Path]::GetFileName($lens.lens))](#lens-$index) | $status | $($row.AnalysisCounts) | $rayText | $(Escape-Cell $issue) |")
    }
    [void]$md.AppendLine()
}
[void]$md.AppendLine("## 每个镜头的详细结果`n")
foreach ($row in $rows) {
    $lens=$summary.lensRows | Where-Object lens -eq $row.Lens
    $lensItems=@($analyses | Where-Object Lens -eq $row.Lens)
    [void]$md.AppendLine("<a id=`"lens-$($row.Number)`"></a>`n`n### $($row.Number). $($row.Lens)`n`n结论：**$($row.Status)**。Core/原生配置：$($lens.coreConfigurations)/$($lens.nativeConfigurations)。分析：$($row.AnalysisCounts)；光线：$($row.RayCounts)。`n`n$($row.Issues)。`n")
    $passed=@($lensItems | Where-Object Conclusion -eq 'Pass')
    if ($passed.Count) { [void]$md.AppendLine("通过项目：$(Describe-Analyses $passed)。`n") }
    $attention=@($lensItems | Where-Object { $_.Conclusion -in @('Difference','Close') })
    if ($attention.Count) {
        [void]$md.AppendLine("| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |`n|---|---:|---|---|---|---|---:|---:|")
        foreach ($item in $attention) {
            $state=if ($item.Conclusion -eq 'Close') {'接近'} else {'差异'}
            [void]$md.AppendLine("| $state | $($item.Configuration) | $($item.Scope) | $(Analysis-Name $item.Analysis) | $($item.Field) | $($item.Wavelength) | $($item.WorstNRMSE) | $($item.MaximumAbsolute) |")
        }
        [void]$md.AppendLine()
    }
    $blocked=@($lensItems | Where-Object Conclusion -in @('Incomparable','Error') | Group-Object Conclusion,Reason)
    if ($blocked.Count) {
        [void]$md.AppendLine("未比较/错误原因（保留原始记录）：`n")
        foreach ($group in $blocked) {
            $item=$group.Group[0]
            $reason=($item.Reason -split '\r?\n')[0]
            if ($reason.StartsWith('Native or Workbench capture failed')) { $reason='采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。' }
            [void]$md.AppendLine("- $($item.Conclusion) × $($group.Count)：$(Escape-Cell $reason)")
        }
        [void]$md.AppendLine()
    }
    $variant=$supplement.lensRows | Where-Object lens -eq $row.Lens
    if ($null -ne $variant) {
        $variantItems=@($supplementAnalyses | Where-Object Lens -eq $row.Lens)
        [void]$md.AppendLine("ZOS 格式补充：**$(Lens-Status $variant)**；分析通过/接近/差异/不可比/错误：**$(Counts-Text $variant.analysisCounts)**；Core/原生配置：$($variant.coreConfigurations)/$($variant.nativeConfigurations)。")
        $variantDiff=@($variantItems | Where-Object Conclusion -eq 'Difference')
        if ($variantDiff.Count) { [void]$md.AppendLine("补充差异：$(Describe-Analyses $variantDiff)。") }
        [void]$md.AppendLine()
    }
}
[void]$md.AppendLine('原始明细：[分析请求 CSV](analysis-matrix.csv)、[逐面光线 JSON](ray-results.json)、[错误分类 CSV](failure-matrix.csv)、[ZOS 补充逐项 CSV](zos-variants/analysis-matrix.csv)。原目录 295 个文件的哈希全部保持；所有原生与软件任务均有完成记录，失败并未记为通过。')
$rows | Export-Csv -LiteralPath (Join-Path $root 'lens-results.csv') -NoTypeInformation -Encoding utf8BOM
[IO.File]::WriteAllText((Join-Path $root 'BY_LENS.md'), $md.ToString(), [Text.UTF8Encoding]::new($false))
$statusCounts=@($rows | Group-Object Status | Select-Object Name,Count)
$statusCounts | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'lens-status-counts.json') -Encoding utf8
if (@(Import-Csv -LiteralPath (Join-Path $root 'lens-results.csv')).Count -ne 95) { throw 'Per-lens output row count failed.' }
$statusCounts | ConvertTo-Json
