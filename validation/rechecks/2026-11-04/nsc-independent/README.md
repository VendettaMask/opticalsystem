# 非序列独立链路：复场查看与完整实验

2026-11-04。按用户要求跳过需要 Zemax/OpticStudio 安装、专有解包或新增原生数值捕获的工作；既有材料与未知导入拒绝门槛保留。本批不调用 Zemax，也不将跳过项计作通过。

## 当前验证

[同一轮相关回归](nsc-independent-final.trx) **262/262** 通过，零失败/跳过；覆盖原有非序列测试、共享膜系/偏振操作数，以及新增复场、归档和实际窗口控制。它是当前定向结果，不能与前阶段 249 叠加为全仓通过数。[默认 App 构建](app-build.txt)独立记录，未安装、发布或控制用户桌面。

- 复场：在共享追迹结果上核对 ±179°、30° 及实虚部；不同模式不求和；零场和消光抵消处相位无定义。提取不修改原始复矢量或功率，掩码只筛查浮点求和误差尺度，不认证总 OPL/相位误差。
- 显示：相位保留 NaN 空白及 −180°…180° 范围，拒绝线性平滑、强度归一和对数；有符号分量不求和归一/取对数。CSV 保存组/波长/模式/局部轴/单位和有效列。所有探测器数据类型都有明确绘图量纲，修复之前 Stokes/相干图刷新时的缺项。
- 窗口：Light/Dark 下实际刷新 Stokes、相干功率、实部、虚部及相位，核对类型化轴、空相位掩码和禁用设置；切换模式立即清除旧图，恢复普通辐照度正常。空工作区可通过真实设计窗口重放归档，应用候选按钮保持禁用，不覆盖当前场景。
- 实验：完整源 STAROPT/网格与引用材料模型、别名、请求、结果及 SHA-256 清单；候选场景不重复保存。相同输入包字节一致，优化/制造抽样复算与归档规范 JSON 一致。原材料被替换后结果仍一致；删除原 STL 后，包内三角网格仍实际阻光，输出功率为零。
- 文件：逐项长度/哈希、固定路径、必需字段、版本/协议、重复字段及预算校验。损坏/截断拒绝，取消/来源不匹配保存不改旧文件，重放不修改当前工作区。不是独立商业数值认证。

命令：

```text
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false /p:UseSharedCompilation=false '/p:OutDir=D:\optical system design\opticalsystem\local-data\nsc-independent-bin\' --filter "FullyQualifiedName~NonSequential|FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~PolarizationOperandTests|FullyQualifiedName~PlotScalingAndCsvExportUseTypedUnitsInsteadOfAxisTitles" --logger "trx;LogFileName=nsc-independent-final.trx" --results-directory validation/rechecks/2026-11-04/nsc-independent
```

源码、程序集及结果身份见 JSON 清单。临时独立输出目录仅用于避开其他任务的测试文件锁；记录后清理复制依赖，不停止任何进程，不保存重复运行库。

## 开发记录

`mesh-fixture-development.trx` 保留一份新夹具错误：曾试图通过“创建默认 Mesh”后再赋资产，而既有产品入口有意拒绝空资产。夹具现使用真实导入资产显式构造 Mesh，未放宽产品检查。随后完整回归通过。早期 9 项和 10 项全通过记录在确认被当前同轮完整覆盖后可清理。

完整功能和剩余物理边界见[当前说明](../../../../docs/NONSEQUENTIAL_PHYSICS_DESIGN_IMPORT_2026-11-04.md)；Zemax 原始文本与前阶段独特失败继续由[前阶段证据](../nsc-physics-design-import/README.md)维护，不复制原始归档。
