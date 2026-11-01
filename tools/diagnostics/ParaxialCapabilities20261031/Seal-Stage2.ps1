param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
$study=Join-Path $repo 'artifacts/validation/model-capabilities-stage2-20261031'
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage2'
if(Test-Path -LiteralPath $report){throw 'Fresh report required'}
New-Item -ItemType Directory -Path $report|Out-Null
function Read-Json([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
$runtime=Join-Path $repo 'artifacts/validation/model-capabilities-stage2-runner-20261031'
$rawSummary=Read-Json "$study/summary.json";$core=$rawSummary.coreSha256
if((Hash "$runtime/OpticalSystemDesign.Core.dll") -ne $core -or (Hash (Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll')) -ne $core){throw 'Core identity mismatch'}
$prior=Read-Json (Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage1/capability-progress.json')
$caps=Read-Json "$study/capability-inventory.json";$results=Read-Json "$study/results.json"
$capMap=@{};foreach($c in $caps){$capMap["$($c.lensIndex):$($c.configuration)"]=$c.issues}
$current=@{};foreach($r in $results){$current[$r.index]=$r}
$progress=@(foreach($r in $prior|Where-Object {-not $_.modelGateRemoved}){
 $issues=@($capMap["$($r.lensIndex):$($r.configuration)"]);$cleared=$issues.Count -eq 0
 if($cleared -and -not $current.ContainsKey($r.index)){throw 'Unreplayed cleared request'}
 [pscustomobject]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key
  modelGateRemoved=$cleared;remainingIssues=$issues;currentConclusion=if($cleared){$current[$r.index].conclusion}else{'NotReplayed_StillBlocked'}
  currentError=if($cleared){$current[$r.index].error}else{$null}}
})
if($progress.Count -ne 751 -or $results.Count -ne 344){throw 'Unexpected stage scope'}
$cleared=@($progress|Where-Object modelGateRemoved);$remaining=@($progress|Where-Object {-not $_.modelGateRemoved})
$counts=[ordered]@{};foreach($g in $cleared|Group-Object currentConclusion){$counts[$g.Name]=$g.Count}
$types=@($remaining|ForEach-Object {$_.remainingIssues.originalType|Sort-Object -Unique}|Group-Object|Sort-Object Count -Descending|ForEach-Object {[pscustomobject]@{type=$_.Name;requests=$_.Count}})
$summary=[pscustomobject]@{status='InProgress';priorUnsupportedRequests=751;newlyUnblockedRequests=$cleared.Count;remainingModelBlocked=$remaining.Count
 cumulativeOriginal1098Unblocked=1098-$remaining.Count;newlyUnblockedResultCounts=$counts;replayedLenses=12;replayedRequests=344
 replayedResultCounts=$rawSummary.counts;remainingTypeCountsOverlap=$types;all751Implemented=$false;all95Replayed=$false
 referenceConvergenceCertified=$false;coreSha256=$core;focusedTestsPassed=150;defaultAppBuild='Passed_ZeroWarnings_ZeroErrors'}
$summary|ConvertTo-Json -Depth 15|Set-Content "$report/summary.json" -Encoding utf8
$progress|ConvertTo-Json -Depth 22|Set-Content "$report/capability-progress.json" -Encoding utf8
foreach($name in 'results','lens-results','capability-inventory','scope','identity'){Copy-Item -LiteralPath "$study/$name.json" -Destination "$report/$name.json"}
$controls=@(foreach($folder in 'paraxial-phase-native-20261031-02','grating-phase-native-20261031','binary-hologram-native-20261031','irregular-native-20261031'){
 foreach($r in Read-Json (Join-Path $repo "artifacts/validation/$folder/product-controls.json")){
  if($r.coreSha256 -ne $core){throw 'Mixed control Core'}
  $limit=1e-8+1e-6*[math]::Abs($r.expected)
  [pscustomobject]@{capture=$folder;id=$r.id;code=$r.code;input=$r.input;expected=$r.expected;actual=$r.actual;absoluteError=[math]::Abs($r.error)
   controlBound=$limit;coreSha256=$core;sourceSha256=$r.sourceSha256;pathReference=$r.pathReference;referenceOnly=$r.referenceOnly
   verdict=if($r.referenceOnly){'ReferenceRow_NotIndependentValidation'}elseif([math]::Abs($r.error) -le $limit){'WithinExistingExplicitRayBound'}else{'Difference'}}
 }
})
$controls|ConvertTo-Json -Depth 18|Set-Content "$report/native-controls.json" -Encoding utf8
@($controls|Group-Object capture,verdict|ForEach-Object {[pscustomobject]@{scope=$_.Name;count=$_.Count;maximumAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})|ConvertTo-Json -Depth 10|Set-Content "$report/native-controls-summary.json" -Encoding utf8
$lines=@('# 本批逐镜头结果','','P/C/D/I/E 为通过、接近未通过、差异、不可比、执行失败。原 95 支账目保持。','','| 镜头 | 请求 | P | C | D | I | E |','|---|---:|---:|---:|---:|---:|---:|')
foreach($lens in Read-Json "$study/lens-results.json"){
 $n=@{};foreach($p in $lens.counts.PSObject.Properties){$n[$p.Name]=[int]$p.Value}
 $lines+='| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $lens.lens,$lens.total,([int]$n.Pass),([int]$n.Close),([int]$n.Difference),([int]$n.Incomparable),([int]$n.Error)
};$lines|Set-Content "$report/BY_LENS.md" -Encoding utf8
$doc=@"
# 模型能力补齐第二批，2026-10-31

针对上一批剩余 751 项，本批 **342 项解除模型能力阻塞，409 项仍被剩余模型阻塞**。原 1,098 项累计解除 689 项；解除阻塞不等于精度通过。全部产品计算为纯 C#、共享 Core，原请求和门槛未修改，后台原生控制未使用桌面或截图。

相关 12 支示例、344 项原请求按同一冻结 Core 重算：**279 Pass、8 Close、20 Difference、35 Incomparable、2 Error**。Close 不计通过。两项 Error 为光谱仪和远心示例的相对照度瞳孔参考距离边界。原 95 支总账保持，没有将本批结果冒充全部 95 支收敛认证。

实现内容：

- PARAXIAL：独立理想平面作用，正确的折射率/空气焦距及零功率；接入 OPD 模式 1 的光束相位积分和模式 2 的无限共轭。主光线相位为零，逆向瞄准和积分均调用共享几何追迹，未收敛明确失败。模式 0/3、反射近轴面及 PARAX_XY 继续保留原不透明能力边界。
- DGRATING：局部 X 方向直线刻槽、每微米线密度、正负级次、折射/反射及曲面基底，方向和相位一起计算。
- BINARY_2：独立偶次非球面基底、最多 240 个归一化相位项；保留归一化半径、衍射级及真实波长单位，一阶传递包含衍射光焦度。现代 PARM 13+、XDAT 及配置参数均保留。
- HOLOGRM2：平面基底上会聚/发散构建光束、偏轴构建点、构建波长及级次，保留原生绝对相位常数；曲面及体全息模式未映射。
- IRREGULA：基准圆锥、球差/像散/彗差、归一化半径和方位角；临时偏心、依次局部 X/Y 倾斜及撤销，不旋转后续表面。净半口径编辑同步其归一化，完整几何可保存及往返。
- 共享波前和追迹的 OPD 汇总读取包含表面相位的光程，独立保留几何光程列；Jones 运输也计入本步表面相位。新增带明确单位与正光程约定的衍射交互，不重定义原 phase 类型的历史约定。缺少完整列的文本导出明确拒绝压平。

150 项针对性测试通过，默认 Release App 构建零警告、零错误，App 与冻结运行器 Core 哈希一致。新原生点控制覆盖有限/无限共轭、像差、非空气、正负级、多波长、平面/曲面、反射、单位转换和文件往返。近轴像差控制最大光程误差约 4.18e-8 mm；其余光栅、Binary 2、全息与不规则控制最大误差小于 3.7e-12。以第一物理面作差的 OPTH 参考行不计独立验证。另有 18 行原生一阶控制；其中 EFFL 请求的是 wave 0 主波长，不宣称捕获了两条独立波长的 EFFL。

剩余能力计数存在重叠：PARAXIAL 251、BIRE__IN/BIRE_OUT 各 208、GRID_SAG 64、BINARY_3 44、GRINSU11 29、PARAX_XY 21。不能求和。尚有 28 项 Close/Difference、35 项参考缺口和两项执行边界，不纳入本次模型能力完成数。首轮含 GLAS AIR 的退化瞳孔控制、模式 0/3 探索和不规则倾斜顺序修正前差异保留，不计通过。

定义依据：[原生近轴面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html)、[Binary 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Binary_2_sequential_surfaces_lens_data_editor.html)、[全息 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Hologram_2.html)、[不规则面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Irregular.html)。刻槽模型另经本机原生直接控制核验，不依赖运行时 DLL。

[逐镜头表](../validation/rechecks/2026-10-31/model-capabilities-stage2/BY_LENS.md)、[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage2/results.json)、[751 项能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage2/capability-progress.json)和[原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage2/native-controls.json)。冻结 Core：$core。
"@
[IO.File]::WriteAllText((Join-Path $repo 'docs/MODEL_CAPABILITIES_STAGE2_2026-10-31.md'),$doc)
$readme=@"
# 模型能力补齐第二批证据

新导入覆盖全部 95 支源文件及 114 个可用配置；重算仅为本批 12 支、344 个原请求。原 751 项中解除 342，剩余 409。详细范围见 summary.json、scope.json、capability-progress.json 和 BY_LENS.md。

原 2026-10-21 分析参考和比较门槛保持，未重新认证其收敛。新增原生点控制使用独立 CreateNewApplication、截图关闭及任务拥有的进程隔离。archive-manifest.json 对不可变 ZIP 的每个非清单载荷列出大小和 SHA-256；verification.json 保存整个 ZIP 哈希及逐载荷读回结果。原第一批和原 95 支账目不改写。
"@;[IO.File]::WriteAllText("$report/README.md",$readme)
$statusPath=Join-Path $repo 'docs/CURRENT_STATUS.md';$status=[IO.File]::ReadAllText($statusPath)
$section=@"

## 2026-10-31 模型能力第二批：剩余 751 项中的 342 项解除阻塞

纯 C# 共享 Core 接入近轴 OPD 模式 1/2、直线刻槽光栅、Binary 2、平面全息 2 和不规则面。剩余模型阻塞 **409 项**；相关 12 支、344 项重算 **279 Pass / 8 Close / 20 Difference / 35 Incomparable / 2 Error**，解除阻塞不等于精度通过。150 项针对性测试和默认 Release App 构建通过，仍无全部 95 支收敛认证或桌面 GUI 验收。详见[实现、结果与边界](MODEL_CAPABILITIES_STAGE2_2026-10-31.md)及[证据](../validation/rechecks/2026-10-31/model-capabilities-stage2/README.md)。
"@
if($status.Contains('## 2026-10-31 模型能力第二批')){throw 'Status section exists'}
$firstNewline=$status.IndexOf("`n");[IO.File]::WriteAllText($statusPath,$status.Substring(0,$firstNewline+1)+$section+"`n"+$status.Substring($firstNewline+1))
$entries=[Collections.Generic.List[object]]::new();$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Payload([string]$path,[string]$name){if(-not $paths.Add($name)){throw "Duplicate archive name: $name"};$script:entries.Add([pscustomobject]@{path=$path;name=$name;bytes=(Get-Item -LiteralPath $path).Length;sha256=(Hash $path)})}
foreach($f in Get-ChildItem -LiteralPath $study -Recurse -File){Add-Payload $f.FullName ('study/'+[IO.Path]::GetRelativePath($study,$f.FullName).Replace('\','/'))}
foreach($f in Get-ChildItem -LiteralPath $runtime -Recurse -File){Add-Payload $f.FullName ('runtime/'+[IO.Path]::GetRelativePath($runtime,$f.FullName).Replace('\','/'))}
foreach($folder in 'paraxial-phase-native-20261031','paraxial-phase-native-20261031-02','grating-phase-native-20261031','binary-hologram-native-20261031','irregular-native-20261031','diffractive-firstorder-native-20261031'){
 $base=Join-Path $repo "artifacts/validation/$folder";foreach($f in Get-ChildItem -LiteralPath $base -Recurse -File|Where-Object {$_.Extension -in '.json','.ZMX','.zmx'}){
  Add-Payload $f.FullName ('native/'+$folder+'/'+[IO.Path]::GetRelativePath($base,$f.FullName).Replace('\','/'))
 }
}
foreach($relative in (& rg --files src/OpticalSystemDesign.Core tools/diagnostics/ParaxialCapabilities20261031 tests/OpticalSystemDesign.Tests/Fixtures|Where-Object {$_ -like 'src/*' -or $_ -like 'tools/*' -or $_ -match '(paraxial-phase|grating-phase|diffractive-phase|diffractive-firstorder|irregular-native)-native-v1\.json$' -or $_ -match '(paraxial-phase-native|grating-phase-native|diffractive-phase-native|diffractive-firstorder-native|irregular-native)-v1\.json$'})){
 Add-Payload (Join-Path $repo $relative) ('source/'+$relative.Replace('\','/'))
}
foreach($relative in 'docs/MODEL_CAPABILITIES_STAGE2_2026-10-31.md','tests/OpticalSystemDesign.Tests/ParaxialLensModelTests.cs','tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-stage2-20261031.trx','artifacts/validation/model-capabilities-stage2-tests-20261031.log','artifacts/validation/model-capabilities-stage2-app-build-20261031.log'){
 Add-Payload (Join-Path $repo $relative) ('proof/'+$relative)
}
foreach($f in Get-ChildItem -LiteralPath $report -File){Add-Payload $f.FullName ('report/'+$f.Name)}
$manifest=@($entries|ForEach-Object {[pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256}})
$manifest|ConvertTo-Json -Depth 6|Set-Content "$report/archive-manifest.json" -Encoding utf8
$archive=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage2-evidence.zip'
if(Test-Path -LiteralPath $archive){throw 'Immutable archive exists'}
Add-Type -AssemblyName System.IO.Compression
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($e in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$e.path,$e.name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/archive-manifest.json",'archive-manifest.json',[IO.Compression.CompressionLevel]::Fastest)|Out-Null}finally{$zip.Dispose()}
$verified=0;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $manifest){$entry=$zip.GetEntry($e.name);if($null -eq $entry -or $entry.Length -ne $e.bytes){throw 'Archive entry size mismatch'};$s=$entry.Open();try{$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($hash -ne $e.sha256){throw 'Archive entry hash mismatch'};$verified++}}finally{$zip.Dispose()}
[pscustomobject]@{archive='validation/repairs/2026-10-31/model-capabilities-stage2-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;verifiedPayloads=$verified;allPayloadsReadBack=$true;coreSha256=$core;priorAuditModified=$false}|ConvertTo-Json -Depth 6|Set-Content "$report/verification.json" -Encoding utf8
Get-Content "$report/summary.json";Get-Content "$report/verification.json"
