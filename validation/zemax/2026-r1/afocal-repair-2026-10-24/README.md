# 四无焦项目复比证据，2026-10-24

状态：本轮确认的主评价链问题已修复，五结构所选光线、OPD、MTF、场曲及畸变控制通过；其余 120 项未验证契约保持不可比。详见[报告](../../../../docs/AFOCAL_REPAIR_2026-10-24.md)及[机器账目](../../../../docs/validation/AFOCAL_REPAIR_2026-10-24.json)。

`captures/` 保存四个原始 ZMX、两个原始 POB、低频及物理全带宽 MTF/场曲捕获、13 组显式 OPD 和选定策略。所有原生数据来自本机持有效许可的 OpticStudio 2026 R1。原请求及返回的设置、环境、模型、有效性和 SHA-256 同存；本轮 Core 数据由正式 Application/共享 Core 新计算。

完整逐面账目、1159 个有焦原请求、52 个既有补充通过项、测试、构建和失败试验的可移植增量包见机器账目中的 archive。未变的有焦原生数组继续依赖既有冻结资料：`artifacts/zemax-sequential-corpus/20261021/full-v1`、Objectives 2026-10-23 修复/补充证据，以及原 2668 文件清单；没有生成替代参考。

仍被历史诊断进程占用的日志列在包内 `archive-exclusions.json`，原文件保留于工作区；最终验收的请求、原生数组、比较账目、测试及构建记录不适用该排除规则。包包含最终 Core 源码快照，其中也有工作区原有及并行修改，不能把整个快照都归为本任务新增代码。

本地重现入口：

```powershell
dotnet build tools/diagnostics/Afocal20261024/Afocal20261024.csproj -c Release
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --filter FullyQualifiedName~Afocal
```

重新采集使用 `SequentialCorpus20261019` 准备、`StandardMatrix20261015/Run-Native.ps1` 调用原生 API，再使用正式 Workbench worker 和比较器。`Set-CutoffControls.ps1` 必须在捕获前固定频率请求；`Reuse-NativeCaptures.ps1` 仅复制源字节和除工作目录路径外全部请求字段相同的成功捕获，并记录原文件及复制哈希。重算、参考复用和新原生捕获分别记录，不将复用称作新采集。

Riflescope 的三个视场、三波长 OPD 共 117 点，另外四个结构控制共 52 点，合计 169；全带宽五请求共 30 条曲线。Beam Expander 和 Apochromat_prism 仅定义轴上视场，零畸变或零场曲不认证未定义的轴外范围。

`legacy-one-sided-manifest.json` 保留先前单侧 1e-5 请求，`manifest.json` 指向另行生成的对称 ±1e-4 新请求与原生复采；旧数组、请求及阶段失败记录不改写。该取样在本轮 2026 R1 实测确定，不宣称所有版本的通用默认。测试按独立屈光度门槛验证全部五结构，并核对独立原生 FCGS/FCGT 操作数；不会把无焦屈光度套用有焦毫米容差。非序列内部每个三角面尚无原生逐面捕获；端口与其后真实面的数值通过不等于组内偏振、场景可视化或其他优化操作数均已认证。
