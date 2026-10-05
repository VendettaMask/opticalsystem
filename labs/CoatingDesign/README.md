# 光学镀膜设计实验室

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](../../docs/PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-02：共享求解器更新到 `coherent-scattering/2`，新增复振幅供正式顺序追迹使用；本实验室仍不读取或应用当前镜头。最终物理与工作流定向 Debug/Release 各 25/25，冻结 120 组参考不变，详见[连接边界](../../docs/COHERENT_COATING_TRANSPORT_2026-10-02.md)。下述 2026-09-28 完整验收保留原范围。

纯 C#/.NET 10、Avalonia 中文独立桌面程序。共享正式 Core 的材料色散、稳定薄膜散射递推、优化器和文件接口；不依赖 Electron、Python 或 Node 运行环境，不读取或修改主程序当前镜头与优化状态。2026-09-28 补齐日常设计流程；高级物理仍按下列边界单列。

## 启动

仓库根目录双击 macOS 的 `Run-CoatingDesign.command` 或 Windows 的 `Run-CoatingDesign.cmd`，脚本构建并启动 Release。终端等价命令：

```sh
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App -c Release
```

正式主程序另有“实验室 → 光学镀膜设计”入口。开发目录须先构建与主程序相同的 Debug/Release 配置：

```sh
dotnet build labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Debug
dotnet build labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release
```

入口只启动进程；不会临时构建实验室。独立部署可把发布输出放在主程序目录 `labs/CoatingDesign/`，或将 `OPTILAND_COATING_DESIGN_LAB_PATH` 指向实验室可执行文件/DLL 的绝对路径。标准主程序安装包目前不捆绑实验室。

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
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App -c Release -- --batch-examples artifacts/coating-examples
```

## 数据与物理边界

- 波长/厚度为 nm；角度为入射介质内对法线的度数；层序从入射侧到基板。入射介质默认空气，也可搜索其它透明材料；吸收入射介质明确拒绝。
- 有限膜层相干、均匀、各向同性、被动、非磁性；复折射率 n+ik，时间因子 exp(-iωt)。支持 S/P、等权非偏振、斜入射、吸收、多层干涉及 R/T/A。
- 基板半无限，T 是进入基板的功率；A 是有限膜层吸收，不含随后基板传播吸收。精确临界角奇点和吸收入射介质明确拒绝。
- OD 使用 log(T) 求得，不钳制原始 T。界面大于 12 时显示饱和，目标验收最多 OD 12；CSV 保留原始 T、ln(T) 和 OD。浮点 T 下溢为零时仍保留有限 ln(T)，与真实全反射区分。
- 窄带复验至少每个目标 FWHM 64 个初始间隔，在实测峰值/半高边缘加密，再用独立更密网格检查收敛。采样上限不足或不收敛时不能判达标。
- 材料快照必须字段齐全；缺失系数不会由兼容读取器的默认值补齐。`.coating.json` schema 1 使用正式 `BoundedFile` 原子写入，保存目标、层表、完整材料模型、设置、输入哈希、算法/求解器版本、结果及候选。候选列表供显式采用；采用候选清空旧结果后需点击复验。公差结果包含种子和输入哈希。

不支持各向异性、粗糙散射、非相干背面反射、任意目标的解析多腔综合、针插入、完整 TFStudio 文件导入、生产控制和从实验室向当前镜头应用膜层的 UI。共享 Core 已单独接入物理膜层追迹，范围见上方连接记录。高反/窄带模板与局部厚度优化不保证达到任意指标。材料编辑限显式 n/k 表格；未提供解析色散系数拟合、温度模型编辑、完整 TFStudio 文件互换。

## 来源与验证

[本地审计、源码复用边界和计算契约](../../docs/COATING_DESIGN_LAB.md)、[源码版本与许可证](../../third_party/coating-reference/README.md)、[实际验收记录](../../validation/coating/README.md)。TFStudio/tmmcore MIT 原始声明和 CC0 材料说明随实验室输出复制。

```sh
dotnet test labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release
```

测试包含固定 tmmcore 0.4.2 的 120 组对照、独立解析物理检查、采样收敛、约束、取消/过期结果、原子保存重开和真实 Avalonia 控件交互。测试通过仅覆盖这些边界，不替代正式产品全量或跨平台安装包验收。

## 参数上限与排错

实验层数上限 200；窄带 1–5 腔，C 腔最少 4C+3 层（单腔 7 层），截止波段最多 8 段且不能重叠目标半高通带。AR 自动模板最多枚举到 12 层；手工膜层表和厚度优化仍遵守用户设定的上限。“2 优化”只调厚度；“结构搜索”还会排序模板、反转层序、尾部增删以及高低材料互换，默认最多 6 个优化起点（可设 1–32）。存在任何固定层时，结构变化禁用且保留固定层。用尽模板后追加有种子的厚度扰动起点；每个起点记录真实算法与停止原因，并做独立密集复验。

材料支持范围以快照为准，所有参与材料都须覆盖整个计算波段；非目录模型捕获时暂按 300–2500 nm 保守限制。无 k 数据的模型按 k=0，不应据此推断实际沉积膜无吸收。未知/不完整材料快照明确报错，不靠名称查表或默认系数补齐。

遇到“采样未收敛”应提高高级设置中的基础采样或缩小计算范围；超出采样预算时明确拒绝验收。遇到“未全部达标”先查看具体 R、峰值、FWHM、截止 OD 和层数/厚度检查；延长优化不保证成功。公差与设计优化的限制见[完整计算契约](../../docs/COATING_DESIGN_LAB.md)。

本轮验证与原生走查范围见[验收记录](../../validation/coating/README.md)；历史 2026-09-27 的 29 项记录保持原样，不作为新增功能证据。

## 日常设计增强示例（2026-09-28）

三个旧目标及原始示例保持不变。以下另存结果采用 6 个起点、固定种子 1234、每起点最多 80 次 DLS 迭代，窄带改为双腔结构；均使用原真实吸收材料。三例保存重开光谱完全一致。

| 示例 | 实际搜索结果 | 达标情况 |
| --- | --- | --- |
| [减反](examples/daily/Antireflection/experiment.coating.json) | 8 层，最大 R 0.090310% | 达标 |
| [高反](examples/daily/HighReflector/experiment.coating.json) | 24 层，最低 R 95.739782% | 未达 98% |
| [双腔窄带](examples/daily/NarrowBand/experiment.coating.json) | 17 层，峰值 549.959762 nm；FWHM 19.961338 nm；峰值 T 69.299967%；左右最低 OD 0.949597 / 1.084292 | 带宽、峰值 T、左截止 OD 未达标 |

每例另有 5 次正态、同材料同步厚度误差和 0.1° 角度误差公差演示，样本少，仅演示可重现流程，不代表生产良率认证。搜索结果只是给定预算内最优的有效候选，不能据此推断物理无解。

```sh
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App -c Release -- --batch-daily-examples artifacts/coating-daily-examples
```

机器数据见 [daily/verification.json](examples/daily/verification.json)。
