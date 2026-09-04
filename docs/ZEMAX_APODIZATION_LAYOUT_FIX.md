# ZMX 切趾与布局修复记录

## 已实现

- 导入 `GFAC <factor> <type>`，类型 0/1/2 分别为均匀、高斯、余弦立方。缺少 GFAC 的旧文件仍使用原来的无切趾状态；非法类型、负值、非有限因子明确报错。
- 新组件 `zemax_pupil` 保留原始类型和因子，包括零因子和非高斯类型中不参与计算的因子。组件克隆、光学快照、STAROPT 和 ZMX 往返保留该状态。
- 系统属性增加“均匀（Zemax）”“高斯（Zemax）”“余弦立方（Zemax）”，参数标为“因子”。原有“高斯”的 σ 定义和旧工程数值保持兼容。
- 高斯模型使用振幅 `exp(-Gρ²)` 对应的光线强度 `exp(-2Gρ²)`。余弦立方强度为 `(1+t²ρ²)^(-3/2)`，t 由当前入瞳半径/物面至入瞳距离计算；物在无限远时 t=0。因子不参与余弦立方计算。直接调用缺少入瞳上下文的余弦立方二参数方法会明确报错。
- ZMX 导出写回 GFAC；原生 σ 高斯按 `G=1/(4σ²)` 换算，其他不可表示的本地切趾模型明确拒绝有损 ZMX 导出。Python 兼容导出支持等效均匀/高斯转换，不承诺保留 Zemax 元数据或支持余弦立方。
- 二维、三维布局共用的发射器现在把系统 `RayAimingEnabled` 传入追迹。此前遗漏该参数，布局可能使用未瞄准的光线，随后被“删除渐晕光线”过滤。
- 无限远物面现在只记录发射状态，不将光线移到有限的占位物面。原行为会越过第一凹面的负矢高区域，造成边缘光线全部渐晕，仅剩轴上光线；标量和批量追迹均已修复，零物距仍按有限物面处理。该发射段在场景中明确标为 Incident/None，不假造折射事件。
- 布局保留高斯切趾强度并乘视场权重；二维、三维正常光线的透明度按相对强度显示。高斯切趾表示入瞳振幅分布，不等于实现了高斯光束 q 参数传播或物理光学传播。
- 未匹配玻璃继续导入为 `UnresolvedMaterial`，保留名称及目录信息，不再根据 GLAS 的 nd/Vd 创建 `AbbeMaterial`，也不替换为空气。STAROPT 保存、重开和其他处方编辑保留占位材料。底部提示缺失名称和表面号，悬停可查看完整状态；提示随文档刷新持续存在，补选材料后清除。
- 缺失玻璃时二维、三维保留几何结构，不绘制依赖缺失色散的光线，光学指标不可用。追迹、分析等能力检查明确报错；原有厚度保留，MAZH 求解暂不执行。补选材料同时更新相邻表面的入射介质，包括中间反射面。

## 用户 266 nm、6 倍扩束文件

- 已读取用户提供的 `beam_expander_266nm_6x_final.zmx`，副本位于 `tests/OptilandWorkbench.Tests/Fixtures/beam-expander-266nm-6x.zmx`，原文件未修改。SHA-256：`C1397BA987F38DCF7E4DE11C8539FC98B54A4A7139F634613ACB8E1874EBA3C0`。
- 文件包含 ENPD=1.2 mm、GFAC=1.44/类型 1、RAIM=0、像方无焦、波长 266 nm。它没有启用光线瞄准，所以单纯传递瞄准开关不足以修复截图问题。
- 第二片材料写为 C7980；配套设计表写为 C79-80。本地已有同一 Corning 7980 目录材料，现通过仅针对 CORNING3 的显式别名使用真实目录色散。266 nm 时折射率为 1.4997247377489915；此前 nd/Vd 近似为 1.5236192714453585，会改变出射准直状态。
- 当前 Workbench 重算：输入边缘高度 0.6 mm，末端高度 3.6000008296163064 mm，与文件记录的像面半口径 3.6000008296163113 mm 一致至数值精度；出射方向 Y/Z 为约 −1.13624×10⁻⁶。已用真实应用的场景控件渲染扩束光线与高斯亮度，结果见 `artifacts/validation/beam-expander-266nm-6x-layout.png`。

## 依据与验证边界

- [Ansys Apodization Type](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Apodization_Type.html) 定义振幅因子、高斯零因子行为及余弦立方的入瞳关系。
- [Ansys 高斯到平顶光束示例](https://optics.ansys.com/hc/en-us/articles/42661743954835-How-to-design-a-Gaussian-to-Top-Hat-beam-shaper) 的官方归档 `Beam Homogenizer-Updated.zmx` 实际包含 `GFAC 9.0 1`，用于核对字段顺序。示例仅在本地临时目录解包，没有新增运行时依赖。
- `ZemaxApodizationImportTests` 验证类型/因子往返、光线强度、物距改变后的余弦立方、应用层编辑/保存/重开以及布局对瞄准开关的响应；原生 σ 高斯另做数值兼容验证。
- 本次是针对导入和布局链路的回归验证，不是重新运行 `123456.ZMX` 的 Zemax 数值精度基线，也没有改变全量测试通过数基线。
- 上述扩束数据来自当前 Workbench 重算和文件自带数值核对，不是本次运行 Zemax/ZOS-API 得到的新基线。

## 本次验证（2026-09-04）

- 默认输出目录构建成功，0 警告、0 错误。
- 定向回归 280 项通过、0 失败，覆盖扩束实际文件、缺失玻璃导入/保存/重开/补选、二维三维结构、高斯强度、批量与标量追迹、有限零物距、应用文档和 Python 兼容。此数值不是全量测试基线。
- 验证命令：`dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore --filter "FullyQualifiedName~UnresolvedZemaxGlassTests|FullyQualifiedName~BeamExpanderLayoutTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~Apodization|FullyQualifiedName~ViewerInteractionTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~FieldDefinitionParityTests|FullyQualifiedName~WorkbenchApplicationTests|FullyQualifiedName~PythonOptiland|FullyQualifiedName~OpaqueGeometry" /m:1 /nr:false -v:q`。
