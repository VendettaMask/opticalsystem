# 方向 F 数、二维 FFT 频率与 Etalon 光程差

## MEMS 波前投影 #1246

OpticStudio 2026 R1 后台以带符号的四个边缘光线验证方向 F 数。独立处方交换偏转边、改变两侧倾角、改用按列寻址并加入离轴视场。Cylinder 对照以正负渐晕偏移验证双侧不等的数值孔径。仅一侧偏转时，原生方向 F 数取该侧的 NA。两侧均偏转时，取两侧 NA 的算术平均。近乎平行的对照将忽略边缘光线的转换范围限定在夹角正弦约 3.02×10⁻⁸ 至 3.02×10⁻⁷。 [边缘光线、操作数与候选公式](predictions.json) [近乎平行的分辨对照](predictions-near.json) [原生载荷](numeric-evidence.zip) [载荷身份](capture-manifest.json) [Ansys 方向 F 数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/First_Order_Optical_Properties.html)

正式的固定网格波前出瞳投影和 SFNO/TFNO 共用该方向约定。投影保持 10,000 上限。操作数继续要求全瞳探测成功和所选轴存在正有限 NA。衍射原有的方向均方根与双侧算术平均路径继续使用各自定义。

#1246 原 64 档下，Workbench 的子午方向 F 数为 1.681536782017370，原生 TFNO 为 1.681536782017385。两边横向投影比例分别为 0.0001681536782017370 和 0.0001681536782017385。原 63 个有效节点的原生独立相位对照通过，NRMSE 为 4.47×10⁻⁵，覆盖 100%。Cylinder #1070 的相位和投影比例复验通过。两条原生有出瞳形状网格的欠采样诊断继续保留。另以近乎平行的弱倾角处方导出了两张 64 档波前图，开关出瞳形状后的数值载荷逐字节相同。该弱倾角对照无法区分两种投影比例计算。 [原请求复算、相位和投影](verification.json) [原生波前诊断和 63 个独立原生节点](../wavefront-axis-reference/README.md) [Ansys 波前出瞳投影定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)

## Cylinder #1073 和 MEMS #1249

两条均为原处方、配置 1、轴上视场和 0.55 µm 波长的有限像面 FFT MTF。每支镜头的 13 条原生实光线在主光线和三个正负边缘档均到达像面且未截光。像面局部弧矢方向余弦相对主光线的最大变化为 0，子午最大变化分别为 0.1223076403 和 0.2569216075。因此原请求的联合二维 FFT 标度缺少正的弧矢频率支撑，Workbench 原请求重算返回相应原因。冻结原生 MTF 均附欠采样诊断。独立结论限定为这两条有限像面二维 FFT MTF 请求的物理适用性。单轴和有限平面 Huygens 各有独立物理契约。 [频率支撑和原请求身份](verification.json) [Ansys FFT MTF 频率定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html)

## Etalon #2216 和 #2220

原处方的像面和出瞳平面重合。同合同的原生有焦显式光线中，每个波长有 80 条非主光线，方向与主光线完全相同。它们距主光线像面截点的垂距均至少为 0.24996 mm，因此无法与以该点为中心的零半径球相交。原生 Absolute 控制保持像面、处方、波长、视场和入瞳光线坐标，只切换 OPD 校正。Absolute 和 ExitPupil 两份显式追迹的实际截点与方向逐点一致。 [原生 Absolute 和 ExitPupil 逐光线载荷](../../2026-11-07/curvature-image-space-reference/native-controls.zip) [Ansys 出瞳参考球与路径校正](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Exit_Pupil.html)

用原生 Absolute 光程差和主光线方向投影恢复平面极限，残差小于 1.43×10⁻¹⁴ 波。在 10¹⁰ mm 计算尺度将方向投影加至基准距离再相减，可以复现原生有焦读数，最大残差为 1.30×10⁻¹⁴ 波。观测到的 ULP 为 1.9073486328125×10⁻⁶ mm，可确定计算距离的二进制数量级。具体内部替代球半径未由原生接口读回。

以垂距 ρ 计算稳定球面增量 ρ² / (√(R²−ρ²) + R)，同时验证实、虚两种校正符号。与观测 ULP 相容的半径区间为 [2³³, 2³⁴) mm，包含 10¹⁰ mm。该球面增量随半径增大单调减小，全区间的最大可能校正小于 1.456×10⁻⁶ 波。每个波长在区间两端及 10¹⁰ mm、两种符号的对照均按原逐点 10⁻⁵ 波绝对门槛通过，两条曲线共同覆盖 82 个原坐标。独立的稳定参考验证通过，两条原冻结 Difference 单列。 [逐节点稳定重建、两种符号和原阈值](verification.json)

复验沿用原请求指纹、原比较策略和相同 Workbench 入口。原生数值写盘后，批处理宿主退出阶段报告 FRU 内部错误。 [Workbench 复算代码](Verify.cs) [方向 F 数原生控制归约](Summarize.ps1) [原生任务状态](capture-manifest.json)


