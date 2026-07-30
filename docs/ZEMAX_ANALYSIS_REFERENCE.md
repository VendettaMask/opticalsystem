# Zemax 分析设置与实现方式参考

本文档根据 Ansys Zemax OpticStudio 官方帮助页整理，用作 Workbench 实现 Zemax 风格分析时的参考。范围只包括图像质量相关分析：

- 光线迹点
- 像差分析
- 波前
- 点扩散函数
- MTF 曲线
- RMS
- 圈入能量
- 扩展图像分析

不包括系统报告、优化、公差、非序列分析、IMA/BIM 文件查看器和 Bitmap 文件查看器。本文不放图片，只记录设置内容、结果展现方式和计算/实现方式。

## 资料来源

主要来源为 Ansys Zemax OpticStudio 2025 R1/R2 官方帮助：

- [Image Quality Group 目录](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/)：给出 Rays and Spots、Aberrations、Wavefront、PSF、MTF、RMS、Enclosed Energy、Extended Scene Analysis 的官方菜单范围。
- [Single Ray Trace](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Single_Ray_Trace.html)
- [Standard Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Standard_Spot_Diagram.html)
- [Full Field Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Full_Field_Spot_Diagram.html)
- [Matrix Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/Matrix_Spot_Diagram.html)
- [Configuration Matrix Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Configuration_Matrix_Spot_Diagram.html)
- [Cardinal Points](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Cardinal_Points_rays_and_spots.html)
- [Vignetting Plot](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Vignetting_Plot.html)
- [Optical Path Difference](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html)
- [Pupil Aberration](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Pupil_Aberration.html)
- [Field Curvature and Distortion](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)
- [Chromatic Focal Shift](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/Chromatic_Focal_Shift.html)
- [Seidel Coefficients](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Seidel_Coefficients.html)
- [Seidel Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Seidel_Diagram.html)
- [Full-Field Aberration](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Full_Field_Aberration.html)
- [Wavefront Map](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)
- [FFT PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)
- [Huygens PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)
- [FFT MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_MTF.html)
- [Huygens Through Focus MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_Through_Focus_MTF.html)
- [Geometric MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Geometric_MTF.html)
- [Geometric MTF vs Field](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/Geometric_MTF_vs_Field.html)
- [RMS vs Field](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html)
- [RMS Field Map](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_Field_Map.html)
- [Encircled Energy operands](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Encircled_Energy_optimization_operands_by_category.html)
- [Extended Source](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Extended_Source.html)
- [Image Simulation](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Image_Simulation.html)
- [Geometric Image Analysis](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Geometric_Image_Analysis.html)
- [Geometric Bitmap Image Analysis](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Geometric_Bitmap_Image_Analysis.html)
- [Light Source Analysis](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Light_Source_Analysis.html)
- [Partially Coherent Image Analysis](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Partially_Coherent_Image_Analysis.html)
- [Extended Diffraction Image Analysis](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Extended_Diffraction_Image_Analysis.html)
- [Relative Illumination](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html)

## 通用约定

- `设置内容` 写官方设置窗口中应暴露的参数类型，括号中写 Workbench 当前已有或应映射的参数名。
- `结果展现` 写分析窗口应该显示的形式：曲线、散点、矩阵图、热图、表面图、灰阶/伪彩图、文本表等。
- `实现方式` 写 Zemax/OpticStudio 帮助中说明的算法路径或计算定义。
- field、wavelength、surface、sampling、polarization、vignetting factors 是许多分析共用设置。
- normalized field coordinates 为 `Hx/Hy`，normalized pupil coordinates 为 `Px/Py`。
- 多数图形支持 Graphic/Text 两类输出；Workbench 结果页可映射到 Plot/Data/Text。

## 光线迹点

### 单光线追迹 / Single Ray Trace

- 设置内容：`Hx`、`Hy`、`Field`、`Wavelength`、`Px`、`Py`、`Global Coordinates`、`Type`。`Type` 包括方向余弦、切线角等输出方式。
- 结果展现：文本/表格窗口，列出单条真实光线和近轴光线在各表面的坐标、方向、角度、光程或追迹状态。
- 实现方式：从指定归一化视场和归一化光瞳坐标发射一条光线，执行序列模式 real ray trace 与 paraxial ray trace。若选择全局坐标，除切线角外数据以全局坐标输出。

### 光线像差图 / Ray Aberration

- 设置内容：`Plot Scale`、`Number of Rays/Ray Density`、`Wavelength`、`Field`、`Tangential`、`Sagittal`、`Surface`、`Use Dashes`、`Vignetting`、`Check Apertures`。
- 结果展现：一组 fan 曲线，横轴为归一化入瞳坐标，纵轴为 X 或 Y 方向 transverse ray aberration。
- 实现方式：沿 pupil 的子午和弧矢截线追迹一束真实光线，比较光线在目标表面处相对参考点的横向误差。该项也可作为 Rays and Spots 组入口。

### 标准点列图 / Standard Spot Diagram

- 设置内容：`Pattern`（hexapolar、square、dithered）、`Refer To`（chief ray、centroid、middle、vertex）、`Show Scale`、`Wavelength`、`Field`、`Surface`、`Plot Scale`、`Delta Focus`、`Ray Density`、`Use Symbols`、`Use Polarization`、`Scatter Rays`、`Airy Disk`、`Direction Cosines`、`Configuration`、`Color Rays By`。
- 结果展现：点列散点图；图下方显示参考点坐标、RMS spot radius、GEO spot radius 等。可按波长、视场或结构着色，可叠加 Airy disk。
- 实现方式：按 pupil 图样追迹光线束到指定表面。RMS/GEO 半径按所选参考点计算；波长权重和 pupil apodization 会影响 ray grid 和 RMS 估计。OpticStudio 不把 vignetted rays 画入 spot，也不用于 RMS/GEO 计算。

### 光迹图 / Footprint Diagram

- 设置内容：通常与 spot 类分析共享 `Ray Density`、`Wavelength`、`Field`、`Surface`、`Color Rays By`、`Delete Vignetted`、`Use Symbols`。当前实现的 `Color Rays By` 默认值为 `视场`，也可切换为 `波长`；旧设置中的 `field` / `wavelength` 继续兼容。
- 结果展现：指定表面上的 ray footprint 散点图，通常用于观察光束在孔径、镜片表面或中间面上的占用范围。图下工程标注显示表面、光线 X/Y 极值、最大半径、参与分析的波长以及图例含义；数据页同时提供 X/Y 绘图缩放和孔径。
- 图例必须和着色依据一致：按波长着色时显示波长及 `µm` 单位；按视场着色时显示视场序号、`(X, Y)` 坐标及当前视场单位（角度为 `°`，高度为 `mm`）。图例开关按相同分组过滤光线。
- 实现方式：追迹 pupil 光线束并记录其在指定表面的实际交点，不一定要求形成焦点。官方中间面说明中，footprint 属于更适合直接在表面处评价的几何分析。

### 离焦点列图 / Through Focus Spot Diagram

- 设置内容：继承标准点列图设置；额外使用 `Delta Focus`。焦点位置为 `-2`、`-1`、`0`、`+1`、`+2` 倍 delta focus。
- 结果展现：每个视场显示五个离焦位置的点列图，可比较焦前/焦后 spot 形态。
- 实现方式：在名义焦面前后移动分析面或等效焦位，分别追迹 spot 光线束并计算相同的 RMS/GEO 指标。官方说明中 through-focus spot 通常追迹的最大 ray 数为标准 spot 的一半。

### 全视场点列图 / Full Field Spot Diagram

- 设置内容：大部分与标准点列图相同；额外有 `Exaggerate`，用于放大 transverse aberration。
- 结果展现：所有视场点用同一比例和共同参考显示在一张图上。
- 实现方式：与标准 spot 类似，但不是每个视场单独参考，而是把所有 spot 放在共同坐标系中，以观察不同视场点之间的相对位置和可分辨性。

### 矩阵点列图 / Matrix Spot Diagram

- 设置内容：与标准点列图相似；额外有 `Ignore Lateral Color`。
- 结果展现：矩阵图，行通常为不同视场，列为不同波长。
- 实现方式：对 field × wavelength 组合逐个生成 spot diagram。`Ignore Lateral Color` 使每个视场/波长单元独立参考自己的参考点，用于分离与波长相关的像差形态。

### 结构矩阵点列图 / Configuration Matrix Spot Diagram

- 设置内容：与标准点列图相似。
- 结果展现：矩阵图，行是不同视场，列是不同 configuration。
- 实现方式：对 field × configuration 组合逐个生成 spot diagram，用于区分与多重结构相关的像差成分。

### 基点 / Cardinal Points

- 设置内容：`First Surface`、`Last Surface`、`Wavelength`、`Orientation`。
- 结果展现：文本/表格列表，输出 principal、nodal、anti-nodal、focal planes 等位置。
- 实现方式：对选定表面范围和波长执行一阶/近轴计算，分别给出 X-Z 或 Y-Z 方向的基面位置。若范围内包含 coordinate break 或非中心光学元件，官方提示结果可能不可靠。

### Y-Ybar Drawing

- 设置内容：官方目录将其列为 Rays and Spots 项；通常不需要复杂设置，主要依赖系统孔径、视场和波长定义。
- 结果展现：Y-Ybar 光线高度图，显示边缘光线和主光线在各表面处的高度。
- 实现方式：用近轴/真实边缘光线和主光线沿系统传播，画出 Y 与 Ybar 随表面的变化，用于判断光阑位置、孔径负担和一阶成像关系。

### 渐晕图 / Vignetting Plot

- 设置内容：`Ray Density`、`Field Density`、`Remove Vignetting Factors`。
- 结果展现：fractional vignetting 随视场角变化曲线。
- 实现方式：对每个视场点追迹 `(2n+1) x (2n+1)` 光线网格，统计入瞳光线中穿过所有遮挡和孔径并到达像面的比例，并归一化到相对 pupil 面积。出错、漏面或全反射光线视为 vignetted。

### 入射角 vs 像高 / Incident Angle vs Image Height

- 设置内容：通常包括 `Field Density`、`Wavelength`、扫描方向或表面选择。
- 结果展现：入射角随像高或视场坐标变化的曲线。
- 实现方式：沿视场扫描主光线或代表光线，计算其到达像面/目标面时相对局部法线或坐标轴的入射角。

## 像差分析

### 光线像差图 / Ray Aberration

- 设置内容：同 Rays and Spots 中的 Ray Aberration。
- 结果展现：子午/弧矢 ray fan 曲线。
- 实现方式：沿归一化 pupil 坐标追迹截线光线，显示 transverse ray aberration，属于几何光线像差。

### 光程差 / Optical Path Difference

- 设置内容：与 ray aberration fan 基本一致，但 tangential/sagittal 数据固定为 OPD。
- 结果展现：OPD fan 曲线，横轴为 normalized entrance pupil coordinate，纵轴为 waves。
- 实现方式：计算每条 ray 的 optical path length 与 chief ray optical path length 之差。通常把差值参考到系统 exit pupil。多波长“All”显示时参考主波长 reference sphere 和 chief ray，但 OPD 数值以各自波长的 waves 输出。

### 光瞳像差 / Pupil Aberration

- 设置内容：与 ray aberration fan 类似；tangential/sagittal 只能选择 pupil aberration；surface 固定为 image，因为数据总是在 stop surface 计算。
- 结果展现：entrance pupil distortion 随 pupil coordinate 的曲线。
- 实现方式：定义为真实光线在 stop surface 的截距，与轴上主波长近轴光线截距之间的差，再除以 paraxial stop radius 得到百分比。官方说明它主要用于判断是否需要 ray aiming；若开启 ray aiming，该像差会接近零。

### 场曲与畸变 / Field Curvature and Distortion

- 设置内容：`Max Curvature`、`Max Distortion`、`Wavelength`、`Use Dashes`、`Ignore Vignetting Factors`、`Distortion` 模型、`Display As`、`Scan Type`、`Ref. Field`、`H/W Aspect`。
- 结果展现：同一分析窗口显示 field curvature 曲线和 distortion 曲线。field curvature 给出 tangential/sagittal 焦面相对像面的距离；distortion 可显示百分比或绝对长度。
- 实现方式：field curvature 使用 parabasal ray trace，在 X/Y 方向求 sagittal/tangential paraxial focal plane 的 Z 坐标，并与系统 image surface Z 坐标比较。distortion 以真实 chief ray height 与 reference ray height 的差定义：百分比为 `(real chief ray height - reference height) / reference height * 100`。
- 畸变模型：`F-Tan(theta)`、`F-Theta`、`Calibrated F-Theta`、`Calibrated F-Tan(theta)`、`SMIA-TV`。`F-Tan(theta)` 使用 `f*tan(theta)` 参考高度；`F-Theta` 使用 `f*theta`，常用于扫描系统；calibrated 模式使用 best-fit focal length。
- 扫描规则：从轴上到最大视场生成半扇形采样，`Scan Type` 必须实际选择 `+Y`、`+X`、`-Y` 或 `-X`。选择 X 扫描时，tangential 曲线对应 XZ 平面，sagittal 曲线对应 YZ 平面；不得用当前已定义视场列表代替扫描。
- 渐晕规则：默认 `Ignore Vignetting Factors = true`。Workbench 在独立工作副本中清零视场渐晕因子；关闭该选项时保留并应用原始因子，且两种模式都不修改用户的原始光学系统。
- Real Image Height：畸变计算临时转换为等效 Field Angle（有限共轭时为 Object Height），因为“命中指定像高”的迭代会掩盖畸变；场曲仍保留原视场定义。
- 适用性：严格来说，该图只适用于旋转对称系统以及平面物面、像面。OpticStudio 对非旋转对称、偏心、倾斜、自由曲面或非平面物像面系统使用推广定义，结果需要谨慎解释；单一畸变值不充分时应使用 Grid Distortion。

### 网格畸变 / Grid Distortion

- 设置内容：通常包括网格尺寸、波长、参考视场、显示方式、比例、field width、H/W aspect 等。
- 结果展现：理想网格与实际成像网格、矢量图或截面图。
- 实现方式：在二维 field/object 网格上追迹 chief rays，比较实际像点与理想线性映射位置。用于非旋转对称系统中无法由单一径向畸变曲线描述的情况。

### 轴向像差 / Longitudinal Aberration

- 设置内容：`Plot Scale`、`Wavelength`、`Use Dashes` 等。
- 结果展现：纵向像差随 pupil zone 变化的曲线，常用于看球差和轴向色差。
- 实现方式：沿 pupil 半径追迹边缘/分区光线，求其与光轴或焦点的交会位置相对参考焦点的偏移。

### 垂轴色差 / Lateral Color

- 设置内容：`Graph Scale`、`All Wavelengths`、`Use Real Rays`、`Show Airy Disk` 等。
- 结果展现：lateral color 随 field 的曲线，通常以像面横向位移表示，可叠加 Airy disk 尺度。
- 实现方式：比较不同波长 chief ray 或实际代表光线在像面上的横向位置差，反映倍率色差。

### 色焦移 / Chromatic Focal Shift

- 设置内容：`Maximum Shift`、`Pupil Zone`。
- 结果展现：back focal shift 相对主波长焦点随波长变化的曲线。
- 实现方式：对每个波长计算像方空间边缘光线焦点相对主波长近轴焦点的偏移。`Pupil Zone=0` 使用近轴光线；0 到 1 之间使用入瞳指定区域的真实边缘光线；1 为全孔径边缘。

### 赛德尔系数 / Seidel Coefficients

- 设置内容：`Wavelength`。
- 结果展现：文本/表格，按表面和系统总和列出 unconverted Seidel、transverse、longitudinal 和 wavefront coefficients。
- 实现方式：基于近轴光线计算三阶像差项。输出包括 SPHA/S1、COMA/S2、ASTI/S3、FCUR/S4、DIST/S5、CLA/CL、CTR/CT，以及 transverse、longitudinal 和 wavefront 系数。官方说明该计算只对轴对称球面、圆锥、二阶/四阶非球面等受支持面型可靠。

### 赛德尔图 / Seidel Diagram

- 设置内容：`First Surface`、`Last Surface`、`Wavelength`、`Plot Scale`、`Ignore Distortion`、`Ignore Chromatic`。
- 结果展现：bar chart，显示未转换 Seidel coefficients，可按表面范围和总和显示。
- 实现方式：复用 Seidel Coefficients 的未转换像差项，把各项以柱状图形式显示。

### 全视场像差 / Full-Field Aberration

- 设置内容：`Field`、`Field Shape`、`Wavelength`、`X/Y Field Width`、`X/Y Field Sampling`、`Pupil Sampling`、`Show As`、像差项选择。
- 结果展现：Icons、Grey Scale、Inverse Grey Scale、False Color、Inverse False Color。Icons 可表达像差大小和方向；灰阶/伪色可显示正负幅值但不显示方向。
- 实现方式：在指定 field sampling grid 上计算 Zernike coefficients。OpticStudio 为使图标看起来像 spot diagram 所显示的 transverse aberration，使用波前导数定义 `Ex = -(R/n)(dW/dx)`、`Ey = -(R/n)(dW/dy)`。适合检查自由曲面系统的全视场像差校正。

## 波前

### 光程差 / Optical Path Difference

- 设置内容、结果展现、实现方式与像差分析中的 OPD 相同。
- 结果展现：OPD fan 曲线，通常用于从波前角度查看 pupil 坐标上的光程误差。

### 波前图 / Wavefront Map

- 设置内容：`Sampling`、`Rotation`、`Scale`、`Polarization`、`Wavelength`、`Field`、`Reference To Primary`、`Use Exit Pupil Shape`、`STAR Data`、`Show As`、`Surface`、`Remove Tilt`、`Contour Format`、`Subaperture Data Sx/Sy/Sr`。
- 结果展现：surface plot、contour map、grey scale 或 false color map，显示 pupil 上 wavefront error。
- 实现方式：在 pupil ray grid 上采样 OPD。若选择 polarization 分量，会把相应电场分量的偏振相位加入 OPD；若相位超过一波，官方提示不做 phase unwrapping。`Remove Tilt` 等价于把 OPD 参考到 centroid。`Use Exit Pupil Shape` 会按指定 field 的像点视角近似显示 exit pupil 形状。

### 干涉图 / Interferogram

- 设置内容：通常与 wavefront map 类似，围绕采样、波长、视场、显示方式。
- 结果展现：干涉条纹或由波前误差转换出的干涉图样。
- 实现方式：以 wavefront OPD 为基础，将相位误差映射为干涉强度/条纹显示，用于直观查看波前形状。

### 傅科分析 / Foucault Analysis

- 设置内容：采样、刀口方向/位置、显示方式、波长、视场等。
- 结果展现：灰阶或伪彩色 pupil/knife-edge 响应图。
- 实现方式：从波前斜率或局部相位梯度模拟刀口截断后的强度变化，用来查看面形/波前低频误差。

### 对比度损失图 / Contrast Loss Map

- 设置内容：官方菜单归在 Wavefront/MTF 相关项，通常涉及 sampling、frequency、field、wavelength、show as。
- 结果展现：contrast loss 随 pupil/field/frequency 变化的图或曲线。
- 实现方式：基于 wavefront 对成像对比度的影响估算损失，本质与 MTF/OTF 计算相关。

### Zernike Fringe 系数 / Zernike Fringe Coefficients

- 设置内容：采样、项数、波长、视场、subaperture 等。
- 结果展现：Zernike coefficient 文本/表格和可能的波前重建图。
- 实现方式：对 pupil OPD 拟合 Fringe Zernike 多项式。Subaperture 由 `Sx/Sy/Sr` 定义。

### Zernike Standard 系数 / Zernike Standard Coefficients

- 设置内容：采样、项数、波长、视场。
- 结果展现：Standard Zernike coefficient 表。
- 实现方式：对波前 OPD 做 Standard Zernike 基函数拟合。

### Zernike Annular 系数 / Zernike Annular Coefficients

- 设置内容：采样、项数、中心遮拦或 annular pupil 参数、波长、视场。
- 结果展现：Annular Zernike coefficient 表。
- 实现方式：在环形 pupil 上拟合 Annular Zernike 多项式，适合中心遮拦系统。

### Zernike 系数 vs 视场 / Zernike Coefficients vs Field

- 设置内容：field density、Zernike term、sampling、wavelength、scan direction 等。
- 结果展现：Zernike 系数随视场变化的多曲线图。
- 实现方式：沿视场扫描，每个视场点计算 wavefront OPD 并拟合 Zernike 系数，然后把指定项组成场曲线。

### 全视场像差 / Full-Field Aberration

- 设置内容、结果展现、实现方式见像差分析中的 Full-Field Aberration。
- 结果展现：作为波前组入口时仍显示 Zernike aberration across field。

## 点扩散函数

### PSF 的官方实现路径

- 几何 PSF：Spot Diagram，本质是点源经几何光线追迹后的 ray intercept 分布，不含干涉/衍射。
- FFT PSF：基于 pupil data 的快速傅里叶变换，速度快但假设更多。
- Huygens PSF：基于 Huygens wavelets direct integration，速度慢但更通用。

### FFT PSF

- 设置内容：`Sampling`、`Display`、`Rotation`、`Wavelength`、`Field`、`Type`（linear/log/phase/real/imaginary）、`Show As`（surface、contour、grey scale、false color）、`Use Polarization`、`Image Delta`、`Normalize`、`Surface`。
- 结果展现：二维 PSF surface/contour/grey/false color 图，也可输出线性强度、对数强度、相位、实部、虚部。
- 实现方式：在与参考波长 chief ray 垂直、以 chief ray 为中心的假想平面上计算点源衍射强度。算法在 pupil space coordinates 中完成，采样后进行 zero padding，使 image-space sampling 为 pupil sampling 的两倍以降低 aliasing。
- 适用限制：chief ray 与像面法线夹角较小、exit pupil 畸变不显著、横向像差不过大、标量衍射足够时较可靠。倾斜像面、广角、异常出瞳、非远心或极快系统中可能过于乐观，应使用 Huygens PSF 交叉检查。

### FFT Cross Section

- 设置内容：继承 FFT PSF 的 sampling、wavelength、field、image delta、normalize、type；增加截线方向或 row/column。
- 结果展现：PSF 中心 X 或 Y 截线曲线。
- 实现方式：先计算 FFT PSF，再取指定行/列的强度或对数强度数据。

### FFT Line/Edge Spread

- 设置内容：FFT sampling、wavelength、field、line/edge、方向、图形比例、polarization。
- 结果展现：line spread function 或 edge spread function 曲线。
- 实现方式：由 FFT PSF 积分得到一维 LSF；ESF 为 LSF 的累积分布。

### Huygens PSF

- 设置内容：`Pupil Sampling`、`Image Sampling`、`Image Delta`、`Rotation`、`Wavelength`、`Field`、`Type`、`Show As`、`Use Polarization` 等。
- 结果展现：二维 Huygens PSF surface/contour/grey/false color 图；同时计算 Strehl ratio。
- 实现方式：用 Huygens wavelets direct integration 计算衍射 PSF。与 FFT PSF 不同，Huygens PSF 在与 image surface chief ray 截点相切的 imaginary plane 上计算，并考虑像面局部倾斜、chief ray 入射角和像面 slope 对 PSF 形状的影响。
- 该虚拟平面的法向量必须是主光线截点处的像面局部法向量，即先在像面局部坐标调用 `Geometry.SurfaceNormal(localIntercept)`，再变换到全局坐标；不能使用 `chief.Direction`。垂直于主光线的虚拟平面属于 FFT PSF 的定义。
- `Image Delta = Δ` 是相邻像面采样点的实际距离。对每轴 `N` 个点，Workbench 使用 `(index - (N-1)/2) * Δ`，总跨度为 `(N-1)Δ`；不得在 `[-NΔ/2,+NΔ/2]` 上 linspace `N` 点。
- Workbench 回归覆盖倾斜平面探测器、曲面探测器的局部法向以及逐点 Image Delta；无像差归一化点也取自 on-axis chief intercept，不再假定全局 `(0,0,imageZ)`。
- 成本：计算时间近似随 `pupil grid size^2 * image grid size^2 * wavelength count` 增长。

### Huygens Cross Section

- 设置内容：Huygens pupil/image sampling、image delta、波长、视场、截线方向。
- 结果展现：Huygens PSF 的一维截线曲线。
- 实现方式：先用 direct integration 生成 Huygens PSF，再提取截线。

## MTF 曲线

### MTF 的官方实现路径

- FFT MTF：基于 pupil data 的 diffraction MTF，速度快，假设接近 FFT PSF。
- Huygens MTF：先用 Huygens PSF direct integration，再由 PSF 得到 OTF/MTF，更慢但适用性更好。
- Geometric MTF：基于 ray aberration data 的几何近似，不直接计算衍射相位。
- Sampled/Contrast 类方法：基于 complex pupil function 或 wavefront difference/overlap 的 MTF 点或损失估计，适合优化或局部频率分析。

### FFT MTF

- 设置内容：`Sampling`、`Show Diffraction Limit`、`Max Frequency`、`Wavelength`、`Field`、`Type`（modulation、real、imaginary、phase、square wave）、`Use Polarization`、`Use Dashes`、`Surface`。
- 结果展现：tangential 和 sagittal MTF 曲线，横轴为空间频率，单位为 cycles/mm 或 afocal 单位。
- 实现方式：基于 pupil data 的 FFT 计算 diffraction MTF。square wave response 由 sinusoidal MTF 按官方公式换算。focal 系统 cutoff frequency 为 `1 / (wavelength * working F/#)`，sagittal/tangential 分别按每个 field 和 wavelength 的 working F/# 计算。
- 限制：OPD PV 或 wavefront slope 太大时采样不足会 alias。exit pupil 在 cosine space 中严重拉伸时 FFT MTF 不准确，应使用 Huygens MTF。OPD 大于约 10 waves 的高像差系统，可优先使用 Geometric MTF。

### FFT Through Focus MTF

- 设置内容：FFT sampling、delta focus、frequency、field、wavelength、type、use polarization、use dashes。
- 结果展现：指定空间频率下 MTF 随 defocus 变化的 tangential/sagittal 曲线。
- 实现方式：在一系列焦移位置上重复 FFT MTF 计算，并在指定频率处取值。

### FFT Surface MTF

- 设置内容：与 FFT MTF 相似，重点选择被评价 surface。
- 结果展现：指定 surface 或中间像面上的 MTF 曲线。
- 实现方式：OpticStudio 会在分析副本上对中间表面构造临时像面或直接评价，原镜头数据不改变。

### FFT MTF vs Field

- 设置内容：sampling、frequency 1-6、wavelength、field density、scan type、remove vignetting factors、use polarization 等。
- 结果展现：一个或多个空间频率下 MTF 随视场变化曲线。
- 实现方式：沿指定 field scan direction 计算 FFT MTF，再在各目标频率处插值得到曲线。

### FFT MTF Map

- 设置内容：`Sampling`、`X/Y Field Width`、`Frequency`、`Use Polarization`、`Wavelength`、`X/Y Pixels`、`MTF Data`、`Reference Field`、`Show As`、`Remove Vignetting Factors`。
- 结果展现：二维 field map，可为 grey scale 或 false color；数据可选 tangential、sagittal、average、minimum、maximum MTF。
- 实现方式：在二维视场网格上逐点计算 FFT MTF 的指定频率值。因为需要未定义中间视场点追迹，官方建议默认移除 vignetting factors。

### Huygens MTF

- 设置内容：`Pupil Sampling`、`Image Sampling`、`Image Delta`、`Max Frequency`、`Wavelength`、`Field` 等。
- 结果展现：Huygens tangential/sagittal MTF 曲线。
- 实现方式：先计算 Huygens PSF，然后对 PSF 做 OTF/MTF 计算。对倾斜像面、exit pupil 畸变或 FFT 假设不成立的系统更可靠。

### Huygens Through Focus MTF

- 设置内容：`Pupil Sampling`、`Image Sampling`、`Image Delta`、configuration、wavelength、field、spatial frequency、focus range/steps。
- 结果展现：Huygens MTF 随 delta focus 变化的曲线。
- 实现方式：每个焦位先直接积分生成 Huygens PSF，再从 PSF 得到 MTF。多波长时，相同波长可做 coherent sum，不同波长 PSF 进行 incoherent sum。

### Huygens Surface MTF

- 设置内容：同 Huygens MTF，加 surface 选择。
- 结果展现：指定 surface 或中间像面上的 Huygens MTF 曲线。
- 实现方式：在中间 surface 评价时，按照官方中间面规则构造临时分析系统。

### Huygens MTF vs Field

- 设置内容：与 FFT MTF vs Field 类似，但使用 Huygens pupil/image sampling 和 image delta。
- 结果展现：固定频率下 Huygens MTF 随视场变化曲线。
- 实现方式：沿视场扫描，每个视场点用 Huygens PSF -> OTF -> MTF 路径求值。

### Geometric MTF

- 设置内容：`Sampling`、`Max Frequency`、`Wavelength`、`Field`、`Multiply by Diffraction Limit`、`Use Polarization`、`Scatter Rays`。
- 结果展现：几何 tangential/sagittal MTF 曲线。
- 实现方式：基于 ray aberration data 近似 diffraction MTF。若 `Multiply by Diffraction Limit` 开启，则把几何 MTF 乘以衍射极限 MTF，以便小像差系统更真实；官方建议通常应开启。

### Geometric Through Focus MTF

- 设置内容：geometric sampling、spatial frequency、focus range/steps、wavelength、field、scatter rays、multiply by diffraction limit。
- 结果展现：几何 MTF 随 defocus 变化曲线。
- 实现方式：逐焦位追迹几何 ray aberration data，计算对应频率的几何 MTF。

### Geometric MTF vs Field

- 设置内容：`Sampling`、`Frequency 1-6`、`Wavelength`、`Use Polarization`、`Use Dashes`、`Remove Vignetting Factors`、`Field Density`、`Scan Type`、以及 scatter/diffraction-limit 相关选项。
- 结果展现：几何 MTF 随视场变化曲线。
- 实现方式：与 diffraction MTF vs Field 类似，但计算值来自 Geometric MTF 而非 FFT/Huygens diffraction MTF。

### Geometric MTF Map

- 设置内容：sampling、X/Y field width、frequency、wavelength、X/Y pixels、MTF data、reference field、show as、scatter rays、remove vignetting factors。
- 结果展现：二维 field map，grey scale 或 false color。
- 实现方式：在二维 field grid 上逐点计算 Geometric MTF。

### Contrast Loss Map

- 设置内容：与 MTF/波前相关，通常涉及 sampling、frequency、field、wavelength。
- 结果展现：对比度损失图或曲线。
- 实现方式：从 wavefront/complex pupil 对指定空间频率下的 contrast degradation 进行估算，可视为 MTF 相关分析。

## RMS

### RMS vs Field

- 设置内容：`Ray Density`、`Field Density`、`Plot Scale`、`Method`（Gaussian Quadrature/GQ 或 Rectangular Array/RA）、`Data`（wavefront error、spot radius、spot x、spot y、Strehl ratio）、`Refer To`（chief ray 或 centroid）、`Orientation`、`Use Dashes`、`Wavelength`、`Show Diffraction Limit`、`Use Polarization`、`Remove Vignetting Factors`。
- 结果展现：RMS 或 Strehl 随 field angle 变化的曲线。可显示每个波长和多波长结果。
- 实现方式：按视场扫描计算 RMS error 或 Strehl。GQ 用径向图样和最优权重估计 RMS；RA 用矩形 pupil grid，忽略圆形入瞳外光线。GQ 高效，但若表面孔径截断光线会不准；有孔径系统计算 RMS wavefront 时建议 RA 和更高采样。
- 波前 RMS：chief ray 参考减 piston；centroid 参考减 piston 和 tilt，通常得到更小 RMS。多波长 RMS 同时对所有波长光瞳样本按权重计算。
- `Show Diffraction Limit` 是便捷判断线，不执行完整衍射计算：spot radius/X/Y 使用 `1.22 × on-axis working F/# × λ`，RMS wavefront 使用 `0.072 waves`，Strehl 使用 `0.8`。勾选后 RMS vs Field 必须把该值作为覆盖整个视场范围的水平虚线加入绘图系列；不能只写入结果元数据。

### RMS vs Wavelength

- 设置内容：ray density、method、data、field、refer to、wavelength sampling/density、`Show Diffraction Limit`、polarization、vignetting factors。
- 结果展现：RMS spot radius、RMS wavefront 或 Strehl 随 wavelength 变化曲线。
- 勾选衍射极限后必须加入参考曲线：spot 数据逐采样波长计算 `1.22 × F/# × λ`，因此随波长变化；wavefront 为 `0.072 waves` 水平线，Strehl 为 `0.8` 水平线。
- 实现方式：在定义波段内取多个 wavelength 样本，对指定 field 和参考方式重复 RMS 计算。

### RMS vs Focus

- 设置内容：focus range、focus density、ray density、method、data、wavelength、field、refer to、`Show Diffraction Limit`、polarization、vignetting factors。
- 勾选衍射极限后必须把相应近似阈值作为覆盖整个 focus range 的水平虚线加入绘图系列；视场 F/# 变化对该方便指标的影响按 Zemax 定义忽略。
- 结果展现：RMS 或 Strehl 随 defocus 变化曲线，可判断最佳焦位。
- 实现方式：移动像面或分析焦位，在每个焦位重复 RMS spot/wavefront 计算。

### RMS Field Map

- 设置内容：`Ray Density`、`Data`、`Method`、`Plot Scale`、`Wavelength`、`Field`、`Refer To`、`Show As`（surface、contour、grey scale、false color）、`Surface`、`Contour Format`、`X/Y Field Size`、`X/Y Field Sampling`、`Use Polarization`、`Remove Vignetting Factors`。
- 结果展现：二维矩形 field map，显示 RMS radial/x/y spot radius、RMS wavefront error、Strehl ratio 或 RA 下的粗略 PTV。
- 实现方式：在以参考 field 为中心的 X/Y field grid 上计算与 RMS vs Field 相同的指标和算法。

## 圈入能量

### Diffraction Encircled Energy

- 设置内容：pupil sampling、image sampling、image delta 或 maximum distance、wavelength、field、type（encircled、X only、Y only、ensquared）、reference point/algorithm（chief ray、centroid、vertex；FFT 或 Huygens 方法有不同编号）。
- 结果展现：fraction of energy vs distance 曲线，或指定 fraction 对应的 distance。
- 实现方式：基于 diffraction PSF 对能量积分。encircled 为圆半径内能量；ensquared 为方框内能量；X/Y only 也称 enslitted energy。FFT 与 Huygens diffraction encircled energy 的采样和 reference 算法不同。

### Geometric Encircled Energy

- 设置内容：pupil sampling、wavelength、field、type、reference point（chief ray、centroid、vertex、middle of spot）、是否乘 diffraction limit。
- 结果展现：几何 encircled/ensquared/X/Y energy 曲线，或指定 fraction 的距离。
- 实现方式：追迹几何光线，统计落点相对参考点在半径、狭缝或方框范围内的累计能量。若启用 diffraction-limit scaling，会用圆孔衍射极限曲线近似修正。

### Geometric Line/Edge Spread

- 设置内容：sampling、wavelength、field、orientation、line/edge、maximum radius、reference。
- 结果展现：line spread function、edge response function，或二者曲线。
- 实现方式：将几何 spot 光线落点投影到 X 或 Y 方向，形成一维能量分布；edge response 是 line spread 的积分。

### Extended Source Encircled Energy

- 设置内容：`Field Size`、`Rays x 1000`、`Type`（encircled、X-only、Y-only、ensquared，也可 X/Y distribution）、`Refer To`、`Surface`、`Use Polarization`、`Multiply by Diffraction Limit`、`Wavelength`、`Field`、`File`、`Max Distance`、`Use Dashes`、`Remove Vignetting Factors`。
- 结果展现：扩展源 encircled/ensquared/enslitted energy 曲线，或 X/Y distribution；distribution 模式报告 geometric full width at half max。
- 实现方式：使用类似 Geometric Image Analysis 的扩展源模型，从 IMA/BIM 图像文件或场尺寸生成扩展目标，追迹大量光线并统计相对参考点的能量累计。X/Y-only 表示以参考点为中心的扩展狭缝内能量分数。

## 扩展图像分析

### Image Simulation

- 设置内容：输入图像文件、field height、oversampling、guard band、rotation、flip、show as（simulated image、source bitmap、PSF grid）、reference、X/Y pixels、pixel size、output file、aberrations（Diffraction、Geometric、None）、use relative illumination、polarization、PSF grid points 等。
- 结果展现：模拟图像、源图、或 PSF grid；可输出 BMP/JPG/PNG。
- 实现方式：用 Point Spread Function 阵列与源位图卷积来模拟成像。考虑 diffraction、aberrations、distortion、relative illumination、image orientation、polarization。流程为：源图过采样/旋转/加 guard band；计算覆盖视场的 PSF grid；对每个像素插值有效 PSF 并卷积；最后按 detector pixel size、geometric distortion、lateral color 缩放和变形。
- PSF 方法：`Diffraction` 使用 Huygens PSF；`Geometric` 使用 spot diagram 积分；`None` 使用 delta functions。若 Diffraction 模式下像差过严重，官方说明可自动切换到 Geometric。
- `Field Height` 表示完成 oversampling、旋转和 guard band 后整幅源图所覆盖的视场高度；它不是只写入结果页的显示参数。
- `Guard Band` 是原图四周的黑色零强度边界，不是镜像、重复或边缘延拓。
- Workbench 实现状态（2026-07-30）：设置页按 Zemax 分为“源位图设置”“网格卷积设置”“探测器和显示设置”。可读取 BMP/PNG/JPEG；先执行源位图翻转、90° 步进旋转、最近邻 oversampling 和黑色 guard band，再把整幅准备后位图映射到 `FieldHeight`。可选择一个视场作为网格中心，选择 RGB 或单一系统波长；随后按视场网格生成并插值 PSF，应用二维 relative-illumination 网格，最后在统一的参考波长像面坐标上拟合逆畸变，从而保留畸变和垂轴色差。
- Workbench 的图像模拟入口默认使用 `Geometric`，以匹配 Zemax 设置页示例；也可切换 `Diffraction` 或 `None`。`Diffraction` 调用 Huygens PSF；几何 RMS 半径超过 Airy 半径 20 倍，或 Huygens 计算失效时，该 field × wavelength 节点独立回退到 `Geometric`，结果中报告实际模式和回退节点数。`Geometric` 和 `None` 不再经过固定 FFT PSF。
- 源图和仿真结果均可按设置翻转；`Reference` 可选择主光线或质心。显式 pixel size 会覆盖自动估算的像面采样间距；非零 X/Y detector pixels 会重采样最终输出。`显示为` 可选择仿真图或源位图，输出文件支持 BMP/JPG/PNG。
- Workbench 当前 `GuardBand` 数值仍表示每边加入的像素数；它与 OpticStudio 界面中的 guard-band level 表示法不同，但边界内容和管线位置遵循上述定义。
- `Use Polarization`、`Apply Fixed Apertures` 和 `Compress Frame` 已作为 Zemax 设置兼容项保存并显示；当前标量 PSF 管线尚未进行 Jones/Mueller 偏振传播，固定孔径仍采用现有顺序追迹的固定孔径行为，`Compress Frame` 尚不改变无框架的栅格输出。PSF-grid 单独显示仍未接入本入口。

### Geometric Image Analysis

- 设置内容：`Field Size`、`Image Size`、`Parity`、`Rotation`、`Rays x 1000`、source file、field、surface、show as、polarization、delete vignetted、remove vignetting factors、apply fixed apertures 等。
- 结果展现：几何图像、spot overlay、灰度/伪色或强度分布。
- 实现方式：完全基于几何 ray tracing。使用 IMA/BIM 文件描述扩展源；在源图像像素单元内随机选点，并随机选择 entrance pupil 坐标，追迹到目标表面后把能量累加到 detector/bin。
- 备注：官方提示 Image Simulation 更适合高分辨率摄影场景。

### Geometric Bitmap Image Analysis

- 设置内容：`Field Y Size`、`Source`（uniform/Lambertian）、`Normalize`、`Use Polarization`、`Field`、`Input`（BMP/JPG/PNG）、`Surface`、`Show Source Bitmap`、`Output`、`Reference`、`Delete Vignetted`、`Suppress Frame`、`Remove Vignetting Factors`、`Apply Fixed Apertures`、parity、rotation、rays/pixel、detector X/Y pixels、pixel size。
- 结果展现：RGB 彩色位图模拟图，可保存输出位图；也可只显示源位图。
- 实现方式：严格几何光线追迹。对源图每个像素和颜色通道随机生成光线，entrance pupil 坐标随机，追迹到接收面后累加 RGB bin count，再归一化成 RGB 图像。
- 注意：real image height 会掩盖畸变，官方说明该功能会自动切换到 paraxial image height；建议使用 object height 更明确。

### Light Source Analysis

- 设置内容：`Input`（DAT、SDF、TM25RAY source file）、`Surface`、`Wavelength`、`Show As`（grey scale/false color）等。
- 结果展现：由复杂光源 ray file 追迹得到的图像或强度分布。
- 实现方式：使用 DAT/SDF/TM25RAY 二进制 ray file 作为源，在序列系统中追迹这些光线。官方说明该功能仅 Premium/Enterprise 可用。由于源光线可从任意位置/方向发出，序列模式中需要假设 object surface field point 为源坐标原点，+Z 平行于物面 +Z。

### Partially Coherent Image Analysis

- 设置内容：`File Size`、`Oversampling`、coherence 相关 `Gamma`、`Alpha`、`Fraction`、`Diffraction Limited`、`Wavelength`、`Field`、`File`、显示类型、polarization 等。
- 结果展现：coherent、incoherent 或 partially coherent diffraction image，也可显示 partially coherent PSF/gamma。
- 实现方式：使用 IMA/BIM/ZBF 文件描述被成像物体，同时考虑 diffraction、aberrations 和 illumination partial coherence。该方法考虑真实系统有限 pass band 和衍射滤波效应。纯非相干图像官方通常建议 Image Simulation 更优。

### Extended Diffraction Image Analysis

- 设置内容：`File Size`、`Show As`、`Data Type`（incoherent/coherent image）、`Diffraction Limited`、`Use Delta Functions`、`File`、`Wavelength`、`Field`、`Contour Format`、`Use Polarization`、`Consider Distortion`、`Output File`、`Use Relative Illumination`、OTF grid 相关采样。
- 结果展现：扩展源 coherent/incoherent diffraction image，可输出 complex amplitude 到 ZBF。
- 实现方式：与 Partially Coherent Image Analysis 类似，但允许 OTF 随视场变化。算法把 IMA 文件逐像素处理：每个像素的 Fourier transform 乘以该像素对应的 OTF，所有像素在频域求和后再反变换形成最终图像。实际不会为每个像素都算 OTF，而是计算覆盖视场的 OTF grid 并插值。
- 备注：官方说明 Image Simulation 通常优于该功能；该功能内存消耗随 OTF grid 和 sampling 快速增长。

### Relative Illumination

- 设置内容：`Ray Density`、`Field Density`、`Use Polarization`、`Remove Vignetting Factors`、`Wavelength`、`Scan Type`、`Log Scale`。
- 结果展现：relative illumination 随 radial field coordinate 变化曲线；文本中还给出 Effective F/#。
- 实现方式：对均匀朗伯场景计算像面单位面积照度，并归一化到视场中最大照度点。计算考虑 apodization、vignetting、apertures、image/pupil aberrations、F/# 变化、chromatic aberrations、image surface shape、incidence angle 和可选 polarization。核心为在 image direction cosine space 上积分从像点看到的 exit pupil effective area。
- 注意：RI 一般不会等于简单 cosine-fourth law；cosine-fourth law 只是慢速、无像差、薄透镜且光阑在透镜处的近似。

## Workbench 当前实现对照注意点

- 本文描述 Zemax/OpticStudio 官方方法。Workbench 当前实现可能只覆盖其中一部分设置或用简化参数名映射。
- Workbench 当前 Ribbon 中“垂轴色差”的 command id 叫 `analysis-distortion`，但实际 canonical name 为 `Lateral Color`；若要做官方 `Field Curvature and Distortion` 的畸变曲线，应单独接入 `DistortionAnalysis`。
- Workbench 当前“对比度损失图”实现路径更接近 sampled MTF 曲线，不等同于完整官方 Contrast Loss Map。
- Workbench 当前“干涉图”若复用 wavefront map，则还缺少独立干涉条纹显示方式。
- 官方菜单包含 `FFT Surface MTF`、`FFT MTF Map`、`Huygens Surface MTF`、`Geometric MTF Map` 等 MTF 变体；若需要“所有 Zemax 方法”完全覆盖，应在 Workbench MTF 分组中补齐这些入口。
