param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
function Read-Json([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
$study=Join-Path $repo 'artifacts/validation/model-capabilities-stage5-20261101';$runtime=Join-Path $repo 'artifacts/validation/model-capabilities-stage5-runner-20261101'
$report=Join-Path $repo 'validation/rechecks/2026-11-01/model-capabilities-stage5'
if(Test-Path -LiteralPath $report){throw 'Fresh report required'};New-Item -ItemType Directory -Path $report|Out-Null
$raw=Read-Json "$study/summary.json";$core=$raw.coreSha256
foreach($p in @("$runtime/OpticalSystemDesign.Core.dll",(Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll'),(Join-Path $repo 'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll'))){if((Hash $p) -ne $core){throw 'App/test/frozen Core identity mismatch'}}
$caps=Read-Json "$study/capability-inventory.json";$capMap=@{};foreach($c in $caps){if($c.coreSha256 -ne $core){throw 'Mixed inventory Core'};$capMap["$($c.lensIndex):$($c.configuration)"]=@($c.issues)}
$results=Read-Json "$study/results.json";$current=@{};foreach($r in $results){$current[$r.index]=$r}
$prior=Read-Json (Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage4/capability-progress.json')
$progress=@(foreach($r in $prior|Where-Object {-not $_.modelGateRemoved}){
 $key="$($r.lensIndex):$($r.configuration)";if(-not $capMap.ContainsKey($key) -or -not $current.ContainsKey($r.index)){throw 'Missing required configuration/replay'}
 [pscustomobject]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key;modelGateRemoved=$capMap[$key].Count -eq 0
  remainingIssues=@($capMap[$key]);currentConclusion=$current[$r.index].conclusion;currentError=$current[$r.index].error;coreSha256=$core}
})
if($progress.Count -ne 237 -or @($progress|Where-Object {-not $_.modelGateRemoved}).Count -ne 0 -or $results.Count -ne 238){throw 'Unexpected stage scope'}
$progress|ConvertTo-Json -Depth 24|Set-Content "$report/capability-progress.json" -Encoding utf8
$allModelProgress=@(foreach($set in @(@('original751','model-capabilities-stage2'),@('original1098','model-capabilities-stage1'))){
 $rows=Read-Json (Join-Path $repo "validation/rechecks/2026-10-31/$($set[1])/capability-progress.json")
 $output=@(foreach($r in $rows){$key="$($r.lensIndex):$($r.configuration)";if(-not $capMap.ContainsKey($key)){throw 'Missing original configuration'}
  [pscustomobject]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key
   modelGateRemoved=$capMap[$key].Count -eq 0;remainingIssues=@($capMap[$key]);availabilityCoreSha256=$core
   replayedUnderThisCore=$current.ContainsKey($r.index);currentConclusion=if($current.ContainsKey($r.index)){$current[$r.index].conclusion}else{'NotReplayedUnderThisCore'}
   accuracyCertified=$false}
 });$output|ConvertTo-Json -Depth 24|Set-Content "$report/capability-progress-$($set[0]).json" -Encoding utf8
 [pscustomobject]@{scope=$set[0];requests=$output.Count;modelGated=@($output|Where-Object {-not $_.modelGateRemoved}).Count}
})
if(($allModelProgress|Where-Object scope -eq original751).requests -ne 751 -or ($allModelProgress|Where-Object scope -eq original1098).requests -ne 1098 -or
 @($allModelProgress|Where-Object modelGated -gt 0).Count -ne 0){throw 'Original ledger mismatch'}
$newCounts=[ordered]@{};foreach($g in $progress|Group-Object currentConclusion){$newCounts[$g.Name]=$g.Count}
[pscustomobject]@{status='ModelGatesRemoved_AccuracyInProgress';priorUnsupportedRequests=237;newlyModelGateRemoved=237;remainingModelGated=0
 original751ModelGated=0;original1098ModelGated=0;freshConfigurationInventory=$caps.Count;menuSources=95;all751AnalysesPassed=$false;all95AccuracyReplayed=$false
 replayedLenses=5;replayedRequests=238;replayedResultCounts=$raw.counts;newlyUnblockedResultCounts=$newCounts;referenceConvergenceCertified=$false
 coreSha256=$core;focusedTestsPassed=215;defaultAppBuild='Passed_ZeroWarnings_ZeroErrors';nativeDesktopUsed=$false
 openItems=@('Grid GRIN Gaussian/first-order definition','Custom-grid bulk precision','Cube configuration 2 first-order and energy results','Earlier Grid Sag first-order and strong non-air Paraxial XY phase differences','Missing/incomparable full native analysis references')
 unclaimedCapabilities=@('Birefringent modes 2/3 and coherent Jones transport','Spatially varying crystal axes','Anisotropic interface coatings','Grid dispersion formulas other than Schott','Independent/coupled X Gaussian API','Lossless text export of a GRIN volume')}|ConvertTo-Json -Depth 18|Set-Content "$report/summary.json" -Encoding utf8
foreach($name in 'results','lens-results','capability-inventory','scope','identity'){Copy-Item -LiteralPath "$study/$name.json" -Destination "$report/$name.json"}
@($results|Where-Object conclusion -ne Pass)|ConvertTo-Json -Depth 30|Set-Content "$report/accuracy-followup.json" -Encoding utf8
$controls=@(foreach($folder in 'birefringent-native-20261031','grid-gradient-convergence-native-20261101'){
 foreach($r in Read-Json (Join-Path $repo "artifacts/validation/$folder/product-controls.json")){
  if($r.coreSha256 -ne $core){throw 'Mixed control Core'}
  if($folder -like 'grid-gradient*' -and $r.id -notin 'sample-2E-05','dispersion-0.0001'){continue}
  $limit=1e-8+1e-6*[math]::Abs($r.expected)
  [pscustomobject]@{capture=$folder;id=$r.id;code=$r.code;input=$r.input;expected=$r.expected;actual=$r.actual;absoluteError=[math]::Abs($r.error);controlBound=$limit
   coreSha256=$core;sourceSha256=$r.sourceSha256;referenceOnly=$r.referenceOnly
   verdict=if($r.referenceOnly){'ReferenceRow_NotIndependentValidation'}elseif([math]::Abs($r.error) -le $limit){'WithinExistingExplicitRayBound'}else{'Difference'}}
 }
})
$controls|ConvertTo-Json -Depth 20|Set-Content "$report/native-controls.json" -Encoding utf8
@($controls|Group-Object capture,id,verdict|ForEach-Object {[pscustomobject]@{scope=$_.Name;count=$_.Count;maximumAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})|ConvertTo-Json|Set-Content "$report/native-controls-summary.json" -Encoding utf8
$fo=Read-Json (Join-Path $repo 'artifacts/validation/grid-gradient-firstorder-native-20261101/product-firstorder.json');foreach($r in $fo){if($r.coreSha256 -ne $core){throw 'Mixed first-order Core'}}
$fo|ConvertTo-Json -Depth 20|Set-Content "$report/grid-firstorder-open-controls.json" -Encoding utf8
$lines=@('# 本批逐镜头结果','','P/C/D/I/E 为通过、接近未通过、差异、不可比、执行失败。仅本批冻结 Core；旧参考收敛未全面认证。','','| 镜头 | 请求 | P | C | D | I | E |','|---|---:|---:|---:|---:|---:|---:|')
foreach($lens in Read-Json "$study/lens-results.json"){$n=@{};foreach($p in $lens.counts.PSObject.Properties){$n[$p.Name]=[int]$p.Value};$lines+='| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $lens.lens,$lens.total,([int]$n.Pass),([int]$n.Close),([int]$n.Difference),([int]$n.Incomparable),([int]$n.Error)};$lines|Set-Content "$report/BY_LENS.md" -Encoding utf8
$doc=@"
# 菜单示例模型能力补齐第五批，2026-11-01

**最后 237 项解除模型阻塞；用户指定的 751 项模型阻塞为 0，最初 1,098 项模型阻塞也为 0。** 这项结论来自当前冻结 Core 对全部菜单源文件重新导入后的 114 个可用配置清单，不是累计推算。原有一个未导入的椭圆锥瞳孔示例不在这 1,098 项模型阻塞集合内，仍保留原记录。本批没有认证全部 95 支示例的分析精度。

最后 5 支示例、9 个配置、238 项原请求重算：**192 Pass、3 Close、16 Difference、27 Incomparable、0 Error**。其中 237 项是上批仍被模型阻塞的原请求，逐项结果见 capability-progress.json。原请求、比较配置和门槛不变，Close 不算通过。原 95 支总账和历史差异不覆写；没有把局部控制升级为完整分析认证。

产品保持纯 C#/.NET 和共享 Core。新增能力：

- 均匀单轴晶体 BIRE__IN/BIRE_OUT 的模式 0/1、平面和标准圆锥基底；解析真实普通/非常光色散，保留入口局部晶轴并转换为每条路径的全局晶轴。界面守恒切向波矢，光线沿能流方向，体光程使用波矢在能流上的投影。反射保留原晶体坐标，缺少晶体、色散或未验证的偏振/膜层组合明确失败。模式 2/3、渐变晶轴和晶体 Jones 运输仍不宣称支持。
- GRINSU11 的完整 Schott GGD 网格、UTF-8/UTF-16、坐标/单位、常数偏置和六个色散项；有限定义域、有界数据量、真实连续积分和完整 STAROPT 保存。相邻资产优先，官方示例数字数据按原始 SHA-256 内置，保存后不依赖本机 Zemax 文件。其他色散公式和外部 GRIN 文本导出仍明确拒绝，不能压成均匀玻璃。
- 网格入口/独立折射率查询采用经多项式和两波长控制验证的局部四节点自然三次样条；体内使用全网格自然样条的值及一致梯度。两条定义明确分开，未改写现有 GRIN 分布。源数据先在节点计算色散。每个不可变网格最多缓存四个精确波长，不跨输入状态复用。
- 入口瞳孔仅验证光阑之前的相关链路；下游非对称介质不再阻塞已明确的入口孔径。网格一阶量提取声明轴的高斯线性部分，保留梯度项，但与原生焦距仍有差异；独立 X/coupled 高斯 API 尚未认证。真实光线不会丢掉线性梯度。

215 项定向回归通过，默认 Release App 构建零警告、零错误，测试、App 与冻结运行器 Core 哈希相同：$core。验证始终后台执行，没有桌面控制、截图、前台验收、安装或发布。

双折射六组直接原生控制覆盖两波长、两分支、倾斜/轴向晶轴及玻璃环境，最大误差小于 1.1e-14。网格示例的原生最大步长从 1、0.1、0.02、0.005 降至 0.0005、0.0001、0.00002；最细的三个代表光线共 60 个独立量全部满足原有显式光线界限（另三行仅作光程参考）。其最大绝对光程差约 9.87e-6 mm，没有把该内部点控制替代为全部示例曲线通过。粗步长原生记录均保留。自定义色散网格的界面指数已对齐，体传播仍有实际差异。

当前精度后续工作仍包括：Grid GRIN 一阶定义及自定义网格体传播；晶体方块配置 2 的一阶/能量结果；旧批次 Grid Sag 一阶量和强非空气 Paraxial XY 相位差异；27 项不可比请求的直接原生参考。旧网格示例分析参考的收敛未逐项确认，因此其旧 Difference 不直接等同于已证实的当前计算错误，也不升级为 Pass。库外新模式、偏振和未验证格式不纳入本批完成数。

[逐镜头表](../validation/rechecks/2026-11-01/model-capabilities-stage5/BY_LENS.md)、[238 项结果](../validation/rechecks/2026-11-01/model-capabilities-stage5/results.json)、[原 751 项当前模型清单](../validation/rechecks/2026-11-01/model-capabilities-stage5/capability-progress-original751.json)、[精度后续项](../validation/rechecks/2026-11-01/model-capabilities-stage5/accuracy-followup.json)。历史批次结果由不同 Core 产生，未合并成当前 Core 的精度通过率。

定义依据：[单轴晶体](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Birefringent_In_and_Birefringent_Out.html)、[Grid Gradient 文件与体积分](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Grid_Gradient.html)。网格插值具体路径以独立原生控制为证据，未加载原生 DLL 参与产品求解。
"@
[IO.File]::WriteAllText((Join-Path $repo 'docs/MODEL_CAPABILITIES_STAGE5_2026-11-01.md'),$doc);[IO.File]::WriteAllText("$report/README.md",$doc)
$statusPath=Join-Path $repo 'docs/CURRENT_STATUS.md';$status=[IO.File]::ReadAllText($statusPath);$section=@"

## 2026-11-01 原 751 项模型阻塞清零，精度差异继续保留

纯 C# 共享 Core 接入均匀单轴晶体和完整 GGD 网格体；当前重新导入清单确认原 751 项、原 1,098 项模型阻塞均为 0。最后 5 支、238 项重算为 192 Pass / 3 Close / 16 Difference / 27 Incomparable / 0 Error。215 项定向回归和默认 Release App 构建通过。模型能力解除不等于全部分析精度通过；网格一阶/自定义体传播、晶体方块配置 2 及旧批次差异仍开放。详见[本批范围、结果和边界](MODEL_CAPABILITIES_STAGE5_2026-11-01.md)。
"@
if($status.Contains('## 2026-11-01 原 751 项模型阻塞')){throw 'Status section exists'};$nl=$status.IndexOf("`n");if([IO.File]::ReadAllText($statusPath) -ne $status){throw 'Concurrent status edit; retry merge'};[IO.File]::WriteAllText($statusPath,$status.Substring(0,$nl+1)+$section+"`n"+$status.Substring($nl+1))
$entries=[Collections.Generic.List[object]]::new();$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Payload([string]$p,[string]$name){if(-not $names.Add($name)){throw 'Duplicate payload'};$entries.Add([pscustomobject]@{path=$p;name=$name;bytes=(Get-Item -LiteralPath $p).Length;sha256=(Hash $p)})}
foreach($pair in @(@($study,'study'),@($runtime,'runtime'),@($report,'report'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File){Add-Payload $f.FullName ($pair[1]+'/'+[IO.Path]::GetRelativePath($pair[0],$f.FullName).Replace('\','/'))}}
foreach($folder in 'birefringent-native-20261031','grid-gradient-native-20261101','grid-gradient-polynomial-native-20261101','grid-gradient-axis-rule-native-20261101','grid-gradient-index-api-native-20261101','grid-gradient-convergence-native-20261101','grid-gradient-firstorder-native-20261101'){
 $dir=Join-Path $repo "artifacts/validation/$folder";foreach($f in Get-ChildItem -LiteralPath $dir -Recurse -File|Where-Object {$_.Extension -in '.json','.ZMX','.zmx','.ggd'}){Add-Payload $f.FullName ('native/'+$folder+'/'+[IO.Path]::GetRelativePath($dir,$f.FullName).Replace('\','/'))}
}
foreach($rel in 'docs/MODEL_CAPABILITIES_STAGE5_2026-11-01.md','tools/diagnostics/ParaxialCapabilities20261031/Seal-Stage5.ps1','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-stage5-20261101.trx','artifacts/validation/model-capabilities-stage5-final-tests-20261101.log','artifacts/validation/model-capabilities-stage5-app-build-20261101.log','artifacts/validation/model-capabilities-archived-runtime-space-recovery-20261031.json','artifacts/validation/model-capabilities-archived-study-space-recovery-20261101.json'){
 Add-Payload (Join-Path $repo $rel) ('proof/'+$rel)
}
foreach($name in 'ParaxialCapabilities20261031.dll','ParaxialCapabilities20261031.deps.json','ParaxialCapabilities20261031.runtimeconfig.json'){
 Add-Payload (Join-Path $repo "tools/diagnostics/ParaxialCapabilities20261031/bin/Release/net10.0/$name") ('runtime/'+$name)
}
foreach($rel in (& rg --files tests/OpticalSystemDesign.Tests|Where-Object {$_ -match '(ParaxialLensModelTests|GridGradientModelTests|GradientIndexParaxialTests|GradientIndexOperandTests|GradientIndexReturnPathTests|ParaxialTransferMatrixTests|ReflectiveParaxialSignTests)\.cs$' -or
 $_ -match 'Fixtures/(paraxial|grating-phase|diffractive|irregular-native|grid-sag|dual-zone|birefringent|grid-gradient|gradient1-return).*\.json$'})){Add-Payload (Join-Path $repo $rel) ('proof/'+$rel.Replace('\','/'))}
$manifest=@($entries|Select-Object name,bytes,sha256);$manifest|ConvertTo-Json -Depth 5|Set-Content "$report/archive-manifest.json" -Encoding utf8
Add-Type -AssemblyName System.IO.Compression;$archive=Join-Path $repo 'validation/repairs/2026-11-01/model-capabilities-stage5-evidence.zip';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($archive))|Out-Null
if(Test-Path -LiteralPath $archive){throw 'Immutable archive exists'};$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($e in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$e.path,$e.name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/archive-manifest.json",'archive-manifest.json',[IO.Compression.CompressionLevel]::Fastest)|Out-Null}finally{$zip.Dispose()}
$verified=0;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $manifest){$entry=$zip.GetEntry($e.name);if($null -eq $entry -or $entry.Length -ne $e.bytes){throw 'Size mismatch'};$s=$entry.Open();try{$h=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($h -ne $e.sha256){throw 'Hash mismatch'};$verified++}}finally{$zip.Dispose()}
[pscustomobject]@{archive='validation/repairs/2026-11-01/model-capabilities-stage5-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;verifiedPayloads=$verified;allPayloadsReadBack=$true;coreSha256=$core
 storageNote='Historical duplicate unpacked study/runtime files were removed only after matching every file to verified immutable archives; reports and raw native captures retained. Restore runtime/ and study/ from those archives when reproducing older stages.'}|ConvertTo-Json|Set-Content "$report/verification.json" -Encoding utf8
Get-Content "$report/summary.json";Get-Content "$report/verification.json"
