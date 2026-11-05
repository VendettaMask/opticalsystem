# 面形公差和机械支点回归

日期为 2026-11-05。范围为本地顺序公差流程，从向导和操作数编辑器到真实曲面追迹、指定频率 MTF、灵敏度、反求、Monte Carlo、装调补偿与公差研究文件。

## 独立解析控制

| 控制 | 验收 |
| --- | --- |
| TEXI，6 mm 净口径，0.6328 µm 检测波长，1 个双程条纹，Fringe 2–5 项 | 相对于名义偶次非球面，口径中心与 +X 边缘的附加矢高分别为 −0.1582 µm 与 +0.4746 µm。验证 X/Y 法线斜率、交点、缩放负端点和恢复。 |
| 具有名义偏心和归一化半径的 Fringe Sag | 原名义多项式保留，扰动与名义归一化坐标一致，沿用名义外推规则。 |
| TEZI，Standard 2–6 项 | 对单位圆盘独立积分，RMS 为 0.004 mm。验证正端点、缩放负端点和各 20 次均匀及修正正态采样。另用 Legendre \(P_{10}\) 的中心值、边界值和边界导数验证 Standard 第 211 项。 |
| ISOA，632.8 nm、0.6328 µm、2 个 0.6328 µm 双程条纹 | 三种单位的边缘峰谷矢高均为 0.6328 µm，中心为零，半径一半处为峰谷值的四分之一。每种单位运行 2048 次，归一化 RMS 在 0.41–0.47 内，样本范围严格位于两端点之间。 |
| Core 串行试验中的名义曲率和 TEZI | 将制造面形放在半径行之前，仍得到半径 52 mm 的解析球面矢高和 0.002 mm 的面形 RMS，试验后恢复名义面。 |
| 机械支点和刚体姿态 | 固定全局支点 (3, 3, 15) mm 旋转 +90°，首末面原点分别到达 (2, 8, 17) 与 (3, −1, 19) mm，末面原始 +Z 法线转到 −Y。验证多轴运动后的支点与反向恢复。 |

## 产品链路

- Cooke 处方通过正式应用服务运行 TEXI、TEZI、ISOA、曲率公差与有界像距补偿。比较单工作线程和双工作线程有序结果，确认 MTF 有效、同种子一致、原处方及其修订保持原值。
- 反向灵敏度独立求解两侧，面形表回写两侧中更严格的共同幅值。检查收紧至零的研究仍可运行。
- 镜组支点与 X/Y 倾斜、X 偏心补偿共用。在固定种子的三次试验中比较单线程和双线程补偿结果，并比较使用首面顶点的结果。检查互相冲突及非有限坐标被拒绝。
- 离屏 Avalonia 控件覆盖向导的 TEZI 单位和项号、公差编辑器切换与保存、支点在明暗主题及 480 DIP / 150% 缩放下的横向测量，以及旧 v1–v4 和新 v5 的版本边界。

## 实际运行

Windows Release 隔离构建成功。公差筛选使用 SurfaceFormTolerancingTests、FringeTolerancingTests、TolerancingWorkflowTests、MtfTolerancingTests 和 MtfTolerancingPanelTests，88/88 通过。SpecialSagSurfaceTests 及 ZemaxZernike 筛选另有 21/21 通过。两组均无失败和跳过。

本地化 sync/check 验证了 3104 条译文及 2864 个静态界面出现位置，漏译和错误均为 0。

## 范围边界

公式与参数定义来自 Ansys 的公开 [TEXI](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/TEXI_Tolerance_on_Surface_Irregularity_Using_the_Fringe.html)、[TEZI](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/TEZI_Tolerance_on_Surface_Irregularity_Using_the_Standard.html) 和 [ISOA](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/ISOA.html) 文档。验证使用独立解析量、产品入口和本地处方。OpticStudio 逐样本和逐值结果、ISO B/C/D、偏置和非圆形 ISO 口径以及专有 TOL 交换仍待验收。支点覆盖镜组装调补偿，制造公差 TETX/TETY 仍使用首面顶点。
