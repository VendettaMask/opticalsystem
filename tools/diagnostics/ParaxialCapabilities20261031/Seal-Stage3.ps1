param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'; $repo=(Resolve-Path -LiteralPath $Repository).Path
function Read-Json([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
$study=Join-Path $repo 'artifacts/validation/model-capabilities-stage3-20261031'
$runtime=Join-Path $repo 'artifacts/validation/model-capabilities-stage3-runner-20261031'
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage3'
if(Test-Path -LiteralPath $report){throw 'Fresh report required'}
New-Item -ItemType Directory -Path $report|Out-Null
$raw=Read-Json "$study/summary.json"; $core=$raw.coreSha256
if((Hash "$runtime/OpticalSystemDesign.Core.dll") -ne $core){throw 'Frozen Core mismatch'}
$prior=Read-Json (Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage2/capability-progress.json')
$caps=Read-Json "$study/capability-inventory.json"; $results=Read-Json "$study/results.json"
$capMap=@{}; foreach($c in $caps){$capMap["$($c.lensIndex):$($c.configuration)"]=@($c.issues)}
$current=@{}; foreach($r in $results){$current[$r.index]=$r}
$progress=@(foreach($r in $prior|Where-Object {-not $_.modelGateRemoved}){
 $issues=@($capMap["$($r.lensIndex):$($r.configuration)"]);$clear=$issues.Count -eq 0
 if($clear -and -not $current.ContainsKey($r.index)){throw 'Unreplayed cleared request'}
 [pscustomobject]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key;modelGateRemoved=$clear
  remainingIssues=$issues;currentConclusion=if($clear){$current[$r.index].conclusion}else{'NotReplayed_StillBlocked'};currentError=if($clear){$current[$r.index].error}else{$null}}
})
$cleared=@($progress|Where-Object modelGateRemoved);$remaining=@($progress|Where-Object {-not $_.modelGateRemoved})
if($progress.Count -ne 409 -or $cleared.Count -ne 107 -or $results.Count -ne 110){throw 'Unexpected stage scope'}
$counts=[ordered]@{};foreach($g in $cleared|Group-Object currentConclusion){$counts[$g.Name]=$g.Count}
$types=@($remaining|ForEach-Object {$_.remainingIssues.originalType|Sort-Object -Unique}|Group-Object|Sort-Object Count -Descending|ForEach-Object {[pscustomobject]@{type=$_.Name;requests=$_.Count}})
[pscustomobject]@{status='InProgress';priorUnsupportedRequests=409;newlyUnblockedRequests=107;remainingModelBlocked=302
 original751NewlyUnblocked=449;cumulativeOriginal1098Unblocked=796;newlyUnblockedResultCounts=$counts;replayedLenses=4;replayedRequests=110
 replayedResultCounts=$raw.counts;remainingTypeCountsOverlap=$types;all751Implemented=$false;all95Replayed=$false;referenceConvergenceCertified=$false
 gridFirstOrderNativeDifference='Open';coreSha256=$core;focusedTestsPassed=157;defaultAppBuild='Passed_ZeroWarnings_ZeroErrors'}|ConvertTo-Json -Depth 18|Set-Content "$report/summary.json" -Encoding utf8
$progress|ConvertTo-Json -Depth 24|Set-Content "$report/capability-progress.json" -Encoding utf8
foreach($name in 'results','lens-results','capability-inventory','scope','identity'){Copy-Item -LiteralPath "$study/$name.json" -Destination "$report/$name.json"}
foreach($pair in @(@('grid-sag-native-20261031-02','product-controls.json','grid-native-controls.json'),@('grid-firstorder-native-20261031','product-firstorder.json','grid-firstorder-controls.json'))){
 $p=Join-Path $repo "artifacts/validation/$($pair[0])/$($pair[1])";foreach($r in Read-Json $p){if($r.coreSha256 -ne $core){throw 'Mixed Core controls'}}
 Copy-Item -LiteralPath $p -Destination "$report/$($pair[2])"
}
$doc=@"
# 模型能力补齐第三批，2026-10-31

上批剩余 409 项中，**107 项解除模型能力阻塞，302 项仍未支持**。用户指定的 751 项累计解除 449 项；原始 1,098 项累计解除 796 项。解除模型阻塞不代表精度通过。

纯 C# 共享 Core 新增嵌入式 Grid Sag 的双三次 Hermite/线性插值、真实节点导数、完整序列化和 ZMX 往返；有限网格外返回无交点。原生线性网格的矢高和法向场分别插值，经独立方向反推验证。保存文件的全零导数保持零值，不擅自重新估计。当前仅激活经过验证的毫米单位、完整 GDAT/GARR、普通变换标志和零 Zernike 扩展；其余格式保留不透明边界。

PARAXIAL OPD 模式 0 仅在无限物距、上游平面均匀介质且保持平面波时激活，并在每次实际计算时复核。有限物距、上游像差及模式 3 不采用别的模式近似。逆向理想近轴作用修正有符号传播方向，增加折射率变化下互易控制。

相关 4 支示例、110 项原请求后台重算：**59 Pass、15 Difference、30 Incomparable、6 Error**。原生参考缺口、非有限一阶结果和分析执行边界如实保留；原比较门槛未修改，原 95 支总账未覆写。Grid Sag 原生点控制四种数据各 193 行，最大误差小于 7.8e-11；网格一阶焦距的直接原生控制仍有实际差异，尤其线性插值网格。原生曲率估计与口径相关，当前顶点导数规则尚未对齐，**不宣称网格全部分析精度通过**。

157 项针对性测试通过，默认 Release App 构建零警告、零错误。冻结 Core：$core。后续剩余模型为双折射入/出面 208 项、Binary 3 44 项、Grid GRIN 29 项、Paraxial XY 21 项；此处计数按请求去重，双折射相关近轴面阻塞仍保留。

[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage3/results.json)、[能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage3/capability-progress.json)、[网格直接原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage3/grid-native-controls.json)、[一阶量未关闭差异](../validation/rechecks/2026-10-31/model-capabilities-stage3/grid-firstorder-controls.json)。证据中的源码为运行前冻结快照。后台原生捕获不占桌面，不使用截图。
"@
[IO.File]::WriteAllText((Join-Path $repo 'docs/MODEL_CAPABILITIES_STAGE3_2026-10-31.md'),$doc)
[IO.File]::WriteAllText("$report/README.md",$doc)
$statusPath=Join-Path $repo 'docs/CURRENT_STATUS.md';$status=[IO.File]::ReadAllText($statusPath)
$section=@"

## 2026-10-31 模型能力第三批：107 项解除阻塞，剩余 302 项

纯 C# 共享 Core 接入嵌入式 Grid Sag 及有明确平面波前提的近轴 OPD 模式 0。用户指定 751 项累计解除 449 项。4 支、110 项重算为 59 Pass / 15 Difference / 30 Incomparable / 6 Error；网格一阶量直接原生差异仍开放，不能视为精度通过。157 项定向测试及默认 Release App 构建通过。详见[本批范围和证据](MODEL_CAPABILITIES_STAGE3_2026-10-31.md)。
"@
if($status.Contains('## 2026-10-31 模型能力第三批')){throw 'Status exists'}
$nl=$status.IndexOf("`n");[IO.File]::WriteAllText($statusPath,$status.Substring(0,$nl+1)+$section+"`n"+$status.Substring($nl+1))
$entries=[Collections.Generic.List[object]]::new()
function Add-Payload([string]$p,[string]$name){$entries.Add([pscustomobject]@{path=$p;name=$name;bytes=(Get-Item -LiteralPath $p).Length;sha256=(Hash $p)})}
foreach($pair in @(@($study,'study'),@($runtime,'runtime'),@($report,'report'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File){Add-Payload $f.FullName ($pair[1]+'/'+[IO.Path]::GetRelativePath($pair[0],$f.FullName).Replace('\','/'))}}
foreach($folder in 'grid-sag-native-20261031','grid-sag-native-20261031-02','grid-firstorder-native-20261031','grid-curvature-radius-native-20261031'){
 $dir=Join-Path $repo "artifacts/validation/$folder";foreach($f in Get-ChildItem -LiteralPath $dir -Recurse -File|Where-Object {$_.Extension -in '.json','.ZMX','.zmx'}){Add-Payload $f.FullName ('native/'+$folder+'/'+[IO.Path]::GetRelativePath($dir,$f.FullName).Replace('\','/'))}
}
foreach($rel in 'docs/MODEL_CAPABILITIES_STAGE3_2026-10-31.md','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-stage3-20261031.trx','artifacts/validation/model-capabilities-stage3-tests-20261031.log','artifacts/validation/model-capabilities-stage3-app-build-20261031.log'){
 Add-Payload (Join-Path $repo $rel) ('proof/'+$rel)
}
$manifest=@($entries|Select-Object name,bytes,sha256);$manifest|ConvertTo-Json -Depth 5|Set-Content "$report/archive-manifest.json" -Encoding utf8
Add-Type -AssemblyName System.IO.Compression
$archive=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage3-evidence.zip'
if(Test-Path -LiteralPath $archive){throw 'Immutable archive exists'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($e in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$e.path,$e.name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/archive-manifest.json",'archive-manifest.json',[IO.Compression.CompressionLevel]::Fastest)|Out-Null}finally{$zip.Dispose()}
$verified=0;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $manifest){$entry=$zip.GetEntry($e.name);if($null -eq $entry -or $entry.Length -ne $e.bytes){throw 'Size mismatch'};$s=$entry.Open();try{$h=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($h -ne $e.sha256){throw 'Hash mismatch'};$verified++}}finally{$zip.Dispose()}
[pscustomobject]@{archive='validation/repairs/2026-10-31/model-capabilities-stage3-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;verifiedPayloads=$verified;allPayloadsReadBack=$true;coreSha256=$core}|ConvertTo-Json|Set-Content "$report/verification.json" -Encoding utf8
Get-Content "$report/summary.json";Get-Content "$report/verification.json"
