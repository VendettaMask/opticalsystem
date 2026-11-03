param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository)
$study=Join-Path $repo 'artifacts/validation/all-example-accuracy-20261103'
$afRoot=Join-Path $repo 'artifacts/validation/all-example-afocal-20261103'
$out=Join-Path $repo 'validation/rechecks/2026-11-03/all-example-accuracy'
[IO.Directory]::CreateDirectory($out)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $out $name),(ConvertTo-Json -InputObject $v -Depth 100),[Text.UTF8Encoding]::new($false))}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Counts($rows){$c=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($r in $rows){$c[$r.conclusion]++};$c}
$id=Read "$study/identity.json";$models=@(Read "$study/models.json");$rows=@(Read "$study/rows.json");$results=@(Read "$study/results.json")
$rays=@(Read "$study/ray-results.json");$integrity=Read "$study/reference-integrity.json"
if($models.Count -ne 95 -or $rows.Count -ne 3024 -or $results.Count -ne 3024 -or $rays.Count -ne 251 -or !$integrity.allMatched){throw 'Incomplete matrix or reference verification'}
foreach($binary in $id.assemblies){if((Hash $binary.path) -ne $binary.sha256){throw 'Execution binary changed'}}
foreach($m in $models){if((Hash $m.source) -ne $m.sourceSha256){throw 'Source changed'}}
if((Hash $id.policy) -ne $id.policySha256){throw 'Comparison policy changed'}
$executions=@(Get-ChildItem "$study/analyses" -Recurse -File -Filter execution.json|ForEach-Object {Read $_.FullName})
if($executions.Count -ne $id.eligible -or @($executions|Where-Object coreSha256 -ne $id.coreSha256).Count -or @($results|Where-Object coreSha256 -ne $id.coreSha256).Count){throw 'Incomplete or mixed execution cohort'}
$old=@(Read (Join-Path $id.prior 'analysis-results.json'))
# The previous full matrix's CSV preserves its row identities after evidence retirement.
$previous=Import-Csv (Join-Path $repo 'validation/rechecks/2026-10-31/all-example-accuracy/request-results.csv')
if($previous.Count -ne 3024){throw 'Previous full matrix incomplete'}
$changes=[Collections.Generic.List[object]]::new();$oldLost=[Collections.Generic.List[object]]::new();$previousLost=[Collections.Generic.List[object]]::new()
$retained=0;$previousRetained=0;$newPass=0;$previousNew=0
for($i=0;$i -lt $results.Count;$i++){
 $r=$results[$i];$o=$old[$i];$p=$previous[$i]
 if($r.index -ne $i+1 -or $p.index -ne $r.index -or $o.lens -ne $r.lens -or $o.run.key -ne $r.key -or $p.lens -ne $r.lens -or $p.analysis -ne $r.key){throw 'Request identity mismatch'}
 $detail=@{index=$r.index;lens=$r.lens;analysis=$r.key;previous=$p.verdict;current=$r.conclusion;reason=$r.reason;error=$r.error}
 if($p.verdict -ne $r.conclusion){$changes.Add($detail)}
 if($o.run.conclusion -eq 'Pass'){if($r.conclusion -eq 'Pass'){$retained++}else{$oldLost.Add($detail)}}elseif($r.conclusion -eq 'Pass'){$newPass++}
 if($p.verdict -eq 'Pass'){if($r.conclusion -eq 'Pass'){$previousRetained++}else{$previousLost.Add($detail)}}elseif($r.conclusion -eq 'Pass'){$previousNew++}
}
Save 'prior-pass-audit.json' @{original=@{pass=1102;retained=$retained;lost=$oldLost;newPass=$newPass};previousFull=@{date='2026-10-31';pass=1204;retained=$previousRetained;lost=$previousLost;newPass=$previousNew};changes=$changes}
$af=@(Read "$afRoot/comparison.json");$afId=Read "$afRoot/runtime-identity.json"
if($af.Count -ne 170 -or $afId.coreSha256 -ne $id.coreSha256){throw 'Afocal cohort incomplete or different Core'}
$afWorkers=@(Read "$afRoot/worker-results.json")
if($afWorkers.Count -ne 170 -or @($afWorkers|Where-Object {$_.exitCode -ne 0 -or $_.timedOut -or $_.cancelled}).Count){throw 'Incomplete afocal computation'}
if((Hash (Join-Path $afRoot 'runner/OpticalSystemDesign.Application.dll')) -ne $afId.applicationSha256 -or (Hash (Join-Path $afRoot 'runner/OpticalSystemDesign.ZemaxComparison.dll')) -ne $afId.comparisonSha256){throw 'Afocal computation binaries changed'}
$afPoints=@($af|ForEach-Object {$_.after.points}|ForEach-Object {$_})
$afSummary=@{requests=170;counts=(Counts @($af|ForEach-Object {@{conclusion=$_.after.run.conclusion}}));components=$afPoints.Count;alignedValues=($afPoints|Measure-Object count -Sum).Sum;violations=($afPoints|Measure-Object violations -Sum).Sum;coreSha256=$id.coreSha256;freshImport=$false;inputs='Original independent afocal snapshots and requests'}
Save 'afocal-summary.json' $afSummary
$perLens=@(foreach($m in $models){
 $part=@($results|Where-Object lens -eq $m.lens);$c=Counts $part;$rayPart=@($rays|Where-Object lens -eq $m.lens);$ray=Counts $rayPart
 $afPart=@($af|Where-Object lens -eq $m.lens);$ac=Counts @($afPart|ForEach-Object {@{conclusion=$_.after.run.conclusion}})
 $domain=@($part|Where-Object error -match 'F-tan distortion has no valid data').Count
 $status=if($m.error){'导入阻断'}elseif($c.Error -gt $domain){'计算或参考求值失败'}elseif($c.Difference){'有数值差异'}elseif($c.Close){'接近但未通过'}elseif($ray.Difference -or $ray.Error){'逐面光线未通过'}elseif($c.Incomparable -or $domain -or $ray.Incomparable -or !$rayPart.Count){'可比项通过，覆盖待补'}else{'本轮既定请求全部通过'}
 [pscustomobject]@{index=$m.index;lens=$m.lens;category=$m.lens.Split('/')[0];configurations=$m.snapshots.Count;importError=$m.error;status=$status;requests=$part.Count;Pass=$c.Pass;Close=$c.Close;Difference=$c.Difference;Incomparable=$c.Incomparable;Error=$c.Error;domainRejections=$domain;rayPass=$ray.Pass;rayDifference=$ray.Difference;rayIncomparable=$ray.Incomparable;rayError=$ray.Error;afocalRequests=$afPart.Count;afocalPass=$ac.Pass;afocalClose=$ac.Close;afocalDifference=$ac.Difference;afocalIncomparable=$ac.Incomparable;afocalError=$ac.Error}
})
Save 'lens-results.json' $perLens;$perLens|Export-Csv "$out/lens-results.csv" -NoTypeInformation -Encoding utf8BOM
$categories=@(foreach($g in ($perLens|Group-Object category)){$c=Counts @($results|Where-Object {$_.lens.Split('/')[0] -eq $g.Name});[pscustomobject]@{category=$g.Name;lenses=$g.Count;requests=($g.Group|Measure-Object requests -Sum).Sum;Pass=$c.Pass;Close=$c.Close;Difference=$c.Difference;Incomparable=$c.Incomparable;Error=$c.Error}})
Save 'categories.json' $categories
$analysis=@(foreach($g in ($results|Group-Object key)){$c=Counts $g.Group;[pscustomobject]@{analysis=$g.Name;requests=$g.Count;Pass=$c.Pass;Close=$c.Close;Difference=$c.Difference;Incomparable=$c.Incomparable;Error=$c.Error}})
Save 'analysis-summary.json' $analysis;$analysis|Export-Csv "$out/analysis-summary.csv" -NoTypeInformation -Encoding utf8BOM
$taxonomy=@(foreach($r in $results){
 $kind=if($r.conclusion -in @('Pass','Close','Difference')){$r.conclusion}elseif(!$rows[$r.index-1].job){'LegacyRequestOrReferenceGap'}elseif($r.error -match 'OpticCapabilityException'){'UnsupportedModelCapability'}elseif($r.error -match 'Incoming crystal requires its entrance coordinate frame|A crystal medium requires an explicit birefringent entry/exit interface'){'CrystalFrameOrInterfaceContext'}elseif($r.error -match 'F-tan distortion has no valid data'){'ReferenceDomainNotApplicable'}elseif($r.error -match 'Chief ray did not reach|geometric chief ray does not reach'){'ChiefRayOrReferenceUnavailable'}elseif($r.error -match 'RayAimingException'){'AimingFailure'}elseif($r.error -match 'Ray tracing did not produce an image-plane sample'){'ImagePlaneSampleUnavailable'}elseif($r.error -match 'FFT image delta is too small'){'DeclaredSamplingRejected'}elseif($r.error -match 'image-plane pupil frequency scale is not finite and positive'){'PupilFrequencyScaleUnavailable'}elseif($r.error -match "requires an element of type 'Number'"){'NumericResultContractFailure'}elseif($r.conclusion -eq 'Incomparable'){'NativeOrComparisonContractGap'}else{'OtherCoreOrEvaluationFailure'}
 [pscustomobject]@{index=$r.index;lens=$r.lens;configuration=$r.configuration;analysis=$r.key;verdict=$r.conclusion;cause=$kind;reason=$r.reason;error=$r.error}
})
$taxonomy|Export-Csv "$out/request-results.csv" -NoTypeInformation -Encoding utf8BOM
$taxonomy|Where-Object verdict -in @('Close','Difference')|Export-Csv "$out/numerical-residuals.csv" -NoTypeInformation -Encoding utf8BOM
Save 'failure-taxonomy.json' @($taxonomy|Group-Object cause|ForEach-Object {@{cause=$_.Name;count=$_.Count}})
$map=@{};foreach($r in $results){$map[$r.index]=$r}
$scoped=[Collections.Generic.List[object]]::new()
foreach($name in @('results-final.json','categories-results-final.json')){
 $file=Join-Path $repo "validation/rechecks/2026-11-03/aperture-footprint/$name";$part=@(Read $file)
 $different=@(foreach($r in $part){if($map[$r.index].conclusion -ne $r.conclusion){@{index=$r.index;scoped=$r.conclusion;full=$map[$r.index].conclusion}}})
 $scoped.Add(@{reference=$file;sha256=(Hash $file);requests=$part.Count;classificationDifferences=$different})
}
Save 'scoped-consistency.json' $scoped
foreach($name in @('identity.json','request-mapping.json','ray-results.json','ray-summary.json')){Copy-Item -LiteralPath "$study/$name" -Destination "$out/$name" -Force}
$summary=@{date='2026-11-03';completedLocal=[DateTimeOffset]::Now.ToString('o');comparisonCompletedUtc=(Read "$study/summary.json").completedUtc;coreSha256=$id.coreSha256;productExamples=95;categories=14;imported=@($models|Where-Object {!$_.error}).Count;configurations=($models|ForEach-Object {$_.snapshots.Count}|Measure-Object -Sum).Sum;mainRequests=3024;mainCounts=(Counts $results);executedRequests=$executions.Count;unexecutableRequests=3024-$executions.Count;rayGroups=$rays.Count;rayCounts=(Counts $rays);physicalSurfaceComparisons=($rays|Measure-Object surfaceComparisons -Sum).Sum;afocal=$afSummary;originalPassRetained=$retained;originalPassLost=$oldLost.Count;previousFullPassRetained=$previousRetained;previousFullPassLost=$previousLost.Count;newPassSincePreviousFull=$previousNew;allRequestPassLenses=@($perLens|Where-Object status -eq '本轮既定请求全部通过').Count;lensStatusCounts=@($perLens|Group-Object status|ForEach-Object {@{status=$_.Name;count=$_.Count}});nativePayloadsVerified=$integrity.verifiedFiles;policyChanged=$false;freshNativeCapture=$false;referenceConvergenceFullyChecked=$false;allExamplesCertified=$false;desktopControlled=$false;scopedConsistency=$scoped}
Save 'summary.json' $summary
$md=[Text.StringBuilder]::new();[void]$md.AppendLine('# 95 支菜单示例逐镜头比较，2026-11-03')
[void]$md.AppendLine('');[void]$md.AppendLine("同一 Core 重新核查 95 个源文件，$($summary.imported) 个导入成功、$($summary.configurations) 个配置。原 3,024 行全部结算，$($summary.executedRequests) 项实际重算，$($summary.unexecutableRequests) 项仍缺可执行原请求。原设置和容差保持。原生参考复用 OpticStudio 2026 R1 冻结捕获，本轮没有重新采集全部原生参考或为所有采样补做收敛。")
[void]$md.AppendLine('');[void]$md.AppendLine('P/C/D/I/E：通过／接近但未通过／数值差异／不可比或覆盖缺口／计算或求值失败。主矩阵和独立无焦 170 项的输入身份不同，分开计数；后者使用原始保存快照，在本轮 Core 重算。无焦列完整显示 P/C/D/I/E。')
[void]$md.AppendLine('');[void]$md.AppendLine('| # | 镜头 | 配置 | 主矩阵 P/C/D/I/E | 逐面光线 P/D/I/E | 状态 | 无焦补充 P/C/D/I/E |')
[void]$md.AppendLine('|---:|---|---:|---:|---:|---|---:|')
foreach($m in $perLens){$a=if($m.afocalRequests){"$($m.afocalPass)/$($m.afocalClose)/$($m.afocalDifference)/$($m.afocalIncomparable)/$($m.afocalError)"}else{'—'};[void]$md.AppendLine("| $($m.index) | $($m.lens) | $($m.configurations) | $($m.Pass)/$($m.Close)/$($m.Difference)/$($m.Incomparable)/$($m.Error) | $($m.rayPass)/$($m.rayDifference)/$($m.rayIncomparable)/$($m.rayError) | $($m.status) | $a |")}
[IO.File]::WriteAllText("$out/BY_LENS.md",$md.ToString(),[Text.UTF8Encoding]::new($false))
$summary|ConvertTo-Json -Depth 8
