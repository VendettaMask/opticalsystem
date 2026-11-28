# 系统偏振分析

顺序模式的 **分析 → 偏振** 包含以下入口，并在命令搜索、分析页面、报告选择及计算指南中共用同一目录。

| 入口 | 结果 |
| --- | --- |
| 偏振光线追迹 | 所选光线逐面的局部复电场 Ex/Ey/Ez、透过率、相位差和椭圆方位 |
| 偏振光瞳图 | 光瞳各点的偏振椭圆，以及完整电场和功率数据 |
| 透过率计算 | 各视场／波长的光瞳积分透过率，及主光线逐面相对、累计透过率 |
| 偏振相位像差 | X、Y 光瞳截面上 Ex/Ey 相对于主光线同分量的相位变化 |
| 透射光瞳像差图 | X、Y 光瞳截面上的功率透过率曲线 |
| Jones 瞳 | 原有的复 Jones 矩阵分量图 |

前五项提供视场、波长、测量面、瞄准及采样设置。默认采用系统偏振；取消“使用系统偏振”后，可为当前分析单独设置 Jx/Jy 和 X/Y 相位。透过率分析另外提供非偏振功率平均。局部设置随分析窗口保存，保持工程的系统偏振。

透过率计算的视场或波长选择 0 表示全部。圆形入瞳网格的积分保留被截光的发射光线，以零输出计入分母；渐晕压缩计入面积因子。非偏振输入对两种正交输入的功率作平均。均匀玻璃界面使用处方实折射率，体内按消光系数作 Beer–Lambert 衰减；相干膜层使用共享求解器的复振幅。

偏振光瞳椭圆为测量面局部 Ex/Ey 的投影，透过率包含 Ez。偏振相位像差不包含几何传播相位，以主光线的同一电场分量为参考，输出 ±0.5 波主值；电场为零时相位为空值。原 Jones 瞳保留其既有矩阵定义与适用范围。

功能与计算定义参考官方的[偏振光瞳说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25201/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Polarization_Pupil_Map.html)、[透过率说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25201/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Transmission.html)和[相位像差说明](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Phase_Aberration.html)。当前实现和本轮功能检查见[状态记录](CURRENT_STATUS.md)。
