# 精度与页面对比验证（2026-07-31）

本报告记录当前工作区的验证结果。它区分三类结论：当前 C# 实现相对 Optiland 0.5.8 金标准的自动数值验收、Workbench 分析页面契约验收，以及以已捕获 Zemax 2026 R1 基线重新计算的 2026-07-31 当前 Workbench 全面对比。三者不能互相替代。

## 当前自动验证结果

| 验证组 | 结果 | 覆盖 |
| --- | ---: | --- |
| 完整回归 | 592/592 通过 | Core、追迹、分析、GUI、导入导出、优化、公差、主题等全部测试 |
| 数值/金标准组 | 195/195 通过 | Optiland 0.5.8 Cooke/Tessar、视场定义、标量/批量/并行追迹、TIR 与异常路径 |
| 分析页面组 | 118/118 通过 | 轴向/横向色差、PSF/MTF、RMS、OPD、Foucault、Seidel、照度、点列图、单光线、波前等页面 |
| GUI 页面契约专项 | 51/51 通过 | 分面、坐标、单位、图例、采样、设置生效、有限值和页面结果结构 |
| RMS/MTF/点列图/波前重点页 | 27/27 通过 | 重点图形页面的数值有限性、字段/波长组织和参考系列 |
| Zemax 基线完整性 | 通过 | 165 项清单、148 项捕获、17 项不适用/未创建、0 超时、148 张截图、1054 个文件 |

当前分析目录实际包含 **69** 个 Workbench 分析入口。全部入口均要求能够生成结果、包含值并导出非空文本；其中 **30** 个分析视图具有 Cooke/Tessar 的 Optiland 0.5.8 数值或显示金标准。

## 自动数值验收精度

自动测试逐点或逐像素比较，不汇总“实测最大误差”；任何一点超过阈值都会直接失败。因此下面给出的是本次通过后可以确认的误差上界，而不是虚构的最大实测误差。

| 数值类型 | 验收上界 | 本次结果 |
| --- | ---: | --- |
| 近轴标量 | 绝对误差 ≤ `1e-11` | 全部通过 |
| 顺序光线位置、方向、OPL/OPD、强度 | 绝对误差 ≤ `1e-10` | 全部通过 |
| 三类视场定义与有限/无限共轭 | `2e-9 × max(1, |reference|)` | 全部通过 |
| 常规分析点、网格、像素 | `2e-8 × max(1, |reference|)` | 全部通过 |
| FFT MTF 数值 | `1e-3 × max(1, |reference|)` | 全部通过 |
| FFT/MMDFT 衍射 PSF 数值 | `2e-4 × max(1, |reference|)` | 全部通过 |
| 图像卷积参考像素 | 绝对误差 ≤ `5e-5` | 全部通过 |
| 标量与批量/SIMD/并行追迹 | 11 位小数一致 | FinalOnly、SelectedSurfaces、FullHistory 全部通过 |

覆盖的 30 个金标准视图包括：Spot Diagram、Encircled Energy、RMS vs Field、RMS Wavefront vs Field、Ray Fan、Best Fit Ray Fan、Pupil Aberration、Through Focus Spot、Through Focus MTF、Pupil/Field Incident Angle vs Height、Incoherent Irradiance、Radiant Intensity、Y-Ybar、Chief/Centroid/Best-Fit Sphere Wavefront、Zernike OPD、FFT/MMDFT/Huygens PSF、FFT/Huygens/Geometric/Sampled MTF、Distortion、Grid Distortion、Field Curvature、Jones Pupil 和 Image Simulation。

Image Simulation 已采用 Zemax 语义扩展，旧的固定 FFT Python 图像不再作为逐像素权威；当前测试锁定 None/Geometric/Diffraction 模式、黑色 Guard Band、相对照度、单位能量、畸变/垂轴色差和严重像差时的 Geometric 回退。

## 页面精度与显示契约

69 个入口全部通过结果生成测试；页面专项另外验证：

- 字段、波长、焦面和配置分面数量与顺序；
- X/Y 轴物理量、单位、范围、零线、等比例和图例；
- Tangential/Sagittal、视场方向、颜色与线型语义；
- 采样点数、热图尺寸、有限值、渐晕过滤和设置参数确实进入计算；
- 标准点列图、离焦点列图、PSF/MTF、RMS、OPD、Foucault、Seidel、色差、照度、单光线和波前页面的结构化结果。

页面测试不是截图像素相似度测试。特别是 Avalonia 波前/OPD 热图目前使用局部反距离插值，而 Python 参考图使用 SciPy cubic `griddata`；原始采样值、坐标、色标和标题受数值契约约束，但采样点之间的渲染像素不声明完全一致。

## Zemax 2026 R1 基线完整性

只读验证命令：

```powershell
python tools/zemax_parity/verify_baseline.py artifacts/zemax/123456-zemax-2026-r1-baseline
```

验证结果：

- AnalysisIDM 总数：165；
- 已捕获：148；
- 不适用或未创建：17；
- 超时：0；
- 截图：148，其中 OpticStudio 原生截图 106、ZOS-API 数据回退渲染 42；
- 当前目录文件：1298，总计 217,637,923 bytes；其中新增的 2026-07-31 对比报告为 244 个文件、151,668,634 bytes，原 Zemax 捕获与旧报告未被覆盖；
- `123456.ZMX` SHA-256：`0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。

这一步证明已捕获基线自身完整，没有证明当前 Workbench 与所有 148 项数值一致。

## 2026-07-31 当前 Workbench–Zemax 全面对比

本次已重新计算当前代码，不再沿用 2026-07-30 的 Workbench 数值。使用同一份 `123456.ZMX`、旧报告保存的同一组分析设置和已校验的 Zemax 2026 R1 捕获基线：

- 当前 Workbench 分析：**69/69 成功**，0 失败；
- 当前原始结果：69 份 JSON，保留设置、序列、分面、点数和逐页耗时；
- 页面截图：**69 张** Workbench/Zemax 并排图；无一页被跳过；
- 数值映射：**32 项**，其中高度一致 12、接近 5、明显差异 15；
- 判定规则：高度一致为中位 NRMSE ≤ 3% 且 P90 ≤ 10%；接近为中位 ≤ 10% 且 P90 ≤ 25%；
- Huygens Through Focus MTF 本次耗时 `1695.36 s`，旧版记录 `249.27 s`，为 **6.80×**，属于明确性能回退。

## 全部数值结果

| Workbench | Zemax | 结论 | 中位 NRMSE | P90 | 最差 | 数值图 |
|---|---|---|---:|---:|---:|---|
| Pupil Aberration | PupilAberrationFan | 明显差异 | 72,020,605% | 518,814,751% | 1,007,633,067% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`pupil-aberration.png) |
| Encircled Energy | GeometricEncircledEnergy | 明显差异 | 855,367% | 857,893% | 859,026% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`encircled-energy.png) |
| Huygens Through Focus MTF | HuygensThroughFocusMtf | 明显差异 | 79.04% | 80.66% | 82.58% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`huygens-through-focus-mtf.png) |
| Huygens MTF vs Field | HuygensMtfvsField | 明显差异 | 73.67% | 74.60% | 74.84% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`huygens-mtf-vs-field.png) |
| Extended Source Encircled Energy | ExtendedSourceEncircledEnergy | 明显差异 | 50.81% | 50.81% | 50.81% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`extended-source-encircled-energy.png) |
| RMS Wavefront vs Field | RMSField | 明显差异 | 50.30% | 93.81% | 104.68% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`rms-wavefront-vs-field.png) |
| Best Fit Sphere Wavefront | WavefrontMap | 明显差异 | 49.21% | 49.21% | 49.21% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`best-fit-sphere-wavefront.png) |
| Centroid Sphere Wavefront | WavefrontMap | 明显差异 | 47.02% | 47.02% | 47.02% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`centroid-sphere-wavefront.png) |
| Contrast Loss Map | ContrastLoss | 明显差异 | 41.50% | 47.44% | 48.93% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`contrast-loss-map.png) |
| Huygens MTF | HuygensMtf | 明显差异 | 37.15% | 42.92% | 45.43% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`huygens-mtf.png) |
| Diffraction Encircled Energy | DiffractionEncircledEnergy | 明显差异 | 17.54% | 18.42% | 19.06% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`diffraction-encircled-energy.png) |
| Wavefront | WavefrontMap | 明显差异 | 17.50% | 17.50% | 17.50% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`wavefront.png) |
| Huygens PSF | HuygensPsf | 明显差异 | 11.50% | 11.50% | 11.50% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`huygens-psf.png) |
| Relative Illumination | RelativeIllumination | 接近 | 6.00% | 6.00% | 6.00% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`relative-illumination.png) |
| Geometric Line Edge Spread | GeometricLineEdgeSpread | 接近 | 5.60% | 5.82% | 5.88% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`geometric-line-edge-spread.png) |
| MTF | FftMtf | 接近 | 5.13% | 6.52% | 7.23% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`mtf.png) |
| Field Curvature | FieldCurvatureAndDistortion | 接近 | 3.88% | 7.24% | 7.55% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`field-curvature.png) |
| Ray Fan | RayFan | 接近 | 2.93% | 15.75% | 26.09% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`ray-fan.png) |
| Geometric MTF | GeometricMtf | 高度一致 | 2.35% | 4.95% | 6.75% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`geometric-mtf.png) |
| Field Curvature and Distortion | FieldCurvatureAndDistortion | 高度一致 | 2.32% | 7.06% | 7.55% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`field-curvature-and-distortion.png) |
| Lateral Color | LateralColor | 明显差异 | 2.17% | 120.55% | 150.15% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`lateral-color.png) |
| Sampled MTF | FftMtf | 高度一致 | 2.00% | 4.06% | 6.90% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`sampled-mtf.png) |
| Distortion | FieldCurvatureAndDistortion | 高度一致 | 1.81% | 1.82% | 1.82% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`distortion.png) |
| Fourier MTF vs Field | FftMtfvsField | 高度一致 | 1.75% | 1.77% | 1.77% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`fourier-mtf-vs-field.png) |
| Geometric MTF vs Field | GeometricMtfvsField | 高度一致 | 1.61% | 1.62% | 1.63% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`geometric-mtf-vs-field.png) |
| PSF | FftPsf | 高度一致 | 0.85% | 0.85% | 0.85% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`psf.png) |
| Geometric Through Focus MTF | GeometricThroughFocusMtf | 高度一致 | 0.82% | 4.22% | 7.26% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`geometric-through-focus-mtf.png) |
| Axial Aberration | LongitudinalAberration | 高度一致 | 0.66% | 0.66% | 0.66% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`axial-aberration.png) |
| Fourier Through Focus MTF | FftThroughFocusMtf | 高度一致 | 0.49% | 0.52% | 0.52% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`fourier-through-focus-mtf.png) |
| Through Focus MTF | FftThroughFocusMtf | 高度一致 | 0.49% | 0.52% | 0.52% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`through-focus-mtf.png) |
| Optical Path Difference | OpticalPathFan | 明显差异 | 0.28% | 107.01% | 128.31% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`optical-path-difference.png) |
| Color Focus Shift | FocalShiftDiagram | 高度一致 | 0.00% | 0.00% | 0.00% | [图](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/`images/numeric/`color-focus-shift.png) |

## 截图和原始数据

完整报告位于：

- `artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/COMPARISON_REPORT.md`；
- `COMPARISON_REPORT.html` 提供全部 69 张截图的浏览页；
- `comparison.json` 保存 32 项逐序列/逐网格误差、相关系数、最大绝对误差和采用的坐标方向；
- `current-manifest.json` 与 `current/*.json` 保存本次 69 个当前 Workbench 页面原始结果；
- `images/numeric/` 保存 32 张数值曲线/网格对比图，`images/screenshots/` 保存 69 张页面并排图。

Workbench 一侧为当前结构化页面数据重绘，Zemax 一侧为仓库中已验证的 OpticStudio 原生或数据回退截图。截图用于人工核对内容、曲线、表格、单位和页面结构，不把不同 UI 框架的像素相似度当作数值精度。

## 结论

当前代码继续满足 Optiland 0.5.8 金标准和 Workbench 页面契约，但 Zemax 全面对比仍有 15/32 个映射项存在明显差异。最需要优先处理的是 Pupil Aberration、Encircled Energy、Huygens 系列、RMS Wavefront、参考球面/普通 Wavefront、Contrast Loss Map 和 OPD 的离轴长尾；同时 Huygens Through Focus MTF 存在 6.80× 的页面性能回退。任何后续修正都应重新运行同一采集器和比较器，并更新本报告，而不是只依据单元测试宣称达到 Zemax 精度。