param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
function Read-Json([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
$study=Join-Path $repo 'artifacts/validation/model-capabilities-stage4-20261031';$runtime=Join-Path $repo 'artifacts/validation/model-capabilities-stage4-runner-20261031'
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage4'
if(Test-Path -LiteralPath $report){throw 'Fresh report required'};New-Item -ItemType Directory -Path $report|Out-Null
$raw=Read-Json "$study/summary.json";$core=$raw.coreSha256
if((Hash "$runtime/OpticalSystemDesign.Core.dll") -ne $core){throw 'Frozen Core mismatch'}
$prior=Read-Json (Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage3/capability-progress.json')
$capMap=@{};foreach($c in Read-Json "$study/capability-inventory.json"){$capMap["$($c.lensIndex):$($c.configuration)"]=@($c.issues)}
$current=@{};foreach($r in Read-Json "$study/results.json"){$current[$r.index]=$r}
$progress=@(foreach($r in $prior|Where-Object {-not $_.modelGateRemoved}){
 $issues=@($capMap["$($r.lensIndex):$($r.configuration)"]);$clear=$issues.Count -eq 0
 if($clear -and -not $current.ContainsKey($r.index)){throw 'Unreplayed request'}
 [pscustomobject]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key;modelGateRemoved=$clear
  remainingIssues=$issues;currentConclusion=if($clear){$current[$r.index].conclusion}else{'NotReplayed_StillBlocked'};currentError=if($clear){$current[$r.index].error}else{$null}}
})
if($progress.Count -ne 302 -or @($progress|Where-Object modelGateRemoved).Count -ne 65){throw 'Unexpected scope'}
$progress|ConvertTo-Json -Depth 24|Set-Content "$report/capability-progress.json" -Encoding utf8
[pscustomobject]@{status='InProgress';priorUnsupportedRequests=302;newlyUnblockedRequests=65;remainingModelBlocked=237;original751NewlyUnblocked=514
 cumulativeOriginal1098Unblocked=861;replayedLenses=2;replayedRequests=66;replayedResultCounts=$raw.counts;all751Implemented=$false;all95Replayed=$false
 referenceConvergenceCertified=$false;strongNonAirXYPhaseDifference='Open';gridFirstOrderDifferenceFromPriorStage='Open';coreSha256=$core;focusedTestsPassed=172
 defaultAppBuild='Passed_ZeroWarnings_ZeroErrors';runner='Prior comparison binaries copied, fresh Core and complete assembly hashes bound in identity.json'}|ConvertTo-Json -Depth 15|Set-Content "$report/summary.json" -Encoding utf8
foreach($name in 'results','lens-results','capability-inventory','scope','identity'){Copy-Item -LiteralPath "$study/$name.json" -Destination "$report/$name.json"}
$controls=@(foreach($folder in 'binary3-native-20261031-03','paraxial-xy-native-20261031'){
 foreach($r in Read-Json (Join-Path $repo "artifacts/validation/$folder/product-controls.json")){
  if($r.coreSha256 -ne $core){throw 'Mixed control Core'};$limit=1e-8+1e-6*[math]::Abs($r.expected)
  [pscustomobject]@{capture=$folder;id=$r.id;code=$r.code;input=$r.input;expected=$r.expected;actual=$r.actual;absoluteError=[math]::Abs($r.error)
   controlBound=$limit;coreSha256=$core;sourceSha256=$r.sourceSha256;referenceOnly=$r.referenceOnly
   verdict=if($r.referenceOnly){'ReferenceRow_NotIndependentValidation'}elseif([math]::Abs($r.error) -le $limit){'WithinExistingExplicitRayBound'}else{'Difference'}}
 }
})
$controls|ConvertTo-Json -Depth 20|Set-Content "$report/native-controls.json" -Encoding utf8
@($controls|Group-Object capture,verdict|ForEach-Object {[pscustomobject]@{scope=$_.Name;count=$_.Count;maximumAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})|ConvertTo-Json|Set-Content "$report/native-controls-summary.json" -Encoding utf8
$doc=@"
# 模型能力补齐第四批，2026-10-31

上批剩余 302 项中，**65 项解除模型能力阻塞，237 项仍未支持**。用户指定的 751 项累计解除 514 项；原始 1,098 项累计解除 861 项。解除阻塞不代表精度通过。

纯 C# 共享 Core 新增 Binary 3 的双区圆锥/偶次非球面、独立归一化衍射相位、级次及恒定相位偏移。两区分别求交，保留断差和边界内区归属；连续模式仅平移外区矢高，断差模式不平移。外区相位在两种模式均连续。完整 STAROPT/ZMX 往返及真实长度单位转换已核验，现代 PARM 13+ 与旧 XDAT 均可导入；原生控制使用实际可载入的 XDAT 保存格式。

Paraxial XY 共享理想近轴交互和物方光束积分，独立 X/Y 空气光焦度单位为 mm^-1。一阶标量结果沿原有 Y 子午定义，不把 Y 功率复用为真实 X 光线功率。非对称导出保留 PARAX_XY，未使用单焦距代替。

相关 2 支示例、66 项原请求后台重算：**56 Pass、2 Close、3 Difference、5 Incomparable**，无执行错误。原门槛和旧参考保持，Close 不计通过，也未认证旧分析参考均已收敛。双区直接原生点控制覆盖两种断差、两波长及空气/玻璃/反射，最大误差约 6.97e-11 mm。XY 控制中，强光焦度、非空气、偏轴斜光线仍有最大约 1.21e-7 mm 的相位差，保留 Difference；方向和坐标已对齐。首轮未载入现代扩展系数的原生控制和受沙箱 IPC 限制的失败捕获均保留，不计精度通过。

172 项定向回归通过，默认 Release App 构建零警告、零错误，测试、App 与冻结运行器 Core 哈希相同：$core。剩余为双折射相关 208 项和 Grid GRIN 29 项。上一批网格一阶量的实际差异仍开放，不能因本批能力完成而关闭。

[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage4/results.json)、[能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage4/capability-progress.json)、[直接原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage4/native-controls.json)。原生后台独立实例截图关闭，不使用用户桌面。定义依据：[Binary 3](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Binary_3.html)、[Paraxial XY](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_XY.html)。
"@
[IO.File]::WriteAllText((Join-Path $repo 'docs/MODEL_CAPABILITIES_STAGE4_2026-10-31.md'),$doc);[IO.File]::WriteAllText("$report/README.md",$doc)
$statusPath=Join-Path $repo 'docs/CURRENT_STATUS.md';$status=[IO.File]::ReadAllText($statusPath)
$section=@"

## 2026-10-31 模型能力第四批：65 项解除阻塞，剩余 237 项

纯 C# 共享 Core 接入 Binary 3 和 Paraxial XY，用户指定 751 项累计解除 514 项。2 支、66 项重算为 56 Pass / 2 Close / 3 Difference / 5 Incomparable。172 项定向测试及默认 Release App 构建通过；强非空气 XY 相位控制和上一批网格一阶量仍有差异。剩余双折射相关 208 项、Grid GRIN 29 项。详见[本批结果和边界](MODEL_CAPABILITIES_STAGE4_2026-10-31.md)。
"@
if($status.Contains('## 2026-10-31 模型能力第四批')){throw 'Status section exists'};$nl=$status.IndexOf("`n");[IO.File]::WriteAllText($statusPath,$status.Substring(0,$nl+1)+$section+"`n"+$status.Substring($nl+1))
$entries=[Collections.Generic.List[object]]::new();$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Payload([string]$p,[string]$name){if(-not $names.Add($name)){throw 'Duplicate payload'};$entries.Add([pscustomobject]@{path=$p;name=$name;bytes=(Get-Item -LiteralPath $p).Length;sha256=(Hash $p)})}
foreach($pair in @(@($study,'study'),@($runtime,'runtime'),@($report,'report'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File){Add-Payload $f.FullName ($pair[1]+'/'+[IO.Path]::GetRelativePath($pair[0],$f.FullName).Replace('\','/'))}}
foreach($folder in 'binary3-native-20261031','binary3-native-20261031-02','binary3-native-20261031-03','paraxial-xy-native-20261031'){
 $dir=Join-Path $repo "artifacts/validation/$folder";foreach($f in Get-ChildItem -LiteralPath $dir -Recurse -File|Where-Object {$_.Extension -in '.json','.ZMX','.zmx'}){Add-Payload $f.FullName ('native/'+$folder+'/'+[IO.Path]::GetRelativePath($dir,$f.FullName).Replace('\','/'))}
}
foreach($rel in 'docs/MODEL_CAPABILITIES_STAGE4_2026-10-31.md','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-stage4-final-20261031.trx','artifacts/validation/model-capabilities-stage4-final-tests-20261031.log','artifacts/validation/model-capabilities-stage4-final-app-build-20261031.log','tests/OpticalSystemDesign.Tests/Fixtures/dual-zone-native-v1.json'){
 Add-Payload (Join-Path $repo $rel) ('proof/'+$rel)
}
$manifest=@($entries|Select-Object name,bytes,sha256);$manifest|ConvertTo-Json -Depth 5|Set-Content "$report/archive-manifest.json" -Encoding utf8
Add-Type -AssemblyName System.IO.Compression;$archive=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage4-evidence.zip'
if(Test-Path -LiteralPath $archive){throw 'Immutable archive exists'};$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($e in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$e.path,$e.name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/archive-manifest.json",'archive-manifest.json',[IO.Compression.CompressionLevel]::Fastest)|Out-Null}finally{$zip.Dispose()}
$verified=0;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $manifest){$entry=$zip.GetEntry($e.name);if($null -eq $entry -or $entry.Length -ne $e.bytes){throw 'Size mismatch'};$s=$entry.Open();try{$h=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($h -ne $e.sha256){throw 'Hash mismatch'};$verified++}}finally{$zip.Dispose()}
[pscustomobject]@{archive='validation/repairs/2026-10-31/model-capabilities-stage4-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;verifiedPayloads=$verified;allPayloadsReadBack=$true;coreSha256=$core}|ConvertTo-Json|Set-Content "$report/verification.json" -Encoding utf8
Get-Content "$report/summary.json";Get-Content "$report/verification.json"
