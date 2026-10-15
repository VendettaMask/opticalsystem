# 光学镀膜设计（产品内置模块）

2026-10-13 起：初始结构、镀膜设计与主程序同进程、同安装包、同升级/修复/卸载。两个模块必须安装，不提供勾选或补装提示，不再支持独立启动器或环境变量指定外部程序。详见[统一交付与验证](../../docs/BUILT_IN_MODULES_2026-10-13.md)。以下带日期的数值记录保留历史范围。

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](../../docs/PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-02：共享求解器更新到 `coherent-scattering/2`，新增复振幅供正式顺序追迹使用；本实验室仍不读取或应用当前镜头。最终物理与工作流定向 Debug/Release 各 25/25，冻结 120 组参考不变，详见[连接边界](../../docs/COHERENT_COATING_TRANSPORT_2026-10-02.md)。下述 2026-09-28 完整验收保留原范围。

纯 C#/.NET 10、Avalonia 中文内置工作区。共享正式 Core 的材料色散、稳定薄膜散射递推、优化器和文件接口；不依赖 Electron、Python 或 Node 运行环境，不读取或修改主程序当前镜头与优化状态。2026-09-28 补齐日常设计流程；高级物理仍按下列边界单列。

## 启动

安装标准 Optical System Design 后，通过“实验室 → 光学镀膜设计”打开；不需要安装额外模块，也不会启动其他进程。开发时只构建/运行根产品：

```sh
dotnet build OpticalSystemDesign.slnx -c Release
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release
```

模块使用主程序主题与显示设置。关闭主程序、切换示例或打开其他实验时，未保存修改仍有保存/放弃/取消保护；这不是模块安装提示。

## 使用

1. 点击“示例：减反 / 高反 / 窄带”，或选择设计类型并输入材料与指标。材料框支持名称包含搜索。
2. 点击“1 生成膜系”：枚举模板，显示真实计算后的候选。可以选中另一候选并点击“采用候选”。
3. 编辑膜层：添加、删除、复制、上下移动、替换材料、修改物理厚度；取消“变量”勾选即可固定该层厚度。
4. 点击“2 优化”调整当前结构的变量膜厚，或用“结构搜索”比较不同结构与多个厚度起点；只需复算时点击“计算 / 复验”。所有计算使用输入和材料快照，修改输入立即清除过期结果。取消及切换实验后的旧任务不能写回。
5. 查看 R/T/A 或 OD 光谱（鼠标显示最近实算采样）、目标阴影/虚线以及逐项指标。“全部目标达标”要求物理约束、指标和独立密集采样收敛同时满足；停止原因不是达标依据。
6. 保存 `.coating.json`，或导出膜层表、光谱、指标、算法记录和实验快照。打开有结果的文件会按其材料快照重新计算。

高级设置默认折叠，包含厚度限制、采样、实际算法、峰值/带宽容差、窄带腔数、搜索预算和公差。支持正式 Core 的 Damped Least Squares、Nelder-Mead、Coordinate Pattern Search。搜索采用结构枚举加多起点局部优化，不宣称全局最优或针插入。

“材料管理”可新建、复制、编辑实验内的 n/k 表格，导入/导出 CSV 或制表符表。首行为 `wavelength_nm,n,k` 或 `wavelength_um,n,k`，数值使用小数点；2–10000 行，波长严格递增、n > 0、k ≥ 0。填写来源与制备条件后保存到实验，再选择用途或替换膜层。解析色散模型不自动转换成近似采样表；如需替换，必须显式提供表格。没有全局材料库写入。示例格式明确标注为用户模型，不能当成测量数据。

公差支持均匀 ±范围或正态 1σ，厚度误差可逐层独立、同材料同步或全部同步，并可叠加入射角误差（°）。使用正式 `UniformSampler` / `NormalSampler` 和固定种子；每个样本使用原始材料快照做密集复验。固定膜层也参与制造公差。负厚度等无效样本记失败，不截断成有效值。每个样本的实际膜厚、角度、检查项和失败原因保存在实验与 `tolerance.json`，`tolerance.csv` 提供摘要。

键盘：Ctrl/Cmd+O 打开、Ctrl/Cmd+S 保存、F5 复验、Esc 取消，膜层表中 Delete 删除选中层。共享主程序 `ThemeRegistry`、Fluent/Blue 控件样式、标题栏、字号和数值格式；支持普通、暗夜、异世界、像素和跟随系统。启动时只读主程序主题、字体、字号/字形及数值显示设置；实验室内主题切换仅对本次会话有效，不写主程序设置。材料管理窗口继承当前字体、字号与字形。左栏宽 256 DIP、限制 240–280，标签与编辑器同排；参数区只纵向滚动。窄窗口的工作区和膜层工具条可滚动，不缩小字号。执行动作使用 `accent`，取消/导出保持中性，禁用按钮有原因提示与无障碍说明。显示舍入不损失存储精度。

## 三个可直接打开的示例

每个目录包含输入、初始结构、优化结果和 CSV；点击下列结果文件可定位，在实验室用“打开实验”读取。界面示例按钮载入同一组规格，可重新生成和优化。

| 示例 | 冻结目标 | 本机实际优化结果（2026-09-27） |
| --- | --- | --- |
| [减反膜](examples/Antireflection/experiment.coating.json) | 500–600 nm，最大 R ≤2%，最多 8 层 | 2 层，最大 R **1.098132%**；达标 |
| [高反膜](examples/HighReflector/experiment.coating.json) | 520–580 nm，最低 R ≥98%，最多 32 层 | 32 层，最低 R **95.683242%**；未达标 |
| [窄带滤光片](examples/NarrowBand/experiment.coating.json) | 550±0.5 nm，FWHM 15±1 nm，峰值 T ≥75%；490–510 / 590–610 nm 的 OD≥1 | 11 层，峰值 **549.970736 nm**，FWHM **27.220208 nm**，峰值 T **72.694745%**；右截止区最低 OD **0.949846**；未达标 |

三例使用保留真实 k 的 SiO₂/Ta₂O₅ 膜数据与 N-BK7 基板。高反/窄带结果展示了当前模板、材料和局部搜索的不足，不是“物理上不可能”的证明。没有关闭吸收、降低目标或修改采样来制造通过。三例密集采样均收敛，保存重开光谱逐项一致。完整机器记录见 [verification.json](examples/verification.json)。

重新运行三例并保存证据（会写入指定目录）：

```sh
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release -- --batch-coating-examples artifacts/coating-examples
```

## 数据与物理边界

用户输入的波长和膜厚为 nm，角度相对入射介质内法线，层序从入射侧到基板。吸收入射介质、精确临界角奇点和不完整材料快照会明确报错；参数变化会清除旧结果，采用候选后需重新复验。

当前范围为各向同性相干有限膜层与半无限基板。OD 显示饱和不是原始数值钳制；密集采样未收敛不能判为达标。完整的复折射率约定、T/A/ln(T) 定义和采样限制统一见 [计算契约](../../docs/COATING_DESIGN_LAB.md#计算契约)。模型可计算不代表给定材料、模板和预算能满足任意目标。

实验文件与镜头文件分别保存，不隐式修改当前镜头；物理模型已进入正式顺序追迹，但实验结果应用到镜头的 UI 尚未实现。保存字段、指纹兼容和 CSV 含义见 [文件契约](../../docs/FILE_FORMATS_AND_PLUGINS.md#镀膜实验文件)，其余未支持能力见 [范围边界](../../docs/COATING_DESIGN_LAB.md#首版边界)。

## 来源与验证

[本地审计、源码复用边界和计算契约](../../docs/COATING_DESIGN_LAB.md)、[源码版本与许可证](../../third_party/coating-reference/README.md)、[实际验收记录](../../validation/coating/README.md)。TFStudio/tmmcore MIT 原始声明和 CC0 材料说明随标准主程序发布到 `Licenses/`。

```sh
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release
```

测试包含固定 tmmcore 0.4.2 的 120 组对照、独立解析物理检查、采样收敛、约束、取消/过期结果、原子保存重开和真实 Avalonia 控件交互。测试通过仅覆盖这些边界，不替代正式产品全量或跨平台安装包验收。

## 参数上限与排错

“2 优化”只调标记为变量的厚度；“结构搜索”额外枚举有限模板和多起点。固定层会禁止结构变化，不会被搜索悄悄删改。允许的层数、腔数、材料波段、采样、优化和公差预算统一见 [资源和材料限制](../../docs/COATING_DESIGN_LAB.md#资源和材料限制)，本页不维护第二份数字表。

遇到“采样未收敛”应提高高级设置中的基础采样或缩小计算范围；遇到“未全部达标”先查看具体 R、峰值、FWHM、截止 OD 和结构约束。缺失材料数据需补齐或改选有效材料，不能由名称查库或默认系数代替。延长优化不保证成功。

## 日常设计增强示例（2026-09-28）

三个旧目标及原始示例保持不变。以下另存结果采用 6 个起点、固定种子 1234、每起点最多 80 次 DLS 迭代，窄带改为双腔结构；均使用原真实吸收材料。三例保存重开光谱完全一致。

| 示例 | 实际搜索结果 | 达标情况 |
| --- | --- | --- |
| [减反](examples/daily/Antireflection/experiment.coating.json) | 8 层，最大 R 0.090310% | 达标 |
| [高反](examples/daily/HighReflector/experiment.coating.json) | 24 层，最低 R 95.739782% | 未达 98% |
| [双腔窄带](examples/daily/NarrowBand/experiment.coating.json) | 17 层，峰值 549.959762 nm；FWHM 19.961338 nm；峰值 T 69.299967%；左右最低 OD 0.949597 / 1.084292 | 带宽、峰值 T、左截止 OD 未达标 |

每例另有 5 次正态、同材料同步厚度误差和 0.1° 角度误差公差演示，样本少，仅演示可重现流程，不代表生产良率认证。搜索结果只是给定预算内最优的有效候选，不能据此推断物理无解。

```sh
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release -- --batch-coating-daily-examples artifacts/coating-daily-examples
```

机器数据见 [daily/verification.json](examples/daily/verification.json)。
