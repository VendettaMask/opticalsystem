# 分析入口与计算章节对应

本表来自当前实际分析目录和定向测试的对应关系，共 76 项。它验证指南覆盖，不验证数值精度。公式见[数学附录](COMPUTATION_FORMULAS.md)，定义见[计算指南](COMPUTATION_GUIDE.md)。

| 分析入口 | 计算章节 |
| --- | --- |
| 单光线追迹 (Single Ray Trace) | 实光线如何相交、折射与反射 |
| 非序列单光线追迹 (Non-Sequential Ray Trace) | 非序列路径、功率与探测器 |
| 探测器查看器 (Non-Sequential Detector Viewer) | 非序列路径、功率与探测器 |
| 系统数据摘要 (First Order) | 一阶光学与焦距如何计算 |
| 赛德尔系数 (Seidel Coefficients) | Seidel、场曲、畸变与色差 |
| 赛德尔图 (Seidel Diagram) | Seidel、场曲、畸变与色差 |
| 标准点列图 (Spot Diagram) | 点列、RMS 与像差 |
| 全视场点列图 (Full Field Spot Diagram) | 点列、RMS 与像差 |
| 矩阵点列图 (Matrix Spot Diagram) | 点列、RMS 与像差 |
| 结构矩阵点列图 (Configuration Matrix Spot Diagram) | 点列、RMS 与像差 |
| 光线像差图 (Ray Fan) | 点列、RMS 与像差 |
| 光迹图 (Footprint Diagram) | 光瞳采样、瞄准与渐晕 |
| 网格畸变 (Grid Distortion) | Seidel、场曲、畸变与色差 |
| 场曲/畸变 (Field Curvature and Distortion) | Seidel、场曲、畸变与色差 |
| 场曲 (Field Curvature) | Seidel、场曲、畸变与色差 |
| 色焦移 (Color Focus Shift) | Seidel、场曲、畸变与色差 |
| 垂轴色差 (Lateral Color) | Seidel、场曲、畸变与色差 |
| 轴向像差 (Axial Aberration) | Seidel、场曲、畸变与色差 |
| 全视场像差 (Full Field Aberration) | Seidel、场曲、畸变与色差 |
| 几何 (Encircled Energy) | MTF、分辨率与圈入能量 |
| 衍射 (Diffraction Encircled Energy) | MTF、分辨率与圈入能量 |
| 几何线/边缘扩散 (Geometric Line Edge Spread) | 线扩散、边缘扩散与扩展源能量 |
| 扩展光源 (Extended Source Encircled Energy) | 线扩散、边缘扩散与扩展源能量 |
| 瞳孔像差 (Pupil Aberration) | 光瞳采样、瞄准与渐晕 |
| RMS vs. 视场 (RMS vs Field) | 扫描、参考球与多结构比较 |
| RMS vs. 波长 (RMS vs Wavelength) | 扫描、参考球与多结构比较 |
| RMS vs. 离焦 (RMS vs Focus) | 扫描、参考球与多结构比较 |
| 二维视场RMS图 (RMS Field Map) | 扫描、参考球与多结构比较 |
| RMS 波前-视场 (RMS Wavefront vs Field) | 扫描、参考球与多结构比较 |
| Zernike系数 vs. 视场 (Zernike vs Field) | Zernike 系数怎样拟合 |
| 离焦点列图 (Through Focus) | 扫描、参考球与多结构比较 |
| 傅里叶离焦 MTF (Fourier Through Focus MTF) | 扫描、参考球与多结构比较 |
| 惠更斯离焦 MTF (Huygens Through Focus MTF) | 扫描、参考球与多结构比较 |
| 几何离焦 MTF (Geometric Through Focus MTF) | 扫描、参考球与多结构比较 |
| 傅里叶 MTF VS 视场 (Fourier MTF vs Field) | 扫描、参考球与多结构比较 |
| 惠更斯 MTF VS 视场 (Huygens MTF vs Field) | 扫描、参考球与多结构比较 |
| 几何 MTF VS 视场 (Geometric MTF vs Field) | 扫描、参考球与多结构比较 |
| 入射角 vs. 像高 (Angle vs Image Height) | 扫描、参考球与多结构比较 |
| 入射角-像高（扫描瞳孔） (Angle vs Image Height - Through Pupil) | 扫描、参考球与多结构比较 |
| 入射角-像高（扫描视场） (Angle vs Image Height - Through Field) | 扫描、参考球与多结构比较 |
| 基面数据 (Cardinal Points Data) | 一阶光学与焦距如何计算 |
| 渐晕图 (Vignetting Diagram) | 光瞳采样、瞄准与渐晕 |
| 相对照度 (Relative Illumination) | 相对照度与辐射度量 |
| 非相干照度 (Incoherent Irradiance) | 相对照度与辐射度量 |
| 辐射强度 (Radiant Intensity) | 相对照度与辐射度量 |
| Y-Ybar (Y-Ybar) | 一阶光学与焦距如何计算 |
| FFT PSF (PSF) | 波前、OPD 与 PSF |
| FFT PSF截面图 (FFT PSF Cross Section) | 波前、OPD 与 PSF |
| FFT 线/边缘扩散 (FFT Line Edge Spread) | 线扩散、边缘扩散与扩展源能量 |
| 惠更斯PSF (Huygens PSF) | 波前、OPD 与 PSF |
| 惠更斯PSF截面图 (Huygens PSF Cross Section) | 波前、OPD 与 PSF |
| 傅里叶 MTF (MTF) | MTF、分辨率与圈入能量 |
| 惠更斯 MTF (Huygens MTF) | MTF、分辨率与圈入能量 |
| 几何 MTF (Geometric MTF) | MTF、分辨率与圈入能量 |
| 采样 MTF (Sampled MTF) | MTF、分辨率与圈入能量 |
| 对比度损失图 (Contrast Loss Map) | MTF、分辨率与圈入能量 |
| 光程差图 (Optical Path Difference) | 波前、OPD 与 PSF |
| 傅科分析 (Foucault Analysis) | 傅科刀口与角谱传播 |
| 波前图 (Wavefront Map) | 波前、OPD 与 PSF |
| 干涉图 (Interferogram) | 干涉图的相位与强度 |
| 波前 (Wavefront) | 波前、OPD 与 PSF |
| 质心参考球波前 (Centroid Sphere Wavefront) | 扫描、参考球与多结构比较 |
| 最佳拟合球波前 (Best Fit Sphere Wavefront) | 扫描、参考球与多结构比较 |
| Zernike Fringe系数 (Zernike Fringe) | Zernike 系数怎样拟合 |
| Zernike Standard系数 (Zernike Standard) | Zernike 系数怎样拟合 |
| Zernike Annular系数 (Zernike Annular) | Zernike 系数怎样拟合 |
| 图像模拟 (Image Simulation) | 几何、相干与部分相干图像 |
| 几何图像分析 (Geometric Image Analysis) | 几何、相干与部分相干图像 |
| 几何位图图像分析 (Geometric Bitmap Image Analysis) | 几何、相干与部分相干图像 |
| 光源分析 (Light Source Analysis) | 几何、相干与部分相干图像 |
| 部分相干图像分析 (Partially Coherent Image Analysis) | 几何、相干与部分相干图像 |
| 扩展图像分析 (Extended Diffraction Image Analysis) | 几何、相干与部分相干图像 |
| Jones 瞳 (Jones Pupil) | 偏振、Fresnel 与镀膜 |
| 表面数据报告 (Prescription Report) | 计算总览：从处方到结果 |
| 系统数据报告 (System Data Report) | 一阶光学与焦距如何计算 |
| 分类数据报告 (Classified Data Report) | 计算总览：从处方到结果 |
