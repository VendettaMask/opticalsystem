# 95 支菜单示例逐镜头比较，2026-11-03

同一 Core 重新核查 95 个源文件，94 个导入成功、114 个配置。原 3,024 行全部结算，2904 项实际重算，120 项仍缺可执行原请求。原设置和容差保持。原生参考复用 OpticStudio 2026 R1 冻结捕获，本轮没有重新采集全部原生参考或为所有采样补做收敛。

P/C/D/I/E：通过／接近但未通过／数值差异／不可比或覆盖缺口／计算或求值失败。主矩阵和独立无焦 170 项的输入身份不同，分开计数；后者使用原始保存快照，在本轮 Core 重算。无焦列完整显示 P/C/D/I/E。

| # | 镜头 | 配置 | 主矩阵 P/C/D/I/E | 逐面光线 P/D/I/E | 状态 | 无焦补充 P/C/D/I/E |
|---:|---|---:|---:|---:|---|---:|
| 1 | Afocal/Afocal Riflescope.ZMX | 1 | 1/0/0/29/0 | 3/0/0/0 | 可比项通过，覆盖待补 | 33/0/0/1/0 |
| 2 | Afocal/Beam Expander.ZMX | 1 | 0/0/0/4/0 | 0/0/0/0 | 可比项通过，覆盖待补 | 25/0/0/1/0 |
| 3 | Afocal/Binocular_System.ZMX | 2 | 2/0/0/58/0 | 0/0/6/0 | 可比项通过，覆盖待补 | 64/0/0/4/0 |
| 4 | Birefringent prisms/Birefringent cube.zmx | 2 | 57/1/2/0/0 | 0/0/6/0 | 有数值差异 | — |
| 5 | Birefringent prisms/Rochon prism.zmx | 2 | 2/0/0/0/58 | 0/0/6/0 | 计算或参考求值失败 | — |
| 6 | Birefringent prisms/Senarmont prism.zmx | 2 | 2/0/0/0/42 | 0/0/2/0 | 计算或参考求值失败 | — |
| 7 | Birefringent prisms/Wollaston prism.zmx | 2 | 2/0/0/0/42 | 0/0/2/0 | 计算或参考求值失败 | — |
| 8 | Diffractive components/Achromatic singlet.zmx | 1 | 28/0/0/2/0 | 0/0/3/0 | 可比项通过，覆盖待补 | — |
| 9 | Diffractive components/Dual Zone CD-DVD lens.ZMX | 2 | 39/2/3/0/0 | 0/0/2/0 | 有数值差异 | — |
| 10 | Diffractive components/Grating.zmx | 1 | 38/0/0/0/0 | 0/0/5/0 | 可比项通过，覆盖待补 | — |
| 11 | Diffractive components/Reflection hologram.zmx | 1 | 21/0/1/0/0 | 0/0/1/0 | 有数值差异 | — |
| 12 | Diffractive components/Sample Spectrometer.ZMX | 1 | 1/1/15/12/1 | 0/0/3/0 | 计算或参考求值失败 | — |
| 13 | Diffractive components/Transmission hologram.zmx | 1 | 21/0/1/0/0 | 0/0/1/0 | 有数值差异 | — |
| 14 | GRINs/Grid GRIN.ZMX | 1 | 9/2/15/4/0 | 0/0/3/0 | 有数值差异 | — |
| 15 | GRINs/Selfoc lens.zmx | 1 | 25/0/4/1/0 | 0/0/3/0 | 有数值差异 | — |
| 16 | Image Simulation/Example 1, A singlet eyepiece.ZMX | 1 | 24/0/2/2/2 | 3/0/0/0 | 计算或参考求值失败 | — |
| 17 | Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX | 1 | 0/0/28/2/0 | 0/0/3/0 | 有数值差异 | — |
| 18 | Image Simulation/Example 3, A blue notch filter.ZMX | 1 | 37/0/1/0/0 | 0/0/5/0 | 有数值差异 | — |
| 19 | Image Simulation/Example 4, a diffraction limited system.ZMX | 1 | 0/0/0/22/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 20 | Image Simulation/Example 5, spatially varying resolution.ZMX | 1 | 0/0/28/2/0 | 0/0/3/0 | 有数值差异 | — |
| 21 | Image Simulation/Example 6, tilted image plane.ZMX | 2 | 43/2/0/6/9 | 0/0/6/0 | 计算或参考求值失败 | — |
| 22 | Interconnects/2 Ball lens interconnect.ZMX | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 23 | Interconnects/Ball coupling.ZMX | 1 | 20/0/0/2/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 24 | Interconnects/Conic interconnect.ZMX | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 25 | Interconnects/GRIN interconnect.ZMX | 1 | 18/1/2/1/0 | 0/0/1/0 | 有数值差异 | — |
| 26 | Interconnects/Optical interconnect.ZMX | 1 | 17/0/4/0/1 | 0/0/1/0 | 计算或参考求值失败 | — |
| 27 | Manufacturing/Apochromatic_for_Cost_3_TDE_defaults.zmx | 1 | 68/0/0/2/0 | 13/0/0/0 | 可比项通过，覆盖待补 | — |
| 28 | Miscellaneous/Apochromat_prism.zmx | 1 | 0/0/0/0/0 | 0/0/0/0 | 可比项通过，覆盖待补 | 41/0/0/1/0 |
| 29 | Miscellaneous/Axicon model.zmx | 1 | 9/3/2/8/0 | 1/0/0/0 | 有数值差异 | — |
| 30 | Miscellaneous/Beam splitter.zmx | 2 | 38/0/0/6/0 | 0/0/2/0 | 可比项通过，覆盖待补 | — |
| 31 | Miscellaneous/Bi-ocular lens.ZMX | 2 | 58/0/0/2/0 | 0/0/6/0 | 可比项通过，覆盖待补 | — |
| 32 | Miscellaneous/Cylinder.zmx | 1 | 16/0/1/2/3 | 1/0/0/0 | 计算或参考求值失败 | — |
| 33 | Miscellaneous/Diode source simulation.zmx | 1 | 18/0/0/4/0 | 0/0/1/0 | 可比项通过，覆盖待补 | — |
| 34 | Miscellaneous/Doubly telecentric system.zmx | 1 | 21/0/0/1/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 35 | Miscellaneous/Elliptical reflector 320 degree light cone .zmx | 0 | 0/0/0/0/0 | 0/0/0/0 | 导入阻断 | — |
| 36 | Miscellaneous/Grid Sag Example.zmx | 2 | 0/0/12/24/8 | 0/0/2/0 | 计算或参考求值失败 | — |
| 37 | Miscellaneous/Grid sag surface.zmx | 1 | 21/0/0/1/0 | 0/0/1/0 | 可比项通过，覆盖待补 | — |
| 38 | Miscellaneous/Irregular surface.ZMX | 1 | 17/1/0/4/0 | 0/0/1/0 | 接近但未通过 | — |
| 39 | Miscellaneous/Mangin mirror.zmx | 1 | 22/0/0/0/0 | 1/0/0/0 | 本轮既定请求全部通过 | — |
| 40 | Miscellaneous/MEMS DLL Sample.ZMX | 1 | 10/0/6/2/4 | 0/0/1/0 | 计算或参考求值失败 | — |
| 41 | Miscellaneous/Optimization demo 1.zmx | 1 | 13/3/1/2/3 | 1/0/0/0 | 计算或参考求值失败 | — |
| 42 | Miscellaneous/Optimization demo 2.zmx | 1 | 27/0/0/3/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 43 | Miscellaneous/Parabolas at 90 degrees.zmx | 1 | 3/0/2/2/15 | 0/0/1/0 | 计算或参考求值失败 | — |
| 44 | Miscellaneous/Telecentric system.zmx | 1 | 16/0/1/4/1 | 0/0/1/0 | 计算或参考求值失败 | — |
| 45 | Miscellaneous/Toroid.zmx | 1 | 3/0/5/9/5 | 0/0/1/0 | 计算或参考求值失败 | — |
| 46 | Miscellaneous/vacuum cell.zmx | 1 | 17/0/4/1/0 | 0/1/0/0 | 有数值差异 | — |
| 47 | Objectives/Apochromat.zmx | 1 | 49/0/0/1/0 | 8/0/0/0 | 可比项通过，覆盖待补 | — |
| 48 | Objectives/Apochromat2.zmx | 1 | 37/0/0/1/0 | 5/0/0/0 | 可比项通过，覆盖待补 | — |
| 49 | Objectives/Apochromat3.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 50 | Objectives/Apochromat4.zmx | 1 | 43/0/0/27/0 | 13/0/0/0 | 可比项通过，覆盖待补 | — |
| 51 | Objectives/Cooke 40 degree field.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 52 | Objectives/Double Gauss 28 degree field.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 53 | Objectives/Double Gauss 5 degree field.zmx | 1 | 35/0/0/3/0 | 5/0/0/0 | 可比项通过，覆盖待补 | — |
| 54 | Objectives/Doublet using MIL number glasses.zmx | 1 | 37/0/0/1/0 | 5/0/0/0 | 可比项通过，覆盖待补 | — |
| 55 | Objectives/Doublet.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 56 | Objectives/Even Asphere.zmx | 1 | 20/0/0/2/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 57 | Objectives/Gaussian Quadrature over Annular Pupils.zmx | 1 | 16/1/0/5/0 | 1/0/0/0 | 接近但未通过 | — |
| 58 | Objectives/Gaussian Quadrature Over Wavelength.ZMX | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 59 | Objectives/Inverse telephoto lens.zmx | 1 | 30/0/0/0/0 | 3/0/0/0 | 本轮既定请求全部通过 | — |
| 60 | Objectives/Petzval.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 61 | Objectives/Relay lens.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 62 | Objectives/Tessar lens using vignetting factors.zmx | 1 | 28/0/0/2/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 63 | Objectives/Wide angle lens 100 degree field.zmx | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 64 | Objectives/Wide angle lens 200 degree field.zmx | 1 | 26/0/0/3/1 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 65 | Objectives/Wide angle lens 210 degree field.zmx | 1 | 26/0/0/3/1 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 66 | Optical testing/Double-pass null test.zmx | 1 | 21/0/0/1/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 67 | Optical testing/Mach Zender Interferometer.zmx | 2 | 42/1/0/1/0 | 0/0/2/0 | 接近但未通过 | — |
| 68 | Optical testing/Offner reflecting compensator.zmx | 1 | 17/0/0/5/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 69 | Optical testing/Offner refracting compensator.zmx | 1 | 21/1/0/0/0 | 1/0/0/0 | 接近但未通过 | — |
| 70 | Optical testing/Twyman Green Interferometer.zmx | 2 | 33/3/3/5/0 | 0/0/2/0 | 有数值差异 | — |
| 71 | Polarization/Etalon.ZMX | 1 | 13/0/4/5/4 | 2/0/0/0 | 计算或参考求值失败 | — |
| 72 | Polarization/Fabry-Perot.ZMX | 1 | 19/0/0/3/0 | 0/0/1/0 | 可比项通过，覆盖待补 | — |
| 73 | Telescopes/Cassegrain-type Ritchey Chretien.zmx | 1 | 17/0/0/5/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 74 | Telescopes/Flat-field Schmidt.zmx | 1 | 21/0/1/8/0 | 3/0/0/0 | 有数值差异 | — |
| 75 | Telescopes/Houghton.zmx | 1 | 23/0/0/11/0 | 4/0/0/0 | 可比项通过，覆盖待补 | — |
| 76 | Telescopes/Hubble.zmx | 1 | 0/0/0/22/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 77 | Telescopes/Maksutov offset secondary.zmx | 1 | 16/0/0/6/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 78 | Telescopes/Maksutov.zmx | 1 | 16/0/0/6/0 | 1/0/0/0 | 可比项通过，覆盖待补 | — |
| 79 | Telescopes/Schalcktelescope 10.ZMX | 1 | 29/0/0/1/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 80 | Telescopes/Schmidt-Cassegrain spider obscuration.zmx | 1 | 12/9/2/15/0 | 0/0/5/0 | 有数值差异 | — |
| 81 | Telescopes/Schmidt-Cassegrain.zmx | 1 | 21/0/0/9/0 | 3/0/0/0 | 可比项通过，覆盖待补 | — |
| 82 | Telescopes/Unobscured Gregorian.zmx | 1 | 8/2/6/5/1 | 0/0/1/0 | 计算或参考求值失败 | — |
| 83 | Telescopes/Yolo.zmx | 1 | 10/2/3/7/0 | 0/0/1/0 | 有数值差异 | — |
| 84 | Tilted systems & prisms/Beamsplitter cube.zmx | 1 | 11/0/6/2/3 | 0/0/1/0 | 计算或参考求值失败 | — |
| 85 | Tilted systems & prisms/Lens pivoted about a point.zmx | 1 | 28/0/0/2/0 | 0/0/3/0 | 可比项通过，覆盖待补 | — |
| 86 | Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1 | 15/0/0/6/1 | 0/0/1/0 | 计算或参考求值失败 | — |
| 87 | Tilted systems & prisms/Offner relay.zmx | 1 | 22/0/0/0/0 | 0/0/1/0 | 可比项通过，覆盖待补 | — |
| 88 | Tilted systems & prisms/Prism using tilted surface.zmx | 1 | 37/0/0/1/0 | 0/0/5/0 | 可比项通过，覆盖待补 | — |
| 89 | Tilted systems & prisms/Prism using total internal reflection.zmx | 1 | 19/0/0/3/0 | 0/0/1/0 | 可比项通过，覆盖待补 | — |
| 90 | Tilted systems & prisms/Prisms using coordinate breaks.ZMX | 1 | 21/0/1/0/0 | 0/0/1/0 | 有数值差异 | — |
| 91 | Tilted systems & prisms/Tilted mirror.zmx | 1 | 20/1/0/1/0 | 0/0/1/0 | 接近但未通过 | — |
| 92 | Tilted systems & prisms/Tilted object.zmx | 1 | 18/2/1/0/1 | 0/0/1/0 | 计算或参考求值失败 | — |
| 93 | Zoom systems/Mirror zoom.zmx | 5 | 72/1/8/29/0 | 5/0/0/0 | 有数值差异 | — |
| 94 | Zoom systems/Scanning mirror.zmx | 3 | 55/0/0/11/0 | 0/0/3/0 | 可比项通过，覆盖待补 | — |
| 95 | Zoom systems/Zoom lens.zmx | 3 | 83/0/0/7/0 | 9/0/0/0 | 可比项通过，覆盖待补 | — |
