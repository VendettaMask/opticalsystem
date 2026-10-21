# 全部 95 支镜头本轮结果

日期：2026-10-21。正式 Application/Core 重算，本机 Zemax OpticStudio 2026 R1 新捕获，原比较策略和容差保持。

共 3024 项分析、251 组逐面光线。不同配置、波长和控制分别计数；不能将请求数量当作缺陷数。Close、不可比、错误、缺失配置及准备阻断均不计为全部通过。

分析顺序为 **Pass / Close / Difference / 不可比 / Error**；光线为 **Pass / Difference / 不可比 / Error**。下表按首要问题互斥分类，详情保留同时存在的问题。

## Afocal

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Afocal Riflescope.ZMX | 存在分析差异 | 1/1 | 1/0/1/28/0 | 3/0/0/0 | Field Curvature and Distortion×1；存在无共同有效参考或能力契约缺口 |
| Beam Expander.ZMX | 导入/准备阻断 | 1/1 | 0/0/0/3/1 | 0/0/0/0 | System.IO.InvalidDataException: Unverified source afocal angular unit: AS；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Binocular_System.ZMX | 存在计算/采集错误 | 2/2 | 0/0/0/48/12 | 0/0/0/6 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## Birefringent prisms

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Birefringent cube.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/0/60 | 0/0/0/6 | 计算/采集错误（见逐项原始原因） |
| Rochon prism.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/0/60 | 0/0/0/6 | 计算/采集错误（见逐项原始原因） |
| Senarmont prism.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/0/44 | 0/0/0/2 | 计算/采集错误（见逐项原始原因） |
| Wollaston prism.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/0/44 | 0/0/0/2 | 计算/采集错误（见逐项原始原因） |

## Diffractive components

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Achromatic singlet.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/30 | 0/0/0/3 | 计算/采集错误（见逐项原始原因） |
| Dual Zone CD-DVD lens.ZMX | 存在计算/采集错误 | 2/2 | 0/0/0/0/44 | 0/0/0/2 | 计算/采集错误（见逐项原始原因） |
| Grating.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/38 | 0/0/0/5 | 计算/采集错误（见逐项原始原因） |
| Reflection hologram.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Sample Spectrometer.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/1/29 | 0/0/0/3 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Transmission hologram.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |

## GRINs

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Grid GRIN.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/1/29 | 0/0/0/3 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Selfoc lens.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/30 | 0/0/0/3 | 计算/采集错误（见逐项原始原因） |

## Image Simulation

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Example 1, A singlet eyepiece.ZMX | 存在分析差异 | 1/1 | 13/0/4/11/2 | 3/0/0/0 | Field Curvature and Distortion×1；MTF×1；Optical Path Difference×1；Wavefront×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Example 2, Double Gauss Experimental Arrangement.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/30 | 0/0/0/3 | 计算/采集错误（见逐项原始原因） |
| Example 3, A blue notch filter.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/38 | 0/0/0/5 | 计算/采集错误（见逐项原始原因） |
| Example 4, a diffraction limited system.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/22/0 | 0/0/0/1 | 存在无共同有效参考或能力契约缺口 |
| Example 5, spatially varying resolution.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/30 | 0/0/0/3 | 计算/采集错误（见逐项原始原因） |
| Example 6, tilted image plane.ZMX | 存在计算/采集错误 | 2/2 | 0/0/0/0/60 | 0/0/0/6 | 计算/采集错误（见逐项原始原因） |

## Interconnects

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| 2 Ball lens interconnect.ZMX | 存在分析差异 | 1/1 | 16/0/3/3/0 | 1/0/0/0 | FFT PSF Cross Section×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Ball coupling.ZMX | 存在分析差异 | 1/1 | 14/0/3/5/0 | 1/0/0/0 | FFT PSF Cross Section×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Conic interconnect.ZMX | 存在分析差异 | 1/1 | 18/0/1/3/0 | 1/0/0/0 | FFT PSF Cross Section×1；存在无共同有效参考或能力契约缺口 |
| GRIN interconnect.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Optical interconnect.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |

## Manufacturing

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Apochromatic_for_Cost_3_TDE_defaults.zmx | 可比项通过，仍有不可比 | 1/1 | 68/0/0/2/0 | 13/0/0/0 | 存在无共同有效参考或能力契约缺口 |

## Miscellaneous

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Apochromat_prism.zmx | 导入/准备阻断 | 0/1 | 0/0/0/0/0 | 0/0/0/0 | System.NotSupportedException: Zemax coordinate-break order flags are not supported (SURF 4, PARM 6). |
| Axicon model.zmx | 存在分析差异 | 1/1 | 5/0/1/5/11 | 1/0/0/0 | Encircled Energy×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Beam splitter.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/2/42 | 0/0/0/2 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Bi-ocular lens.ZMX | 存在分析差异 | 2/2 | 28/4/18/10/0 | 0/0/6/0 | Diffraction Encircled Energy×1；Encircled Energy×1；FFT PSF Cross Section×2；Huygens PSF×1；Huygens PSF Cross Section×1；MTF×2；Optical Path Difference×4；Ray Fan×4；RMS vs Field×1；Spot Diagram×3；Wavefront×2；存在无共同有效参考或能力契约缺口 |
| Cylinder.zmx | 存在分析差异 | 1/1 | 16/0/1/4/1 | 1/0/0/0 | Field Curvature and Distortion×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Diode source simulation.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Doubly telecentric system.zmx | 可比项通过，仍有不可比 | 1/1 | 21/0/0/1/0 | 1/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Elliptical reflector 320 degree light cone .zmx | 导入/准备阻断 | 0/1 | 0/0/0/0/0 | 0/0/0/0 | System.NotSupportedException: Zemax object-cone-angle apertures (OBNA subtype 1) are not supported. |
| Grid Sag Example.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/2/42 | 0/0/0/2 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Grid sag surface.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Irregular surface.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Mangin mirror.zmx | 存在分析差异 | 1/1 | 18/0/1/3/0 | 1/0/0/0 | First Order×1；存在无共同有效参考或能力契约缺口 |
| MEMS DLL Sample.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Optimization demo 1.zmx | 存在分析差异 | 1/1 | 10/3/2/6/1 | 1/0/0/0 | Encircled Energy×1；First Order×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Optimization demo 2.zmx | 可比项通过，仍有不可比 | 1/1 | 27/0/0/3/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Parabolas at 90 degrees.zmx | 存在分析差异 | 1/1 | 0/0/1/7/14 | 0/0/1/0 | First Order×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Telecentric system.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Toroid.zmx | 存在分析差异 | 1/1 | 7/0/4/10/1 | 0/0/1/0 | Field Curvature and Distortion×1；Optical Path Difference×2；Wavefront×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| vacuum cell.zmx | 存在分析差异 | 1/1 | 17/0/4/1/0 | 0/1/0/0 | Diffraction Encircled Energy×1；First Order×1；Single Ray Trace×2；存在无共同有效参考或能力契约缺口 |

## Objectives

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Apochromat.zmx | 可比项通过，仍有不可比 | 1/1 | 46/0/0/4/0 | 8/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Apochromat2.zmx | 可比项通过，仍有不可比 | 1/1 | 34/0/0/4/0 | 5/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Apochromat3.zmx | 可比项通过，仍有不可比 | 1/1 | 26/0/0/4/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Apochromat4.zmx | 可比项通过，仍有不可比 | 1/1 | 43/0/0/27/0 | 13/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Cooke 40 degree field.zmx | 本次请求全部通过 | 1/1 | 30/0/0/0/0 | 3/0/0/0 |  |
| Double Gauss 28 degree field.zmx | 可比项通过，仍有不可比 | 1/1 | 29/0/0/1/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Double Gauss 5 degree field.zmx | 可比项通过，仍有不可比 | 1/1 | 35/0/0/3/0 | 5/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Doublet using MIL number glasses.zmx | 可比项通过，仍有不可比 | 1/1 | 34/0/0/4/0 | 5/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Doublet.zmx | 可比项通过，仍有不可比 | 1/1 | 27/0/0/3/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Even Asphere.zmx | 可比项通过，仍有不可比 | 1/1 | 20/0/0/2/0 | 1/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Gaussian Quadrature over Annular Pupils.zmx | 可比项通过，仍有不可比 | 1/1 | 18/0/0/4/0 | 1/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Gaussian Quadrature Over Wavelength.ZMX | 可比项通过，仍有不可比 | 1/1 | 29/0/0/1/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Inverse telephoto lens.zmx | 本次请求全部通过 | 1/1 | 30/0/0/0/0 | 3/0/0/0 |  |
| Petzval.zmx | 可比项通过，仍有不可比 | 1/1 | 29/0/0/1/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Relay lens.zmx | 可比项通过，仍有不可比 | 1/1 | 29/0/0/1/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Tessar lens using vignetting factors.zmx | 可比项通过，仍有不可比 | 1/1 | 28/0/0/2/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Wide angle lens 100 degree field.zmx | 可比项通过，仍有不可比 | 1/1 | 29/0/0/1/0 | 3/0/0/0 | 存在无共同有效参考或能力契约缺口 |
| Wide angle lens 200 degree field.zmx | 存在逐面光线差异 | 1/1 | 11/0/0/6/13 | 0/3/0/0 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Wide angle lens 210 degree field.zmx | 存在逐面光线差异 | 1/1 | 13/0/0/4/13 | 0/3/0/0 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## Optical testing

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Double-pass null test.zmx | 存在分析差异 | 1/1 | 1/1/2/6/12 | 0/1/0/0 | FFT PSF Cross Section×1；First Order×1；MTF×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Mach Zender Interferometer.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/0/44 | 0/0/0/2 | 计算/采集错误（见逐项原始原因） |
| Offner reflecting compensator.zmx | 存在分析差异 | 1/1 | 4/2/7/9/0 | 0/1/0/0 | Encircled Energy×1；FFT PSF Cross Section×1；First Order×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Offner refracting compensator.zmx | 存在分析差异 | 1/1 | 17/1/1/3/0 | 1/0/0/0 | Diffraction Encircled Energy×1；First Order×1；存在无共同有效参考或能力契约缺口 |
| Twyman Green Interferometer.zmx | 存在计算/采集错误 | 2/2 | 0/0/0/2/42 | 0/0/0/2 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## Polarization

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Etalon.ZMX | 存在分析差异 | 1/1 | 13/0/6/5/2 | 2/0/0/0 | Diffraction Encircled Energy×1；Encircled Energy×1；First Order×1；MTF×1；Optical Path Difference×2；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Fabry-Perot.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## Telescopes

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Cassegrain-type Ritchey Chretien.zmx | 存在分析差异 | 1/1 | 4/1/8/9/0 | 0/1/0/0 | Diffraction Encircled Energy×1；Encircled Energy×1；FFT PSF Cross Section×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Flat-field Schmidt.zmx | 存在分析差异 | 1/1 | 18/3/1/8/0 | 3/0/0/0 | Diffraction Encircled Energy×1；First Order×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；存在无共同有效参考或能力契约缺口 |
| Houghton.zmx | 存在分析差异 | 1/1 | 9/1/12/12/0 | 0/4/0/0 | Diffraction Encircled Energy×1；Encircled Energy×1；FFT PSF Cross Section×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Relative Illumination×1；Spot Diagram×5；存在无共同有效参考或能力契约缺口 |
| Hubble.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/22/0 | 0/0/0/1 | 存在无共同有效参考或能力契约缺口 |
| Maksutov offset secondary.zmx | 存在分析差异 | 1/1 | 6/1/8/7/0 | 0/1/0/0 | Encircled Energy×1；FFT PSF Cross Section×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Relative Illumination×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Maksutov.zmx | 存在分析差异 | 1/1 | 6/0/9/7/0 | 0/1/0/0 | Encircled Energy×1；FFT PSF Cross Section×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Relative Illumination×1；Spot Diagram×2；存在无共同有效参考或能力契约缺口 |
| Schalcktelescope 10.ZMX | 存在分析差异 | 1/1 | 27/0/1/1/1 | 3/0/0/0 | First Order×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Schmidt-Cassegrain spider obscuration.zmx | 存在分析差异 | 1/1 | 5/0/12/21/0 | 0/0/5/0 | Encircled Energy×1；FFT PSF Cross Section×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；Spot Diagram×6；存在无共同有效参考或能力契约缺口 |
| Schmidt-Cassegrain.zmx | 存在分析差异 | 1/1 | 8/2/10/10/0 | 0/3/0/0 | Encircled Energy×1；FFT PSF Cross Section×1；Huygens MTF×1；Huygens PSF×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；MTF×1；PSF×1；Relative Illumination×1；Spot Diagram×3；存在无共同有效参考或能力契约缺口 |
| Unobscured Gregorian.zmx | 存在计算/采集错误 | 1/1 | 2/0/0/6/14 | 0/0/1/0 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Yolo.zmx | 存在分析差异 | 1/1 | 8/2/4/7/1 | 0/0/1/0 | Encircled Energy×1；Field Curvature and Distortion×1；First Order×1；RMS vs Field×1；Spot Diagram×1；Wavefront×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## Tilted systems & prisms

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Beamsplitter cube.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Lens pivoted about a point.zmx | 存在分析差异 | 1/1 | 21/1/1/6/1 | 0/0/3/0 | FFT PSF Cross Section×1；Wavefront×1；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| OAP using Chebyshev Polynomial surface.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/1/21 | 0/0/0/1 | 计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Offner relay.zmx | 存在分析差异 | 1/1 | 19/0/1/2/0 | 0/0/1/0 | First Order×1；存在无共同有效参考或能力契约缺口 |
| Prism using tilted surface.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/38 | 0/0/0/5 | 计算/采集错误（见逐项原始原因） |
| Prism using total internal reflection.zmx | 存在分析差异 | 1/1 | 13/2/2/5/0 | 0/0/1/0 | First Order×1；Huygens PSF Cross Section×1；Huygens Through Focus MTF×1；Optical Path Difference×1；存在无共同有效参考或能力契约缺口 |
| Prisms using coordinate breaks.ZMX | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Tilted mirror.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |
| Tilted object.zmx | 存在计算/采集错误 | 1/1 | 0/0/0/0/22 | 0/0/0/1 | 计算/采集错误（见逐项原始原因） |

## Zoom systems

| 镜头 | 本轮状态 | Core/原生配置 | 分析 P/C/D/I/E | 光线 P/D/I/E | 已记录问题 |
|---|---|---:|---:|---:|---|
| Mirror zoom.zmx | 存在分析差异 | 5/5 | 26/0/9/10/65 | 3/2/0/0 | Encircled Energy×4；Spot Diagram×5；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Scanning mirror.zmx | 存在分析差异 | 3/3 | 5/1/34/25/1 | 0/0/3/0 | Encircled Energy×2；First Order×3；Huygens MTF×3；Huygens PSF×3；Huygens PSF Cross Section×3；Huygens Through Focus MTF×3；MTF×1；Optical Path Difference×6；Ray Fan×4；Spot Diagram×4；Wavefront×3；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |
| Zoom lens.zmx | 存在分析差异 | 3/3 | 71/0/9/7/3 | 9/0/0/0 | Optical Path Difference×9；计算/采集错误（见逐项原始原因）；存在无共同有效参考或能力契约缺口 |

## 范围与明细

95 个名称各算一支，不重复计入 ZOS 格式副本。本表主矩阵使用原 ZMX，ZOS 第一配置模型读回独立记录在 [机器汇总](summary.json)。本次请求限于 18 类主分析、各正权波长末视场四项控制与 GQ6 逐面光线，不涵盖任意设置的穷举。

[逐项分析](analysis-matrix.csv)、[逐面光线](ray-results.json)、[错误分类](failure-matrix.csv)保留具体误差和原因。原生无效输出不能成为通过参考，反射/坐标约定和未支持面型不能直接归为算法缺陷。

