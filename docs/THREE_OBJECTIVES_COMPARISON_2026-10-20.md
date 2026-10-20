# 三个指定 Objectives 镜头的 Zemax 数值对比

本报告保留修复前的三镜头结果。Petzval 两项后续修复及同设置重验见[修复报告](PETZVAL_ILLUMINATION_RMS_REPAIR_2026-10-20.md)，下方原始计数和数据不改写。

日期：2026-10-20。对象为用户指定的 `Cooke 40 degree field.zmx`、`Double Gauss 28 degree field.zmx` 和 `Petzval.zmx`。

**修复前结论：Cooke 本次 30 项全部通过；Double Gauss 的 29 项通过，另一个低采样无效参考在独立高采样控制中通过；Petzval 有相对照度超差和 RMS 随视场的 Close。当时不能把三个镜头概括为全部一致。**

## 方法与身份

原文件来自本机 `Documents/Zemax/Samples/Sequential/Objectives`，先按文件名明确选取，再逐字节复制到独立工作区。原件、选取副本及实际计算副本的 SHA-256 均核对，完成后原件 3/3 保持。原始文件名和哈希见[原件完整性](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/original-source-integrity.json)。

原生数值由新建的 OpticStudio **2026 R1，build 260127，Enterprise** API 会话重新采集；软件端由当前工作区的正式 Application/共享 C# Core Release 构建重新计算。没有复用旧数值捕获或修改产品计算实现。计算期间三份记录程序集的 SHA-256 保持，身份见[计算记录](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/computation-identity.json)。本轮验证的是当前源码构建，不是重新验收已发布安装包。

每镜头执行 18 类主分析，以及三个正权波长各四项边缘视场控制，共 **90 项**；另对各定义视场、三个波长执行 GQ6 逐面光线检查，共 **9 组、1728 条发射光线、16200 个逐面记录、97200 个坐标/方向值**。采集任务 102/102 完成、原生失败为零，软件分析 90/90 完成。覆盖包含一阶参数、单光线、点列、光线扇形、OPD、波前、FFT/Huygens PSF 与 MTF、能量、照度、RMS 扫描和场曲/畸变的现有可比通道；First Order 仅核对已实现的 EFL 与 `abs(EFL)/EPDI`，不扩大其物理覆盖。

沿用原[比较设置](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/comparison-settings.json)，SHA-256 为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。Pass 表示满足该设置中的逐点或 NRMSE 判据；Close 仍未达到 Pass，不计为通过。主矩阵覆盖固定视场、波长和采样请求，不代表所有设置或采样收敛的通用认证。

## 原设置结果

| 镜头 | Pass | Close | Difference | 不可比 | 错误 | 逐面光线 |
|---|---:|---:|---:|---:|---:|---:|
| Cooke 40 degree field | 30 | 0 | 0 | 0 | 0 | 3/3 通过 |
| Double Gauss 28 degree field | 29 | 0 | 0 | 1 | 0 | 3/3 通过 |
| Petzval | 27 | 1 | 1 | 1 | 0 | 3/3 通过 |
| 合计 | 86 | 1 | 1 | 2 | 0 | 9/9 通过 |

逐项字段、波长、采样和原始误差见[90 项分析矩阵](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/analysis-matrix.csv)，总数见[机器汇总](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/summary.json)。

一阶有效焦距的代表性读数如下，单位均为 mm：

| 镜头 | Zemax | 软件 | 绝对差 |
|---|---:|---:|---:|
| Cooke | 49.99999950000002 | 49.99999950000007 | 4.97e-14 |
| Double Gauss | 99.50067904919331 | 99.50067904919332 | 1.42e-14 |
| Petzval | 100.00000074783689 | 100.00000074783692 | 2.84e-14 |

逐面光线的坐标最大差分别约 **5.37e-14、4.97e-14、1.99e-10 mm**；九组的方向最大差均不超过 **1.78e-15**。坐标采用既有 `1e-6 + 1e-6*abs(reference)` mm 判据，方向采用 `1e-8 + 1e-6*abs(reference)`，通光与首遮拦面要求完全一致。实测通光差异、首遮拦面差异及坐标/方向超限数均为零；详见[逐面光线记录](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/ray-results.json)。光线一致不能代替全部分析一致。

## Petzval 的两项残差

| 分析 | 设置/位置 | Zemax | 软件 | 差异与判定 |
|---|---|---:|---:|---|
| 相对照度 | 0.5876 µm，轴上 | 1.0000000000 | 0.9621076166 | 最大绝对差 0.0378923834，约 3.789 个百分点；NRMSE 0.0179001，Difference |
| RMS 随视场 | 0.5876 µm，归一化视场 0.2，即 0.8° | 8.0492195529 µm | 8.3017346135 µm | 最大绝对差 0.2525150606 µm；NRMSE 0.00879000，Close |

相对照度原 Pass NRMSE 上限为 0.003、Close 上限为 0.01，本次超过两者。RMS 的对应上限也是 0.003/0.01，本次只满足 Close。没有通过放宽容差、重新归一化或拟合曲线改变结论。本轮确认存在数值残差，尚未以额外诊断证明其具体算法根因。

可直接复核[相对照度原生值](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/petzval-relative-illumination/normalized-native.json)、[软件值](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/petzval-relative-illumination/normalized-workbench.json)，以及 [RMS 原生值](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/petzval-rms-vs-field/normalized-native.json)、[软件值](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/petzval-rms-vs-field/normalized-workbench.json)。[残差汇总](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/remaining-differences.json)保留完整请求和指标。

## 两项衍射包围能量补充控制

原设置的 32×32 瞳采样下，Double Gauss 和 Petzval 的原生报告均提示“取样太低，数据不准确”，因此原矩阵两项保持不可比。另建独立对照，把瞳采样提高至 **128×128**，沿用现有派生 FFT 网格规则（软件像面 256×256）；视场 1、波长 2、0–5 µm 半径、参考和容差保持。两项新采集均没有低采样警告，并通过原能量容差：

| 镜头 | 128 档结论 | 各比较曲线中的最大绝对差 | 最差 NRMSE |
|---|---|---:|---:|
| Double Gauss | Pass | 8.38e-11 | 1.47e-10 |
| Petzval | Pass | 4.94e-8 | 1.18e-7 |

这 **2 项是独立设置的补充通过**，不替换或重计原 90 项中的两条不可比。结果见[补充汇总](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/supplemental-energy-128.json)及[补充设置](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/supplemental-comparison-settings-128.json)。

## 原始证据与复核

原生模型、实际设置读回、CFG、报告/数组、软件输出、逐面数据、完成记录及日志保存在本地 `artifacts/zemax-three-objectives/2026-10-20/run-v1`；独立高采样证据位于同级 `controls-128`。这些大体积本地原始输出不假称已随 Git 分发。[证据清单](../validation/zemax/2026-r1/three-selected-objectives-2026-10-20/evidence-manifest.json)保存相对路径、SHA-256 和长度，仓库内保留汇总、完整分析矩阵、逐面误差及两项残差曲线。

复核沿用现有工具流程：构建 `SequentialCorpus20261019` 与 `Reporting`，将明确选取的三份源文件放入新目录，依次 `prepare`、原生采集、`workbench`、`compare`、报告生成。补充控制通过正式比较 CLI 仅选择 `Diffraction Encircled Energy` 并传入独立的 128 档设置。原设置、原捕获和历史 Objectives 全目录结果均保留。
