# 顺序光学面型支持

## 导入与计算范围

支持 Zemax ZMX 导入。下表按原文件的 TYPE 代码核对实际计算实现。可读取的面型会通过共享 Core 进行求交、法线和光学交互计算，未映射的类型通过原始参数快照保留。各分析的适用范围与数值验收仍使用逐分析记录。

| 面型系列 | 原文件 TYPE | 当前计算范围 |
| --- | --- | --- |
| 标准球面、圆锥、平面 | STANDARD | 已实现 |
| 偶次、奇次非球面 | EVENASPH、ODDASPHE | 已实现八项径向系数导入 |
| 双圆锥 | BICONICX | 已实现独立 X/Y 半径和圆锥系数 |
| 扩展偶次非球面 | XASPHERE | 已实现最多 240 项，最高径向幂次 480 |
| 扩展奇次非球面 | XOSPHERE | 已实现最多 240 项，最高径向幂次 240。非零一次项在顶点形成尖点，该点的法线和近轴曲率不可用 |
| 扩展 XY 多项式 | XPOLYNOM | 已实现 230 项，最高总阶数 20，包含圆锥基底 |
| 环曲面 | TOROIDAL | 已实现基底和径向非球面项，附加 Zernike 项仍有限制 |
| 倾斜平面、不规则面 | TILTSURF、IRREGULA | 已实现当前参数子集 |
| Fringe 矢高 | FZERNSAG | 已实现偶次基底、归一化、偏心及最多 37 项 |
| Chebyshev 矢高 | CHEBYSHV | 已实现受限阶数、归一化和定义域 |
| 网格矢高 | GRID_SAG | 已实现受限的内嵌网格、插值及空数据处理。外部资源和变换有适用限制 |
| Q 型非球面 | QED_TYPE | 已实现法向斜率基底子集，其他子型继续保留为只读 |
| 近轴薄透镜 | PARAXIAL、PARAX_XY | 已实现支持参数的薄透镜交互 |
| 光栅及相位面 | DGRATING、BINARY_2、BINARY_3、HOLOGRM2 | 已实现特定几何、级次及相位子集 |
| 晶体边界 | BIRE__IN、BIRE_OUT | 已实现受支持晶体定义的传播与偏振交互 |
| 梯度材料与网格体 | GRINSUR1、GRINSUR9、GRINSU11 | 已实现特定材料和参数定义，其他导入类型继续待接入 |
| 用户定义面 | USERSURF | 当前仅有已映射的特定镜阵列，通用 DLL 待接入 |
| 标准、环形 Zernike 矢高与双圆锥 Zernike | SZERNSAG、AZERNSAG、BICONICZ | 待接入原格式计算映射 |
| 普通 XY 多项式 | POLYNOMI | 本地多项式几何已有实现，原文件参数映射待接入 |
| 备用根非球面、超圆锥、离轴自由曲面、Q 自由曲面 | ALTERNAT、ALTERNAO、SUPERCON、OFFAXISCF、QFREEFORM | 待接入 |
| Fresnel 面 | FRESNELS、XFRESNEL、CFRESNEL、GEN_FRES | 待接入 |
| 样条、NURBS、复合自由曲面 | CUSPLINE、XCUSPLIN、RADNURBS、TORNURBS、TrueFreeForm | 待接入 |
| 其他相位、光栅及全息类型 | BINARY_1、BINARY_4、GRID_PHA、FZERNPHA、SZERNPHA、AZERNPHA、TOROGRAT、TOROGRAX、VARLSGRT 等 | 待接入 |
| 黑盒、矩阵及其他特殊交互 | BLACKBOX、ABCDSURF、JONESMAT、ATMOSPHR、RETROREF 等 | 待接入 |

本地 Zernike、Chebyshev、Forbes Q 和多项式几何采用各自的参数定义。原文件的新类型需要明确的系数顺序、基底和归一化映射。

## 2026-11-20 新增四类面型

双圆锥的主曲率半径和圆锥系数对应 Y 方向，X 方向由独立参数给出。已有原生工程保存的 X/Y 几何值继续按各自轴读取。三类扩展面保存长度单位的系数和归一化半径，在归一化坐标下求值，避免把高阶系数换算成极大的非归一化数值。输入支持 PARM 13 起的扩展列和 XDAT，并处理多配置覆盖及拾取的同类参数列。

扩展面保留有效项数之后的已存系数，计算使用声明的有效项数。保存和 ZMX 再导出均包含这些系数。归一化半径必须有限且为正，项数必须是范围内整数。非零的未映射普通参数保留为只读类型。

从「镜头数据 → 表面属性 → 类型」选择相应面型。扩展面可编辑归一化半径、有效项数和按 A1、A2…顺序的系数。系数用空格或换行分隔，支持科学计数法。双圆锥的 X 半径和 X 圆锥系数在属性页编辑，Y 参数在镜头数据表编辑。应用、还原、撤销、重做和原生工程保存共用现有工作区事务。

新增面型由共享 CPU 几何路径执行。专用 GPU 导数、系数变量优化和每项特殊制造操作仍需各自的能力映射。新增面型的独立公式和持久化测试单列记录，95 支菜单示例仍是产品数值验收范围。

扩展 XY 面的倾斜项或 XY 耦合项非零时，几何求交和法线仍可计算，当前标量近轴矩阵会报告需要耦合一阶模型。依赖该矩阵的分析适用范围相应受限。

参数定义依据原格式的[类型代码表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/SETSURFACEPROPERTY_SURP.html)、[双圆锥定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Biconic.html)、[扩展偶次定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Extended_Asphere.html)、[扩展奇次定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Extended_Odd_Asphere.html)和[扩展 XY 定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Extended_Polynomial.html)。

本批的独立公式、单位和持久化回归、实际控件编辑及五主题离屏渲染，见[定向验证记录](../validation/rechecks/2026-11-20/extended-surfaces/results.json)。这些记录用于确认实现和编辑流程；新增面型的外部逐值对照及完整 95 支示例重验仍待单独开展，不能据此认定所有分析已完成精度验收。

## 后续顺序

1. 标准及环形 Zernike 矢高、双圆锥 Zernike 和普通多项式的格式映射。
2. Q 型子类、离轴圆锥及其他自由曲面。
3. Fresnel、样条及 NURBS。
4. 特殊相位、光栅、黑盒、矩阵与用户定义接口。

每批同时覆盖物理面定义、导入、共享求交与法线、原生保存、可用的属性编辑及失败诊断。新数值对照按具体面型、材料、波长、视场和采样记录。
