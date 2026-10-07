# 软件可靠性问题修复 · 2026-10-05

本页保留 2026-10-05 的阶段验证计数。当前完整 Debug/Release 与剩余失败见[商业质量第一阶段](COMMERCIAL_PHASE1_2026-10-06.md)。

本轮以远端 `949623b70224df249185d3b5d458288f3107d049` 为起点，修复本机审计复现的八类问题，并修复回归中确认的镀膜实验文件跨平台指纹问题。保留纯 C#/.NET 共享 Core、既有数值算法和外部基线；未放宽数值断言或重写冻结数据。

## 已实现行为

| 问题 | 修复后的行为 | 回归覆盖 |
| --- | --- | --- |
| 保存时继续编辑后，“保存并继续”放行 | 保存完成后核对修订和脏状态；保存旧修订成功仍不能授权关闭/切换。公差保存后也复核两类脏状态 | 实际 MainWindow 保存路径及 UnsavedChangesGuard；文件捕获值和当前编辑值 |
| 旧打开任务覆盖新文档 | 替换事务锁内检查令牌、代次和源修订；新建、后续打开或编辑使旧请求取消 | 控制读文件、等待 Gate 和新建的交错；另验证等待读取期间编辑被保留 |
| 嵌套几何修改后使用旧缓存 | 标准几何上报半径/圆锥常数变化；表面转发自身及非球面/光栅 Base 的通知并同步处方；活动后端替换也使缓存失效 | 标准面、偶次/奇次非球面、Forbes Q、标准光栅、同名后端替换；与清除缓存重算一致 |
| 优化占用共享文档锁 | 短锁捕获完整快照，锁外求解及自动半口径刷新，最后核对修订/代次并一次提交 | 实际聚焦计算中读取处方、取消/新建；三类优化刷新期间编辑；旧结果不能覆盖新状态 |
| 卷积等长循环不响应取消 | 卷积行/列块、加权图、PSF 采样、Gram、分量重建和 Jacobi 迭代检查统一令牌 | 调用前及计算中取消；保留奇数/偶数核的中心和边缘数值 |
| 图像仿真预算漏算主成本 | 估算准备后图像×核×均值及 EigenPSF 核数×波长数；另预算 Gram、Jacobi 扫描与重建 | 512² 图像/256² 核在生成 PSF 前拒绝；全波长/分量计入；低光线密度不能绕过分解预算 |
| 单镜头格式被当作完整多配置保存 | 写文件前拒绝所有非 STAROPT 多配置保存，保留原文件、路径、脏状态和配置 | ZMX、SEQ、LEN、原生 JSON；完整多配置继续使用 STAROPT |
| Core 默认图像仿真超出自身上限 | Core 与分析共用一般默认：3×3 PSF 网格、32² PSF、16 光瞳采样、16 像素补边、9×9 畸变网格 | 支持的小视场系统省略 Core 配置，实际完成并返回有限图像 |
| 镀膜旧文件在 Windows 被指纹校验拒绝 | 新指纹固定 LF；旧 LF/CRLF 须与实际输入匹配，读写后规范化结果和公差指纹 | 仓库旧示例；旧 Windows 结果/公差打开与保存；拒绝已改输入 |

优化快照采用 `Optic.Clone()`，保留已注册数值后端与材料目录；配置捕获、切换、复制和文档替换也保留这些注册项。成功优化仍是一条可撤销的完整文档变更；取消、求解或自动半口径刷新失败不提交计算结果、不增加历史。

卷积上界为 300,000,000，PSF 分解上界为 200,000,000；这是直接计算实现的工作量限制，不是运行时间承诺。多波长仿真按总成本校验，公开卷积入口自身也校验。已有内存、图像尺寸和 PSF 采样限制继续有效。大图可降低补边、采样、PSF 尺寸、分量或波长数量。物理上不支持的瞳孔映射仍返回明确错误。

多配置 ZMX/SEQ/LEN/JSON 保存现在明确失败。低层活动镜头导出仍有各自表达边界，不能用导出成功清除整工程脏状态。STAROPT 格式版本未变。

## 验证与证据

环境为 Windows x64，SDK 10.0.300、Runtime 10.0.8，使用仓库默认输出目录。正式解决方案及镀膜实验室默认 Debug/Release 构建均零警告、零错误；初始结构默认 Release 构建同样零警告、零错误。改动源码的格式验证与 `git diff --check` 通过。

| 实际执行范围 | 结果 | 证据 |
| --- | --- | --- |
| 正式完整 Release | 4259 通过/5 个既有失败/4264 项，零跳过 | product-release.trx |
| 正式 Debug 相关回归 | 228/228，含新增 28 项 | targeted-debug.trx |
| 新增正式 Release 回归 | 28/28，提取自正式全量结果，未与全量累加 | product-release.trx |
| 镀膜完整 Release | 47/47，含新增 3 项旧 Windows 指纹回归 | coating-release.trx |
| 镀膜完整 Debug | 47/47 | coating-debug.trx |
| 初始结构完整 Release | 258 通过/2 失败/260 项 | initial-structure-release.trx |
| 原始远端 Core 对照 | 初始结构相同两项仍失败，0 通过/2 失败/2 项 | initial-structure-baseline.trx |
| 比较工具完整 Release | 102 通过/2 个既有失败/104 项 | comparison-release.trx |

正式新增 28 项与相邻集合包含于完整 Release。Debug 未跑正式全量，不能把 228 项通过当作该配置的全量认证。初始结构的两项失败为 `trial-0078` 重启可行性和 Secant 冻结用例 `index: 9` 目标验收；隔离使用原始 HEAD Core 后在相同断言复现，确认不是本轮 Core 改动引入。

镀膜初轮为 43 通过/1 失败；指纹探针得到原文件 LF 哈希 `A8A419A856065FDC043170C1DDAB0699947BAA4DB94317E764172EB984C293F7`，Windows 默认 CRLF 哈希 `BD03EC9BF2C547121141304537FDD83D4E36BF96CAC3E46C03B235D008FD178D`。修复并加入兼容回归后 47 项全部通过。没有替换旧文件哈希来绕过校验。

新增正式回归位于 `tests/OptilandWorkbench.Tests/AuditReliabilityFixTests.cs`；镀膜回归扩展 `DailyDesignTests.cs`。并发测试控制提交锁、保存写入和数值后端开始信号，验证实际计算中的读取与取消。

TRX 与[机器清单](../artifacts/validation/audit-reliability-fixes-20261005/verification.json)位于 `artifacts/validation/audit-reliability-fixes-20261005/`；修复前复现数据保留在 `artifacts/code-audit/2026-10-05/`。临时探针和原始 Core 副本只用于本机对照，不参与生产依赖。

```powershell
dotnet build OptilandWorkbench.slnx -c Release --no-restore /m:1 /nr:false -p:UseSharedCompilation=false
dotnet build OptilandWorkbench.slnx -c Debug --no-restore /m:1 /nr:false -p:UseSharedCompilation=false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-build --no-restore /m:1 /nr:false --logger 'trx;LogFileName=product-release.trx' --results-directory artifacts/validation/audit-reliability-fixes-20261005
dotnet test tests/OptilandWorkbench.ZemaxComparison.Tests/OptilandWorkbench.ZemaxComparison.Tests.csproj -c Release --no-build --no-restore /m:1 /nr:false --logger 'trx;LogFileName=comparison-release.trx' --results-directory artifacts/validation/audit-reliability-fixes-20261005
```

## 范围和未关闭问题

本轮关闭上述八类实际复现路径，与商业审计 F01/F02/F04/F05/F06/F12 重合。F08/F09 只关闭这里列出的图像仿真循环和预算部分，尚未证明所有衍射循环、非二次幂变换及并发总预算都已完成整改。其余审计条目按各自证据记录，不把本轮等同于全部问题解决。

修复前正式全量为 4231 通过/5 失败/共 4236 项；比较工具为 102 通过/2 失败/共 104 项。已有失败包括历史有限角度参考、近轴入口测试参数映射、Cooke 默认图像仿真照度映射和两项干涉图页脚舍入。比较工具的两项固定误差上限未修改；本轮不新建 Zemax 原生捕获或生成 Optiland 历史比较。
