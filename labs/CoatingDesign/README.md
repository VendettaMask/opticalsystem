# 光学镀膜设计实验室

纯 C#/.NET 10、Avalonia 中文独立桌面程序。共享正式 Core 的材料色散、稳定薄膜散射递推、优化器和文件接口；不依赖 Electron、Python 或 Node 运行环境，不读取或修改主程序当前镜头与优化状态。

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
4. 点击“2 优化”，或者只点击“计算 / 复验”。所有计算使用输入和材料快照，修改输入立即清除过期结果。取消及切换实验后的旧任务不能写回。
5. 查看 R/T/A 光谱、目标阴影/虚线以及逐项指标。“全部目标达标”要求物理约束、指标和独立密集采样收敛同时满足；停止原因不是达标依据。
6. 保存 `.coating.json`，或导出膜层表、光谱、指标、算法记录和实验快照。打开有结果的文件会按其材料快照重新计算。

高级设置默认折叠，包括厚度限制、采样、算法、位置/带宽容差以及均匀膜厚扰动公差。支持 Damped Least Squares、Nelder-Mead 和 Coordinate Pattern Search，调用正式 Core 的对应真实实现。公差记录固定随机种子；确定性优化的随机种子为 null。首版公差仅为各层独立均匀厚度扰动，不包含材料误差或补偿。

键盘：Ctrl/Cmd+O 打开、Ctrl/Cmd+S 保存、F5 复验、Esc 取消，膜层表中 Delete 删除选中层。界面使用主程序的 Light 蓝色语义资源、共享字号和数值格式；显示最多三位小数不损失保存/计算精度。首版不联动主程序其它主题或用户显示设置。

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

- 波长/厚度为 nm；角度为入射介质内对法线的度数；层序从入射侧到基板。界面入射介质固定为空气，Core 允许其它透明介质。
- 有限膜层相干、均匀、各向同性、被动、非磁性；复折射率 n+ik，时间因子 exp(-iωt)。支持 S/P、等权非偏振、斜入射、吸收、多层干涉及 R/T/A。
- 基板半无限，T 是进入基板的功率；A 是有限膜层吸收，不含随后基板传播吸收。精确临界角奇点和吸收入射介质明确拒绝。
- OD 使用 log(T) 求得，不钳制原始 T。界面大于 12 时显示饱和，目标验收最多 OD 12；CSV 保留原始 T、ln(T) 和 OD。浮点 T 下溢为零时仍保留有限 ln(T)，与真实全反射区分。
- 窄带复验至少每个目标 FWHM 64 个初始间隔，在实测峰值/半高边缘加密，再用独立更密网格检查收敛。采样上限不足或不收敛时不能判达标。
- 材料快照必须字段齐全；缺失系数不会由兼容读取器的默认值补齐。`.coating.json` schema 1 使用正式 `BoundedFile` 原子写入，保存目标、层表、完整材料模型、设置、输入哈希、算法/求解器版本、结果及候选。候选列表供显式采用；采用候选清空旧结果后需点击复验。公差结果包含种子和输入哈希。

不支持各向异性、粗糙散射、非相干背面反射、多腔自动综合、针插入、完整 TFStudio 文件导入、生产控制和正式光线追迹镀膜应用。高反/窄带模板与局部厚度优化不保证达到任意指标。首版不提供材料编辑/外部材料文件导入界面，已有目录搜索与实验内冻结材料可用。

## 来源与验证

[本地审计、源码复用边界和计算契约](../../docs/COATING_DESIGN_LAB.md)、[源码版本与许可证](../../third_party/coating-reference/README.md)、[实际验收记录](../../validation/coating/README.md)。TFStudio/tmmcore MIT 原始声明和 CC0 材料说明随实验室输出复制。

```sh
dotnet test labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release
```

测试包含固定 tmmcore 0.4.2 的 120 组对照、独立解析物理检查、采样收敛、约束、取消/过期结果、原子保存重开和真实 Avalonia 控件交互。测试通过仅覆盖这些边界，不替代正式产品全量或跨平台安装包验收。

## 参数上限与排错

实验层数上限 200；单腔窄带至少 7 层，截止波段最多 8 段且不能重叠目标半高通带。AR 自动模板最多枚举到 12 层；手工膜层表和厚度优化仍遵守用户设定的上限。优化不会自动增删层或更换材料。

材料支持范围以快照为准，所有参与材料都须覆盖整个计算波段；非目录模型捕获时暂按 300–2500 nm 保守限制。无 k 数据的模型按 k=0，不应据此推断实际沉积膜无吸收。未知/不完整材料快照明确报错，不靠名称查表或默认系数补齐。

遇到“采样未收敛”应提高高级设置中的基础采样或缩小计算范围；超出采样预算时明确拒绝验收。遇到“未全部达标”先查看具体 R、峰值、FWHM、截止 OD 和层数/厚度检查；延长优化不保证成功。公差与设计优化的限制见[完整计算契约](../../docs/COATING_DESIGN_LAB.md)。

2026-09-27 已记录 Debug/Release 各 29/29，正式相关 Release 198/198；本轮文档同步仅核对既有证据，未重跑测试。原生 macOS 自动化检查超时未完成，Windows/Linux 原生验收和安装包验收也未完成。
