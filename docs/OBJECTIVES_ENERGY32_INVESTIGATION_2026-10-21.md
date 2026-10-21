# Objectives 原 32 档能量差异继续调查

日期：2026-10-21。状态：**未修复，未交付新包**。本轮只增加离线诊断、原生捕获和报告，没有修改产品 Core、Application、分析默认值、测试容差或原参考。调查基线为 `ecf35b1f5ca1365e426ad0b3ac2e8695fb836965`。

用户要求继续修复 Apochromat2、Apochromat4、MIL 双胶合的原 32 档差异，保持原容差，只做相关验证，修好后同步并覆盖原包。此前[新增 128 档验收](../validation/zemax/2026-r1/objectives-energy128-acceptance-2026-10-21/acceptance.json)保持原始身份，不能替代这次要求。未将诊断候选作为产品修复，也没有把测试包重新打包成一个仍有相同问题的新版本。

## 原设置重新验证

从产品自带原始镜头经正式导入、Workbench、规范化器和比较器重新计算十个原可比能量请求，结果仍为 **7 Pass / 3 Difference**。原七个通过项保持，不能据此推算完整 638 项目录的当前通过数。

| 原 32 档请求 | 理想曲线最大差 | 实际曲线最大差 | 请求结果 |
| --- | --- | --- | --- |
| Apochromat2 | 0.013207881802895782 | 0.013207487429917286 | Difference |
| Apochromat4 | 0.01320291846083399 | 0.013198800141370759 | Difference |
| Doublet using MIL number glasses | 0.013210155293478243 | 0.009771899587863175 | Difference；实际曲线单独为 Close |

三项及 Apochromat 对照还重新运行了独立 Zemax OpticStudio 2026 R1 / build 260127 / 有效 Enterprise API。四个案例的两个序列各 401 点，X/Y 数组与冻结原生结果逐值完全相同。重新序列化的 JSON 文件格式不同，文件哈希不同，不能把它们称为原文件字节相同。四项捕获完成，进程退出码为 0；退出时的 `FRU__delta_init` 原始消息保留在日志中。

配置 1、轴上、原所选主波长、32 档瞳/64 档像面、质心参考、401 点、5 µm 和非偏振设置保持。EnergyFraction 原策略仍为 Absolute=`1e-5`、Relative=`0.002`、NRMSE=`0.003`、CloseNRMSE=`0.01`、MinimumCoverage=`0.95`；策略 SHA-256 仍为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。

## 新增定位结果与拒绝的候选

1. **实际曲线验证拒绝了一个看似可用的网格修法。** 将物理频率间隔和截止频率相乘后取倒数，三项实际曲线最大差分别降至约 `1.247e-12`、`4.909e-13`、`5.651e-12`。但同一构造让原 Apochromat 理想/实际曲线最大差变为 `0.0131719 / 0.0130723`。完整 FFT 追迹确认它错误纳入四个对角结点，不能只展示三项成功结果或当作可交付修复。
2. **宽度先于间距的构造是新的离线线索，尚不是修法。** 采用 `pitch = sqrt(N) * lambdaMM * F / N`，再形成 `step = 1 / (N * pitch / lambdaMM / F)`，使用原生工作 F 数时能解释这十个参考的边界支持集。使用产品自身光锥时不能全部保持。原生 F 数属于外部诊断输入，未进入运行时代码；这组有限案例也不能证明 Zemax 的内部实现或跨设置适用性。
3. **归一化与法线末位差已分离。** 对 Apochromat、Apochromat2、Apochromat4、MIL、共同 Cooke 对照的 88 条非平面轴向边缘记录，在原生交点处计算圆锥隐式梯度，法线 Z 分量 88/88 与原生一致，完整法线 76/88 一致。额外归一化会改变部分末位。该检查使用原生位置，不表示产品完整追迹已经逐位相同。
4. **源文件保存曲率与加载后活动几何也有末位差。** Apochromat2 等带曲率求解的表面可观察到这种差别；改用原生加载后的半径仅作为因果控制，仍不能闭合三项。没有把外部活动几何、原生折射率或方向余弦写入产品计算。
5. **折射、求交与更高精度均未形成安全替代。** 208 条单界面控制还包含八条带符号折射率的反射探针。普通比例 Snell 写法在这组控制中仍优于测试过的等价重排；独立十进制高精度控制也未改善逐位一致性。稳定求根、单位方向假设、法线重排及原生活动几何组合均有回退。不能据此断言原生使用扩展精度，也不能把某个原生导出量当作其全部内部中间状态。

上述结果进一步定位了四个圆瞳对角结点的有限精度跳变，但**没有获得同时通过三项原请求并保持既有通过项的通用产品算法**。后续需要继续隔离由活动几何、局部坐标传播和方向归一化进入光锥尺度的舍入，不应继续通过挑选镜头、目标结点数、参考曲线或任意浮点阈值拼接运行时分支。

Ansys 的 [IRayTraceNormUnpolData 说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/IRayTraceNormUnpolData.html)明确区分输出方向余弦与表面法线；[折射率计算说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Index_of_Refraction_Computation.html)说明内部光线使用相对折射率。具体末位差和边界支持集是本机控制的测量结果，不是官方内部算法声明。

## 验证、证据和交付状态

诊断项目 Release 构建零警告/错误。相关 `ObjectivesResidualPrecisionTests` **6/6** 通过，包括两个既有 PSF 守卫、三项 128 档验收及参考/策略完整性；它们没有覆盖原 32 档失败。VSTest 已完成并生成 TRX，外层 PowerShell 因日志参数的未引用分号又尝试执行一个无效命令，外层退出码为 1；保存完成的 TRX 并明确记录该差别，没有重复运行或把外层命令记为成功。未跑全测试、全目录矩阵、独立 Cooke/Tessar 调查、重新发布或本机安装。

工作区和 HEAD 的 **2668/2668** 冻结资产保持，聚合 SHA-256 仍为 `3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10`。正式重算使用的 Release Core/Application SHA-256 分别为 `491f5903cda789d8390164047673d32f2660efc27d066f60d156731212d5a0f1` 和 `8174c117c2517faed31ffed4cedbbee9a5d9884958e906de4a05cc564e8a0f46`，与此前默认输出相同。

[本轮调查证据包](../validation/repairs/2026-10-21/objectives-energy32-investigation-evidence.zip)保存 **429** 个载荷、19,970,604 字节，SHA-256 为 `a8fce156f8d4af99ee3208d6783323fd718615b46a5e6c7b87a533be18946b50`。所有载荷从 ZIP 读回验证长度和 SHA-256。包内包含原请求/参考、独立原生重复捕获、逐面控制、失败候选、正式重算、TRX、源码、冻结完整性报告和清单，不含专有 API 二进制。关键内部路径：

- `artifacts/validation/objectives32-repair-20261021/formal-recheck-v1/summary.json`：正式原策略结果。
- `.../formal-recheck-v1/native-repeat-numeric-comparison.json`：四组原生重复的数值一致性。
- `.../frequency-control-v1/summary.json`：三项消差但破坏 Apochromat 的完整候选结果。
- `.../geometry-audit-v2-active/rows.json`：原生活动几何隔离控制。
- `.../investigation-scope.json`、`.../related-residual-guards.trx`：范围、退出码区别和六项测试结果。

当前发布仍为 `1.0.4-win-x64-33f392807053`，来源及原包记录见[前次交付账目](validation/UPDATE_SYNC_PACKAGE_REFRESH_2026-10-21.json)。本轮未推送远端、未覆盖原安装器；“修好后同步并覆盖原包”的条件尚未满足。[本轮机器账目](validation/OBJECTIVES_ENERGY32_INVESTIGATION_2026-10-21.json)与本报告保持相同状态。
