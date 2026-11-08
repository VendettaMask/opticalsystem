# Cylinder 与 MEMS 的有限像面 FFT 适用性

六条请求均在本轮开放台账中。已核对菜单处方与冻结原生处方的 SHA-256、配置 1、轴上视场、0.55 µm 波长、像面选择和原比较门槛。MEMS 的旧请求与当前导入后的请求使用既有面号映射，均选择最终像面，证据中记录映射前后的两种指纹。 [六项设置回读和身份](verification.json) [复验前的独立开放台账](../parallel-precision-followup/independent-open.csv)

## 弧矢方向缺少有限点像

每支镜头的 13 条独立原生光线包括主光线、三个带符号的子午边缘对和三个弧矢边缘对。它们均到达像面且未截光。弧矢瞳坐标为 ±1、±0.99、±0.95 时，正负光线的完整像方方向均相同，局部 X 方向余弦均为 0。满瞳边缘的 X 截点分别为 Cylinder 的 −5 和 +5 mm、MEMS 的 −2.5 和 +2.5 mm。子午方向有非零角差，因此单一标量 F 数会掩盖二维标度的退化。 [逐档原生截点和方向](verification.json) [原生实光线和模型回读](../wavefront-directional-f-number/numeric-evidence.zip)

Cylinder 的柱面沿 X 平移不变，曲率仅作用于子午截面。轴上光线的 X 向切向光学动量始终为零，弧矢截点随入射位置平移。MEMS 的两个坐标断点绕 X 轴旋转，原处方按 Y 行寻址。对同一行作 X 平移时，镜面相交平面和所选状态法向相同，反射方向保持不变。偏转行的输出可有 X 方向分量，仍无法沿入瞳 X 连续建立独立的聚焦角度映射。两种处方均缺少将 X 瞳坐标的傅里叶频率换算为有限焦面 X 像高的尺度。 [Ansys MEMS 几何模型](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/User_Defined.html) [Ansys FFT PSF 的像面标度与瞳坐标](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html)

## 原合同判定

| 请求 | 原生设置 | 独立适用性 |
| --- | --- | --- |
| #1071、#1247 FFT PSF | 64×64 瞳采样、128×128 像面，0.25 µm 像面间距，线性强度 | 二维像面需两个有限标度。指定像面间距不能建立缺少的弧矢标度。 |
| #1075、#1251 FFT PSF 截线 | 32 档，中心行，X-Linear，原生自动横轴尺度 | 原输出的横轴正是退化的 X 轴。子午截线有另一物理含义。 |
| #1078、#1254 FFT 衍射包围能量 | 32 档，FFT，质心参考，圆形积分，最大半径 5 µm | 5 µm 的物理圆需在两轴上都有有效的像面坐标和面积。单轴积分不能给出该圆内的能量比例。 |

六条的独立判定限于原有限像面 FFT 坐标合同的物理不适用性。有限探测平面上的衍射仍可存在，适用的有限平面传播有独立的距离、坐标与归一化合同。原冻结 Incomparable、原生错误与欠采样诊断保留。 [原生设置、载荷哈希和逐项判定](verification.json) [Ansys FFT 截线设置](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/MODIFYSETTINGS_keywords.html) [Ansys 衍射包围能量定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html)
