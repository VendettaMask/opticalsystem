# 分析窗口的统一结构选择

日期：2026-10-22。此说明记录本地源码和默认构建输出，不代表安装包已发布，也不构成 Zemax 数值认证。

## 使用方式

所有顺序分析设置共用“结构”行。单结构文件整行隐藏，多结构文件按以下顺序列出选项：

| 选项 | 含义 |
| --- | --- |
| 所有 | 仅在支持全部结构比较的分析出现，显示独立叠加或结构矩阵 |
| 当前的 | 每次运行使用工程当前活动结构，活动结构变化会触发重新计算 |
| 1 / N、2 / N… | 固定使用此编号，主界面切换活动结构后编号保持 |

普通分析默认“当前的”；结构矩阵点列图沿用“所有”。选择器放在共用设置层，覆盖专用点列、PSF、波前、图像模拟、基面以及通用设置布局。非序列模型目前不具有多个顺序结构，此行隐藏。

编号通过十进制字符串保存；`Current` / `All` 为规范设置值。可翻译标签、总结构数和 `X / N` 显示不参与编号解析。设置预设及页面副本使用相同设置字典。增加结构更新分母，固定编号保持；切换文件刷新选项，已不存在或不支持的旧选择回到当前结构。直接执行的无效编号请求则报错，不暗中用当前结构代替。

## 所有结构的显示范围

| 显示模式 | 分析 |
| --- | --- |
| 结构矩阵 | Configuration Matrix Spot Diagram；按视场行、结构列排列，每格保留全部所选波长 |
| 散点/像差叠加 | Spot Diagram、Full Field Spot Diagram、Matrix Spot Diagram、Through Focus、Ray Fan、Optical Path Difference、Footprint Diagram、Field Curvature and Distortion、Field Curvature、Color Focus Shift、Lateral Color、Axial Aberration |
| 能量叠加 | Encircled Energy、Diffraction Encircled Energy、Geometric Line Edge Spread、Extended Source Encircled Energy |
| RMS/系数曲线叠加 | RMS vs Field、RMS vs Wavelength、RMS vs Focus、RMS Wavefront vs Field、Zernike vs Field |
| MTF/扩散曲线叠加 | MTF、Geometric MTF、Sampled MTF、Fourier Through Focus MTF、Geometric Through Focus MTF、Fourier MTF vs Field、Geometric MTF vs Field、FFT PSF Cross Section、FFT Line Edge Spread |
| 其他曲线叠加 | Angle vs Image Height 及 Through Pupil / Through Field、Vignetting Diagram、Relative Illumination、Y-Ybar |
| 当前或单个结构 | 上述以外的分析，包括波前面形、PSF 图面、图像模拟、Huygens 各分析和文字报告 |

“所有”保留每个结构的原始点，不对光斑或曲线平均、重采样或强度相加。波长配色及类别颜色语义保持，线型/点型区分结构。图例由结构号和原系列标识组成，可独立开关；跨子图收集完整图例，长横向图例换行。叠加页脚说明独立比较，完整逐结构指标保留在数据/文本页。

各结构的坐标量/单位及子图布局必须一致；不满足时明确拒绝普通叠加，可改选单个结构或结构矩阵。某个明确选择的视场、波长或表面不存在时指出对应结构；矩阵选择所有视场时仍允许缺失格并说明原因。当前实现对混合有焦/无焦普通叠加报错，不采用 Zemax 点列图的自动改回当前结构行为。

## Zemax 文档依据与边界

[OpticStudio Standard Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Standard_Spot_Diagram.html) 说明了 All、Current、单个结构和多结构点列比较，并说明混合有焦/无焦时自动回到 Current。本次采用其选择形式；额外的曲线比较能力由应用显式能力表声明，不能据此认定 Zemax 的每一种分析也提供相同 All 功能。

[Huygens PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html) 与 [Huygens MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_MTF.html) 的 All 涉及同波长跨结构复振幅相干求和，再按波长非相干合成，并具有波长和像面一致性要求。此相干合成尚未接入；相关分析只提供当前/单个结构，不用普通强度或曲线叠加冒充原生 All。官方帮助作为本轮交互/语义参考，未开展新的原生数值对照。

## 计算与导出

执行时冻结同一修订的完整多结构文档，在独立 worker 中选定结构。不改变主工程活动编号、处方、撤销状态或修订；不同结构不使用当前结构的同一光线缓存身份。基面数据的镜头场景、数值和文档摘要也由所选结构产生。界面通过类型化 `ConfigurationScope`、系列结构号和矩阵图格元数据标识结构。

多结构结果 CSV 的最后一列为 `configuration`，从系列的显式结构号输出；纯表格及摘要的单结构请求也写入实际编号。单结构文件维持原 CSV 格式。PNG 页脚保留结构选择与实际编号，矩阵保留列号，叠加保留完整图例。切换文件先清理旧结果，锁定页面也遵守该规则。

## 验证证据

定向测试包含全部 75 项顺序分析设置布局、单结构隐藏、多结构编号与三语显示、运行时语言切换、添加结构和切换文件、Current 跟随与固定编号不变、基面场景来源、真实变焦三结构点列、MTF/RMS/场曲叠加、矩阵单列真实编号、无效索引及不兼容坐标拒绝。全部 36 项声明为普通叠加的分析逐项运行，结构矩阵另有单独守卫，不推导为全目录认证。

数值及截图主要采用现有 Zoom lens 三结构、三视场、三波长。该文件的相对照度即使只计算当前结构，也会因像方方向余弦包络没有面积而报不可计算；本轮未改光学算法或用假曲线代替。相对照度叠加使用现有应用 Cooke 示例的三个克隆结构做功能回归，另外守卫 Zoom 的单结构原始错误和全结构的带结构号错误传播。这是复用既有样例的功能检查，没有新增 Cooke 残差调查或原生认证。

最终结果、构建和截图范围见[当前状态](CURRENT_STATUS.md)、[机器账目](validation/ANALYSIS_CONFIGURATION_SELECTION_2026-10-22.json)；可复查的测试日志与三语宽/窄截图打包为[证据归档](../validation/rechecks/2026-10-22/analysis-configuration-selection-evidence.zip)。首次界面回归发现的翻译与控件重挂问题已修正；逐项测试的过低采样、空白夹具以及 Zoom 相对照度原有不可计算记录同样保留。未运行全测试或重新发布安装包。
