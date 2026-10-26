# 干涉条纹后台回归

日期：2026-10-26。默认 Release App/测试输出重新编译，最终定向 **33 通过、0 失败、0 跳过**，见 [interferogram-regression.trx](interferogram-regression.trx)。这不是全仓测试结果，早先 31 项中间结果不累计。源码和程序集摘要、最终测试账目见 [verification.json](verification.json)。

相关源码完成空白格式整理，最终差异空白检查通过；仓库严格文档检查通过，断链 0、未闭合代码围栏 0、当前指南重复段落 0，见 [documentation-audit.json](documentation-audit.json)。这些检查不替代上面的数值与渲染测试。

## 验证范围与结果

- 共享 Core：零相位和半波相位、解析纯倾斜条纹、倍率先于倾斜、同结构抵消、交换双光束、半波主光线总光程的互补强度、OPD/相位/强度分离、真实子孔径采样、分析副本不改源处方、中间面焦面、顶点参考和倍率约束、等高线格式、无波长/取消及类型化坐标量。
- 应用链路：两个结构的完整上下文传入后台分析，单独指定分析结构不改变源文档；任意等高线文本经设置标准化后仍保留。`-1 - 当前结构` 的负编号解析有专项回归。
- 真实绘图/导出：Avalonia Headless + Skia 创建正常 `AnalysisPanel` 绘图页，再调用真实 `ReportImageRenderer.WriteAsync`，比较解码后的全部像素。灰度/伪彩色/等高线及 Light/Dark 四种组合均 **0 像素通道差异**，画布 1200×900；没有用测试占位图冒充实际渲染。
- 灰度 Light 使用 128×128 的真实计算采样，其他显示用 32×32；所有显示共用强度 0～1，倍率 2、X 倾斜 3、Y 倾斜 −1。页脚实际参数也有断言。既有 `WavefrontSurfaceRenderTests` 同时通过，默认波前色图行为保留。

实际产品渲染结果：

- [灰度，Light，128×128](captures/fringes-gray-Light.png)
- [伪彩色，Light，32×32](captures/fringes-color-Light.png)
- [等高线，Light，32×32](captures/fringes-contour-Light.png)
- [灰度，Dark，32×32](captures/fringes-gray-Dark.png)

## 冻结 Zemax 原生数值核对

唯一外部数值基准为仓库原有 [123456.ZMX / OpticStudio 2026 R1 干涉图捕获](../../../../artifacts/zemax/123456-zemax-2026-r1-baseline/analyses/054-interferogram/data.json)，并以其独立 [打印网格](../../../../artifacts/zemax/123456-zemax-2026-r1-baseline/analyses/054-interferogram/data.txt) 交叉核对。没有重新启动 Zemax，没有改写捕获。

显式设置为 64×64、第 2 波长 0.4400 µm、第 1 轴上视场、倍率 1、倾斜 0、当前结构与参考光束，不使用出瞳形状。**3001 个有效点**的最大归一化强度绝对差为 **7.546728131302416×10⁻⁹**，通过事先固定的 **10⁻⁸** 门槛。Core OPD PV 为 **0.12616552489610264 waves**，原生文本为四位小数 0.1262。逐点结果见 [native-grid-comparison.json](captures/native-grid-comparison.json)。

网格方向不是通过调节光学数据拟合的：Core 点对应 `column=round(x×31+32)`、`row=round(y×31+32)`；原生 API 数组为 `[63-row][63-column]`，独立打印网格为 `[row][63-column]`。测试先核对原生 JSON 与五位小数打印值的舍入误差，再比较 Core 强度。该轴上案例不单独认证离轴瞳方向或所有新增设置。

## 保留的失败记录

- [first-core-failed.trx](first-core-failed.trx)：最初测试假设小 OPD 场景必有强度低于 0.8，假设不成立；移除该截图亮度式断言，改以解析相位/强度与原生网格核对，没有修改物理模型迎合断言。
- [interferogram-core.trx](interferogram-core.trx)、[native-orientation-failed.trx](native-orientation-failed.trx)、[native-grid-first.trx](native-grid-first.trx)：原生数组/打印网格方向排查的中间失败，未计为通过；最终方向关系由两份原生输出交叉核对，门槛没有放宽。
- [intermediate-guard-first.trx](intermediate-guard-first.trx)：32 通过、1 失败。新边界测试发现“不支持的光阑前中间面 + Ray Aiming”检查晚于图像高度视场转换，先触发瞄准失败；将限制检查提前后最终 33 项全部通过，源处方仍不变。

## 复现命令

从仓库根目录使用 PowerShell：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_INTERFEROGRAM_CAPTURE_DIR = Join-Path $PWD 'validation/rechecks/2026-10-26/interferogram/captures'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~InterferogramAnalysisTests|FullyQualifiedName~InterferogramFooterTests|FullyQualifiedName~InterferogramRenderingTests|FullyQualifiedName~WavefrontSurfaceRenderTests' --logger 'trx;LogFileName=interferogram-regression.trx' --results-directory validation/rechecks/2026-10-26/interferogram
```

## 边界与桌面隔离

所有 UI 用无桌面 Headless 后端，实际图像由 Skia 离屏渲染；没有调用 Win32 前台窗口、发送键鼠、切换焦点、读取或改写剪贴板，没有退出用户应用。未安装/发布新版本，也未进行原生 GUI 操作验收。

中间面在光阑之前且开启 Ray Aiming 的物理瞳模型仍不支持，现提前明确拒绝；顶点参考限于倍率 1 和小主光线偏角。双光束使用等强、归一化瞳完全重合模型。除上述冻结单一原生条件外，新增设置没有逐项原生 Zemax 认证，不能用后台解析/渲染通过代替该认证。完整定义见[实现说明](../../../../docs/INTERFEROGRAM_2026-10-26.md)。
