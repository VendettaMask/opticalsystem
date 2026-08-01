# 123456.ZMX 全量精度与图片复检报告

复检日期：2026-08-01

精度权威：Zemax OpticStudio 2026 R1 `123456.ZMX` 捕获基线
源文件 SHA-256：`0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`

## 结论

- 隔离目录重新计算全部 **69/69** 个 Workbench 分析，0 失败，总耗时 `304.7 s`。
- 正式结果与本轮隔离重算的 **69/69 份原始 JSON 均逐字节一致**，0 个不一致；包括带随机/采样设置的页面，因此当前结果可重复。
- 与 Zemax 有同物理定义的数值映射共 30 项：**25 项高度一致、5 项接近、0 项明显差异、0 项未完成**。
- 另外 2 项参考球页面明确属于非等价映射，不计入精度通过或失败。
- 图片检查覆盖 **30/30 张数值对比图**和 **69/69 张页面并排图**；文件数量、尺寸、文件体积和图像内容方差检查均通过，0 张缺失、0 张空白或损坏。
- 人工按 17 张分组缩略检查页复核了全部图片。数值图用于判断曲线/网格精度；页面图用于检查标题、曲线、表格、单位、分面和内容结构。不同 UI 框架的截图不做像素相似度评分。

完整页面浏览入口：[69 张页面 HTML 报告](COMPARISON_REPORT.html)。

## 判定规则

- 高度一致：中位 NRMSE ≤ 3% 且 P90 ≤ 10%。
- 接近：中位 NRMSE ≤ 10% 且 P90 ≤ 25%。
- 明显差异：未满足以上条件。
- Pupil Aberration 在 ray aiming 下是近零量；报告只在 NRMSE 分母使用 `1e-4%` 数值分辨率下限，不修改任何光线或结果。

## 全部 30 项数值精度

| Workbench | Zemax | 结论 | 中位 NRMSE | P90 | 最差 | 数值图 |
|---|---|---|---:|---:|---:|---|
| Relative Illumination | RelativeIllumination | 接近 | 6.00% | 6.00% | 6.00% | [图](images/numeric/relative-illumination.png) |
| Geometric Line Edge Spread | GeometricLineEdgeSpread | 接近 | 5.60% | 5.82% | 5.88% | [图](images/numeric/geometric-line-edge-spread.png) |
| MTF | FftMtf | 接近 | 5.15% | 6.52% | 7.24% | [图](images/numeric/mtf.png) |
| Encircled Energy | GeometricEncircledEnergy | 接近 | 3.84% | 4.18% | 4.25% | [图](images/numeric/encircled-energy.png) |
| Ray Fan | RayFan | 接近 | 2.93% | 15.75% | 26.09% | [图](images/numeric/ray-fan.png) |
| Geometric MTF | GeometricMtf | 高度一致 | 2.35% | 4.95% | 6.75% | [图](images/numeric/geometric-mtf.png) |
| Extended Source Encircled Energy | ExtendedSourceEncircledEnergy | 高度一致 | 2.00% | 2.00% | 2.00% | [图](images/numeric/extended-source-encircled-energy.png) |
| Sampled MTF | FftMtf | 高度一致 | 2.00% | 4.06% | 6.90% | [图](images/numeric/sampled-mtf.png) |
| Field Curvature | FieldCurvatureAndDistortion | 高度一致 | 1.90% | 6.24% | 7.95% | [图](images/numeric/field-curvature.png) |
| Distortion | FieldCurvatureAndDistortion | 高度一致 | 1.81% | 1.82% | 1.82% | [图](images/numeric/distortion.png) |
| Field Curvature and Distortion | FieldCurvatureAndDistortion | 高度一致 | 1.81% | 5.22% | 7.95% | [图](images/numeric/field-curvature-and-distortion.png) |
| Huygens Through Focus MTF | HuygensThroughFocusMtf | 高度一致 | 1.75% | 1.79% | 1.79% | [图](images/numeric/huygens-through-focus-mtf.png) |
| Fourier MTF vs Field | FftMtfvsField | 高度一致 | 1.75% | 1.76% | 1.77% | [图](images/numeric/fourier-mtf-vs-field.png) |
| Geometric MTF vs Field | GeometricMtfvsField | 高度一致 | 1.61% | 1.62% | 1.63% | [图](images/numeric/geometric-mtf-vs-field.png) |
| Diffraction Encircled Energy | DiffractionEncircledEnergy | 高度一致 | 1.23% | 1.38% | 1.48% | [图](images/numeric/diffraction-encircled-energy.png) |
| PSF | FftPsf | 高度一致 | 0.85% | 0.85% | 0.85% | [图](images/numeric/psf.png) |
| Geometric Through Focus MTF | GeometricThroughFocusMtf | 高度一致 | 0.82% | 4.22% | 7.26% | [图](images/numeric/geometric-through-focus-mtf.png) |
| Contrast Loss Map | ContrastLoss | 高度一致 | 0.78% | 0.78% | 0.78% | [图](images/numeric/contrast-loss-map.png) |
| Axial Aberration | LongitudinalAberration | 高度一致 | 0.66% | 0.66% | 0.66% | [图](images/numeric/axial-aberration.png) |
| Fourier Through Focus MTF | FftThroughFocusMtf | 高度一致 | 0.49% | 0.51% | 0.51% | [图](images/numeric/fourier-through-focus-mtf.png) |
| Through Focus MTF | FftThroughFocusMtf | 高度一致 | 0.49% | 0.51% | 0.51% | [图](images/numeric/through-focus-mtf.png) |
| Huygens PSF | HuygensPsf | 高度一致 | 0.41% | 0.41% | 0.41% | [图](images/numeric/huygens-psf.png) |
| Lateral Color | LateralColor | 高度一致 | 0.23% | 0.23% | 0.23% | [图](images/numeric/lateral-color.png) |
| Pupil Aberration | PupilAberrationFan | 高度一致 | 0.20% | 0.38% | 0.44% | [图](images/numeric/pupil-aberration.png) |
| Huygens MTF | HuygensMtf | 高度一致 | 0.19% | 1.41% | 1.69% | [图](images/numeric/huygens-mtf.png) |
| Huygens MTF vs Field | HuygensMtfvsField | 高度一致 | 0.19% | 0.21% | 0.21% | [图](images/numeric/huygens-mtf-vs-field.png) |
| RMS Wavefront vs Field | RMSField | 高度一致 | 0.15% | 0.18% | 0.19% | [图](images/numeric/rms-wavefront-vs-field.png) |
| Color Focus Shift | FocalShiftDiagram | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/color-focus-shift.png) |
| Optical Path Difference | OpticalPathFan | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/optical-path-difference.png) |
| Wavefront | WavefrontMap | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/wavefront.png) |

## 图片复核发现

- 25 个“高度一致”数值图的主曲线、网格方向、峰谷位置和视场/波长顺序与 Zemax 基线一致；差异主要位于采样或归一化的低幅残差。
- Relative Illumination 的曲线形状和单调性一致，但 Workbench 全视场端约为 `0.842`，Zemax 约为 `0.972`，属于稳定的照度衰减幅值差异。
- Geometric Line Edge Spread 的中心和积分方向一致；Workbench 线扩散曲线存在更明显的离散起伏，边扩散过渡也更宽。
- MTF 的曲线顺序和总体下降形状一致；Workbench 中高频衰减略慢，最大 Y 视场的 Tangential 曲线最差 NRMSE 为 `7.24%`。
- Encircled Energy 的五视场排序、台阶位置和终值一致；Workbench 在主上升段略向小半径移动，最大曲线 NRMSE 为 `4.25%`。
- Ray Fan 的大多数曲线接近，但最大 Y 视场 Tangential 三个波长的边缘幅值和形状仍有明显残差；最差单曲线 NRMSE `26.09%`、最低相关系数 `0.762341`。因此 Ray Fan 是下一轮应优先继续追查的项目。
- 页面并排图中有些 Zemax 参考来自数据回退渲染，坐标布局和 UI 与 Workbench 不同；Image Simulation、几何图像、光源/辐射类页面也可能使用不同的演示源图。它们只通过页面内容和结构检查，不能据此宣称像素级精度相同。
- `Centroid Sphere Wavefront` 与 `Best Fit Sphere Wavefront` 没有同物理定义的 Zemax Wavefront Map 参考，继续标记为非等价，而不是用比例或偏置强行对齐。

## 全部 69 张页面图片

每一行的原始分析 JSON 在本轮重算中均与正式结果逐字节一致；“页面复核”只表示图片和页面内容已检查，不等于存在 Zemax 数值精度映射。

| # | Workbench 页面 | 检验类型/结论 | 图片 |
|---:|---|---|---|
| 1 | Single Ray Trace | 页面复核（无等价数值映射） | [并排图](images/screenshots/single-ray-trace.png) |
| 2 | First Order | 页面复核（无等价数值映射） | [并排图](images/screenshots/first-order.png) |
| 3 | Seidel Coefficients | 页面复核（无等价数值映射） | [并排图](images/screenshots/seidel-coefficients.png) |
| 4 | Seidel Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/seidel-diagram.png) |
| 5 | Spot Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/spot-diagram.png) |
| 6 | Full Field Spot Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/full-field-spot-diagram.png) |
| 7 | Matrix Spot Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/matrix-spot-diagram.png) |
| 8 | Configuration Matrix Spot Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/configuration-matrix-spot-diagram.png) |
| 9 | Ray Fan | 接近 | [并排图](images/screenshots/ray-fan.png) |
| 10 | Footprint Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/footprint-diagram.png) |
| 11 | Field Curvature and Distortion | 高度一致 | [并排图](images/screenshots/field-curvature-and-distortion.png) |
| 12 | Distortion | 高度一致 | [并排图](images/screenshots/distortion.png) |
| 13 | Grid Distortion | 页面复核（无等价数值映射） | [并排图](images/screenshots/grid-distortion.png) |
| 14 | Field Curvature | 高度一致 | [并排图](images/screenshots/field-curvature.png) |
| 15 | Color Focus Shift | 高度一致 | [并排图](images/screenshots/color-focus-shift.png) |
| 16 | Lateral Color | 高度一致 | [并排图](images/screenshots/lateral-color.png) |
| 17 | Axial Aberration | 高度一致 | [并排图](images/screenshots/axial-aberration.png) |
| 18 | Full Field Aberration | 页面复核（无等价数值映射） | [并排图](images/screenshots/full-field-aberration.png) |
| 19 | Encircled Energy | 接近 | [并排图](images/screenshots/encircled-energy.png) |
| 20 | Diffraction Encircled Energy | 高度一致 | [并排图](images/screenshots/diffraction-encircled-energy.png) |
| 21 | Geometric Line Edge Spread | 接近 | [并排图](images/screenshots/geometric-line-edge-spread.png) |
| 22 | Extended Source Encircled Energy | 高度一致 | [并排图](images/screenshots/extended-source-encircled-energy.png) |
| 23 | Pupil Aberration | 高度一致 | [并排图](images/screenshots/pupil-aberration.png) |
| 24 | RMS vs Field | 页面复核（无等价数值映射） | [并排图](images/screenshots/rms-vs-field.png) |
| 25 | RMS vs Wavelength | 页面复核（无等价数值映射） | [并排图](images/screenshots/rms-vs-wavelength.png) |
| 26 | RMS vs Focus | 页面复核（无等价数值映射） | [并排图](images/screenshots/rms-vs-focus.png) |
| 27 | RMS Field Map | 页面复核（无等价数值映射） | [并排图](images/screenshots/rms-field-map.png) |
| 28 | RMS Wavefront vs Field | 高度一致 | [并排图](images/screenshots/rms-wavefront-vs-field.png) |
| 29 | Through Focus | 页面复核（无等价数值映射） | [并排图](images/screenshots/through-focus.png) |
| 30 | Through Focus MTF | 高度一致 | [并排图](images/screenshots/through-focus-mtf.png) |
| 31 | Fourier Through Focus MTF | 高度一致 | [并排图](images/screenshots/fourier-through-focus-mtf.png) |
| 32 | Huygens Through Focus MTF | 高度一致 | [并排图](images/screenshots/huygens-through-focus-mtf.png) |
| 33 | Geometric Through Focus MTF | 高度一致 | [并排图](images/screenshots/geometric-through-focus-mtf.png) |
| 34 | Fourier MTF vs Field | 高度一致 | [并排图](images/screenshots/fourier-mtf-vs-field.png) |
| 35 | Huygens MTF vs Field | 高度一致 | [并排图](images/screenshots/huygens-mtf-vs-field.png) |
| 36 | Geometric MTF vs Field | 高度一致 | [并排图](images/screenshots/geometric-mtf-vs-field.png) |
| 37 | Angle vs Image Height | 页面复核（无等价数值映射） | [并排图](images/screenshots/angle-vs-image-height.png) |
| 38 | Angle vs Image Height - Through Pupil | 页面复核（无等价数值映射） | [并排图](images/screenshots/angle-vs-image-height-through-pupil.png) |
| 39 | Angle vs Image Height - Through Field | 页面复核（无等价数值映射） | [并排图](images/screenshots/angle-vs-image-height-through-field.png) |
| 40 | Cardinal Points Data | 页面复核（无等价数值映射） | [并排图](images/screenshots/cardinal-points-data.png) |
| 41 | Vignetting Diagram | 页面复核（无等价数值映射） | [并排图](images/screenshots/vignetting-diagram.png) |
| 42 | Relative Illumination | 接近 | [并排图](images/screenshots/relative-illumination.png) |
| 43 | Incoherent Irradiance | 页面复核（无等价数值映射） | [并排图](images/screenshots/incoherent-irradiance.png) |
| 44 | Radiant Intensity | 页面复核（无等价数值映射） | [并排图](images/screenshots/radiant-intensity.png) |
| 45 | Y-Ybar | 页面复核（无等价数值映射） | [并排图](images/screenshots/y-ybar.png) |
| 46 | PSF | 高度一致 | [并排图](images/screenshots/psf.png) |
| 47 | FFT PSF Cross Section | 页面复核（无等价数值映射） | [并排图](images/screenshots/fft-psf-cross-section.png) |
| 48 | FFT Line Edge Spread | 页面复核（无等价数值映射） | [并排图](images/screenshots/fft-line-edge-spread.png) |
| 49 | Huygens PSF | 高度一致 | [并排图](images/screenshots/huygens-psf.png) |
| 50 | Huygens PSF Cross Section | 页面复核（无等价数值映射） | [并排图](images/screenshots/huygens-psf-cross-section.png) |
| 51 | MTF | 接近 | [并排图](images/screenshots/mtf.png) |
| 52 | Huygens MTF | 高度一致 | [并排图](images/screenshots/huygens-mtf.png) |
| 53 | Geometric MTF | 高度一致 | [并排图](images/screenshots/geometric-mtf.png) |
| 54 | Sampled MTF | 高度一致 | [并排图](images/screenshots/sampled-mtf.png) |
| 55 | Contrast Loss Map | 高度一致 | [并排图](images/screenshots/contrast-loss-map.png) |
| 56 | Optical Path Difference | 高度一致 | [并排图](images/screenshots/optical-path-difference.png) |
| 57 | Foucault Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/foucault-analysis.png) |
| 58 | Wavefront | 高度一致 | [并排图](images/screenshots/wavefront.png) |
| 59 | Centroid Sphere Wavefront | 非等价映射 | [并排图](images/screenshots/centroid-sphere-wavefront.png) |
| 60 | Best Fit Sphere Wavefront | 非等价映射 | [并排图](images/screenshots/best-fit-sphere-wavefront.png) |
| 61 | Zernike | 页面复核（无等价数值映射） | [并排图](images/screenshots/zernike.png) |
| 62 | Image Simulation | 页面复核（无等价数值映射） | [并排图](images/screenshots/image-simulation.png) |
| 63 | Geometric Image Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/geometric-image-analysis.png) |
| 64 | Geometric Bitmap Image Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/geometric-bitmap-image-analysis.png) |
| 65 | Light Source Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/light-source-analysis.png) |
| 66 | Partially Coherent Image Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/partially-coherent-image-analysis.png) |
| 67 | Extended Diffraction Image Analysis | 页面复核（无等价数值映射） | [并排图](images/screenshots/extended-diffraction-image-analysis.png) |
| 68 | Jones Pupil | 页面复核（无等价数值映射） | [并排图](images/screenshots/jones-pupil.png) |
| 69 | Prescription Report | 页面复核（无等价数值映射） | [并排图](images/screenshots/prescription-report.png) |

## 范围边界

本报告证明当前 `123456.ZMX` 对标设置下：69 个 Workbench 分析可稳定重算，30 个同定义映射没有“明显差异”，并且全部报告图片有效。它不证明未映射页面与 Zemax 数值等同，也不证明其他镜头、非序列模式、偏振、STAR 或外部数据场景已经达到相同精度。
