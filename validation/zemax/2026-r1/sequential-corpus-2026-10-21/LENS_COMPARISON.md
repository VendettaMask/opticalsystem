原生参考格式：ZMX。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。

# Sequential 选定目录逐镜头对比

日期：2026-10-21。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。

| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Afocal/Afocal Riflescope.ZMX | 1/1 | 0 | 1 | 0 | 1 | 28 | 0 | 3/0/0/0 | Equal | 3.22266E+10 |
| Afocal/Beam Expander.ZMX | 1/1 | 1 | 0 | 0 | 0 | 3 | 1 | 0/0/0/0 | Equal | — |
| Afocal/Binocular_System.ZMX | 2/2 | 0 | 0 | 0 | 0 | 48 | 12 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Birefringent cube.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Rochon prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Senarmont prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Difference | — |
| Birefringent prisms/Wollaston prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Difference | — |
| Diffractive components/Achromatic singlet.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Diffractive components/Dual Zone CD-DVD lens.ZMX | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Diffractive components/Grating.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Diffractive components/Reflection hologram.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Diffractive components/Sample Spectrometer.ZMX | 1/1 | 0 | 0 | 0 | 0 | 1 | 29 | 0/0/0/3 | Equal | — |
| Diffractive components/Transmission hologram.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| GRINs/Grid GRIN.ZMX | 1/1 | 0 | 0 | 0 | 0 | 1 | 29 | 0/0/0/3 | Equal | — |
| GRINs/Selfoc lens.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 1, A singlet eyepiece.ZMX | 1/1 | 0 | 13 | 0 | 4 | 11 | 2 | 3/0/0/0 | Equal | 2.89346 |
| Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 3, A blue notch filter.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Image Simulation/Example 4, a diffraction limited system.ZMX | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 0/0/0/1 | Equal | — |
| Image Simulation/Example 5, spatially varying resolution.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Image Simulation/Example 6, tilted image plane.ZMX | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Equal | — |
| Interconnects/2 Ball lens interconnect.ZMX | 1/1 | 0 | 16 | 0 | 3 | 3 | 0 | 1/0/0/0 | Equal | 0.154187 |
| Interconnects/Ball coupling.ZMX | 1/1 | 0 | 14 | 0 | 3 | 5 | 0 | 1/0/0/0 | Equal | 0.135142 |
| Interconnects/Conic interconnect.ZMX | 1/1 | 0 | 18 | 0 | 1 | 3 | 0 | 1/0/0/0 | Equal | 0.0122158 |
| Interconnects/GRIN interconnect.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Interconnects/Optical interconnect.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Manufacturing/Apochromatic_for_Cost_3_TDE_defaults.zmx | 1/1 | 0 | 68 | 0 | 0 | 2 | 0 | 13/0/0/0 | Equal | 0.000542302 |
| Miscellaneous/Apochromat_prism.zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Axicon model.zmx | 1/1 | 0 | 5 | 0 | 1 | 5 | 11 | 1/0/0/0 | Equal | 0.996132 |
| Miscellaneous/Beam splitter.zmx | 2/2 | 0 | 0 | 0 | 0 | 2 | 42 | 0/0/0/2 | Equal | — |
| Miscellaneous/Bi-ocular lens.ZMX | 2/2 | 0 | 28 | 4 | 18 | 10 | 0 | 0/0/6/0 | Equal | 3029.08 |
| Miscellaneous/Cylinder.zmx | 1/1 | 0 | 16 | 0 | 1 | 4 | 1 | 1/0/0/0 | Equal | 0.771327 |
| Miscellaneous/Diode source simulation.zmx | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Equal | — |
| Miscellaneous/Doubly telecentric system.zmx | 1/1 | 0 | 21 | 0 | 0 | 1 | 0 | 1/0/0/0 | Equal | 0.00115683 |
| Miscellaneous/Elliptical reflector 320 degree light cone .zmx | 0/1 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Miscellaneous/Grid Sag Example.zmx | 2/2 | 0 | 0 | 0 | 0 | 2 | 42 | 0/0/0/2 | Equal | — |
| Miscellaneous/Grid sag surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/Irregular surface.ZMX | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Equal | — |
| Miscellaneous/Mangin mirror.zmx | 1/1 | 0 | 18 | 0 | 1 | 3 | 0 | 1/0/0/0 | Equal | 2 |
| Miscellaneous/MEMS DLL Sample.ZMX | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Difference | — |
| Miscellaneous/Optimization demo 1.zmx | 1/1 | 0 | 10 | 3 | 2 | 6 | 1 | 1/0/0/0 | Equal | 1 |
| Miscellaneous/Optimization demo 2.zmx | 1/1 | 0 | 27 | 0 | 0 | 3 | 0 | 3/0/0/0 | Equal | 0.00242834 |
| Miscellaneous/Parabolas at 90 degrees.zmx | 1/1 | 0 | 0 | 0 | 1 | 7 | 14 | 0/0/1/0 | Equal | 1.66667 |
| Miscellaneous/Telecentric system.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/Toroid.zmx | 1/1 | 0 | 7 | 0 | 4 | 10 | 1 | 0/0/1/0 | Equal | 8.93607 |
| Miscellaneous/vacuum cell.zmx | 1/1 | 0 | 17 | 0 | 4 | 1 | 0 | 0/1/0/0 | Equal | 0.0162543 |
| Objectives/Apochromat.zmx | 1/1 | 0 | 46 | 0 | 0 | 4 | 0 | 8/0/0/0 | Equal | 3.52054E-06 |
| Objectives/Apochromat2.zmx | 1/1 | 0 | 34 | 0 | 0 | 4 | 0 | 5/0/0/0 | Equal | 7.7275E-06 |
| Objectives/Apochromat3.zmx | 1/1 | 0 | 26 | 0 | 0 | 4 | 0 | 3/0/0/0 | Equal | 0.000310185 |
| Objectives/Apochromat4.zmx | 1/1 | 0 | 43 | 0 | 0 | 27 | 0 | 13/0/0/0 | Equal | 0.0011322 |
| Objectives/Cooke 40 degree field.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 8.20787E-05 |
| Objectives/Double Gauss 28 degree field.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Difference | 0.000477181 |
| Objectives/Double Gauss 5 degree field.zmx | 1/1 | 0 | 35 | 0 | 0 | 3 | 0 | 5/0/0/0 | Equal | 0.00135975 |
| Objectives/Doublet using MIL number glasses.zmx | 1/1 | 0 | 34 | 0 | 0 | 4 | 0 | 5/0/0/0 | Equal | 3.23421E-06 |
| Objectives/Doublet.zmx | 1/1 | 0 | 27 | 0 | 0 | 3 | 0 | 3/0/0/0 | Equal | 1.0963E-05 |
| Objectives/Even Asphere.zmx | 1/1 | 0 | 20 | 0 | 0 | 2 | 0 | 1/0/0/0 | Equal | 2.0791E-05 |
| Objectives/Gaussian Quadrature over Annular Pupils.zmx | 1/1 | 0 | 18 | 0 | 0 | 4 | 0 | 1/0/0/0 | Equal | 3.22174E-06 |
| Objectives/Gaussian Quadrature Over Wavelength.ZMX | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.00133981 |
| Objectives/Inverse telephoto lens.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 0.000391505 |
| Objectives/Petzval.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.00096647 |
| Objectives/Relay lens.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 7.71896E-05 |
| Objectives/Tessar lens using vignetting factors.zmx | 1/1 | 0 | 28 | 0 | 0 | 2 | 0 | 3/0/0/0 | Equal | 0.000454577 |
| Objectives/Wide angle lens 100 degree field.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.00277596 |
| Objectives/Wide angle lens 200 degree field.zmx | 1/1 | 0 | 11 | 0 | 0 | 6 | 13 | 0/3/0/0 | Equal | 0.000790241 |
| Objectives/Wide angle lens 210 degree field.zmx | 1/1 | 0 | 13 | 0 | 0 | 4 | 13 | 0/3/0/0 | Equal | 1.38308E-05 |
| Optical testing/Double-pass null test.zmx | 1/1 | 0 | 1 | 1 | 2 | 6 | 12 | 0/1/0/0 | Equal | 1.99669 |
| Optical testing/Mach Zender Interferometer.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Optical testing/Offner reflecting compensator.zmx | 1/1 | 0 | 4 | 2 | 7 | 9 | 0 | 0/1/0/0 | Equal | 2 |
| Optical testing/Offner refracting compensator.zmx | 1/1 | 0 | 17 | 1 | 1 | 3 | 0 | 1/0/0/0 | Equal | 2 |
| Optical testing/Twyman Green Interferometer.zmx | 2/2 | 0 | 0 | 0 | 0 | 2 | 42 | 0/0/0/2 | Equal | — |
| Polarization/Etalon.ZMX | 1/1 | 0 | 13 | 0 | 6 | 5 | 2 | 2/0/0/0 | Equal | 1 |
| Polarization/Fabry-Perot.ZMX | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Equal | — |
| Telescopes/Cassegrain-type Ritchey Chretien.zmx | 1/1 | 0 | 4 | 1 | 8 | 9 | 0 | 0/1/0/0 | Equal | 0.126093 |
| Telescopes/Flat-field Schmidt.zmx | 1/1 | 0 | 18 | 3 | 1 | 8 | 0 | 3/0/0/0 | Equal | 2 |
| Telescopes/Houghton.zmx | 1/1 | 0 | 9 | 1 | 12 | 12 | 0 | 0/4/0/0 | Equal | 0.237237 |
| Telescopes/Hubble.zmx | 1/1 | 0 | 0 | 0 | 0 | 22 | 0 | 0/0/0/1 | Equal | — |
| Telescopes/Maksutov offset secondary.zmx | 1/1 | 0 | 6 | 1 | 8 | 7 | 0 | 0/1/0/0 | Equal | 0.437789 |
| Telescopes/Maksutov.zmx | 1/1 | 0 | 6 | 0 | 9 | 7 | 0 | 0/1/0/0 | Equal | 1.53967 |
| Telescopes/Schalcktelescope 10.ZMX | 1/1 | 0 | 27 | 0 | 1 | 1 | 1 | 3/0/0/0 | Equal | 2 |
| Telescopes/Schmidt-Cassegrain spider obscuration.zmx | 1/1 | 0 | 5 | 0 | 12 | 21 | 0 | 0/0/5/0 | Equal | 0.146247 |
| Telescopes/Schmidt-Cassegrain.zmx | 1/1 | 0 | 8 | 2 | 10 | 10 | 0 | 0/3/0/0 | Equal | 0.218345 |
| Telescopes/Unobscured Gregorian.zmx | 1/1 | 0 | 2 | 0 | 0 | 6 | 14 | 0/0/1/0 | Equal | 2.08225E-12 |
| Telescopes/Yolo.zmx | 1/1 | 0 | 8 | 2 | 4 | 7 | 1 | 0/0/1/0 | Equal | 1.73402 |
| Tilted systems & prisms/Beamsplitter cube.zmx | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Lens pivoted about a point.zmx | 1/1 | 0 | 21 | 1 | 1 | 6 | 1 | 0/0/3/0 | Equal | 0.140829 |
| Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 1 | 21 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Offner relay.zmx | 1/1 | 0 | 19 | 0 | 1 | 2 | 0 | 0/0/1/0 | Equal | 2 |
| Tilted systems & prisms/Prism using tilted surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 38 | 0/0/0/5 | Equal | — |
| Tilted systems & prisms/Prism using total internal reflection.zmx | 1/1 | 0 | 13 | 2 | 2 | 5 | 0 | 0/0/1/0 | Equal | 2 |
| Tilted systems & prisms/Prisms using coordinate breaks.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Tilted mirror.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Tilted systems & prisms/Tilted object.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Zoom systems/Mirror zoom.zmx | 5/5 | 0 | 26 | 0 | 9 | 10 | 65 | 3/2/0/0 | Equal | 0.492074 |
| Zoom systems/Scanning mirror.zmx | 3/3 | 0 | 5 | 1 | 34 | 25 | 1 | 0/0/3/0 | Equal | 66.7736 |
| Zoom systems/Zoom lens.zmx | 3/3 | 0 | 71 | 0 | 9 | 7 | 3 | 9/0/0/0 | Equal | 2172.67 |

P/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。

分析结论：{"Incomparable":449,"Pass":1102,"Difference":181,"Error":1267,"Close":25}；逐面光线：{"Pass":119,"Error":88,"Incomparable":23,"Difference":21}。源文件 295/295 保持。

