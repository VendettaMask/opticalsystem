# 按镜头对比结果（95 个镜头）

日期：2026-10-19。参考为本机 Zemax OpticStudio 2026 R1 新采集；软件由正式 Application / Core 重算，原容差不变。以下按源目录和镜头逐一列出，点击对应详情可查看具体配置、视场、波长和误差。

**分析计数顺序：通过 / 接近 / 差异 / 不可比 / 错误。光线计数顺序：通过 / 差异 / 不可比 / 错误。** 接近单列，不算严格通过；导入失败即使没有分析请求，也不能算通过。分析行是请求数，可能包含不同配置、波长及控制；并非独立缺陷数。光线行是控制组数。

“本次请求全部通过”只适用于本次明确设置；“已比较项通过”保留未覆盖项。分析差异和原始反射光线方向差异仍需排除坐标/适配器约定，不能直接全部归为产品算法缺陷。最大 NRMSE 只在对应分析内解释，不用于跨镜头排名。

95 个名称对应 95 对 ZMX/ZOS，不把格式副本重复算作镜头。13 个格式存在差异的镜头另做 ZOS 补充，结果列于同一镜头详情中；Core 读取的是原生导出的 ZMX，不代表产品支持直接导入 ZOS。

## Afocal

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Afocal Riflescope.ZMX](#lens-1) | 分析不可比，精度未认证 | 0/0/0/30/0 | 3/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Beam Expander.ZMX](#lens-2) | 分析不可比，精度未认证 | 0/0/0/22/0 | 1/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Binocular_System.ZMX](#lens-3) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |

## Birefringent prisms

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Birefringent cube.zmx](#lens-4) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Rochon prism.zmx](#lens-5) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Senarmont prism.zmx](#lens-6) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Wollaston prism.zmx](#lens-7) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |

## Diffractive components

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Achromatic singlet.zmx](#lens-8) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Dual Zone CD-DVD lens.ZMX](#lens-9) | 存在计算/采集错误 | 0/0/0/0/44 | 0/0/0/2 | 模型面型/能力未支持×44 |
| [Grating.zmx](#lens-10) | 存在计算/采集错误 | 0/0/0/0/38 | 0/0/0/5 | 模型面型/能力未支持×38 |
| [Reflection hologram.zmx](#lens-11) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Sample Spectrometer.ZMX](#lens-12) | 存在计算/采集错误 | 0/0/0/0/30 | 0/0/0/3 | 模型面型/能力未支持×30 |
| [Transmission hologram.zmx](#lens-13) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |

## GRINs

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Grid GRIN.ZMX](#lens-14) | 存在计算/采集错误 | 0/0/0/0/30 | 0/0/0/3 | 模型面型/能力未支持×30 |
| [Selfoc lens.zmx](#lens-15) | 存在计算/采集错误 | 0/0/0/0/30 | 0/0/0/3 | 模型面型/能力未支持×30 |

## Image Simulation

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Example 1, A singlet eyepiece.ZMX](#lens-16) | 存在分析差异 | 13/0/4/11/2 | 3/0/0/0 | 场曲与畸变×1、FFT MTF×1、光程差×1、波前×1；软件计算失败×2；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Example 2, Double Gauss Experimental Arrangement.ZMX](#lens-17) | 存在计算/采集错误 | 0/0/0/0/30 | 0/0/0/3 | 模型面型/能力未支持×30 |
| [Example 3, A blue notch filter.ZMX](#lens-18) | 存在计算/采集错误 | 0/0/0/0/38 | 0/0/0/5 | 模型面型/能力未支持×38 |
| [Example 4, a diffraction limited system.ZMX](#lens-19) | 存在计算/采集错误 | 0/0/0/22/0 | 0/0/0/1 | 现有分析适配器限制毫米有焦像空间 |
| [Example 5, spatially varying resolution.ZMX](#lens-20) | 存在计算/采集错误 | 0/0/0/0/30 | 0/0/0/3 | 模型面型/能力未支持×30 |
| [Example 6, tilted image plane.ZMX](#lens-21) | 存在计算/采集错误 | 0/0/0/0/60 | 0/0/0/6 | 模型面型/能力未支持×60 |

## Interconnects

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [2 Ball lens interconnect.ZMX](#lens-22) | 存在分析差异 | 16/0/3/3/0 | 1/0/0/0 | FFT PSF 截面×1、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Ball coupling.ZMX](#lens-23) | 存在分析差异 | 13/1/3/5/0 | 1/0/0/0 | FFT PSF 截面×1、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Conic interconnect.ZMX](#lens-24) | 存在分析差异 | 17/1/1/3/0 | 1/0/0/0 | FFT PSF 截面×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [GRIN interconnect.ZMX](#lens-25) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Optical interconnect.ZMX](#lens-26) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |

## Manufacturing

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Apochromatic_for_Cost_3_TDE_defaults.zmx](#lens-27) | 分析不可比，精度未认证 | 0/0/0/70/0 | 13/0/0/0 | 现有分析适配器限制毫米有焦像空间 |

## Miscellaneous

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Apochromat_prism.zmx](#lens-28) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.NotSupportedException: Zemax coordinate-break order flags are not supported (SURF 4, PARM 6). |
| [Axicon model.zmx](#lens-29) | 存在分析差异 | 5/0/1/3/13 | 1/0/0/0 | 几何包围能量×1；主光线未到达像面×7；软件计算失败×6；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Beam splitter.zmx](#lens-30) | 存在计算/采集错误 | 0/0/0/0/44 | 0/0/0/2 | 模型面型/能力未支持×44 |
| [Bi-ocular lens.ZMX](#lens-31) | 存在分析差异 | 28/4/18/10/0 | 0/0/6/0 | 几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、光程差×4、光线扇形图×4、RMS 随视场×1、点列图×3、波前×2；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Cylinder.zmx](#lens-32) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Diode source simulation.zmx](#lens-33) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Doubly telecentric system.zmx](#lens-34) | 部分接近，仍需核对 | 20/1/0/1/0 | 1/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Elliptical reflector 320 degree light cone .zmx](#lens-35) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.NotSupportedException: Zemax object-cone-angle apertures (OBNA subtype 1) are not supported. |
| [Grid Sag Example.zmx](#lens-36) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Grid sag surface.zmx](#lens-37) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Irregular surface.ZMX](#lens-38) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Mangin mirror.zmx](#lens-39) | 存在分析差异 | 16/0/3/3/0 | 0/1/0/0 | 一阶参数×1、单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [MEMS DLL Sample.ZMX](#lens-40) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Optimization demo 1.zmx](#lens-41) | 存在分析差异 | 8/3/4/5/2 | 0/1/0/0 | 几何包围能量×1、一阶参数×1、单光线×2；原生采集适配器失败×1；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Optimization demo 2.zmx](#lens-42) | 存在分析差异 | 26/0/1/2/1 | 3/0/0/0 | FFT MTF×1；原生采集适配器失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Parabolas at 90 degrees.zmx](#lens-43) | 存在分析差异 | 0/0/1/7/14 | 0/0/1/0 | 一阶参数×1；主光线未到达像面×11；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Telecentric system.zmx](#lens-44) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Toroid.zmx](#lens-45) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [vacuum cell.zmx](#lens-46) | 存在分析差异 | 17/0/4/1/0 | 0/1/0/0 | 衍射包围能量×1、一阶参数×1、单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |

## Objectives

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Apochromat.zmx](#lens-47) | 已比较项通过，存在未覆盖项 | 46/0/0/4/0 | 8/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Apochromat2.zmx](#lens-48) | 存在分析差异 | 33/0/1/4/0 | 5/0/0/0 | 衍射包围能量×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Apochromat3.zmx](#lens-49) | 已比较项通过，存在未覆盖项 | 26/0/0/4/0 | 3/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Apochromat4.zmx](#lens-50) | 分析不可比，精度未认证 | 0/0/0/70/0 | 13/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Cooke 40 degree field.zmx](#lens-51) | 本次请求全部通过 | 30/0/0/0/0 | 3/0/0/0 | 本次已比较项未见超差；范围限于本次设置 |
| [Double Gauss 28 degree field.zmx](#lens-52) | 已比较项通过，存在未覆盖项 | 29/0/0/1/0 | 3/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Double Gauss 5 degree field.zmx](#lens-53) | 分析不可比，精度未认证 | 0/0/0/38/0 | 5/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Doublet using MIL number glasses.zmx](#lens-54) | 存在计算/采集错误 | 0/0/0/0/38 | 0/0/0/5 | 模型面型/能力未支持×38 |
| [Doublet.zmx](#lens-55) | 存在计算/采集错误 | 25/0/0/3/2 | 3/0/0/0 | 软件计算失败×2；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Even Asphere.zmx](#lens-56) | 存在分析差异 | 19/0/1/2/0 | 1/0/0/0 | FFT MTF×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Gaussian Quadrature over Annular Pupils.zmx](#lens-57) | 存在分析差异 | 16/0/2/4/0 | 0/1/0/0 | 单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Gaussian Quadrature Over Wavelength.ZMX](#lens-58) | 已比较项通过，存在未覆盖项 | 29/0/0/1/0 | 3/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Inverse telephoto lens.zmx](#lens-59) | 本次请求全部通过 | 30/0/0/0/0 | 3/0/0/0 | 本次已比较项未见超差；范围限于本次设置 |
| [Petzval.zmx](#lens-60) | 存在计算/采集错误 | 26/2/0/1/1 | 3/0/0/0 | 软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Relay lens.zmx](#lens-61) | 已比较项通过，存在未覆盖项 | 29/0/0/1/0 | 3/0/0/0 | 部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Tessar lens using vignetting factors.zmx](#lens-62) | 存在计算/采集错误 | 27/0/0/2/1 | 3/0/0/0 | 软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Wide angle lens 100 degree field.zmx](#lens-63) | 分析不可比，精度未认证 | 0/0/0/30/0 | 3/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Wide angle lens 200 degree field.zmx](#lens-64) | 分析不可比，精度未认证 | 0/0/0/30/0 | 3/0/0/0 | 现有分析适配器限制毫米有焦像空间 |
| [Wide angle lens 210 degree field.zmx](#lens-65) | 存在分析差异 | 24/0/2/4/0 | 3/0/0/0 | 光程差×2；部分原生/软件输出缺少可比数据，见下方逐项原因 |

## Optical testing

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Double-pass null test.zmx](#lens-66) | 存在分析差异 | 1/1/2/6/12 | 0/1/0/0 | FFT PSF 截面×1、一阶参数×1；主光线未到达像面×7；软件计算失败×5；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Mach Zender Interferometer.zmx](#lens-67) | 导入/准备失败 | 0/0/0/0/0 | 0/0/0/0 | System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers. |
| [Offner reflecting compensator.zmx](#lens-68) | 存在光线差异，待核坐标约定 | 0/0/0/22/0 | 0/1/0/0 | 现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核 |
| [Offner refracting compensator.zmx](#lens-69) | 存在分析差异 | 15/1/3/2/1 | 0/1/0/0 | 一阶参数×1、单光线×2；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Twyman Green Interferometer.zmx](#lens-70) | 存在计算/采集错误 | 0/0/0/0/44 | 0/0/0/2 | 模型面型/能力未支持×44 |

## Polarization

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Etalon.ZMX](#lens-71) | 存在分析差异 | 13/0/6/4/3 | 2/0/0/0 | 衍射包围能量×1、几何包围能量×1、一阶参数×1、FFT MTF×1、光程差×2；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因 |
| [Fabry-Perot.ZMX](#lens-72) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |

## Telescopes

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Cassegrain-type Ritchey Chretien.zmx](#lens-73) | 存在分析差异 | 2/1/10/9/0 | 0/1/0/0 | 衍射包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Flat-field Schmidt.zmx](#lens-74) | 存在分析差异 | 15/2/5/8/0 | 0/3/0/0 | 一阶参数×1、单光线×4；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Houghton.zmx](#lens-75) | 存在光线差异，待核坐标约定 | 0/0/0/34/0 | 0/4/0/0 | 现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核 |
| [Hubble.zmx](#lens-76) | 存在计算/采集错误 | 0/0/0/22/0 | 0/0/0/1 | 现有分析适配器限制毫米有焦像空间 |
| [Maksutov offset secondary.zmx](#lens-77) | 存在分析差异 | 4/1/10/7/0 | 0/1/0/0 | 几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Maksutov.zmx](#lens-78) | 存在分析差异 | 4/0/11/7/0 | 0/1/0/0 | 几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、相对照度×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Schalcktelescope 10.ZMX](#lens-79) | 存在分析差异 | 22/1/5/1/1 | 0/3/0/0 | 一阶参数×1、单光线×4；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核 |
| [Schmidt-Cassegrain spider obscuration.zmx](#lens-80) | 分析不可比，精度未认证 | 0/0/0/38/0 | 0/0/5/0 | 现有分析适配器限制毫米有焦像空间；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Schmidt-Cassegrain.zmx](#lens-81) | 存在光线差异，待核坐标约定 | 0/0/0/30/0 | 0/3/0/0 | 现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核 |
| [Unobscured Gregorian.zmx](#lens-82) | 存在分析差异 | 1/0/1/6/14 | 0/0/1/0 | RMS 随视场×1；主光线未到达像面×10；软件计算失败×4；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Yolo.zmx](#lens-83) | 导入/准备失败 | 0/0/0/0/1 | 0/0/0/0 | System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[0].surface: surface reference 9 does not exist.；导入/准备失败×1 |

## Tilted systems & prisms

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Beamsplitter cube.zmx](#lens-84) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Lens pivoted about a point.zmx](#lens-85) | 存在分析差异 | 20/2/1/6/1 | 0/0/3/0 | FFT PSF 截面×1；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [OAP using Chebyshev Polynomial surface.zmx](#lens-86) | 导入/准备失败 | 0/0/0/0/1 | 0/0/0/0 | System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[1].surface: surface reference 4 does not exist.；导入/准备失败×1 |
| [Offner relay.zmx](#lens-87) | 存在分析差异 | 19/0/1/2/0 | 0/0/1/0 | 一阶参数×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Prism using tilted surface.zmx](#lens-88) | 存在计算/采集错误 | 0/0/0/0/38 | 0/0/0/5 | 模型面型/能力未支持×38 |
| [Prism using total internal reflection.zmx](#lens-89) | 存在分析差异 | 12/3/2/4/1 | 0/0/1/0 | 一阶参数×1、Huygens 离焦 MTF×1；原生采集适配器失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Prisms using coordinate breaks.ZMX](#lens-90) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Tilted mirror.zmx](#lens-91) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |
| [Tilted object.zmx](#lens-92) | 存在计算/采集错误 | 0/0/0/0/22 | 0/0/0/1 | 模型面型/能力未支持×22 |

## Zoom systems

| 镜头 | 结论 | 分析：通过/接近/差异/不可比/错误 | 光线：通过/差异/不可比/错误 | 具体差异或阻断 |
|---|---|---:|---:|---|
| [Mirror zoom.zmx](#lens-93) | 存在光线差异，待核坐标约定 | 0/0/0/110/0 | 0/5/0/0 | 现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核 |
| [Scanning mirror.zmx](#lens-94) | 存在分析差异 | 5/1/34/22/4 | 0/0/3/0 | 几何包围能量×2、一阶参数×3、Huygens MTF×2、Huygens PSF×3、Huygens PSF 截面×3、Huygens 离焦 MTF×3、FFT MTF×1、光程差×6、光线扇形图×4、点列图×4、波前×3；原生采集适配器失败×3；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制 |
| [Zoom lens.zmx](#lens-95) | 存在分析差异 | 69/1/10/7/3 | 9/0/0/0 | 衍射包围能量×1、光程差×9；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因 |

## 每个镜头的详细结果

<a id="lens-1"></a>

### 1. Afocal/Afocal Riflescope.ZMX

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/30/0；光线：3/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 30：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-2"></a>

### 2. Afocal/Beam Expander.ZMX

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/22/0；光线：1/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 22：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-3"></a>

### 3. Afocal/Binocular_System.ZMX

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**存在计算/采集错误**；分析通过/接近/差异/不可比/错误：**0/0/0/60/0**；Core/原生配置：2/2。

<a id="lens-4"></a>

### 4. Birefringent prisms/Birefringent cube.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**导入/准备失败**；分析通过/接近/差异/不可比/错误：**0/0/0/0/0**；Core/原生配置：0/2。

<a id="lens-5"></a>

### 5. Birefringent prisms/Rochon prism.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**导入/准备失败**；分析通过/接近/差异/不可比/错误：**0/0/0/0/0**；Core/原生配置：0/2。

<a id="lens-6"></a>

### 6. Birefringent prisms/Senarmont prism.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**导入/准备失败**；分析通过/接近/差异/不可比/错误：**0/0/0/0/0**；Core/原生配置：0/2。

<a id="lens-7"></a>

### 7. Birefringent prisms/Wollaston prism.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**导入/准备失败**；分析通过/接近/差异/不可比/错误：**0/0/0/0/0**；Core/原生配置：0/2。

<a id="lens-8"></a>

### 8. Diffractive components/Achromatic singlet.zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-9"></a>

### 9. Diffractive components/Dual Zone CD-DVD lens.ZMX

结论：**存在计算/采集错误**。Core/原生配置：2/2。分析：0/0/0/0/44；光线：0/0/0/2。

模型面型/能力未支持×44。

未比较/错误原因（保留原始记录）：

- Error × 44：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-10"></a>

### 10. Diffractive components/Grating.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/38；光线：0/0/0/5。

模型面型/能力未支持×38。

未比较/错误原因（保留原始记录）：

- Error × 38：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-11"></a>

### 11. Diffractive components/Reflection hologram.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-12"></a>

### 12. Diffractive components/Sample Spectrometer.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/30；光线：0/0/0/3。

模型面型/能力未支持×30。

未比较/错误原因（保留原始记录）：

- Error × 29：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

ZOS 格式补充：**存在计算/采集错误**；分析通过/接近/差异/不可比/错误：**0/0/0/0/30**；Core/原生配置：1/1。

<a id="lens-13"></a>

### 13. Diffractive components/Transmission hologram.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-14"></a>

### 14. GRINs/Grid GRIN.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/30；光线：0/0/0/3。

模型面型/能力未支持×30。

未比较/错误原因（保留原始记录）：

- Error × 29：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-15"></a>

### 15. GRINs/Selfoc lens.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/30；光线：0/0/0/3。

模型面型/能力未支持×30。

未比较/错误原因（保留原始记录）：

- Error × 30：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-16"></a>

### 16. Image Simulation/Example 1, A singlet eyepiece.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：13/0/4/11/2；光线：3/0/0/0。

场曲与畸变×1、FFT MTF×1、光程差×1、波前×1；软件计算失败×2；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光线扇形图×1、RMS 随视场×1、单光线×1、点列图×4。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 光程差 | 1 | 2 | 2.540836409992852 | 10.07796319502783 |
| 差异 | 1 | primary | 波前 | 1 | 2 | 2.8934613407860295 | 10.077963195027907 |
| 差异 | 1 | primary | FFT MTF | 1 | 2 | 0.06534142573005332 | 0.8013814514981193 |
| 差异 | 1 | primary | 场曲与畸变 | 1 | 2 | 0.040385952917489434 | 53395.71314325015 |

未比较/错误原因（保留原始记录）：

- Error × 2：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 1：
- Incomparable × 6：A curve requires at least two finite coordinate/value pairs
- Incomparable × 3：Incomplete or unordered real-ray surface table
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-17"></a>

### 17. Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/30；光线：0/0/0/3。

模型面型/能力未支持×30。

未比较/错误原因（保留原始记录）：

- Error × 30：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-18"></a>

### 18. Image Simulation/Example 3, A blue notch filter.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/38；光线：0/0/0/5。

模型面型/能力未支持×38。

未比较/错误原因（保留原始记录）：

- Error × 38：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-19"></a>

### 19. Image Simulation/Example 4, a diffraction limited system.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/22/0；光线：0/0/0/1。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 22：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-20"></a>

### 20. Image Simulation/Example 5, spatially varying resolution.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/30；光线：0/0/0/3。

模型面型/能力未支持×30。

未比较/错误原因（保留原始记录）：

- Error × 30：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-21"></a>

### 21. Image Simulation/Example 6, tilted image plane.ZMX

结论：**存在计算/采集错误**。Core/原生配置：2/2。分析：0/0/0/0/60；光线：0/0/0/6。

模型面型/能力未支持×60。

未比较/错误原因（保留原始记录）：

- Error × 60：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-22"></a>

### 22. Interconnects/2 Ball lens interconnect.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：16/0/3/3/0；光线：1/0/0/0。

FFT PSF 截面×1、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：衍射包围能量×1、几何包围能量×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、PSF×1、光线扇形图×2、单光线×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 点列图 | 1 | 1 | 0.15418663142336866 | 0.44835419684108224 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.012291013558206363 | 0.06874154383105924 |
| 差异 | 1 | edge | 点列图 | 1 | 1 | 0.15418663142336866 | 0.44835419684108224 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-23"></a>

### 23. Interconnects/Ball coupling.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：13/1/3/5/0；光线：1/0/0/0。

FFT PSF 截面×1、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×2、光线扇形图×2、单光线×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 点列图 | 1 | 1 | 0.13514204651234127 | 8.285381749614501 |
| 接近 | 1 | primary | FFT MTF | 1 | 1 | 0.011882967374133522 | 0.028206541655755757 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.01843733464109438 | 0.007099386503291005 |
| 差异 | 1 | edge | 点列图 | 1 | 1 | 0.13514204651234127 | 8.285381749614501 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-24"></a>

### 24. Interconnects/Conic interconnect.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：17/1/1/3/0；光线：1/0/0/0。

FFT PSF 截面×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、PSF×1、光线扇形图×2、单光线×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.012215668728299083 | 0.07626955245732536 |
| 接近 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.0075174749101230755 | 0.012842842912133334 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-25"></a>

### 25. Interconnects/GRIN interconnect.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-26"></a>

### 26. Interconnects/Optical interconnect.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-27"></a>

### 27. Manufacturing/Apochromatic_for_Cost_3_TDE_defaults.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/70/0；光线：13/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 70：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-28"></a>

### 28. Miscellaneous/Apochromat_prism.zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.NotSupportedException: Zemax coordinate-break order flags are not supported (SURF 4, PARM 6).。

<a id="lens-29"></a>

### 29. Miscellaneous/Axicon model.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：5/0/1/3/13；光线：1/0/0/0。

几何包围能量×1；主光线未到达像面×7；软件计算失败×6；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：一阶参数×1、单光线×2、点列图×2。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 几何包围能量 | 0 | 1 | 0.9961317549209602 | 0.001346997689479281 |

未比较/错误原因（保留原始记录）：

- Error × 12：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-30"></a>

### 30. Miscellaneous/Beam splitter.zmx

结论：**存在计算/采集错误**。Core/原生配置：2/2。分析：0/0/0/0/44；光线：0/0/0/2。

模型面型/能力未支持×44。

未比较/错误原因（保留原始记录）：

- Error × 42：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 2：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-31"></a>

### 31. Miscellaneous/Bi-ocular lens.ZMX

结论：**存在分析差异**。Core/原生配置：2/2。分析：28/4/18/10/0；光线：0/0/6/0。

几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、光程差×4、光线扇形图×4、RMS 随视场×1、点列图×3、波前×2；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：衍射包围能量×1、几何包围能量×1、场曲与畸变×2、一阶参数×2、Huygens MTF×2、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×2、光程差×4、光线扇形图×4、相对照度×2、RMS 随视场×1、点列图×5。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 波前 | 1 | 2 | 0.026586380008021097 | 0.039381187890115354 |
| 接近 | 1 | primary | FFT MTF | 1 | 2 | 0.019010462076993098 | 0.03127017256174308 |
| 接近 | 1 | primary | FFT PSF 截面 | 1 | 2 | 0.004781705689614372 | 0.010550508393078417 |
| 差异 | 2 | primary | 光线扇形图 | 1 | 2 | 0.3126753499141053 | 2.4031323632378587 |
| 差异 | 2 | primary | 光程差 | 1 | 2 | 0.18774886438098276 | 0.03439102115939998 |
| 差异 | 2 | primary | 波前 | 1 | 2 | 0.04705095201343386 | 0.07862684496292066 |
| 差异 | 2 | primary | Huygens PSF | 1 | 2 | 0.05479548023168401 | 0.04360314767779694 |
| 接近 | 2 | primary | FFT MTF | 1 | 2 | 0.0190104620891002 | 0.03127017258368198 |
| 差异 | 2 | primary | FFT PSF 截面 | 1 | 2 | 0.02104423489581364 | 0.057447517915504676 |
| 差异 | 2 | primary | Huygens PSF 截面 | 1 | 2 | 0.05796749873026547 | 0.04360357596116432 |
| 差异 | 2 | primary | 几何包围能量 | 0 | 2 | 3029.0773869415702 | 0.15809208146482734 |
| 接近 | 2 | primary | 衍射包围能量 | 1 | 2 | 0.0046169652800316895 | 0.001646069570451686 |
| 差异 | 2 | primary | RMS 随视场 | 1 | 2 | 0.24889314767639772 | 152.27523582051964 |
| 差异 | 2 | edge | 点列图 | 4 | 1 | 0.9315672859702406 | 590.7431458741371 |
| 差异 | 2 | edge | 光线扇形图 | 4 | 1 | 0.3959801215228313 | 666.4763586365883 |
| 差异 | 2 | edge | 光程差 | 4 | 1 | 0.4810889604747776 | 7.617291633595645 |
| 差异 | 2 | edge | 点列图 | 4 | 2 | 0.9322575618461132 | 557.9828062404581 |
| 差异 | 2 | edge | 光线扇形图 | 4 | 2 | 0.3888288284700151 | 627.9697506902462 |
| 差异 | 2 | edge | 光程差 | 4 | 2 | 0.4638418590546978 | 6.068033197129692 |
| 差异 | 2 | edge | 点列图 | 4 | 3 | 0.9360859765221465 | 554.2670395392901 |
| 差异 | 2 | edge | 光线扇形图 | 4 | 3 | 0.3847655724442381 | 618.8883366179496 |
| 差异 | 2 | edge | 光程差 | 4 | 3 | 0.4564962962003088 | 5.339434392141367 |

未比较/错误原因（保留原始记录）：

- Incomparable × 8：Incomplete or unordered real-ray surface table
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]

<a id="lens-32"></a>

### 32. Miscellaneous/Cylinder.zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-33"></a>

### 33. Miscellaneous/Diode source simulation.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 21：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-34"></a>

### 34. Miscellaneous/Doubly telecentric system.zmx

结论：**部分接近，仍需核对**。Core/原生配置：1/1。分析：20/1/0/1/0；光线：1/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×2、PSF×1、光线扇形图×2、相对照度×1、RMS 随视场×1、单光线×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 接近 | 1 | primary | FFT MTF | 1 | 1 | 0.012291776422362186 | 0.03117406428845826 |

未比较/错误原因（保留原始记录）：

- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

ZOS 格式补充：**部分接近，仍需核对**；分析通过/接近/差异/不可比/错误：**20/1/0/1/0**；Core/原生配置：1/1。

<a id="lens-35"></a>

### 35. Miscellaneous/Elliptical reflector 320 degree light cone .zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.NotSupportedException: Zemax object-cone-angle apertures (OBNA subtype 1) are not supported.。

<a id="lens-36"></a>

### 36. Miscellaneous/Grid Sag Example.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-37"></a>

### 37. Miscellaneous/Grid sag surface.zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-38"></a>

### 38. Miscellaneous/Irregular surface.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 21：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-39"></a>

### 39. Miscellaneous/Mangin mirror.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：16/0/3/3/0；光线：0/1/0/0。

一阶参数×1、单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、PSF×1、光线扇形图×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 2.0000000000000004 | 200.0000001383908 |
| 差异 | 1 | primary | 单光线 | 1 | 1 | 1.5442351720294532 | 197.45572249205736 |
| 差异 | 1 | edge | 单光线 | 1 | 1 | 1.5442351720294532 | 197.45572249205736 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-40"></a>

### 40. Miscellaneous/MEMS DLL Sample.ZMX

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

ZOS 格式补充：**存在计算/采集错误**；分析通过/接近/差异/不可比/错误：**0/0/0/0/22**；Core/原生配置：1/1。

<a id="lens-41"></a>

### 41. Miscellaneous/Optimization demo 1.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：8/3/4/5/2；光线：0/1/0/0。

几何包围能量×1、一阶参数×1、单光线×2；原生采集适配器失败×1；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：Huygens MTF×1、光程差×2、光线扇形图×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1 | 10000000000 |
| 差异 | 1 | primary | 单光线 | 1 | 1 | 2 | 400 |
| 接近 | 1 | primary | Huygens PSF | 1 | 1 | 0.01627551739231368 | 0.0407992030517369 |
| 接近 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.009269562504054106 | 0.02061206099002777 |
| 差异 | 1 | primary | 几何包围能量 | 0 | 1 | 0.08825170135174928 | 0.0012547051442910915 |
| 接近 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.004481326698505912 | 0.002349613166538922 |
| 差异 | 1 | edge | 单光线 | 1 | 1 | 2 | 400 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-42"></a>

### 42. Miscellaneous/Optimization demo 2.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：26/0/1/2/1；光线：3/0/0/0。

FFT MTF×1；原生采集适配器失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×4、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | FFT MTF | 1 | 2 | 0.03825249267704539 | 0.10414283763837412 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-43"></a>

### 43. Miscellaneous/Parabolas at 90 degrees.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：0/0/1/7/14；光线：0/0/1/0。

一阶参数×1；主光线未到达像面×11；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1.6666666666666687 | 20.833333333333343 |

未比较/错误原因（保留原始记录）：

- Error × 14：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 2：Incomplete or unordered real-ray surface table
- Incomparable × 1：Native RMS field extent differs from the selected scan direction
- Incomparable × 1：Unavailable: No energy data
- Incomparable × 1：Unavailable: No transmitted pupil area

<a id="lens-44"></a>

### 44. Miscellaneous/Telecentric system.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-45"></a>

### 45. Miscellaneous/Toroid.zmx

结论：**导入/准备失败**。Core/原生配置：0/1。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-46"></a>

### 46. Miscellaneous/vacuum cell.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：17/0/4/1/0；光线：0/1/0/0。

衍射包围能量×1、一阶参数×1、单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、光线扇形图×2、相对照度×1、RMS 随视场×1、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 0.0004342327617403443 | 0.0351945758948915 |
| 差异 | 1 | primary | 单光线 | 1 | 1 | 0.00034763282620120273 | 0.03929427548340669 |
| 差异 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.016254345712348624 | 0.011951125038377974 |
| 差异 | 1 | edge | 单光线 | 2 | 1 | 0.00022303993199577652 | 0.04043689646982784 |

未比较/错误原因（保留原始记录）：

- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]

<a id="lens-47"></a>

### 47. Objectives/Apochromat.zmx

结论：**已比较项通过，存在未覆盖项**。Core/原生配置：1/1。分析：46/0/0/4/0；光线：8/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×9、光线扇形图×9、单光线×9、点列图×9、波前×1。

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-48"></a>

### 48. Objectives/Apochromat2.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：33/0/1/4/0；光线：5/0/0/0。

衍射包围能量×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×6、光线扇形图×6、单光线×6、点列图×6、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 衍射包围能量 | 1 | 3 | 0.011433558634052285 | 0.013207881802895782 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-49"></a>

### 49. Objectives/Apochromat3.zmx

结论：**已比较项通过，存在未覆盖项**。Core/原生配置：1/1。分析：26/0/0/4/0；光线：3/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、光线扇形图×4、单光线×4、点列图×4、波前×1。

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-50"></a>

### 50. Objectives/Apochromat4.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/70/0；光线：13/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 70：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-51"></a>

### 51. Objectives/Cooke 40 degree field.zmx

结论：**本次请求全部通过**。Core/原生配置：1/1。分析：30/0/0/0/0；光线：3/0/0/0。

本次已比较项未见超差；范围限于本次设置。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、PSF×1、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

<a id="lens-52"></a>

### 52. Objectives/Double Gauss 28 degree field.zmx

结论：**已比较项通过，存在未覆盖项**。Core/原生配置：1/1。分析：29/0/0/1/0；光线：3/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、PSF×1、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

未比较/错误原因（保留原始记录）：

- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

ZOS 格式补充：**存在计算/采集错误**；分析通过/接近/差异/不可比/错误：**26/0/0/3/1**；Core/原生配置：1/1。

<a id="lens-53"></a>

### 53. Objectives/Double Gauss 5 degree field.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/38/0；光线：5/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 38：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-54"></a>

### 54. Objectives/Doublet using MIL number glasses.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/38；光线：0/0/0/5。

模型面型/能力未支持×38。

未比较/错误原因（保留原始记录）：

- Error × 38：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-55"></a>

### 55. Objectives/Doublet.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：25/0/0/3/2；光线：3/0/0/0。

软件计算失败×2；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、PSF×1、光线扇形图×4、单光线×3、点列图×3、波前×1。

未比较/错误原因（保留原始记录）：

- Error × 2：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-56"></a>

### 56. Objectives/Even Asphere.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：19/0/1/2/0；光线：1/0/0/0。

FFT MTF×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×2、光线扇形图×2、相对照度×1、RMS 随视场×1、单光线×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.031776294953050496 | 0.07288468354695565 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-57"></a>

### 57. Objectives/Gaussian Quadrature over Annular Pupils.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：16/0/2/4/0；光线：0/1/0/0。

单光线×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、光线扇形图×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 单光线 | 1 | 1 | 0.8164965809766014 | 523.0508291402898 |
| 差异 | 1 | edge | 单光线 | 1 | 1 | 0.8164965809766014 | 523.0508291402898 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-58"></a>

### 58. Objectives/Gaussian Quadrature Over Wavelength.ZMX

结论：**已比较项通过，存在未覆盖项**。Core/原生配置：1/1。分析：29/0/0/1/0；光线：3/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、PSF×1、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

未比较/错误原因（保留原始记录）：

- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-59"></a>

### 59. Objectives/Inverse telephoto lens.zmx

结论：**本次请求全部通过**。Core/原生配置：1/1。分析：30/0/0/0/0；光线：3/0/0/0。

本次已比较项未见超差；范围限于本次设置。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、PSF×1、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

<a id="lens-60"></a>

### 60. Objectives/Petzval.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：26/2/0/1/1；光线：3/0/0/0。

软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×4、PSF×1、光线扇形图×4、单光线×4、点列图×4、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 接近 | 1 | primary | FFT MTF | 1 | 2 | 0.011984531441633776 | 0.022689494073294264 |
| 接近 | 1 | primary | RMS 随视场 | 1 | 2 | 0.008790004758462085 | 0.25251506061864504 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-61"></a>

### 61. Objectives/Relay lens.zmx

结论：**已比较项通过，存在未覆盖项**。Core/原生配置：1/1。分析：29/0/0/1/0；光线：3/0/0/0。

部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、光线扇形图×4、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

未比较/错误原因（保留原始记录）：

- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]

<a id="lens-62"></a>

### 62. Objectives/Tessar lens using vignetting factors.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：27/0/0/2/1；光线：3/0/0/0。

软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、光线扇形图×4、RMS 随视场×1、单光线×4、点列图×4、波前×1。

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-63"></a>

### 63. Objectives/Wide angle lens 100 degree field.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/30/0；光线：3/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 30：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

ZOS 格式补充：**分析不可比，精度未认证**；分析通过/接近/差异/不可比/错误：**0/0/0/30/0**；Core/原生配置：1/1。

<a id="lens-64"></a>

### 64. Objectives/Wide angle lens 200 degree field.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/30/0；光线：3/0/0/0。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 30：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-65"></a>

### 65. Objectives/Wide angle lens 210 degree field.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：24/0/2/4/0；光线：3/0/0/0。

光程差×2；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×1、FFT PSF 截面×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×1、PSF×1、光线扇形图×3、相对照度×1、RMS 随视场×1、单光线×4、点列图×4、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | edge | 光程差 | 4 | 1 | 620.5699174523581 | 9874.198243064957 |
| 差异 | 1 | edge | 光程差 | 4 | 2 | 2019.9537873829756 | 8177.999789411592 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]
- Incomparable × 1：Tangential/sagittal curvature in mm and distortion in percent, under one explicit wavelength and field scan.

<a id="lens-66"></a>

### 66. Optical testing/Double-pass null test.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：1/1/2/6/12；光线：0/1/0/0。

FFT PSF 截面×1、一阶参数×1；主光线未到达像面×7；软件计算失败×5；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：PSF×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1.9966921332812348 | 308.30809021847926 |
| 接近 | 1 | primary | FFT MTF | 1 | 1 | 0.01912107133780898 | 0.027238523908831414 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.03655933740974274 | 0.029296264639936953 |

未比较/错误原因（保留原始记录）：

- Error × 12：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 2：Incomplete or unordered real-ray surface table
- Incomparable × 1：Native RMS field extent differs from the selected scan direction
- Incomparable × 1：Unavailable: No energy data

<a id="lens-67"></a>

### 67. Optical testing/Mach Zender Interferometer.zmx

结论：**导入/准备失败**。Core/原生配置：0/2。分析：0/0/0/0/0；光线：0/0/0/0。

System.IO.InvalidDataException: Zemax PARM indices are one-based positive integers.。

<a id="lens-68"></a>

### 68. Optical testing/Offner reflecting compensator.zmx

结论：**存在光线差异，待核坐标约定**。Core/原生配置：1/1。分析：0/0/0/22/0；光线：0/1/0/0。

现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核。

未比较/错误原因（保留原始记录）：

- Incomparable × 22：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-69"></a>

### 69. Optical testing/Offner refracting compensator.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：15/1/3/2/1；光线：0/1/0/0。

一阶参数×1、单光线×2；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：几何包围能量×1、FFT PSF 截面×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、PSF×1、光线扇形图×2、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 2.0000000000000013 | 323.1973351665643 |
| 差异 | 1 | primary | 单光线 | 1 | 1 | 1.5437250477125841 | 7199.421065994465 |
| 接近 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.006948665365407137 | 0.01322704194386648 |
| 差异 | 1 | edge | 单光线 | 1 | 1 | 1.5437250477125841 | 7199.421065994465 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 1：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-70"></a>

### 70. Optical testing/Twyman Green Interferometer.zmx

结论：**存在计算/采集错误**。Core/原生配置：2/2。分析：0/0/0/0/44；光线：0/0/0/2。

模型面型/能力未支持×44。

未比较/错误原因（保留原始记录）：

- Error × 42：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 2：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-71"></a>

### 71. Polarization/Etalon.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：13/0/6/4/3；光线：2/0/0/0。

衍射包围能量×1、几何包围能量×1、一阶参数×1、FFT MTF×1、光程差×2；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：FFT PSF 截面×1、光程差×1、光线扇形图×3、RMS 随视场×1、单光线×3、点列图×3、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1 | 10000000000 |
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.050515890176211345 | 0.7353465953131666 |
| 差异 | 1 | primary | 几何包围能量 | 0 | 1 | 0.08825170135174928 | 0.0012547051442910915 |
| 差异 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.045802089597414636 | 0.00052455151933457 |
| 差异 | 1 | edge | 光程差 | 2 | 1 | 0.5973690656044373 | 0.0009121537694981691 |
| 差异 | 1 | edge | 光程差 | 2 | 2 | 0.5973690656044373 | 0.0009119713569855207 |

未比较/错误原因（保留原始记录）：

- Error × 2：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 3：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：No valid grid overlap

<a id="lens-72"></a>

### 72. Polarization/Fabry-Perot.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 21：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-73"></a>

### 73. Telescopes/Cassegrain-type Ritchey Chretien.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：2/1/10/9/0；光线：0/1/0/0。

衍射包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：一阶参数×1、Huygens MTF×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 单光线 | 1 | 1 | 0.8164965809766014 | 523.0508291402898 |
| 差异 | 1 | primary | 点列图 | 1 | 1 | 0.03135512788811761 | 0.004164625483758205 |
| 差异 | 1 | primary | Huygens PSF | 1 | 1 | 0.04170401219551645 | 0.06079484914252481 |
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.13034497555496877 | 0.19499066333198856 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.013018505498212159 | 0.05309984106912291 |
| 差异 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.0285113408749585 | 0.05388522366552262 |
| 接近 | 1 | primary | 几何包围能量 | 0 | 1 | 0.007946797392351247 | 0.06842875568073459 |
| 差异 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.07684466398166628 | 0.08915711148845706 |
| 差异 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.020819363012330425 | 0.012859601299248569 |
| 差异 | 1 | edge | 单光线 | 1 | 1 | 0.8164965809766014 | 523.0508291402898 |
| 差异 | 1 | edge | 点列图 | 1 | 1 | 0.03135512788811761 | 0.004164625483758205 |

未比较/错误原因（保留原始记录）：

- Incomparable × 1：
- Incomparable × 6：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native RMS field extent differs from the selected scan direction

<a id="lens-74"></a>

### 74. Telescopes/Flat-field Schmidt.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：15/2/5/8/0；光线：0/3/0/0。

一阶参数×1、单光线×4；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、Huygens MTF×1、Huygens PSF×1、FFT MTF×1、PSF×1、相对照度×1、RMS 随视场×1、点列图×4、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 2 | 2 | 200.0000007058987 |
| 差异 | 1 | primary | 单光线 | 1 | 2 | 1.4554844673297513 | 196.2864909841448 |
| 接近 | 1 | primary | Huygens PSF 截面 | 1 | 2 | 0.003981907821929008 | 0.002935575318885067 |
| 接近 | 1 | primary | Huygens 离焦 MTF | 1 | 2 | 0.0059220250128694096 | 0.011152855045855836 |
| 差异 | 1 | edge | 单光线 | 3 | 1 | 1.4492694616930626 | 196.27530368835525 |
| 差异 | 1 | edge | 单光线 | 3 | 2 | 1.4492725359902794 | 196.27534386390704 |
| 差异 | 1 | edge | 单光线 | 3 | 3 | 1.449273951026083 | 196.2753626142362 |

未比较/错误原因（保留原始记录）：

- Incomparable × 8：A curve requires at least two finite coordinate/value pairs

<a id="lens-75"></a>

### 75. Telescopes/Houghton.zmx

结论：**存在光线差异，待核坐标约定**。Core/原生配置：1/1。分析：0/0/0/34/0；光线：0/4/0/0。

现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核。

未比较/错误原因（保留原始记录）：

- Incomparable × 34：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-76"></a>

### 76. Telescopes/Hubble.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/22/0；光线：0/0/0/1。

现有分析适配器限制毫米有焦像空间。

未比较/错误原因（保留原始记录）：

- Incomparable × 22：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-77"></a>

### 77. Telescopes/Maksutov offset secondary.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：4/1/10/7/0；光线：0/1/0/0。

几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：场曲与畸变×1、一阶参数×1、Huygens MTF×1、RMS 随视场×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 单光线 | 1 | 1 | 0.8164965810565772 | 1182.1067872170483 |
| 差异 | 1 | primary | 点列图 | 1 | 1 | 0.4377890122243428 | 0.5737301785511408 |
| 差异 | 1 | primary | Huygens PSF | 1 | 1 | 0.08425232099231872 | 0.13569625577867123 |
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.27924677125952413 | 0.37112427931568065 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.028015954377808062 | 0.11856664697953928 |
| 差异 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.05428880878714537 | 0.11168698836593938 |
| 差异 | 1 | primary | 几何包围能量 | 0 | 1 | 0.1788817600107943 | 0.30952610751857934 |
| 差异 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.044473217129121265 | 0.02752493890139396 |
| 接近 | 1 | primary | 相对照度 | 1 | 1 | 0.009084278598598363 | 0.014205846689157475 |
| 差异 | 1 | edge | 单光线 | 3 | 1 | 0.8164965810698295 | 1181.8611516992432 |
| 差异 | 1 | edge | 点列图 | 3 | 1 | 0.032762957834363705 | 0.13327495204694628 |

未比较/错误原因（保留原始记录）：

- Incomparable × 1：
- Incomparable × 4：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-78"></a>

### 78. Telescopes/Maksutov.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：4/0/11/7/0；光线：0/1/0/0。

几何包围能量×1、FFT PSF 截面×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、相对照度×1、单光线×2、点列图×2；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：场曲与畸变×1、一阶参数×1、Huygens MTF×1、RMS 随视场×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 单光线 | 1 | 1 | 0.6666666666364217 | 31.8023892944984 |
| 差异 | 1 | primary | 点列图 | 1 | 1 | 1.5396735290747365 | 3.6229852537435927 |
| 差异 | 1 | primary | Huygens PSF | 1 | 1 | 0.06446902314206289 | 0.08208475602709212 |
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.22786880045649707 | 0.3420828906570367 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 1 | 0.016374318727299737 | 0.06813430227390581 |
| 差异 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.04868544294868377 | 0.07995284597804467 |
| 差异 | 1 | primary | 几何包围能量 | 0 | 1 | 0.3192260096172696 | 0.19974149376912795 |
| 差异 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.05453810396603807 | 0.039120463890265245 |
| 差异 | 1 | primary | 相对照度 | 1 | 1 | 0.016258431048613665 | 0.026959964472298203 |
| 差异 | 1 | edge | 单光线 | 2 | 1 | 0.6658545266991196 | 31.720952080074966 |
| 差异 | 1 | edge | 点列图 | 2 | 1 | 0.7420043855821491 | 6.077187133581624 |

未比较/错误原因（保留原始记录）：

- Incomparable × 1：
- Incomparable × 4：A curve requires at least two finite coordinate/value pairs
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-79"></a>

### 79. Telescopes/Schalcktelescope 10.ZMX

结论：**存在分析差异**。Core/原生配置：1/1。分析：22/1/5/1/1；光线：0/3/0/0。

一阶参数×1、单光线×4；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；逐面光线有差异；反射方向符号需另核。

通过项目：几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×4、光线扇形图×4、RMS 随视场×1、点列图×4、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 2 | 1.999999999999999 | 3443.1771718380037 |
| 差异 | 1 | primary | 单光线 | 1 | 2 | 1.8935543179556051 | 2438.7226596043224 |
| 接近 | 1 | primary | 衍射包围能量 | 1 | 2 | 0.008716592497421814 | 0.013178993820832319 |
| 差异 | 1 | edge | 单光线 | 3 | 1 | 1.8884667730528786 | 2430.907502649399 |
| 差异 | 1 | edge | 单光线 | 3 | 2 | 1.888608975358034 | 2430.907502649399 |
| 差异 | 1 | edge | 单光线 | 3 | 3 | 1.8886701328049103 | 2430.907502649399 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 1：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]

ZOS 格式补充：**存在分析差异**；分析通过/接近/差异/不可比/错误：**22/1/5/1/1**；Core/原生配置：1/1。
补充差异：一阶参数×1、单光线×4。

<a id="lens-80"></a>

### 80. Telescopes/Schmidt-Cassegrain spider obscuration.zmx

结论：**分析不可比，精度未认证**。Core/原生配置：1/1。分析：0/0/0/38/0；光线：0/0/5/0。

现有分析适配器限制毫米有焦像空间；部分逐面光线受坐标断点折叠或能力边界限制。

未比较/错误原因（保留原始记录）：

- Incomparable × 38：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-81"></a>

### 81. Telescopes/Schmidt-Cassegrain.zmx

结论：**存在光线差异，待核坐标约定**。Core/原生配置：1/1。分析：0/0/0/30/0；光线：0/3/0/0。

现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核。

未比较/错误原因（保留原始记录）：

- Incomparable × 30：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

<a id="lens-82"></a>

### 82. Telescopes/Unobscured Gregorian.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：1/0/1/6/14；光线：0/0/1/0。

RMS 随视场×1；主光线未到达像面×10；软件计算失败×4；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：一阶参数×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | RMS 随视场 | 1 | 1 | 113.09102837149068 | 13368.982342990175 |

未比较/错误原因（保留原始记录）：

- Error × 14：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：A curve requires at least two finite coordinate/value pairs
- Incomparable × 2：Incomplete or unordered real-ray surface table
- Incomparable × 1：Unavailable: No energy data
- Incomparable × 1：Unavailable: No transmitted pupil area

<a id="lens-83"></a>

### 83. Telescopes/Yolo.zmx

结论：**导入/准备失败**。Core/原生配置：1/1。分析：0/0/0/0/1；光线：0/0/0/0。

System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[0].surface: surface reference 9 does not exist.；导入/准备失败×1。

未比较/错误原因（保留原始记录）：

- Error × 1：Core import/preparation failed before request materialization: System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[0].surface: surface reference 9 does not exist.

<a id="lens-84"></a>

### 84. Tilted systems & prisms/Beamsplitter cube.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 21：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-85"></a>

### 85. Tilted systems & prisms/Lens pivoted about a point.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：20/2/1/6/1；光线：0/0/3/0。

FFT PSF 截面×1；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：几何包围能量×1、场曲与畸变×1、一阶参数×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、光程差×4、光线扇形图×4、RMS 随视场×1、点列图×4。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 接近 | 1 | primary | 波前 | 1 | 2 | 0.006913283673642438 | 0.15677020194436597 |
| 接近 | 1 | primary | FFT MTF | 1 | 2 | 0.02157713760897347 | 0.06326100389552995 |
| 差异 | 1 | primary | FFT PSF 截面 | 1 | 2 | 0.14218426006114676 | 0.00796241215788235 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 4：Incomplete or unordered real-ray surface table
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-86"></a>

### 86. Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx

结论：**导入/准备失败**。Core/原生配置：1/1。分析：0/0/0/0/1；光线：0/0/0/0。

System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[1].surface: surface reference 4 does not exist.；导入/准备失败×1。

未比较/错误原因（保留原始记录）：

- Error × 1：Core import/preparation failed before request materialization: System.IO.InvalidDataException: Invalid optic snapshot at $.meritOperands[1].surface: surface reference 4 does not exist.

<a id="lens-87"></a>

### 87. Tilted systems & prisms/Offner relay.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：19/0/1/2/0；光线：0/0/1/0。

一阶参数×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：衍射包围能量×1、几何包围能量×1、FFT PSF 截面×1、场曲与畸变×1、Huygens MTF×1、Huygens PSF×1、Huygens PSF 截面×1、Huygens 离焦 MTF×1、FFT MTF×1、光程差×2、PSF×1、光线扇形图×2、相对照度×1、RMS 随视场×1、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1.9999999999999987 | 110.54477832825427 |

未比较/错误原因（保留原始记录）：

- Incomparable × 2：Incomplete or unordered real-ray surface table

ZOS 格式补充：**存在分析差异**；分析通过/接近/差异/不可比/错误：**19/0/1/2/0**；Core/原生配置：1/1。
补充差异：一阶参数×1。

<a id="lens-88"></a>

### 88. Tilted systems & prisms/Prism using tilted surface.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/38；光线：0/0/0/5。

模型面型/能力未支持×38。

未比较/错误原因（保留原始记录）：

- Error × 38：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-89"></a>

### 89. Tilted systems & prisms/Prism using total internal reflection.zmx

结论：**存在分析差异**。Core/原生配置：1/1。分析：12/3/2/4/1；光线：0/0/1/0。

一阶参数×1、Huygens 离焦 MTF×1；原生采集适配器失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：几何包围能量×1、场曲与畸变×1、Huygens MTF×1、Huygens PSF×1、光程差×1、光线扇形图×2、相对照度×1、RMS 随视场×1、点列图×2、波前×1。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 1.9999999999999993 | 318.19865443585815 |
| 接近 | 1 | primary | FFT MTF | 1 | 1 | 0.027065377706509422 | 0.08050274317970779 |
| 接近 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.00404472671783824 | 0.00011658073586412205 |
| 差异 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.017658248204889093 | 0.022706857567597827 |
| 接近 | 1 | edge | 光程差 | 3 | 1 | 0.009641775026606314 | 0.20198608515865502 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 2：Incomplete or unordered real-ray surface table
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

<a id="lens-90"></a>

### 90. Tilted systems & prisms/Prisms using coordinate breaks.ZMX

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-91"></a>

### 91. Tilted systems & prisms/Tilted mirror.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-92"></a>

### 92. Tilted systems & prisms/Tilted object.zmx

结论：**存在计算/采集错误**。Core/原生配置：1/1。分析：0/0/0/0/22；光线：0/0/0/1。

模型面型/能力未支持×22。

未比较/错误原因（保留原始记录）：

- Error × 22：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。

<a id="lens-93"></a>

### 93. Zoom systems/Mirror zoom.zmx

结论：**存在光线差异，待核坐标约定**。Core/原生配置：5/5。分析：0/0/0/110/0；光线：0/5/0/0。

现有分析适配器限制毫米有焦像空间；逐面光线有差异；反射方向符号需另核。

未比较/错误原因（保留原始记录）：

- Incomparable × 110：Existing analysis adapter requires MM focal image space. Unit/afocal lens retained in model and explicit-ray coverage; no unit substitution.

ZOS 格式补充：**存在光线差异，待核坐标约定**；分析通过/接近/差异/不可比/错误：**0/0/0/110/0**；Core/原生配置：5/5。

<a id="lens-94"></a>

### 94. Zoom systems/Scanning mirror.zmx

结论：**存在分析差异**。Core/原生配置：3/3。分析：5/1/34/22/4；光线：0/0/3/0。

几何包围能量×2、一阶参数×3、Huygens MTF×2、Huygens PSF×3、Huygens PSF 截面×3、Huygens 离焦 MTF×3、FFT MTF×1、光程差×6、光线扇形图×4、点列图×4、波前×3；原生采集适配器失败×3；软件计算失败×1；部分原生/软件输出缺少可比数据，见下方逐项原因；部分逐面光线受坐标断点折叠或能力边界限制。

通过项目：几何包围能量×1、光线扇形图×2、点列图×2。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 一阶参数 | 1 | 1 | 2.0000000000000004 | 302.5214896857243 |
| 差异 | 1 | primary | 光程差 | 1 | 1 | 0.06335865400256074 | 2.040278962192458 |
| 差异 | 1 | primary | 波前 | 1 | 1 | 0.06406910667417551 | 2.0402789621407766 |
| 差异 | 1 | primary | Huygens PSF | 1 | 1 | 0.03364184161465323 | 0.005047122427969113 |
| 差异 | 1 | primary | FFT MTF | 1 | 1 | 0.163549971938449 | 0.3455991881651357 |
| 接近 | 1 | primary | Huygens MTF | 1 | 1 | 0.01101057297724239 | 0.020378452133332337 |
| 差异 | 1 | primary | Huygens PSF 截面 | 1 | 1 | 0.030782498306618618 | 0.0026472065521160465 |
| 差异 | 1 | primary | Huygens 离焦 MTF | 1 | 1 | 0.04751915785550447 | 0.042758565553723704 |
| 差异 | 1 | edge | 光程差 | 1 | 1 | 0.06335865400256074 | 2.040278962192458 |
| 差异 | 2 | primary | 一阶参数 | 1 | 1 | 2.0000000000000004 | 302.5214896857243 |
| 差异 | 2 | primary | 点列图 | 1 | 1 | 0.24695184885734392 | 116.17570319763269 |
| 差异 | 2 | primary | 光线扇形图 | 1 | 1 | 0.18095782935421686 | 133.90601632234757 |
| 差异 | 2 | primary | 光程差 | 1 | 1 | 0.6929146380072724 | 9.966458337241331 |
| 差异 | 2 | primary | 波前 | 1 | 1 | 0.14288039323988175 | 9.994143669198001 |
| 差异 | 2 | primary | Huygens PSF | 1 | 1 | 0.3710089487273462 | 0.0020907758784821737 |
| 差异 | 2 | primary | Huygens MTF | 1 | 1 | 0.10731822736116473 | 0.19999459805234043 |
| 差异 | 2 | primary | Huygens PSF 截面 | 1 | 1 | 0.48557674619593305 | 0.0018894849361955504 |
| 差异 | 2 | primary | 几何包围能量 | 0 | 1 | 66.77355045313618 | 0.00138077910559808 |
| 差异 | 2 | primary | Huygens 离焦 MTF | 1 | 1 | 0.5643972007770257 | 0.355592299326691 |
| 差异 | 2 | edge | 点列图 | 1 | 1 | 0.24695184885734392 | 116.17570319763269 |
| 差异 | 2 | edge | 光线扇形图 | 1 | 1 | 0.18095782935421686 | 133.90601632234757 |
| 差异 | 2 | edge | 光程差 | 1 | 1 | 0.6929146380072724 | 9.966458337241331 |
| 差异 | 3 | primary | 一阶参数 | 1 | 1 | 2.0000000000000004 | 302.5214896857243 |
| 差异 | 3 | primary | 点列图 | 1 | 1 | 0.24695184885730373 | 116.17570319761808 |
| 差异 | 3 | primary | 光线扇形图 | 1 | 1 | 0.1809578293542188 | 133.9060163223582 |
| 差异 | 3 | primary | 光程差 | 1 | 1 | 0.6473809676334369 | 8.733534024517295 |
| 差异 | 3 | primary | 波前 | 1 | 1 | 0.13436415348791259 | 8.733534024465623 |
| 差异 | 3 | primary | Huygens PSF | 1 | 1 | 0.7942593564903024 | 0.005892016848149469 |
| 差异 | 3 | primary | Huygens MTF | 1 | 1 | 0.04551102356373301 | 0.0833758415578878 |
| 差异 | 3 | primary | Huygens PSF 截面 | 1 | 1 | 0.647274078240201 | 0.0015504414317706811 |
| 差异 | 3 | primary | 几何包围能量 | 0 | 1 | 66.77355045313618 | 0.00138077910559808 |
| 差异 | 3 | primary | Huygens 离焦 MTF | 1 | 1 | 0.1721549674775664 | 0.15183752570865716 |
| 差异 | 3 | edge | 点列图 | 1 | 1 | 0.24695184885730373 | 116.17570319761808 |
| 差异 | 3 | edge | 光线扇形图 | 1 | 1 | 0.1809578293542188 | 133.9060163223582 |
| 差异 | 3 | edge | 光程差 | 1 | 1 | 0.6473809676334369 | 8.733534024517295 |

未比较/错误原因（保留原始记录）：

- Error × 1：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Error × 3：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 5：A curve requires at least two finite coordinate/value pairs
- Incomparable × 6：Incomplete or unordered real-ray surface table
- Incomparable × 3：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 5：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]
- Incomparable × 3：Native RMS field extent differs from the selected scan direction

<a id="lens-95"></a>

### 95. Zoom systems/Zoom lens.zmx

结论：**存在分析差异**。Core/原生配置：3/3。分析：69/1/10/7/3；光线：9/0/0/0。

衍射包围能量×1、光程差×9；软件计算失败×3；部分原生/软件输出缺少可比数据，见下方逐项原因。

通过项目：几何包围能量×3、FFT PSF 截面×3、一阶参数×3、Huygens MTF×3、Huygens PSF×3、Huygens PSF 截面×3、Huygens 离焦 MTF×3、FFT MTF×2、光程差×3、PSF×1、光线扇形图×12、RMS 随视场×3、单光线×12、点列图×12、波前×3。

| 状态 | 配置 | 主/边缘控制 | 分析 | 视场 | 波长 | NRMSE | 最大绝对差（各自分析单位） |
|---|---:|---|---|---|---|---:|---:|
| 差异 | 1 | primary | 衍射包围能量 | 1 | 1 | 0.016703968825424294 | 0.020098533590749645 |
| 差异 | 1 | edge | 光程差 | 3 | 1 | 2172.6725646401687 | 967.9347208823494 |
| 差异 | 1 | edge | 光程差 | 3 | 2 | 1207.747212055622 | 1169.8922809750916 |
| 差异 | 1 | edge | 光程差 | 3 | 3 | 1208.9393747598472 | 866.5941268523151 |
| 差异 | 2 | edge | 光程差 | 3 | 1 | 212.16295705639865 | 790.6774873516036 |
| 差异 | 2 | edge | 光程差 | 3 | 2 | 214.06142230697878 | 955.6507835728924 |
| 差异 | 2 | edge | 光程差 | 3 | 3 | 253.86715216620544 | 707.895324102308 |
| 接近 | 3 | primary | FFT MTF | 1 | 1 | 0.011676348131984211 | 0.03052206741618471 |
| 差异 | 3 | edge | 光程差 | 3 | 1 | 103.52960754059919 | 630.966478801229 |
| 差异 | 3 | edge | 光程差 | 3 | 2 | 127.52298805494371 | 762.6163860382294 |
| 差异 | 3 | edge | 光程差 | 3 | 3 | 109.6710465694235 | 564.9057042042374 |

未比较/错误原因（保留原始记录）：

- Error × 3：采集或软件计算失败；具体分类见上方及 failure-matrix.csv，原始进程错误另存。
- Incomparable × 3：Field-coordinate conversion requires an audited native field-type mapping
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}]
- Incomparable × 2：Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}]

原始明细：[分析请求 CSV](analysis-matrix.csv)、[逐面光线 JSON](ray-results.json)、[错误分类 CSV](failure-matrix.csv)、[ZOS 补充逐项 CSV](zos-variants/analysis-matrix.csv)。原目录 295 个文件的哈希全部保持；所有原生与软件任务均有完成记录，失败并未记为通过。
