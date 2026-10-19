原生参考格式：ZMX。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。

# Sequential 全目录逐镜头对比

日期：2026-10-19。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。

| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Afocal/Afocal Riflescope.ZMX | 1/1 | 0 | 0 | 0 | 0 | 30 | 0 | 3/0/0/0 | Equal | — |
| Afocal/Beam Expander.ZMX | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 1/0/0/0 | Equal | — |
| Afocal/Binocular_System.ZMX | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Birefringent prisms/Birefringent cube.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Birefringent prisms/Rochon prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Birefringent prisms/Senarmont prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Birefringent prisms/Wollaston prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Diffractive components/Achromatic singlet.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Diffractive components/Dual Zone CD-DVD lens.ZMX | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Diffractive components/Grating.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Diffractive components/Reflection hologram.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Diffractive components/Sample Spectrometer.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Diffractive components/Transmission hologram.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| GRINs/Grid GRIN.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| GRINs/Selfoc lens.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 1, A singlet eyepiece.ZMX | 1/1 | 0 | 13 | 0 | 4 | 11 | 2 | 3/0/0/0 | Equal | 2.89346 |
| Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 3, A blue notch filter.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Image Simulation/Example 4, a diffraction limited system.ZMX | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 0/0/0/1 | Equal | — |
| Image Simulation/Example 5, spatially varying resolution.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 6, tilted image plane.ZMX | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Equal | — |
| Interconnects/2 Ball lens interconnect.ZMX | 1/1 | 0 | 16 | 0 | 3 | 3 | 0 | 1/0/0/0 | Equal | 0.154187 |
| Interconnects/Ball coupling.ZMX | 1/1 | 0 | 13 | 1 | 3 | 5 | 0 | 1/0/0/0 | Equal | 0.135142 |
| Interconnects/Conic interconnect.ZMX | 1/1 | 0 | 17 | 1 | 1 | 3 | 0 | 1/0/0/0 | Equal | 0.0122157 |
| Interconnects/GRIN interconnect.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Interconnects/Optical interconnect.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Manufacturing/Apochromatic_for_Cost_3_TDE_defaults.zmx | 1/1 | 0 | 0 | 0 | 0 | 70 | 0 | 13/0/0/0 | Equal | — |
| Miscellaneous/Apochromat_prism.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Axicon model.zmx | 1/1 | 0 | 5 | 0 | 1 | 3 | 13 | 1/0/0/0 | Equal | 0.996132 |
| Miscellaneous/Beam splitter.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Miscellaneous/Bi-ocular lens.ZMX | 2/2 | 0 | 28 | 4 | 18 | 10 | 0 | 0/0/6/0 | Equal | 3029.08 |
| Miscellaneous/Cylinder.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Diode source simulation.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/Doubly telecentric system.zmx | 1/1 | 0 | 20 | 1 | 0 | 1 | 0 | 1/0/0/0 | Equal | 0.0122918 |
| Miscellaneous/Elliptical reflector 320 degree light cone .zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Grid Sag Example.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Grid sag surface.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Irregular surface.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/Mangin mirror.zmx | 1/1 | 0 | 16 | 0 | 3 | 3 | 0 | 0/1/0/0 | Equal | 2 |
| Miscellaneous/MEMS DLL Sample.ZMX | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Difference | — |
| Miscellaneous/Optimization demo 1.zmx | 1/1 | 0 | 8 | 3 | 4 | 5 | 2 | 0/1/0/0 | Equal | 2 |
| Miscellaneous/Optimization demo 2.zmx | 1/1 | 0 | 26 | 0 | 1 | 2 | 1 | 3/0/0/0 | Equal | 0.0382525 |
| Miscellaneous/Parabolas at 90 degrees.zmx | 1/1 | 0 | 0 | 0 | 1 | 7 | 14 | 0/0/1/0 | Equal | 1.66667 |
| Miscellaneous/Telecentric system.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/Toroid.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/vacuum cell.zmx | 1/1 | 0 | 17 | 0 | 4 | 1 | 0 | 0/1/0/0 | Equal | 0.0162543 |
| Objectives/Apochromat.zmx | 1/1 | 0 | 46 | 0 | 0 | 4 | 0 | 8/0/0/0 | Equal | 0.00259608 |
| Objectives/Apochromat2.zmx | 1/1 | 0 | 33 | 0 | 1 | 4 | 0 | 5/0/0/0 | Equal | 0.0114336 |
| Objectives/Apochromat3.zmx | 1/1 | 0 | 26 | 0 | 0 | 4 | 0 | 3/0/0/0 | Equal | 0.00250088 |
| Objectives/Apochromat4.zmx | 1/1 | 0 | 0 | 0 | 0 | 70 | 0 | 13/0/0/0 | Equal | — |
| Objectives/Cooke 40 degree field.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 0.00555827 |
| Objectives/Double Gauss 28 degree field.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Difference | 0.00304886 |
| Objectives/Double Gauss 5 degree field.zmx | 1/1 | 0 | 0 | 0 | 0 | 38 | 0 | 5/0/0/0 | Equal | — |
| Objectives/Doublet using MIL number glasses.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Objectives/Doublet.zmx | 1/1 | 0 | 25 | 0 | 0 | 3 | 2 | 3/0/0/0 | Equal | 0.00250568 |
| Objectives/Even Asphere.zmx | 1/1 | 0 | 19 | 0 | 1 | 2 | 0 | 1/0/0/0 | Equal | 0.0317763 |
| Objectives/Gaussian Quadrature over Annular Pupils.zmx | 1/1 | 0 | 16 | 0 | 2 | 4 | 0 | 0/1/0/0 | Equal | 0.816497 |
| Objectives/Gaussian Quadrature Over Wavelength.ZMX | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.0013398 |
| Objectives/Inverse telephoto lens.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 0.00132908 |
| Objectives/Petzval.zmx | 1/1 | 0 | 26 | 2 | 0 | 1 | 1 | 3/0/0/0 | Equal | 0.0119845 |
| Objectives/Relay lens.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.00737022 |
| Objectives/Tessar lens using vignetting factors.zmx | 1/1 | 0 | 27 | 0 | 0 | 2 | 1 | 3/0/0/0 | Equal | 0.00822062 |
| Objectives/Wide angle lens 100 degree field.zmx | 1/1 | 0 | 0 | 0 | 0 | 30 | 0 | 3/0/0/0 | Difference | — |
| Objectives/Wide angle lens 200 degree field.zmx | 1/1 | 0 | 0 | 0 | 0 | 30 | 0 | 3/0/0/0 | Equal | — |
| Objectives/Wide angle lens 210 degree field.zmx | 1/1 | 0 | 24 | 0 | 2 | 4 | 0 | 3/0/0/0 | Equal | 2019.95 |
| Optical testing/Double-pass null test.zmx | 1/1 | 0 | 1 | 1 | 2 | 6 | 12 | 0/1/0/0 | Equal | 1.99669 |
| Optical testing/Mach Zender Interferometer.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Optical testing/Offner reflecting compensator.zmx | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 0/1/0/0 | Equal | — |
| Optical testing/Offner refracting compensator.zmx | 1/1 | 0 | 15 | 1 | 3 | 2 | 1 | 0/1/0/0 | Equal | 2 |
| Optical testing/Twyman Green Interferometer.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Polarization/Etalon.ZMX | 1/1 | 0 | 13 | 0 | 6 | 4 | 3 | 2/0/0/0 | Equal | 1 |
| Polarization/Fabry-Perot.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Telescopes/Cassegrain-type Ritchey Chretien.zmx | 1/1 | 0 | 2 | 1 | 10 | 9 | 0 | 0/1/0/0 | Equal | 0.816497 |
| Telescopes/Flat-field Schmidt.zmx | 1/1 | 0 | 15 | 2 | 5 | 8 | 0 | 0/3/0/0 | Equal | 2 |
| Telescopes/Houghton.zmx | 1/1 | 0 | 0 | 0 | 0 | 34 | 0 | 0/4/0/0 | Equal | — |
| Telescopes/Hubble.zmx | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 0/0/0/1 | Equal | — |
| Telescopes/Maksutov offset secondary.zmx | 1/1 | 0 | 4 | 1 | 10 | 7 | 0 | 0/1/0/0 | Equal | 0.816497 |
| Telescopes/Maksutov.zmx | 1/1 | 0 | 4 | 0 | 11 | 7 | 0 | 0/1/0/0 | Equal | 1.53967 |
| Telescopes/Schalcktelescope 10.ZMX | 1/1 | 0 | 22 | 1 | 5 | 1 | 1 | 0/3/0/0 | Equal | 2 |
| Telescopes/Schmidt-Cassegrain spider obscuration.zmx | 1/1 | 0 | 0 | 0 | 0 | 38 | 0 | 0/0/5/0 | Equal | — |
| Telescopes/Schmidt-Cassegrain.zmx | 1/1 | 0 | 0 | 0 | 0 | 30 | 0 | 0/3/0/0 | Equal | — |
| Telescopes/Unobscured Gregorian.zmx | 1/1 | 0 | 1 | 0 | 1 | 6 | 14 | 0/0/1/0 | Equal | 113.091 |
| Telescopes/Yolo.zmx | 1/1 | 1 | 0 | 0 | 0 | 0 | 1 | 0/0/0/0 | Equal | — |
| Tilted systems & prisms/Beamsplitter cube.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Lens pivoted about a point.zmx | 1/1 | 0 | 20 | 2 | 1 | 6 | 1 | 0/0/3/0 | Equal | 0.142184 |
| Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1/1 | 1 | 0 | 0 | 0 | 0 | 1 | 0/0/0/0 | Equal | — |
| Tilted systems & prisms/Offner relay.zmx | 1/1 | 0 | 19 | 0 | 1 | 2 | 0 | 0/0/1/0 | Equal | 2 |
| Tilted systems & prisms/Prism using tilted surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Tilted systems & prisms/Prism using total internal reflection.zmx | 1/1 | 0 | 12 | 3 | 2 | 4 | 1 | 0/0/1/0 | Equal | 2 |
| Tilted systems & prisms/Prisms using coordinate breaks.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Tilted mirror.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Tilted object.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Zoom systems/Mirror zoom.zmx | 5/5 | 0 | 0 | 0 | 0 | 110 | 0 | 0/5/0/0 | Equal | — |
| Zoom systems/Scanning mirror.zmx | 3/3 | 0 | 5 | 1 | 34 | 22 | 4 | 0/0/3/0 | Equal | 66.7736 |
| Zoom systems/Zoom lens.zmx | 3/3 | 0 | 69 | 1 | 10 | 7 | 3 | 9/0/0/0 | Equal | 2172.67 |

P/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。

分析结论：{"Incomparable":744,"Error":836,"Pass":770,"Difference":150,"Close":26}；逐面光线：{"Pass":107,"Error":61,"Incomparable":21,"Difference":28}。源文件 295/295 保持。

