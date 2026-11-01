param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repositoryPath=(Resolve-Path -LiteralPath $Repository).Path
$study=Join-Path $repositoryPath 'artifacts/validation/model-capabilities-final-20261031'
$report=Join-Path $repositoryPath 'validation/rechecks/2026-10-31/model-capabilities-stage1'
if(Test-Path -LiteralPath $report){throw 'Fresh report required'}
New-Item -ItemType Directory -Path $report|Out-Null
function Read-Json([string]$path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Count-Value($object,[string]$name){$p=$object.PSObject.Properties[$name];if($null -eq $p){0}else{[int]$p.Value}}
$summary=Read-Json "$study/summary.json"
$results=@(Read-Json "$study/results.json")
$prior=@(Read-Json (Join-Path $repositoryPath 'artifacts/validation/all-example-accuracy-20261031/results.json'))
$caps=@(Read-Json "$study/capability-inventory.json")
$capMap=@{};foreach($c in $caps){$capMap["$($c.lensIndex):$($c.configuration)"]=$c}
$current=@{};foreach($r in $results){$current[$r.index]=$r}
$blocked=@($prior|Where-Object {$_.error -match 'OpticCapabilityException'})
if($blocked.Count -ne 1098 -or $results.Count -ne 392){throw 'Unexpected source audit scope'}
$progress=@(foreach($r in $blocked){
    $issues=@($capMap["$($r.lensIndex):$($r.configuration)"].issues)
    $cleared=$issues.Count -eq 0
    if($cleared -and -not $current.ContainsKey($r.index)){throw 'Unreplayed cleared request'}
    [pscustomobject][ordered]@{index=$r.index;lensIndex=$r.lensIndex;lens=$r.lens;configuration=$r.configuration;key=$r.key
        originalConclusion=$r.conclusion;originalReason=$r.reason;modelGateRemoved=$cleared
        remainingIssues=$issues;currentConclusion=if($current.ContainsKey($r.index)){$current[$r.index].conclusion}else{'NotReplayed_StillBlocked'}
        currentError=if($current.ContainsKey($r.index)){$current[$r.index].error}else{$null}}
})
$cleared=@($progress|Where-Object modelGateRemoved)
$remaining=@($progress|Where-Object {-not $_.modelGateRemoved})
$remainingTypes=@($remaining|ForEach-Object {$_.remainingIssues.originalType|Sort-Object -Unique}|Group-Object|Sort-Object Count -Descending|ForEach-Object {@{type=$_.Name;requests=$_.Count}})
$counts=[ordered]@{};foreach($group in $cleared|Group-Object currentConclusion){$counts[$group.Name]=$group.Count}
$state=[ordered]@{status='InProgress';originalUnsupportedRequests=1098;modelGateRemoved=$cleared.Count;remainingModelBlocked=$remaining.Count
    removedGateResultCounts=$counts;replayedLenses=12;replayedRequests=392;currentResults=$summary.counts
    all95ResultsReplayed=$false;all95Certified=$false;all1098Implemented=$false;coreSha256=$summary.coreSha256
    preservedOriginalAudit='validation/rechecks/2026-10-31/all-example-accuracy';remainingTypeCountsOverlap=$remainingTypes}
$state|ConvertTo-Json -Depth 15|Set-Content -LiteralPath "$report/summary.json" -Encoding utf8
$progress|ConvertTo-Json -Depth 20|Set-Content -LiteralPath "$report/capability-progress.json" -Encoding utf8
foreach($name in 'results','lens-results','capability-inventory','scope','identity'){
    Copy-Item -LiteralPath "$study/$name.json" -Destination "$report/$name.json"
}
$controlGroups=@(
    @{folder='surface-model-controls-20261031-03';file='core-controls-final.json'},
    @{folder='surface-model-controls-20261031-04';file='core-controls-final.json'},
    @{folder='surface-model-controls-20261031-05';file='core-controls-final.json'},
    @{folder='mems-native-controls-20261031';file='core-controls-final.json'},
    @{folder='gradient9-native-controls-20261031';file='core-controls-final.json'},
    @{folder='gradient9-return-native-20261031';file='core-controls.json'}
)
$controls=@(foreach($g in $controlGroups){foreach($r in Read-Json (Join-Path $repositoryPath "artifacts/validation/$($g.folder)/$($g.file)")){
    $exclusion=if($g.folder -eq 'surface-model-controls-20261031-04' -and $r.id -eq 'cheb-nonzero'){'Missing explicit CURV in exploratory input; native default differs; not equivalent input'}
        elseif($r.skipped){$r.skipped}
        elseif($g.folder -eq 'gradient9-return-native-20261031' -and $r.code -in 'REAA','REAB','REAC' -and $r.input.int1 -eq 3){'Native zero-thickness mirror/GRIN intermediate direction mapping remains open'}
        else{$null}
    $limit=if($null -ne $r.expected){1e-8+1e-6*[Math]::Abs($r.expected)}else{$null}
    [pscustomobject][ordered]@{capture=$g.folder;id=$r.id;code=$r.code;input=$r.input;expected=$r.expected;actual=$r.actual
        absoluteError=$r.absoluteError;controlBound=$limit;excludedReason=$exclusion
        verdict=if($exclusion){'NotPassed_ExcludedOrUnresolved'}elseif($null -eq $r.actual){'NoData'}elseif($r.absoluteError -le $limit){'WithinExistingExplicitRayBound'}else{'Difference'}}
}})
$controls|ConvertTo-Json -Depth 18|Set-Content -LiteralPath "$report/native-controls.json" -Encoding utf8
$controlSummary=@($controls|Group-Object capture,verdict|ForEach-Object {@{scope=$_.Name;count=$_.Count;maxAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})
$controlSummary|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$report/native-controls-summary.json" -Encoding utf8
$lensLines=@('# 本批逐镜头结果','','P/C/D/I/E 为通过、接近未通过、差异、不可比、执行/求值失败。原 95 支台账保持不变。','','| 镜头 | 请求 | P | C | D | I | E |','|---|---:|---:|---:|---:|---:|---:|')
foreach($lens in Read-Json "$study/lens-results.json"){
    $lensLines+='| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $lens.lens,$lens.total,(Count-Value $lens.counts 'Pass'),(Count-Value $lens.counts 'Close'),(Count-Value $lens.counts 'Difference'),(Count-Value $lens.counts 'Incomparable'),(Count-Value $lens.counts 'Error')
}
$lensLines|Set-Content -LiteralPath "$report/BY_LENS.md" -Encoding utf8
$doc=@'
# 模型能力补齐第一批，2026-10-31

本批接入 **TILTSURF、FZERNSAG、CHEBYSHV、GRINSUR1、GRINSUR9，以及明确的 US_MEMS 示例契约**。计算均在纯 C# 共享 Core。原 1,098 项中 **{{CLEARED}} 项解除原面型/材料能力阻塞，{{REMAINING}} 项仍被剩余模型阻塞**；这不是全部实现完成，也不代表解除阻塞的项目全部精度通过。

相关 **12 支示例、392 项原请求**按同一冻结 Core 重算，结果为 **{{PASS}} Pass、{{CLOSE}} Close、{{DIFFERENCE}} Difference、{{INCOMPARABLE}} Incomparable、{{ERROR}} Error**。Close 未通过，参考缺失和执行失败不计通过。原 95 支总体账目未覆盖写入；本批没有重新运行其余镜头或重采全部原生分析图。

完整[逐镜头表](../validation/rechecks/2026-10-31/model-capabilities-stage1/BY_LENS.md)、[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage1/results.json)、[原 1,098 项逐项能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage1/capability-progress.json)、[原生直接控制及排除原因](../validation/rechecks/2026-10-31/model-capabilities-stage1/native-controls.json)已保存。

## 实现范围

| 能力 | 当前实现及边界 |
|---|---|
| 倾斜平面 | 真实平面求交、解析法线；斜率为无量纲，后续坐标框架不随面倾斜而旋转 |
| Fringe Sag | 基础圆锥/偶次非球面、37 项 Fringe 序列、偏心、归一化半径及外推标志；解析多项式法线；共享原有 Fringe 项号表 |
| Chebyshev | 球面基础、最高 14×14 阶、原点参考、归一化及矩形定义域；解析法线 |
| Gradient 1 | 接入原有连续积分、空间折射率及一阶传递；当前导入非零轴上不可微径向一次项仍保留为未支持 |
| Gradient 9 | 真实 B/C/K0/K1/K2 色散和空间梯度；完整系数随 STAROPT 保存；相邻 GRADIENT_9.DAT 优先，否则使用带源哈希的完整九材料目录；缺失材料明确拒绝，不使用 GLAS 中的均匀折射率代替；非零面斜率导入仍未接入 |
| GRIN 返回路径 | 有符号一阶传播、反射矩阵、正反向追迹；反射保留原材料坐标框架；材料局部精确交点随不可变路径状态传递，避免大坐标量化导致重合面错过交点 |
| MEMS | 仅 US_MEMS.DLL 已知示例契约：平面交会、三态像素法线、行/列/逐像素寻址和每字 15 个三进制状态；产品计算不调用 DLL；未知 DLL 仍为不可计算负载 |
| 文件与身份 | 新几何的 STAROPT 和对应 ZMX 参数往返；GRIN 外部文本导出仍明确拒绝；缺少完整列的文本格式拒绝把新几何压平成标准面；未知 TYPE 参数及 XDAT 保留 |

定义依据为 Ansys 官方的[倾斜面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Tilted.html)、[Fringe Sag](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Fringe_Sag.html)、[Chebyshev](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Chebyshev_Polynomial.html)、[Gradient 1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_1.html)及[Gradient 9](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_9.html)。MEMS 对照绑定本机示例 DLL 哈希与随安装提供的源契约；DLL 和 C 源码未成为产品运行依赖。

## 验证与未关闭项

- 118 项针对性测试通过，默认 App Release 构建通过。闭式 GRIN 轴上传递、真实小光线极限、反向追迹、轴向梯度反射坐标框架，以及原无效处方拒绝均有检查。
- 同一 Core 的新原生标量控制覆盖非零 Fringe/偏心、Chebyshev、MEMS 三态、SELFOC 三波长及双程 GRIN。原生采用独立 CreateNewApplication、截图关闭和拥有者进程隔离，未操作用户桌面。
- Gradient 9 的 0.1→0.05→0.025 步长控制保留全部原记录。SELFOC 的 765 个控制值满足既有显式光线界限；双程系统以 0.025 步长为收敛控制，577 个已映射值满足原界限。双程较粗两档合计 14 个超限值仍保存为 Difference。不能据这些点控制升级全部旧分析图的收敛认证。
- 双程镜头表面 3 的零厚度镜面/GRIN 重入中间方向定义仍未闭合；原差异保留，54 个中间方向/每步长不计通过。其后表面 4、5、6 的实际方向分别核验，不覆盖该中间记录。
- 一轮探索性请求误含原生声明范围之外的波长 2、3；即使原生替换成主波长，这些值也全部排除。准备脚本已改为仅采用声明波长。另保留缺少显式 CURV 的早期 Chebyshev 控制，其原生缺省与 Core 输入不同，不用作等价处方证据。
- 尚未通过的分析、主光线/像面缺失、采样与参考缺口，以及无焦 Huygens 历史收敛工作仍开放。GRIN 偏振连续运输及布局曲线节点等原边界仍明确拒绝；本批没有桌面 GUI 验收。

剩余能力优先级：**PARAXIAL/PARAX_XY 的真实作用及 OPD 规则 → BIRE__IN/BIRE_OUT → 衍射/二元/全息相位 → 网格面与 Grid GRIN → Irregular**。不能将既有二次薄透镜 OPD 直接当作完整近轴面支持；原生近轴面有依赖共轭的模式及逐光线相位积分，需补共享 Core 的相位路径与控制。[原生近轴面定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html)

本批 Core：`{{CORE}}`。原请求、容差文件和旧失败记录保持，旧不透明快照不被静默转换；新能力来自重新导入源文件。所有统计均为请求数，剩余面型涉及数存在重叠，不能求和。
'@
foreach($pair in @(@('CLEARED',$cleared.Count),@('REMAINING',$remaining.Count),@('PASS',$summary.counts.Pass),@('CLOSE',$summary.counts.Close),@('DIFFERENCE',$summary.counts.Difference),@('INCOMPARABLE',$summary.counts.Incomparable),@('ERROR',$summary.counts.Error),@('CORE',$summary.coreSha256))){$doc=$doc.Replace('{{'+$pair[0]+'}}',[string]$pair[1])}
[IO.File]::WriteAllText((Join-Path $repositoryPath 'docs/MODEL_CAPABILITIES_STAGE1_2026-10-31.md'),$doc)
$payload=[Collections.Generic.List[object]]::new()
function Add-Payload([string]$path,[string]$name){
    $resolved=(Resolve-Path -LiteralPath $path).Path
    if(-not $resolved.StartsWith($repositoryPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Payload outside repository'}
    $payload.Add([ordered]@{path=$resolved;entry=$name.Replace('\','/');sha256=Hash $resolved;bytes=(Get-Item -LiteralPath $resolved).Length})
}
foreach($file in Get-ChildItem -LiteralPath $study -File -Recurse){Add-Payload $file.FullName ('study/'+[IO.Path]::GetRelativePath($study,$file.FullName))}
foreach($g in $controlGroups){$directory=Join-Path $repositoryPath "artifacts/validation/$($g.folder)";foreach($file in Get-ChildItem -LiteralPath $directory -File -Recurse){Add-Payload $file.FullName ('native/'+$g.folder+'/'+[IO.Path]::GetRelativePath($directory,$file.FullName))}}
$runtime=Join-Path $repositoryPath 'artifacts/validation/model-capabilities-final-runner-20261031'
foreach($file in Get-ChildItem -LiteralPath $runtime -File -Recurse){Add-Payload $file.FullName ('runtime/'+[IO.Path]::GetRelativePath($runtime,$file.FullName))}
foreach($file in Get-ChildItem -LiteralPath $report -File){Add-Payload $file.FullName ('report/'+$file.Name)}
foreach($file in @('docs/MODEL_CAPABILITIES_STAGE1_2026-10-31.md','artifacts/validation/model-capabilities-final-tests-20261031.log','artifacts/validation/model-capabilities-final-default-app-build-20261031.log','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-final-verified-20261031.trx','tests/OpticalSystemDesign.Tests/Fixtures/special-sag-native-v1.json','tests/OpticalSystemDesign.Tests/Fixtures/gradient9-native-v1.json','tests/OpticalSystemDesign.Tests/Fixtures/gradient9-return-native-v1.json')){Add-Payload (Join-Path $repositoryPath $file) ('proof/'+$file)}
$coreSources=@(Get-ChildItem -LiteralPath (Join-Path $repositoryPath 'src/OpticalSystemDesign.Core') -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}|ForEach-Object {@{path=[IO.Path]::GetRelativePath($repositoryPath,$_.FullName).Replace('\','/');sha256=Hash $_.FullName}})
$coreSources|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$report/core-source-hashes.json" -Encoding utf8
Add-Payload "$report/core-source-hashes.json" 'report/core-source-hashes.json'
$payload|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$report/payload-manifest.json" -Encoding utf8
$archive=Join-Path $repositoryPath 'validation/repairs/2026-10-31/model-capabilities-stage1-evidence.zip'
if(Test-Path -LiteralPath $archive){throw 'Existing evidence must not be replaced'}
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force|Out-Null
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($item in $payload){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$item.path,$item.entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/payload-manifest.json",'payload-manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive);$verified=0
try{foreach($item in $payload){$entry=$zip.GetEntry($item.entry);if($null -eq $entry -or $entry.Length -ne $item.bytes){throw 'Missing payload'}
    $stream=$entry.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
    if($actual -ne $item.sha256){throw "Readback mismatch: $($item.entry)"};$verified++
}}finally{$zip.Dispose()}
[ordered]@{archive=[IO.Path]::GetRelativePath($repositoryPath,$archive).Replace('\','/');sha256=Hash $archive;verifiedPayloads=$verified;bytes=(Get-Item -LiteralPath $archive).Length;coreSha256=$summary.coreSha256;allPayloadsReadBack=$true;oldAuditUnmodified=$true}|ConvertTo-Json|Set-Content -LiteralPath "$report/verification.json" -Encoding utf8
$state|ConvertTo-Json -Depth 12
