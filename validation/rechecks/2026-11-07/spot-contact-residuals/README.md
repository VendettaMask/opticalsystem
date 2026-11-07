# 余弦立方点列、接触光瞳 Huygens MTF 和数值余项

## 余弦立方点列

有限物距的余弦立方六角点列按入口平面的累计通量反解径向采样坐标，并用等通量的统计权重。点列边缘和光线总数保持原定义。其他正式追迹和衍射入口继续使用原物理照明权重。有限平面控制的入口半径为 5 mm，物距和像方传播距离各为 5 mm，避免用单一折叠镜头反推采样规则。OpticStudio 2026 R1 后台对照覆盖平面控制的 3、6、20 环和泰曼–格林原处方的 3、6、20 环，并检查了两种处方的均匀照明。 [采样与照明定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Standard_Spot_Diagram.html) [余弦立方定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Apodization_Type.html) [原生对照和设置哈希](native-spot-controls.json)

冻结的 #2153 和 #2170 使用相同的 20 环、主光线参考和 0.55 µm 波长。RMS 半径从 69.45860584 µm 变为 70.55693208 µm，与原生 70.55693208 µm 一致。两条请求由接近转为通过。RMS 和最大光斑半径的最大误差为 7.62×10⁻¹² µm。 [原请求复验](focused-results.json)

## 接触光瞳 Huygens MTF

Grid Sag 两配置及 Etalon 的轴上 Huygens MTF 与通焦 MTF 现在复用已有的接触光瞳 PSF。轴上平面波在固定像面窗口内沿法线移动时，传播相位的共同增量从强度中消去。通焦计算逐个所选视场判定这个条件，因此含有其他离轴视场的 Etalon 仍可正确求出轴上曲线。频率曲线保留原 64 点变换，通焦保留原 32 点变换。该条件要求无偏振、平行轴上照明，以及满足既有接触光瞳判定的处方。

冻结参考的两项 Grid Sag 频率 MTF 在 50 cycles/mm 为 0.7573484155，通焦 ±0.01 mm 为 0.5242604617。Etalon 对应为 0.7573484143 和 0.5242604593。原频率与通焦请求分别为 #1140、#1162、#2204 和 #1145、#1167、#2209。六条冻结请求由不可比转为通过，曲线最大绝对差为 3.50×10⁻⁹。同期复验的三项 Huygens PSF #1138、#1160、#2202 均保持通过。 [原请求复验](focused-results.json)

上述 11 条请求均从普通 Workbench 入口重放。逐条记录了原设置指纹、冻结参考文件哈希和最终 Core 哈希，全部使用原比较阈值。 [复验身份和指标](focused-results.json)

## 原差异和接近项

53 条原请求逐条记录已有独立证据及未决原因。两条泰曼–格林点列本轮修复。37 条已有独立同源加密、细步长、等效设置或舍入诊断参考，原冻结判定继续保留。其余 14 条需要进一步区分采样、物理域和实现差异。 [逐条记录](frozen-residual-triage.csv)

Axicon #954 的镜头仅定义轴上视场，原生照度数组却从 0 延伸到 10⁻⁶ 度并用该区间最大值归一。归一后的轴上值为 0.9935457747，本软件单一物理视场为 1。保留原判定。Tilted object #2752 在共同 0–10 度域内仍有 0.008285 的最大照度差。GRIN interconnect #825 的焦距相对差为 1.246×10⁻⁶，略高于原通过门槛 10⁻⁶。
