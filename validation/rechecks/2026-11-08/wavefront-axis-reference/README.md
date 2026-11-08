# 波前投影与轴上参考

在原菜单处方、配置、视场和波长下，通过 OpticStudio 2026 R1 后台接口补采。原生波前的 1024 与 2048 档均按原设置读回，#1070、#1246 仍报取样太低，均无数值网格。以下独立对照保留这两条原生诊断。 [原始载荷](numeric-evidence.zip) [请求、策略与载荷哈希](manifest.json)

## 波前

另采归一化入瞳图和 63 条显式子午光线。两种原生接口在原 64 档子午物理坐标上分别吻合到 1.29×10⁻¹¹ 波（#1070）和 8.10×10⁻¹³ 波（#1246）。两条的归一化入瞳图在 128、256 档均按原门槛与 Core 通过，掩码共同覆盖率均为 100%。这些全瞳图独立于原先开启出瞳形状的图。 [逐档比较](comparisons.json) [Ansys 波前图设置](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)

#1070 Cylinder 的原 64 档投影由两轴工作 F 数确定。原生与 Core 的弧矢 F 数均达到 10,000 上限，子午 F 数均为 4.08805205238，横向投影比例为 0.000408805205238。以该比例在原坐标网格逆投影，全部 63 个有效节点都落在子午线。与独立原生节点相比，Core 最大差为 9.37×10⁻¹¹ 波，覆盖率 100%。原投影结果的独立验收通过。 [投影与节点度量](comparisons.json) [Ansys 工作 F 数定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Working_F.html)

#1246 MEMS DLL Sample 的原 63 个投影节点通过相位比较，NRMSE 为 4.47×10⁻⁵。原生子午两侧的 NA 分别为 0.297347049 和 0。原生 TFNO 1.681536782 对应有偏转的一侧，此阶段 Core 的 2.378052123 对应两侧 NA 的均方根。横向投影比例分别为 0.0001681536782 和 0.0002378052123。后续修正方向工作 F 数后，投影比例的独立对照通过。 [修复前原生 TFNO/SFNO 与 Core 输出](comparisons.json) [逐光线原生方向](numeric-evidence.zip) [方向 F 数修复后复验](../wavefront-directional-f-number/README.md)

## 轴上 RMS

#2605 OAP using Chebyshev Polynomial surface 只定义一个实际轴上视场。原生 RMS 曲线仍无数值。使用 6、10、16 环的原生实光线像面截点独立计算透射光线的加权质心 RMS。在 72、200、512 个发射节点中，原生与 Core 的实际通光掩码分别逐点匹配 48、128、296 条光线，三档的物理 RMS 最大差为 3.49×10⁻¹² µm。Core 的正式 GQ 计算按既有方法还纳入可延拓的未截光几何交点，三档分别用到 66、182、458 个节点。正式原 6 环发布值与最细独立原生物理参考相差 4.13×10⁻⁸ µm，低于原绝对容差 0.001 µm。10→16 环的原生物理结果与 Core 正式结果各自通过原阈值的加密核对。唯一物理坐标的 RMS 独立通过。 [逐档、掩码及阈值](comparisons.json) [Ansys RMS 与 GQ 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html)

## 相对照度

Grid Sag Example 两个配置、Twyman Green Interferometer 和 Etalon 在 10、20、40 档的 RELI 与 EFNO 均返回 0。以相同接口复算的 Axicon 正控返回 RELI = 1 且 EFNO 有限为正。后续实光线补采分别确定了三项的物理不适用和 Twyman Green 唯一视场的归一化相对值。 [操作数及读回](numeric-evidence.zip) [后续实光线复验](../illumination-angular-support/README.md) [Ansys RELI/EFNO 定义](https://ansyshelp.ansys.com/public/Views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Relative_Illumination_Operand.html)

复采用 [准备脚本](Prepare-Controls.ps1) 和现有的 `tools/diagnostics/StandardMatrix20261015/Run-Native.ps1`。比较程序的 `prepare-oap` 模式可补充轴上实光线请求。所有请求只加载原处方，使用独立后台进程。原生批量宿主在各请求成功写出回执之后关闭时报告 FRU 内部错误。 [比较源代码](verify/Program.cs)
