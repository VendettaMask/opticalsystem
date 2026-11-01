# 全部 95 支菜单示例逐镜头结果，2026-10-31

本轮同一冻结 Core 重新导入全部文件，按原 3,024 行账目重算 2,904 个可执行请求；原生参考复用 OpticStudio 2026 R1 冻结数组。P/C/D/I/E 分别为通过、接近但未通过、数值差异、不可比/覆盖缺口、计算返回失败。E 包含未支持模型和适用性拒绝，不能全部解释为算法精度错误。全部既定请求通过不等于所有分析/采样均获收敛认证。

原主矩阵中的无焦旧适配缺口保留，最后一列给出本轮同 Core 的独立 170 项复核；不同请求集合不相加。19 支物镜的两项 F-Tan ≥90° 拒绝为定义边界，历史 F-Theta 补充另有有效参考。详细原因见 `request-results.csv`，回退见 `prior-pass-audit.json`。

| # | 镜头 | 配置 | 原矩阵 P/C/D/I/E | 逐面光线 P/D/I/E | 首要状态 | 同 Core 无焦补充 P/I |
|---:|---|---:|---:|---:|---|---:|
| 1 | Afocal/Afocal Riflescope.ZMX | 1 | 1/0/0/29/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | 33/1 |
| 2 | Afocal/Beam Expander.ZMX | 1 | 0/0/0/4/0 | 0/0/0/0 | 可比项通过，覆盖或适用性待补 | 25/1 |
| 3 | Afocal/Binocular_System.ZMX | 2 | 2/0/0/58/0 | 0/0/6/0 | 可比项通过，覆盖或适用性待补 | 64/4 |
| 4 | Birefringent prisms/Birefringent cube.zmx | 2 | 0/0/0/0/60 | 0/0/6/0 | 计算或能力缺口 | — |
| 5 | Birefringent prisms/Rochon prism.zmx | 2 | 0/0/0/0/60 | 0/0/6/0 | 计算或能力缺口 | — |
| 6 | Birefringent prisms/Senarmont prism.zmx | 2 | 0/0/0/0/44 | 0/0/2/0 | 计算或能力缺口 | — |
| 7 | Birefringent prisms/Wollaston prism.zmx | 2 | 0/0/0/0/44 | 0/0/2/0 | 计算或能力缺口 | — |
| 8 | Diffractive components/Achromatic singlet.zmx | 1 | 0/0/0/0/30 | 0/0/3/0 | 计算或能力缺口 | — |
| 9 | Diffractive components/Dual Zone CD-DVD lens.ZMX | 2 | 0/0/0/0/44 | 0/0/2/0 | 计算或能力缺口 | — |
| 10 | Diffractive components/Grating.zmx | 1 | 0/0/0/0/38 | 0/0/5/0 | 计算或能力缺口 | — |
| 11 | Diffractive components/Reflection hologram.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 12 | Diffractive components/Sample Spectrometer.ZMX | 1 | 0/0/0/1/29 | 0/0/3/0 | 计算或能力缺口 | — |
| 13 | Diffractive components/Transmission hologram.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 14 | GRINs/Grid GRIN.ZMX | 1 | 0/0/0/1/29 | 0/0/3/0 | 计算或能力缺口 | — |
| 15 | GRINs/Selfoc lens.zmx | 1 | 0/0/0/0/30 | 0/0/3/0 | 计算或能力缺口 | — |
| 16 | Image Simulation/Example 1, A singlet eyepiece.ZMX | 1 | 16/0/7/5/2 | 3/0/0/0 | 计算或能力缺口 | — |
| 17 | Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX | 1 | 0/0/0/0/30 | 0/0/3/0 | 计算或能力缺口 | — |
| 18 | Image Simulation/Example 3, A blue notch filter.ZMX | 1 | 0/0/0/0/38 | 0/0/5/0 | 计算或能力缺口 | — |
| 19 | Image Simulation/Example 4, a diffraction limited system.ZMX | 1 | 0/0/0/22/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 20 | Image Simulation/Example 5, spatially varying resolution.ZMX | 1 | 0/0/0/0/30 | 0/0/3/0 | 计算或能力缺口 | — |
| 21 | Image Simulation/Example 6, tilted image plane.ZMX | 2 | 0/0/0/0/60 | 0/0/6/0 | 计算或能力缺口 | — |
| 22 | Interconnects/2 Ball lens interconnect.ZMX | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 23 | Interconnects/Ball coupling.ZMX | 1 | 20/0/0/2/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 24 | Interconnects/Conic interconnect.ZMX | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 25 | Interconnects/GRIN interconnect.ZMX | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 26 | Interconnects/Optical interconnect.ZMX | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 27 | Manufacturing/Apochromatic_for_Cost_3_TDE_defaults.zmx | 1 | 68/0/0/2/0 | 13/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 28 | Miscellaneous/Apochromat_prism.zmx | 1 | 0/0/0/0/0 | 0/0/0/0 | 原矩阵缺参考 | 41/1 |
| 29 | Miscellaneous/Axicon model.zmx | 1 | 6/1/1/3/11 | 1/0/0/0 | 计算或能力缺口 | — |
| 30 | Miscellaneous/Beam splitter.zmx | 2 | 0/0/0/2/42 | 0/0/2/0 | 计算或能力缺口 | — |
| 31 | Miscellaneous/Bi-ocular lens.ZMX | 2 | 28/4/18/10/0 | 0/0/6/0 | 有数值差异 | — |
| 32 | Miscellaneous/Cylinder.zmx | 1 | 16/0/1/4/1 | 1/0/0/0 | 计算或能力缺口 | — |
| 33 | Miscellaneous/Diode source simulation.zmx | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 34 | Miscellaneous/Doubly telecentric system.zmx | 1 | 21/0/0/1/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 35 | Miscellaneous/Elliptical reflector 320 degree light cone .zmx | 0 | 0/0/0/0/0 | 0/0/0/0 | 导入阻断 | — |
| 36 | Miscellaneous/Grid Sag Example.zmx | 2 | 0/0/0/2/42 | 0/0/2/0 | 计算或能力缺口 | — |
| 37 | Miscellaneous/Grid sag surface.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 38 | Miscellaneous/Irregular surface.ZMX | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 39 | Miscellaneous/Mangin mirror.zmx | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 40 | Miscellaneous/MEMS DLL Sample.ZMX | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 41 | Miscellaneous/Optimization demo 1.zmx | 1 | 13/3/1/3/2 | 1/0/0/0 | 计算或能力缺口 | — |
| 42 | Miscellaneous/Optimization demo 2.zmx | 1 | 27/0/0/3/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 43 | Miscellaneous/Parabolas at 90 degrees.zmx | 1 | 1/0/1/4/16 | 0/0/1/0 | 计算或能力缺口 | — |
| 44 | Miscellaneous/Telecentric system.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 45 | Miscellaneous/Toroid.zmx | 1 | 7/0/4/10/1 | 0/0/1/0 | 计算或能力缺口 | — |
| 46 | Miscellaneous/vacuum cell.zmx | 1 | 17/0/4/1/0 | 0/1/0/0 | 有数值差异 | — |
| 47 | Objectives/Apochromat.zmx | 1 | 49/0/0/1/0 | 8/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 48 | Objectives/Apochromat2.zmx | 1 | 37/0/0/1/0 | 5/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 49 | Objectives/Apochromat3.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 50 | Objectives/Apochromat4.zmx | 1 | 43/0/0/27/0 | 13/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 51 | Objectives/Cooke 40 degree field.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 52 | Objectives/Double Gauss 28 degree field.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 53 | Objectives/Double Gauss 5 degree field.zmx | 1 | 35/0/0/3/0 | 5/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 54 | Objectives/Doublet using MIL number glasses.zmx | 1 | 37/0/0/1/0 | 5/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 55 | Objectives/Doublet.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 56 | Objectives/Even Asphere.zmx | 1 | 20/0/0/2/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 57 | Objectives/Gaussian Quadrature over Annular Pupils.zmx | 1 | 21/0/0/1/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 58 | Objectives/Gaussian Quadrature Over Wavelength.ZMX | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 59 | Objectives/Inverse telephoto lens.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 60 | Objectives/Petzval.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 61 | Objectives/Relay lens.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 62 | Objectives/Tessar lens using vignetting factors.zmx | 1 | 28/0/0/2/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 63 | Objectives/Wide angle lens 100 degree field.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 64 | Objectives/Wide angle lens 200 degree field.zmx | 1 | 26/0/0/3/1 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 65 | Objectives/Wide angle lens 210 degree field.zmx | 1 | 26/0/0/3/1 | 3/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 66 | Optical testing/Double-pass null test.zmx | 1 | 3/1/1/4/13 | 0/1/0/0 | 计算或能力缺口 | — |
| 67 | Optical testing/Mach Zender Interferometer.zmx | 2 | 0/0/0/0/44 | 0/0/2/0 | 计算或能力缺口 | — |
| 68 | Optical testing/Offner reflecting compensator.zmx | 1 | 8/0/0/2/12 | 1/0/0/0 | 计算或能力缺口 | — |
| 69 | Optical testing/Offner refracting compensator.zmx | 1 | 21/1/0/0/0 | 1/0/0/0 | 接近但未通过 | — |
| 70 | Optical testing/Twyman Green Interferometer.zmx | 2 | 0/0/0/2/42 | 0/0/2/0 | 计算或能力缺口 | — |
| 71 | Polarization/Etalon.ZMX | 1 | 13/0/5/5/3 | 2/0/0/0 | 计算或能力缺口 | — |
| 72 | Polarization/Fabry-Perot.ZMX | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 73 | Telescopes/Cassegrain-type Ritchey Chretien.zmx | 1 | 8/0/0/2/12 | 0/1/0/0 | 计算或能力缺口 | — |
| 74 | Telescopes/Flat-field Schmidt.zmx | 1 | 20/2/0/8/0 | 3/0/0/0 | 接近但未通过 | — |
| 75 | Telescopes/Houghton.zmx | 1 | 9/3/2/5/15 | 0/4/0/0 | 计算或能力缺口 | — |
| 76 | Telescopes/Hubble.zmx | 1 | 0/0/0/22/0 | 1/0/0/0 | 可比项通过，覆盖或适用性待补 | — |
| 77 | Telescopes/Maksutov offset secondary.zmx | 1 | 7/0/1/2/12 | 0/1/0/0 | 计算或能力缺口 | — |
| 78 | Telescopes/Maksutov.zmx | 1 | 5/0/3/2/12 | 0/1/0/0 | 计算或能力缺口 | — |
| 79 | Telescopes/Schalcktelescope 10.ZMX | 1 | 28/0/0/1/1 | 3/0/0/0 | 计算或能力缺口 | — |
| 80 | Telescopes/Schmidt-Cassegrain spider obscuration.zmx | 1 | 3/0/7/12/16 | 0/0/5/0 | 计算或能力缺口 | — |
| 81 | Telescopes/Schmidt-Cassegrain.zmx | 1 | 6/1/5/4/14 | 0/3/0/0 | 计算或能力缺口 | — |
| 82 | Telescopes/Unobscured Gregorian.zmx | 1 | 2/0/3/4/13 | 0/0/1/0 | 计算或能力缺口 | — |
| 83 | Telescopes/Yolo.zmx | 1 | 8/2/4/7/1 | 0/0/1/0 | 计算或能力缺口 | — |
| 84 | Tilted systems & prisms/Beamsplitter cube.zmx | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 85 | Tilted systems & prisms/Lens pivoted about a point.zmx | 1 | 21/1/1/6/1 | 0/0/3/0 | 计算或能力缺口 | — |
| 86 | Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1 | 0/0/0/1/21 | 0/0/1/0 | 计算或能力缺口 | — |
| 87 | Tilted systems & prisms/Offner relay.zmx | 1 | 20/0/0/2/0 | 0/0/1/0 | 可比项通过，覆盖或适用性待补 | — |
| 88 | Tilted systems & prisms/Prism using tilted surface.zmx | 1 | 0/0/0/0/38 | 0/0/5/0 | 计算或能力缺口 | — |
| 89 | Tilted systems & prisms/Prism using total internal reflection.zmx | 1 | 14/2/1/5/0 | 0/0/1/0 | 有数值差异 | — |
| 90 | Tilted systems & prisms/Prisms using coordinate breaks.ZMX | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 91 | Tilted systems & prisms/Tilted mirror.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 92 | Tilted systems & prisms/Tilted object.zmx | 1 | 0/0/0/0/22 | 0/0/1/0 | 计算或能力缺口 | — |
| 93 | Zoom systems/Mirror zoom.zmx | 5 | 30/0/5/10/65 | 5/0/0/0 | 计算或能力缺口 | — |
| 94 | Zoom systems/Scanning mirror.zmx | 3 | 12/1/35/17/1 | 0/0/3/0 | 计算或能力缺口 | — |
| 95 | Zoom systems/Zoom lens.zmx | 3 | 71/0/9/7/3 | 9/0/0/0 | 计算或能力缺口 | — |

