param([Parameter(Mandatory)][string]$ReportDirectory, [Parameter(Mandatory)][string]$BaselineDirectory)
$ErrorActionPreference='Stop'
$report=(Resolve-Path -LiteralPath $ReportDirectory).Path
$baseline=(Resolve-Path -LiteralPath $BaselineDirectory).Path
$summary=Get-Content (Join-Path $report 'summary.json') -Raw | ConvertFrom-Json
$lenses=@(Import-Csv (Join-Path $report 'lens-matrix.csv'))
$analyses=@(Import-Csv (Join-Path $report 'analysis-matrix.csv'))
if(!$summary.completeExecutionAttempts -or $summary.sourceLenses -ne 19 -or $lenses.Count -ne 19 -or $analyses.Count -ne $summary.analysisRequests){throw 'A complete 19-lens matrix is required'}
function Key($row){@($row.Lens,$row.Configuration,$row.Scope,$row.Analysis,$row.Field,$row.Wavelength,$row.PupilSampling) -join '|'}
$after=@{}
foreach($row in $analyses){$key=Key $row; if($after.ContainsKey($key)){throw 'Ambiguous analysis key'}; $after[$key]=$row}
$previous=@(Import-Csv (Join-Path $baseline 'analysis-matrix.csv'))
$regressions=@()
foreach($row in $previous | Where-Object Conclusion -eq 'Pass'){
    $current=$after[(Key $row)]
    if(!$current -or $current.Conclusion -ne 'Pass'){$regressions+=@{lens=$row.Lens;analysis=$row.Analysis;scope=$row.Scope;field=$row.Field;wave=$row.Wavelength;before=$row.Conclusion;after=if($current){$current.Conclusion}else{'Missing'}}}
}
[ordered]@{previousPass=@($previous | Where-Object Conclusion -eq 'Pass').Count; preservedPass=@($previous | Where-Object Conclusion -eq 'Pass').Count-$regressions.Count; regressions=$regressions} |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $report 'previous-pass-preservation.json') -Encoding utf8
$names=@{'Diffraction Encircled Energy'='衍射包围能量';'Relative Illumination'='相对照度';'FFT PSF Cross Section'='FFT PSF 截面';'RMS vs Field'='RMS 随视场'}
$lines=[Collections.Generic.List[string]]::new()
$lines.Add('# Objectives：19 个镜头逐项结果')
$lines.Add('')
$lines.Add('日期：2026-10-20。全部原文件保持，使用本机 OpticStudio 2026 R1 与正式 C# Core，同设置、原容差。分析计数按 **通过 / Close / 超差 / 不可比 / 错误**；光线计数按 **通过 / 超差 / 不可比 / 错误**。不可比和 Close 均不计为通过。')
$lines.Add('')
$lines.Add("共 $($summary.analysisRequests) 项分析；$($summary.rayJobs) 组逐面光线。详见 [逐分析数据](analysis-matrix.csv)、[逐面光线原始比较](ray-results.json)及[机器汇总](summary.json)。")
$lines.Add('')
$lines.Add('| 镜头（Objectives/） | 分析 P/C/D/I/E | 光线 P/D/I/E | 主要结论 |')
$lines.Add('|---|---:|---:|---|')
foreach($lens in $lenses){
    $rows=@($analyses | Where-Object Lens -eq $lens.Lens)
    $issues=@($rows | Where-Object Conclusion -in @('Difference','Close') | ForEach-Object {
        $name=if($names.ContainsKey($_.Analysis)){$names[$_.Analysis]}else{$_.Analysis}
        $kind=if($_.Conclusion -eq 'Difference'){'超差'}else{'Close'}
        "$name $kind"
    } | Select-Object -Unique)
    $note=if([int]$lens.Error -gt 0){'存在错误；见逐项原因'}elseif($issues.Count -gt 0){$issues -join '；'}elseif([int]$lens.Incomparable -gt 0){'已比较项通过，存在不可比项'}else{'本次请求全部通过'}
    $analysisCount=@($lens.Pass,$lens.Close,$lens.Difference,$lens.Incomparable,$lens.Error) -join '/'
    $rayCount=@($lens.RayPass,$lens.RayDifference,$lens.RayIncomparable,$lens.RayError) -join '/'
    $name=$lens.Lens -replace '^Objectives/',''
    $lines.Add("| $name | $analysisCount | $rayCount | $note |")
}
$lines.Add('')
$lines.Add('本次覆盖固定主分析和正权波长的边缘视场控制，不代表任意采样、全部场点及所有设置的通用认证。原生无效数据、零跨度曲线及其他缺少共同物理输出的请求保留为不可比。')
$lines.Add('')
$lines.Add("修正前通过的 $(@($previous | Where-Object Conclusion -eq 'Pass').Count) 项中，保持通过 $(@($previous | Where-Object Conclusion -eq 'Pass').Count-$regressions.Count) 项；退化 $($regressions.Count) 项。")
[IO.File]::WriteAllLines((Join-Path $report 'BY_LENS.md'),$lines,[Text.UTF8Encoding]::new($false))
if($regressions.Count -ne 0){throw 'A previously passing analysis regressed; report retained, do not certify this candidate.'}
Write-Output "Wrote $($lenses.Count) lens rows; preserved all previous passing analyses."
