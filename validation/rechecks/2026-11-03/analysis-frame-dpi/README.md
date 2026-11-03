# 完整分析图框与无损导出 · 2026-11-03

本轮覆盖用户要求的分析外框、格式与字体中的导出设置，以及随后明确的 PNG/BMP 无损输出约束。普通图片导出统一使用 PNG；图像模拟原始栅格还支持真正的无压缩 BMP。移除 JPEG 输出编码与保存选项，并在手填 JPEG/其他不支持的输出扩展名时提前拒绝，保护已有文件；读取 JPEG 输入及保留原始输入文件的行为保持。

## 当前实现

- 普通分析与二维/三维布局在固定源图外侧显示共享的 1 DIP 直角图框；图框随完整页面适应窗口，100%模式仍可滚动。源图继续为1200×900逻辑尺寸，绘图区740高、图签160高，边框不挤压源内容。实体模型、探测器热图/剖面、镀膜光谱与堆栈、相机SFR图也使用统一外框及无损导出路径。
- “设置 → 格式与字体 → 图像导出”提供1080、1440、2160像素高度预设及自定义高度，始终保留完整原图比例，并显示实际像素。UI明确标注2K/4K档为1440/2160高，不声称所有源图都是16:9。固定原图含边框时，三档分别为1440×1080、1919×1440、2879×2160；边框比例及像素取整决定实际宽度。
- 打印 DPI 独立设置，默认150，可配置24–600。改变DPI不重采样已经选择的像素尺寸；PNG的`pHYs`密度标记记录捕获时的DPI，即使保存对话框期间设置变化也保留原捕获值。像素高度与DPI可保存/恢复，旧设置缺少字段时使用1080高和150DPI，保存失败回滚。
- 保存绘制后的完整图像，不重排或裁剪图签、图例、色标和子图，保留原数据、相机、缩放和筛选。PNG及Word/PDF/HTML/Markdown报告共用绘图；报告固定纸面图尺寸保持。原始图像模拟结果保持实际栅格像素，不套用分析图的尺寸缩放。Core及两个内置模块的计算源码无差异，Application Runtime只增加输出文件格式门禁，未改分析公式或验收阈值。

## 验证范围

最终去重 **154项不同定向用例通过**：主产品147项及镀膜7项。没有将重跑次数累加为测试数。

| 范围 | 证据 |
| --- | --- |
| 五类输出尺寸/设置边界、三语设置、保存回滚、打印DPI与像素独立、PNG签名/像素、JPEG路径保护 | 新增`AnalysisFrameDpiTests`及扩展的图像文件测试 |
| 普通曲线、波前表面、点列、FFT PSF、基面数据、矩阵/扇形、完整图签、源几何/字体/缩放稳定 | 分析图片及统一源图回归 |
| 2D/3D布局、实体模型、窗口缩放、相机状态、24材料长图例、取消和失败保护 | 布局/视口导出回归及[完整长图例](viewers/solid-complete-24-material-legend.png) |
| Double Gauss真实波前、独立配色、灰度干涉图与三类SFR | 共享App/Core实际结果与绘图回归 |
| Word/PDF/HTML/Markdown | 原始PNG逐像素及嵌入字节保持；PDF无DCT/JPEG图像编码；Word中的品牌图与分析图分别核对 |
| 镀膜光谱与堆栈 | 完整图签、s/p开关、读数/状态、域外背景、语言与PNG真实像素/密度保持 |

主批主产品147项中145项首次通过；两个既有测试前提修正后分别复核。镀膜7项中6项首次通过，光谱尺寸前提修正后通过。保留旧192DPI断言、旧导出图标名称、旧双倍像素坐标、半透明色块参考、Word品牌图片数量、一次未开启真实渲染导致跳过、一次错误测试筛选导致无匹配，以及解码器固定96DPI读数误用等中间记录，不计为最终通过率。长图例最终用真实Avalonia源色块渲染作为颜色参考；DPI直接核对PNG文件标记，不依赖查看组件的显示值。

最新默认Release App、主测试项目与镀膜测试项目构建成功、零警告/错误。三个产品程序集在正常App与两个测试输出中一致；主批与复核保持同一App二进制。23个相关产品源码在三份编译PDB中共69个身份核对一致。详见[receipt.json](receipt.json)，可使用[校验脚本](../../../../tools/diagnostics/AnalysisFrameDpi20261103/Summarize.ps1)检查。所有测试均后台离屏，没有控制或重启用户前台软件；未执行全仓测试、新的外部光学精度认证、安装、发布或远端同步。

本轮修改的八份文档无坏链或围栏错误，见[文档范围检查](documentation-scope.json)。保留[全仓扫描日志](documentation.log)：严格扫描发现并行图标工作中四条尚未生成的图标包/验证记录链接，涉及`ICON_SEMANTIC_DESIGN.md`和`LOCAL_ICONS.md`，不属于本轮导出功能；因此不宣称全仓文档审计通过，也未改写该并行工作。

## 预览与参考

- [1080高完整图框](wavefront-1080.png)、[1440高](wavefront-1440.png)、[2160高](wavefront-2160.png)：使用相同源数据，三例分别验证96、300、600打印DPI，说明DPI与像素预设可独立选择。
- 设置：[中文](settings-zh-CN-Light.png)、[English / Dark](settings-en-US-Dark.png)、[日本語](settings-ja-JP-Light.png)。截图来自设置验证流程，实际保存与失败回滚分别测试。
- 设计参考：[OpticStudio像素尺寸及比例](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Graphics_OpticStudio_preferences.html)、[MATLAB尺寸与DPI](https://www.mathworks.com/help/matlab/ref/exportgraphics.html)、[COMSOL尺寸单位与打印密度](https://doc.comsol.com/6.3/doc/com.comsol.help.comsol/comsol_ref_results.37.214.html)。PNG密度标记遵循[W3C PNG pHYs](https://www.w3.org/TR/png-3/#11pHYs)。这些是功能设计参考，不是新的光学精度基准。
