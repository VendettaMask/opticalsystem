# 123456.ZMX：Workbench 与 Zemax 2026 R1 当前版全面对比

## 覆盖

- 当前 Workbench 页面：69/69 成功。
- 页面截图：69 张，每张均为当前 Workbench 结构化页面重绘；可映射项右侧使用已验证的 Zemax 2026 R1 截图。
- 数值对齐：30 项；高度一致 26，接近 4，明显差异 0，未完成 0。
- 镜头结构：23 个表面、5 个视场、3 个波长；源文件 SHA-256 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。
- Huygens Through Focus MTF：本次 0.00 秒，旧版 249.27 秒，耗时比 0.00×。

## 全部数值结果

| Workbench | Zemax | 结论 | 中位 NRMSE | P90 | 最差 | 数值图 |
|---|---|---|---:|---:|---:|---|
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
| Relative Illumination | RelativeIllumination | 高度一致 | 0.04% | 0.04% | 0.04% | [图](images/numeric/relative-illumination.png) |
| Color Focus Shift | FocalShiftDiagram | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/color-focus-shift.png) |
| Optical Path Difference | OpticalPathFan | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/optical-path-difference.png) |
| Wavefront | WavefrontMap | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/wavefront.png) |

## 排除的非等价数值映射

- `Best Fit Sphere Wavefront` ↔ `WavefrontMap`：Workbench reports residual OPD after fitting a reference sphere to the traced wavefront. Zemax Wavefront Map uses the wavelength reference sphere; Zemax Best Fit Sphere data is a surface-sag/manufacturing analysis, not a Wavefront Map reference option.
- `Centroid Sphere Wavefront` ↔ `WavefrontMap`：Workbench uses Optiland's centroid-sphere fit as the reference surface. Zemax Wavefront Map keeps the wavelength reference sphere; its Remove Tilt option only removes linear X and Y tilt (centroid-referenced OPD). The two analyses therefore do not report the same physical quantity.

## 页面截图

[打开 HTML 截图库](COMPARISON_REPORT.html)

## 方法和边界

- Zemax 一侧来自仓库中已校验的 2026 R1 捕获基线，本次没有重新启动 OpticStudio。
- Workbench 一侧全部由本次当前代码重新计算，旧报告仅提供分析名称和物理系列映射，不复用旧 Workbench 数值。
- 只有物理量定义等价的分析才进入数值精度统计；名称相似但定义不同的映射会列入“排除的非等价数值映射”。
- 曲线以 257 个归一化扫描位置重采样；二维网格统一尺寸并记录采用的坐标方向。
- “高度一致”为中位 NRMSE ≤ 3% 且 P90 ≤ 10%；“接近”为中位 ≤ 10% 且 P90 ≤ 25%。
- Pupil Aberration 在 ray aiming 下是近零量；其 NRMSE 只在分母使用 `1e-4%` 绝对数值分辨率下限，避免放大约 `1e-6%` 的舍入噪声，不修改光线或分析结果。
- 页面截图用于显示内容、曲线、表格和 Zemax 参考的人工复核，不做 UI 像素相似度判定。
