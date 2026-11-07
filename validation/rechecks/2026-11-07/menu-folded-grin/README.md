# 折叠镜组、分光棱镜、MEMS 和 GRIN 精度复验

## 折叠双抛物镜

对经过 90° 坐标断点的有限共轭光线，起点在变换后参考面上且方向沿面时，保留零交距。真实光学平面的相交判据保持独立。原生逐面比较将保留坐标断点的法线统一到变换后的入射方向。掠入射下若顶点平面移算距离远超处方尺度，交点由原坐标方程求解，避免长距离平移的数值消减。

两条单光线、波前图和离焦 MTF 均通过。波前最大差从约 1.07×10⁻⁵ 波降至 1.07×10⁻⁶ 波，共同覆盖 99.32%。Huygens 离焦沿实际像面局部 Z 轴移动，场曲与畸变只有轴上采样点时使用实际追迹的参考点。该镜头的 22 个原请求均通过。[Ansys Huygens Through Focus MTF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Huygens_Through_Focus_MTF.html)

## 分光棱镜

倾斜平面进入实际出瞳求解。两项光程差和一项波前图通过。像方光线平行时，Huygens 有限采样单元的角宽为零，使用相应的傅里叶极限。原 Huygens PSF、截线和离焦 MTF 均通过。

该平面折射处方在有焦模式下缺有限焦面标度。Core 对直接 FFT PSF 和 FFT MTF 返回不适用。独立 OpticStudio 四档瞳采样 64、128、256 和 512 均读回实际设置，PSF 全部报无效，MTF 全部报欠采样。保留各档原生文本、诊断与数值缓冲。[出瞳位置定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Exit_Pupil_Position.html) · [后台采集](native-controls.json)

## MEMS 像素边界

八条原生探针确认逐面位置、方向和实际光程一致。微镜像素边界的指向跳变使两侧方向差在探针步长缩小时保持有限。以两侧光线的极限交点确定出瞳轴向位置，从与主光线连续的分支求出瞳宽度。分段平面波叠加使用实际像素方向和质心偏移，原生中心开关及 32、64、128 档读回均已验证。

两项光程差、Huygens PSF、截线、MTF 与离焦 MTF 共六项按原阈值通过。普通 MTF 最大差约 4.8×10⁻⁹，离焦 MTF 最大差约 4.7×10⁻⁷。[原生设置和结果归档](native-controls.zip) · [Ansys Huygens PSF 中心定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)

## GRIN 弧长与往返镜面

Selfoc 和 GRIN interconnect 共六个原请求的逐面报告在第 2 面将非零体段填为 0 mm。比较器按材料类型、处方厚度和实际体段追迹确认缺报，保留其余已报告数据的比较。两种 GRIN 的 76 组原生截点使用 0.001 和 0.0005 mm 两档积分步长，细档有 32 个弦段，通过 8、16、32 段外推核对。六个独立弧长均通过，Core 最大误差约 1.31×10⁻⁹ mm。冻结整表仍标为不可比。[逐光线弧长](arcs.json) · [Ansys 单光线报告](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Single_Ray_Trace.html)

Optical interconnect 的两条原单光线仍有镜面后报告差异。九组原生控制复现了相邻零间距镜面和回程 GRIN 的角度变换，用体内局部折射率与轴上折射率的比值可将入射角差复算到 7.4×10⁻¹⁰ 度以内。逐面比较规则尚待接入。该镜头的一阶焦距在原生 0.01→0.001 mm 步长下相差约 3.3×10⁻¹¹ mm，细档为 2.424207569 mm，Core 为 2.424613029 mm，一阶差异继续开放。[往返 GRIN 补采](reflective-grin.json) · [Ansys Gradient 9](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Gradient_9.html)

## 远心相对照度

无穷远出瞳下，有限图像余弦网格的像点校正项趋于零。积分沿用实际追迹的入射方向与原瞳面采样和边缘权重。Telecentric system 的 21 个原视场点全部通过，归一化照度最大差约 4.44×10⁻¹⁵，有效 F 数为 5.1480732877，符合原报告的显示精度。[定向结果](focused-results.json)

## 全量复验与剩余项

保留 95 支菜单镜头、原请求、原阈值和冻结参考。原矩阵 3,024 条请求，重算 2,904 条。94 支可导入，共 114 个配置，93 支有原矩阵条目。分段微镜传播与无穷远出瞳照度最后两处修正采用定向重算。两次导入的 114 个配置快照逐字节一致，分别只有第 40 支 MEMS 和第 44 支远心镜头第 1 配置命中对应条件。最终版重算这两支的 23 条相关原请求，其余采用全量重算结果。[完整身份与代码哈希](identity.json) · [定向范围审计](coverage-audit.json)

| 冻结判定 | 上一轮 | 本轮 |
| --- | ---: | ---: |
| 通过 | 2,461 | 2,496 |
| 接近 | 25 | 22 |
| 数值差异 | 67 | 47 |
| 不可比 | 415 | 424 |
| 求值错误 | 56 | 35 |

新增 35 项通过，原通过项无回退。独立无焦阈值补充 15 项通过。251 组固定光线仍为 143 组通过、108 组不可比，共比较 259,395 个物理面样本。30,700 个冻结文件哈希全部匹配。[计数](summary.json) · [逐镜头结果](by-lens.csv) · [判定变化](verdict-changes.csv)

四次 Release 定向测试分别通过 117、14、20 和 48 项，按测试 ID 去重后共 194 项通过。[Core 与 App](core-current.trx) · [对照工具](comparison-current.trx) · [Huygens 分段传播](segmented-huygens.trx) · [远心照度](telecentric-illumination.trx)

剩余 528 项逐条记录，其中 35 项求值错误里有 8 项的冻结原生报告也带诊断，另 27 项需核对本地输出或物理域。不可比项包括缺少原规范请求、原生无效或欠采样、掩码或坐标覆盖差异。320° 椭圆反射镜仍缺能发射后半球方向的物方锥角光阑支持。[逐项剩余清单](remaining-requests.csv) · [固定光线剩余项](remaining-fixed-rays.csv) · [Ansys Aperture Type](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Aperture_Type.html)
