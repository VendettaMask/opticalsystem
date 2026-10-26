# AutoLens 原版 Python 与现有 C# 比较（2026-10-26）

结论：本次实测的原版优化规则，以及同处方、同输入光线、同为 float64 时的追迹、RGB 损失和所检梯度相符；不能据此宣称两种实现完整训练后的处方、像质、成功率或速度一致。

## 找到的代码及历史记录

- 官方源码：[AI4Optics/AutoLens](https://github.com/AI4Optics/AutoLens)，本次下载的 main 为 `e58db32f3a0b07f25861b5839a45970f77b5c947`，与项目已有参考版本相同。原版受 Git 跟踪的源文件保持未修改。
- 本机下载位置：`D:\optical system design\tmp\autolens-upstream-20261026\source`，入口为 `1_autolens.py`。依赖的 `tqdm` 仅补装在旁边的 `python-deps`，未改全局 Python 安装。
- 产品库原已有 `third_party/autolens` 来源/许可，以及 `labs/InitialStructure` 下的 C# 改写版和参考测试，没有打包原版 Python 运行环境。
- [10 月 24 日记录](../../../../docs/validation/AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.json)保存原版默认手机配置完整退出 0、2001 + 3001 次更新、5742.922 秒。其 `D:\Projects\autolens-upstream-20261024\source` 原目录在本机不存在；本次没有将历史运行冒充重新完成。

## 本机实际执行

环境为 Python 3.12.10、PyTorch 2.11.0+cu128、CUDA 12.8、NVIDIA GeForce RTX 5060 Laptop GPU（约 8 GiB）。

1. 重新执行库里已有的原始函数夹具生成器，直接调用新下载的原版函数。生成的 policy JSON 与冻结参考解析后全部相等，包括两个种子的初始化、两种 RGB 参考、损失及梯度、几何/材料约束、Adam 矩/调度和真实色散；四个原始源码哈希也相同。另重新执行原始 JSON 写入/读取，两个交接用例与冻结参考全部相等。没有覆盖冻结参考。
2. 本机现有代码 Release `AutoLensReferenceTests` **15/15 通过，0 失败、0 跳过**，包含 CPU/CUDA 对照、预算/恢复和小采样完整两阶段。记录见 [TRX](reference-rerun-host.trx)。它们不是 15 个设计成功样本。
3. 新增仅用于验证的固定光线比较工具，使用六片手机初始处方、种子 42、三个 RGB 波长、0°/10°/30°/45° 四个半视场、两个方向及七个瞳位置。共 168 条光线，144 有效、24 被挡；原版和 C# CPU/CUDA 的有效性判断全部一致。双方显式使用 float64，比较原版和现有共享 Core。

| 比较项 | C# CPU 与原版 | C# CUDA 与原版 |
| --- | ---: | ---: |
| 最大落点坐标绝对差 | 1.1939e-10 mm | 1.2018e-10 mm |
| 两种参考下最大图像损失绝对差 | 3.1425e-11 | 3.1278e-11 |
| 所检四参数梯度最大绝对差 | 8.5791e-11 | 8.3865e-11 |

四个参数为第一折射面的曲率、绝对轴向位置、连续材料折射率和像面绝对位置。这里只检查这些参数的图像损失梯度；不把本次初始处方比较扩展成所有处方、全部参数或最终优化结果的认证。CUDA 实际执行 24 批、144 条有效光线求导，没有回退原因。输入、原版输出及逐项比较见 [原版固定光线](upstream-fixed-rays.json)和[比较结果](fixed-ray-comparison.json)。

4. 原版六片 MOBILE_CONFIG 的课程和目录精修均已实际跑通 GPU：显式将迭代数改为 **8 + 8**，原版循环实际为 **9 + 9 次更新**，检查间隔为 4；保留默认训练光线数量、材料投影/JSON 往返、修形和最终 pruning，保留原版绘图函数及其随机数消耗，没有替换光学函数。总用时 **209.52 秒**。未生成动画。原始 TSV、处方、PNG 和 [运行摘要](upstream-short-training/run-summary.json)已保存。

短程终点为约 **18.2014 mm、F/6.552**，没有达到原始 **5 mm、F/1.8** 目标。这是流程运行验证，不能描述成设计达标，也没有将其与完整 C# 训练终点比较。[原版最终图](upstream-short-training/final_lens.png)可供查看。

## 已知仍不同的部分

- 原版完整默认训练采用 float32；产品共享 Core 保留 double。本次固定光线比较统一为 float64 以隔离数学定义。
- 产品训练随机流没有逐位模拟 PyTorch CUDA Philox，也没有复制所有诊断/绘图的随机数消耗；种子相同仍不能保证训练光线与优化轨迹相同。
- 原版另一套 CAMERA_CONFIG 的内部光阑、混合面型和胶合拓扑尚未作为完整原配置复现。
- 产品独立物理硬验收与训练损失分开保留；原版训练结束不等于通过产品验收。

本次不修改产品数值代码、安装包或历史参考。初次受限 testhost 连接中止、验证工具的缺少命名空间/参数声明错误及修正后成功结果均保留；最终 15/15 以正常宿主 TRX 为准。原版提示缺少 Nimbus Sans 字体并回退字体，不影响本次数值比较。

## 复查入口

- [原版调用脚本](run_upstream_probe.py)：参数 `--source`、`--output` 及 `--mode fixed-rays` / `short-training`；运行时将本机 `python-deps` 加入 `PYTHONPATH`。
- [C# 比较工具](fixed-ray-probe/Program.cs)：对应项目读取 `upstream-fixed-rays.json` 并输出 `fixed-ray-comparison.json`。仅开发验证使用，产品不依赖 Python。
- [汇总脚本](summarize_comparison.py)：参数为下载的原版源码目录，汇总参考相等性、数值差、TRX、短程 TSV 与证据 SHA-256，生成 [机器可读账目](comparison-summary.json)。
