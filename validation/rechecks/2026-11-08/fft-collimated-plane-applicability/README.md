# 平面反射镜与分光立方体的有限像面 FFT

#1273、#1276、#2547、#2550 均在 37 条缺有效原生参考的清单中。核对了原处方和冻结原生源文件的 SHA-256、配置 1、轴上视场、0.55 µm、像面选择、规范请求指纹及原比较门槛。四项的冻结判定为 Incomparable。 [逐项合同、哈希和归档条目](verification.json) [原生证据归档](../../../repairs/2026-10-21/sequential-corpus-full-evidence.zip)

## 处方和原生实光线

Optimization demo 1 的物体在无穷远，原配置只以零曲率平面镜反射到平面探测面。曲率的可变量标志未改变原处方或原生回读中的零值。平面镜的法向恒定，反射方向不随瞳位置改变。已有原生实光线追迹覆盖 72 个通光瞳内节点，像面局部方向逐条均为 (0, 0, 1)，X 和 Y 截点各自分布在 −19.6594 至 +19.6594 mm。 [原生逐光线归档位置及摘要](verification.json)

Beamsplitter cube 的物体在无穷远，平面与倾斜平面之间是均匀的空气、BK7 和 SF1。每个面法向恒定。固定入射方向在第一个界面的折射方向相同，递推到最后一个界面仍只得到同一个传播方向。倾斜界面使整束光发生偏转。已有原生单光线报告在像面给出方向 (0, −0.1931158077, 0.9811759704)。两条原生光扇各有 41 个有效点，子午和弧矢的相对截点分别线性跨越 ±5564.1257 和 ±5000 µm。两支镜头的原生 EFFL 饱和值和物理零光焦度状态此前已分别通过一阶对照。 [原生单光线与光扇的归档位置和指标](verification.json) [一阶无限状态验证](../../2026-11-05/first-order-state/README.md)

## 原分析适用性

两支镜头的透射或反射光束在到达像面时保持平行，像方方向相对于两维入瞳位置的雅可比矩阵为零。原有限像面 FFT 无法用工作 F 数把出瞳空间的傅里叶坐标换算为有限像高。

| 请求 | 原生定义 | 独立结论 |
| --- | --- | --- |
| #1273、#2547 FFT PSF 截线 | 32 档，中心行，未归一峰值的线性 X 截线，自动像高尺度 | X 像高缺少有限的 FFT 尺度。 |
| #1276、#2550 FFT 衍射包围能量 | 32 档，FFT，质心参考，圆形积分，最大半径 5 µm | 5 µm 物理圆在两轴上均缺少可用的 FFT 像高尺度。 |

有限探测平面上仍可有衍射，原有焦 FFT 的适用性判定与有限平面传播分开。两条 FFT 截线和两条 FFT 能量的独立判定为原坐标合同不适用。修正前 Workbench 对这两份处方的直接 PSF 和 MTF 已报告准直域。四条本报告请求的修正前相位诊断和原生缺失记录均单列。 [相关 Workbench 输出及原冻结结果](verification.json) [Ansys FFT PSF 像高尺度](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html) [Ansys 衍射包围能量](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html)

## 正式入口复验

FFT 截线与包围能量沿用已有平面链判定，分别在原分析入口报告缺少有限焦面尺度。截线继续支持既有的接触瞳面合同。按原请求经 Workbench 正常入口重放四条记录，均返回 NotApplicable 且不生成曲线，冻结判定均为 Incomparable。Etalon #2205 截线和 #2208 包围能量的输出与修正前逐字节一致。#2205 对冻结原生参考仍通过，#2208 仍为 Incomparable。 [六条原请求、结果及 Core 哈希](entry-point-replay.json) [复验代码](PlaneReplay.cs)

同一 Core 的 23 项产品定向回归全部通过，包含四条平面链入口和 Etalon 接触瞳面截线的五个场景。 [定向测试回执](focal-plane-fft.trx)
