# 123456.ZMX：Workbench 与 Zemax 2026 R1 全面对比

## 覆盖范围

- Workbench 分析：69/69 完成，0 超时，0 失败。
- 数值对齐：32 项；高度一致 12 项、接近 4 项、明显差异 16 项。
- 视觉对照：32 张数值曲线/热图，32 张结构化结果与 Zemax 截图并排图。
- 镜头结构：两边均为 23 个表面、5 个视场、3 个波长。

[打开完整图片报告](COMPARISON_REPORT.html) · [查看全部误差概览](images/overview.png)

## 主要结论

- FFT/几何 MTF、轴向像差、焦移、畸变、PSF 等主干分析已经能较好复现 Zemax。
- 几何环围能量存在确定的归一化错误：Workbench 结果约到 9158，而 Zemax 为 0–1。
- 光瞳像差不是小数误差，而是量纲或定义未对齐；Workbench 约为 ±15，Zemax 参考值接近 0。
- Huygens 系列整体偏低：离焦 Huygens MTF 中位 NRMSE 79.04%，MTF vs Field 为 73.48%。
- 波前图形状相关但幅值/参考面仍不一致；普通 Wavefront 相关系数 0.8922、NRMSE 18.96%。
- 横向色差的 Airy 两条曲线接近，但 shortest–longest 主曲线符号相反，因此整体被判为明显差异。
- OPD 的中位误差只有 0.219%，但少数离轴/波长曲线偏差很大，使 P90 达 71.11%。

## 全部数值结果

| Workbench | Zemax | 类型 | 结论 | 中位 NRMSE | P90 | 最差 | 对比图 |
|---|---|---|---|---:|---:|---:|---|
| Pupil Aberration | PupilAberrationFan | curves | 明显差异 | 37,112,939% | 305,483,081% | 592,295,679% | [图](images/numeric/pupil-aberration.png) |
| Encircled Energy | GeometricEncircledEnergy | curves | 明显差异 | 855,367% | 857,893% | 859,026% | [图](images/numeric/encircled-energy.png) |
| Huygens Through Focus MTF | HuygensThroughFocusMtf | curves | 明显差异 | 79.04% | 80.66% | 82.57% | [图](images/numeric/huygens-through-focus-mtf.png) |
| Huygens MTF vs Field | HuygensMtfvsField | curves | 明显差异 | 73.48% | 74.42% | 74.66% | [图](images/numeric/huygens-mtf-vs-field.png) |
| Centroid Sphere Wavefront | WavefrontMap | grids | 明显差异 | 52.82% | 52.82% | 52.82% | [图](images/numeric/centroid-sphere-wavefront.png) |
| Extended Source Encircled Energy | ExtendedSourceEncircledEnergy | curves | 明显差异 | 50.81% | 50.81% | 50.81% | [图](images/numeric/extended-source-encircled-energy.png) |
| RMS Wavefront vs Field | RMSField | curves | 明显差异 | 50.30% | 93.81% | 104.7% | [图](images/numeric/rms-wavefront-vs-field.png) |
| Best Fit Sphere Wavefront | WavefrontMap | grids | 明显差异 | 50.11% | 50.11% | 50.11% | [图](images/numeric/best-fit-sphere-wavefront.png) |
| Huygens MTF | HuygensMtf | curves | 明显差异 | 37.16% | 42.93% | 45.44% | [图](images/numeric/huygens-mtf.png) |
| Contrast Loss Map | ContrastLoss | grids | 明显差异 | 26.08% | 26.08% | 26.08% | [图](images/numeric/contrast-loss-map.png) |
| Ray Fan | RayFan | curves | 明显差异 | 24.87% | 32.53% | 35.14% | [图](images/numeric/ray-fan.png) |
| Wavefront | WavefrontMap | grids | 明显差异 | 18.96% | 18.96% | 18.96% | [图](images/numeric/wavefront.png) |
| Diffraction Encircled Energy | DiffractionEncircledEnergy | curves | 明显差异 | 17.55% | 18.42% | 19.06% | [图](images/numeric/diffraction-encircled-energy.png) |
| Huygens PSF | HuygensPsf | grids | 明显差异 | 11.50% | 11.50% | 11.50% | [图](images/numeric/huygens-psf.png) |
| Lateral Color | LateralColor | curves | 明显差异 | 2.17% | 120.6% | 150.1% | [图](images/numeric/lateral-color.png) |
| Optical Path Difference | OpticalPathFan | curves | 明显差异 | 0.22% | 71.11% | 91.51% | [图](images/numeric/optical-path-difference.png) |
| Relative Illumination | RelativeIllumination | curves | 接近 | 6.00% | 6.00% | 6.00% | [图](images/numeric/relative-illumination.png) |
| Geometric Line Edge Spread | GeometricLineEdgeSpread | curves | 接近 | 5.60% | 5.82% | 5.88% | [图](images/numeric/geometric-line-edge-spread.png) |
| MTF | FftMtf | curves | 接近 | 5.13% | 6.52% | 7.23% | [图](images/numeric/mtf.png) |
| Field Curvature | FieldCurvatureAndDistortion | curves | 接近 | 3.54% | 5.67% | 5.91% | [图](images/numeric/field-curvature.png) |
| Geometric MTF | GeometricMtf | curves | 高度一致 | 2.35% | 4.95% | 6.75% | [图](images/numeric/geometric-mtf.png) |
| Field Curvature and Distortion | FieldCurvatureAndDistortion | curves | 高度一致 | 2.32% | 5.53% | 5.91% | [图](images/numeric/field-curvature-and-distortion.png) |
| Sampled MTF | FftMtf | curves | 高度一致 | 2.11% | 3.76% | 7.30% | [图](images/numeric/sampled-mtf.png) |
| Distortion | FieldCurvatureAndDistortion | curves | 高度一致 | 1.81% | 1.82% | 1.82% | [图](images/numeric/distortion.png) |
| PSF | FftPsf | grids | 高度一致 | 0.85% | 0.85% | 0.85% | [图](images/numeric/psf.png) |
| Geometric Through Focus MTF | GeometricThroughFocusMtf | curves | 高度一致 | 0.82% | 4.22% | 7.26% | [图](images/numeric/geometric-through-focus-mtf.png) |
| Fourier Through Focus MTF | FftThroughFocusMtf | curves | 高度一致 | 0.49% | 0.52% | 0.52% | [图](images/numeric/fourier-through-focus-mtf.png) |
| Through Focus MTF | FftThroughFocusMtf | curves | 高度一致 | 0.49% | 0.52% | 0.52% | [图](images/numeric/through-focus-mtf.png) |
| Axial Aberration | LongitudinalAberration | curves | 高度一致 | 0.46% | 0.58% | 0.61% | [图](images/numeric/axial-aberration.png) |
| Fourier MTF vs Field | FftMtfvsField | curves | 高度一致 | 0.19% | 0.22% | 0.23% | [图](images/numeric/fourier-mtf-vs-field.png) |
| Geometric MTF vs Field | GeometricMtfvsField | curves | 高度一致 | 0.14% | 0.14% | 0.14% | [图](images/numeric/geometric-mtf-vs-field.png) |
| Color Focus Shift | FocalShiftDiagram | curves | 高度一致 | 0.00% | 0.00% | 0.00% | [图](images/numeric/color-focus-shift.png) |

## 判定与限制

- 曲线按固定物理映射、单位换算及扫描顺序重采样为 257 点；NRMSE 以 Zemax 对应物理量峰值归一化。
- 二维网格重采样到同一尺寸，并尝试转置/坐标翻转；PSF 比较前分别按峰值归一化。
- “高度一致”：中位 NRMSE ≤ 3% 且 P90 ≤ 10%；“接近”：中位 ≤ 10% 且 P90 ≤ 25%。
- 结构化视觉图用于人工审阅，Workbench 侧是结构化数据重绘，Zemax 侧是原生或 ZOS-API 回退截图；二者 UI 像素不作相似度判定。
- RMS vs Field/Wavelength/Focus/Field Map 的默认物理量未锁定一致；源图驱动的图像分析也未纳入数值验收。
