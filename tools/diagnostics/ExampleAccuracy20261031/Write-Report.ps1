param([string]$RepositoryRoot=(Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference='Stop'
$root=Join-Path $RepositoryRoot 'artifacts/validation/all-example-accuracy-20261031'
$out=Join-Path $RepositoryRoot 'validation/rechecks/2026-10-31/all-example-accuracy'
New-Item -ItemType Directory $out -Force|Out-Null
function ReadJson($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function WriteJson($v,$p){$v|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $p -Encoding utf8NoBOM}
function CountStatus($list){$h=@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($v in $list){$h[$v.conclusion]++};return $h}
function CountString($h){"$($h.Pass)/$($h.Close)/$($h.Difference)/$($h.Incomparable)/$($h.Error)"}
$id=ReadJson "$root/identity.json";$models=@(ReadJson "$root/models.json");$rows=@(ReadJson "$root/rows.json");$results=@(ReadJson "$root/results.json");$rays=@(ReadJson "$root/ray-results.json")
$baseline=@(ReadJson (Join-Path $id.prior 'analysis-results.json'));$nativeIntegrity=ReadJson "$root/reference-integrity.json"
if($models.Count -ne 95 -or $results.Count -ne 3024 -or $rows.Count -ne 3024 -or !$nativeIntegrity.allMatched){throw 'Incomplete example comparison'}
foreach($binary in $id.assemblies){if((Get-FileHash -LiteralPath $binary.path).Hash.ToLowerInvariant() -ne $binary.sha256){throw 'Frozen computation binary changed'}}
foreach($m in $models){if((Get-FileHash -LiteralPath $m.source).Hash.ToLowerInvariant() -ne $m.sourceSha256){throw 'Product example changed'}}
$executions=@(Get-ChildItem "$root/analyses" -Filter execution.json -File -Recurse|ForEach-Object{ReadJson $_.FullName})
if($executions.Count -ne @($rows|Where-Object job).Count -or @($executions|Where-Object {$_.coreSha256 -ne $id.coreSha256}).Count){throw 'Incomplete or mixed Core computation cohort'}
$regressions=@();$retained=0;$newPass=0
for($i=0;$i -lt $results.Count;$i++){
 if($results[$i].index -ne $i+1 -or $results[$i].lens -ne $baseline[$i].lens -or $results[$i].key -ne $baseline[$i].run.key){throw 'Legacy/current row identity mismatch'}
 if($baseline[$i].run.conclusion -eq 'Pass'){
  if($results[$i].conclusion -eq 'Pass'){$retained++}else{$regressions+=@{index=$results[$i].index;lens=$results[$i].lens;analysis=$results[$i].key;previous='Pass';current=$results[$i].conclusion;reason=$results[$i].reason;error=$results[$i].error}}
 }elseif($results[$i].conclusion -eq 'Pass'){$newPass++}
}
WriteJson @{oldPass=1102;retained=$retained;notMaintained=$regressions.Count;newPass=$newPass;cases=$regressions} "$out/prior-pass-audit.json"
$afRoot=Join-Path $RepositoryRoot 'artifacts/validation/all-example-afocal-20261031'
$af=@(ReadJson "$afRoot/comparison.json");$afId=ReadJson "$afRoot/runtime-identity.json"
if($af.Count -ne 170 -or $afId.coreSha256 -ne $id.coreSha256){throw 'Afocal scope incomplete or different Core'}
$afPoints=@($af|ForEach-Object{$_.after.points}|ForEach-Object{$_})
$afSummary=@{requests=$af.Count;counts=(CountStatus @($af|ForEach-Object{@{conclusion=$_.after.run.conclusion}}));components=$afPoints.Count;alignedValues=($afPoints|Measure-Object count -Sum).Sum;violations=($afPoints|Measure-Object violations -Sum).Sum;coreSha256=$id.coreSha256}
WriteJson $afSummary "$out/afocal-summary.json"
$perLens=foreach($m in $models){
 $part=@($results|Where-Object lens -eq $m.lens);$counts=CountStatus $part;$rayPart=@($rays|Where-Object lens -eq $m.lens);$rayCounts=CountStatus $rayPart
 $afPart=@($af|Where-Object lens -eq $m.lens);$afCounts=CountStatus @($afPart|ForEach-Object{@{conclusion=$_.after.run.conclusion}})
 $domain=@($part|Where-Object {$_.error -match 'F-tan distortion has no valid data'}).Count
 $status=if($m.error){'导入阻断'}elseif(!$part.Count){'原矩阵缺参考'}elseif($counts.Error -gt $domain){'计算或能力缺口'}elseif($counts.Difference){'有数值差异'}elseif($counts.Close){'接近但未通过'}elseif($rayCounts.Difference){'逐面光线差异'}elseif($counts.Incomparable -or $domain -or $rayCounts.Incomparable -or !$rayPart.Count){'可比项通过，覆盖或适用性待补'}else{'本轮既定请求全部通过'}
 $issues=@($part|Where-Object {$_.conclusion -ne 'Pass'}|Group-Object key|Sort-Object Count -Descending|ForEach-Object{"$($_.Name) × $($_.Count)"})
 [pscustomobject]@{index=$m.index;lens=$m.lens;category=$m.lens.Split('/')[0];configurations=$m.snapshots.Count;importError=$m.error;status=$status;
  requests=$part.Count;Pass=$counts.Pass;Close=$counts.Close;Difference=$counts.Difference;Incomparable=$counts.Incomparable;Error=$counts.Error;domainRejections=$domain;
  rayPass=$rayCounts.Pass;rayDifference=$rayCounts.Difference;rayIncomparable=$rayCounts.Incomparable;rayError=$rayCounts.Error;
  afocalScopeRequests=$afPart.Count;afocalPass=$afCounts.Pass;afocalIncomparable=$afCounts.Incomparable;issues=$issues -join '；'}
}
$perLens|Export-Csv "$out/lens-results.csv" -NoTypeInformation -Encoding utf8BOM
WriteJson $perLens "$out/lens-results.json"
$categories=foreach($group in $perLens|Group-Object category){$part=@($results|Where-Object {$_.lens.Split('/')[0] -eq $group.Name});$c=CountStatus $part
 [pscustomobject]@{category=$group.Name;lenses=$group.Count;requests=$part.Count;Pass=$c.Pass;Close=$c.Close;Difference=$c.Difference;Incomparable=$c.Incomparable;Error=$c.Error}}
WriteJson $categories "$out/categories.json"
$analysis=foreach($g in $results|Group-Object key){$c=CountStatus $g.Group;[pscustomobject]@{analysis=$g.Name;requests=$g.Count;Pass=$c.Pass;Close=$c.Close;Difference=$c.Difference;Incomparable=$c.Incomparable;Error=$c.Error}}
$analysis|Export-Csv "$out/analysis-summary.csv" -NoTypeInformation -Encoding utf8BOM
WriteJson $analysis "$out/analysis-summary.json"
$taxonomy=foreach($r in $results){$kind=if($r.conclusion -in @('Pass','Close','Difference')){$r.conclusion}
 elseif(!$rows[$r.index-1].job){'LegacyRequestOrReferenceGap'}elseif($r.error -match 'OpticCapabilityException'){'UnsupportedModelCapability'}
 elseif($r.error -match 'F-tan distortion has no valid data'){'ReferenceDomainNotApplicable'}elseif($r.error -match 'Chief ray did not reach|geometric chief ray does not reach'){'ChiefRayOrReferenceUnavailable'}
 elseif($r.error -match 'RayAimingException'){'AimingFailure'}elseif($r.error -match 'Ray tracing did not produce an image-plane sample'){'ImagePlaneSampleUnavailable'}
 elseif($r.conclusion -eq 'Incomparable'){'NativeOrComparisonContractGap'}else{'OtherCoreOrEvaluationFailure'}
 [pscustomobject]@{index=$r.index;lens=$r.lens;analysis=$r.key;verdict=$r.conclusion;cause=$kind;reason=$r.reason;legacyBlocker=if(!$rows[$r.index-1].job){$rows[$r.index-1].run.reason}else{''};error=$r.error}}
WriteJson @($taxonomy|Group-Object cause|ForEach-Object{@{cause=$_.Name;count=$_.Count}}) "$out/failure-taxonomy.json"
$taxonomy|Export-Csv "$out/request-results.csv" -NoTypeInformation -Encoding utf8BOM
$types=foreach($r in $results|Where-Object {$_.error -match 'OpticCapabilityException'}){foreach($t in ([regex]::Matches($r.error,'Zemax TYPE ([A-Z0-9_]+)')|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)){[pscustomobject]@{type=$t;lens=$r.lens}}}
WriteJson @($types|Group-Object type|ForEach-Object{@{type=$_.Name;requests=$_.Count;lenses=@($_.Group.lens|Sort-Object -Unique).Count}}|Sort-Object requests -Descending) "$out/unsupported-types.json"
Copy-Item "$root/ray-results.json" "$out/ray-results.json";Copy-Item "$root/ray-summary.json" "$out/ray-summary.json"
Copy-Item "$root/regression-probe.json" "$out/regression-probe.json"
$summary=@{date='2026-10-31';coreSha256=$id.coreSha256;productExamples=95;categories=14;imported=@($models|Where-Object {!$_.error}).Count;configurations=($models|ForEach-Object{$_.snapshots.Count}|Measure-Object -Sum).Sum;
 mainRequests=3024;mainCounts=(CountStatus $results);executedRequests=$executions.Count;rayGroups=$rays.Count;rayCounts=(CountStatus $rays);afocal=$afSummary;
 originalPassRetained=$retained;originalPassNotMaintained=$regressions.Count;newPass=$newPass;allRequestPassLenses=@($perLens|Where-Object status -eq '本轮既定请求全部通过').Count;
 lensStatusCounts=@($perLens|Group-Object status|ForEach-Object{@{status=$_.Name;count=$_.Count}});nativePayloadsVerified=$nativeIntegrity.verifiedFiles;
 referenceConvergenceFullyChecked=$false;allExamplesCertified=$false;desktopControlled=$false;policyChanged=$false;freshNativeCapture=$false}
WriteJson $summary "$out/summary.json"
$md=[Text.StringBuilder]::new();[void]$md.AppendLine('# 全部 95 支菜单示例逐镜头结果，2026-10-31')
[void]$md.AppendLine('');[void]$md.AppendLine('本轮同一冻结 Core 重新导入全部文件，按原 3,024 行账目重算 2,904 个可执行请求；原生参考复用 OpticStudio 2026 R1 冻结数组。P/C/D/I/E 分别为通过、接近但未通过、数值差异、不可比/覆盖缺口、计算返回失败。E 包含未支持模型和适用性拒绝，不能全部解释为算法精度错误。全部既定请求通过不等于所有分析/采样均获收敛认证。')
[void]$md.AppendLine('');[void]$md.AppendLine('原主矩阵中的无焦旧适配缺口保留，最后一列给出本轮同 Core 的独立 170 项复核；不同请求集合不相加。19 支物镜的两项 F-Tan ≥90° 拒绝为定义边界，历史 F-Theta 补充另有有效参考。详细原因见 `request-results.csv`，回退见 `prior-pass-audit.json`。')
[void]$md.AppendLine('');[void]$md.AppendLine('| # | 镜头 | 配置 | 原矩阵 P/C/D/I/E | 逐面光线 P/D/I/E | 首要状态 | 同 Core 无焦补充 P/I |')
[void]$md.AppendLine('|---:|---|---:|---:|---:|---|---:|')
foreach($m in $perLens){$afText=if($m.afocalScopeRequests){"$($m.afocalPass)/$($m.afocalIncomparable)"}else{'—'};[void]$md.AppendLine("| $($m.index) | $($m.lens) | $($m.configurations) | $($m.Pass)/$($m.Close)/$($m.Difference)/$($m.Incomparable)/$($m.Error) | $($m.rayPass)/$($m.rayDifference)/$($m.rayIncomparable)/$($m.rayError) | $($m.status) | $afText |")}
$md.ToString()|Set-Content "$out/BY_LENS.md" -Encoding utf8NoBOM
Write-Output ($summary|ConvertTo-Json -Depth 8)
