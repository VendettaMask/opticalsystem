# Zemax 一致性采集工具

面向任意新 ZMX 的入口已新增为独立 [C# ZemaxComparison](../OpticalSystemDesign.ZemaxComparison/README.md)。本目录保留固定基准维护流程，新工具不会调用这里的脚本；固定镜头的映射/设置不得作为新工具的通用默认值。原生 ZPL 截图方式在新工具中由 C# 调度，并显式加载捕获 CFG。

本目录保存 OpticStudio 基线采集、Workbench 结果捕获和对比报告工具。
这是产品之外的外部验证工具边界：Python 和 pythonnet 只服务于 Zemax ZOS-API 捕获与离线报告，不是产品计算后端，不随产品发布，不得从 `src` 项目引用。这里不使用 Optiland，也不生成 Optiland 对照。主要权威为已提交的 OpticStudio 2026 R1 基准；结论逐项绑定镜头、设置、版本、单位、采样和容差，不能把一个捕获文件的设置称为通用默认值。
当前保留的入口均为 Python 或 ZPL 工具；历史外部探针和临时接口探针已删除，不属于当前基线采集链路。

本目录同时处理分析数据、设置、截图和 Merit Function Editor golden 基线。`[MS-L7]` 的评价函数目录与导入边界由 Core 测试和 [Zemax 顺序模式操作数支持规范](../../docs/ZEMAX_OPERAND_SUPPORT.md) 管理；MFE golden 只能证明指定文件、版本和行参数下的数值，不得用它替代 383 项操作数的完整验收。

## 工具用途

- `zosapi_export.py`：通过官方 Python ZOS-API Standalone 连接加载顺序模式 ZMX，并导出 FFT MTF。
- `zosapi_through_focus_export.py`：通过官方 Python ZOS-API 导出 FFT Through Focus MTF 与相关波前/追迹参考数据。
- `zosapi_merit_function_export.py`：加载指定 ZMX，导出 MFE 行顺序、六个原始参数槽、目标、权重、当前值、贡献和总评价函数，并记录源文件 SHA-256。
- `zosapi_merit_control_probe.py`：在不保存源镜头的前提下，用 `SKIS/SKIN`、两个可区分的执行区间和真实贡献值探测 MFE 条件控制流。
- `zosapi_capture_baseline.py`：为一个镜头枚举完整 `AnalysisIDM` 目录，并记录每项分析状态。
- `capture_analysis_window.zpl`：在存在对应窗口代码时捕获真实 OpticStudio 分析窗口。
- `verify_baseline.py`：验证清单、源文件哈希、JSON、设置/文本引用和截图。

适用分析保留原生设置文件、原始文本、结构化 ZOS-API JSON，以及真实 OpticStudio 截图；没有 ZPL 窗口代码时，才使用由 ZOS-API 数据绘制并明确标识的图像。需要非顺序数据、外部文件、STAR 数据或缺失模块的分析仍写入清单并标记“不适用”，不会生成替代数值。

## 运行前要求

运行采集前必须关闭所有可见和后台 `OpticStudio.exe`。脚本默认拒绝在已有实例时启动，防止旧实例或第二实例占用 API 许可证并导致 `IsValidLicenseForAPI` 返回 `false`。

只有在确实需要保留交互会话、且许可证允许额外实例时才使用 `--allow-existing`。每个采集子进程仍保持隔离，只关闭自己创建的实例。

## 连接探测与基线采集

使用 Ansys 自带 Python 导出 FFT MTF：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_export.py"
```

为指定源镜头采集一份新的基线快照；已提交的 `123456.ZMX` 基准保持只读：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_capture_baseline.py" `
  --zmx "C:\Users\19851\Desktop\123456.ZMX" `
  --output "D:\Projects\opticalsystem\artifacts\zemax-captures\new-run"
```

若一次采集后修复了序列化或外部依赖，可先用 `--retry-failed --data-only` 只重算失败项，再用 `--screenshots-only` 补齐缺失截图；已有原生截图不会被覆盖。

验证基线：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\verify_baseline.py" `
  "D:\Projects\opticalsystem\artifacts\zemax\123456-zemax-2026-r1-baseline"
```

采集 `[MS-L7]` 的 MFE golden：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_merit_function_export.py"
```

实测评价函数条件控制流：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_merit_control_probe.py"
```

## Workbench 图像口径

两类图像用途不同，不得混用：

- `images/current/*.png`：由结构化结果 JSON 离线绘制的 Matplotlib 图，只用于数据形状诊断，不能证明 GUI 一致。
- `images/gui-current/*.png`：由真实 Avalonia `AnalysisPanel` 渲染，包含导入镜头、保存的分析设置、明亮主题、工具栏、图表/数据/文本页签和报告页脚；只有这类图像可用于 Workbench 与 Zemax 的 GUI 对比。

构建桌面应用后可按固定基线清单捕获历史 69 个 GUI 分析页。69 是旧 `current-manifest.json` 的捕获项数，当前分析目录以 Core 为准；新增和合并入口不追溯改写原清单。捕获与报告写入独立的新目录，原基准只读：

```powershell
dotnet src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.App.dll `
  --capture-analysis-gui `
  --source=artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  --settings-manifest=artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  --output=artifacts/workbench-recapture/new-run/images/gui-current `
  --start=1 --end=69
```

捕获清单为每页记录 `captured`、`analysis-error` 或 `failed`。`analysis-error` 也可能生成截图，但它只记录真实错误界面，不代表数值计算成功。

生成并排图像报告：

```powershell
python tools/zemax_parity/generate_gui_image_report.py `
  --capture-manifest artifacts/workbench-recapture/new-run/images/gui-current/capture-manifest.json `
  --baseline-root artifacts/zemax/123456-zemax-2026-r1-baseline `
  --output artifacts/workbench-recapture/new-run
```

报告会区分直接参考、仅最近似参考、来源/设置不同以及没有等价 Zemax 截图的分析，不会从图像尺寸或像素相似度推断视觉一致。

## 重算与定向采集

历史重算已并入 [ZemaxComparison](../OpticalSystemDesign.ZemaxComparison/README.md#historical-workbench-recapture) 的 `--recapture-workbench`，独立 AccuracyCapture 工程已删除。使用保存的设置作为只读输入，在全新的输出目录重算：

```powershell
dotnet run -c Release --project tools/OpticalSystemDesign.ZemaxComparison -- --recapture-workbench `
  artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  artifacts/workbench-recapture/new-run

python tools/zemax_parity/generate_workbench_comparison.py `
  artifacts/zemax/123456-zemax-2026-r1-baseline `
  artifacts/workbench-recapture/new-run `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-30
```

命令的第四、第五个位置参数为当前目录中从 1 开始的 `start-index`、`end-index`。按 `--list-analyses` 顺序或新清单的 `index` 选择，旧固定索引表已退场。新目录中的区间外项目记为 `not-requested`；已有重算结果只有在源文件、Core/Application 身份、设置和输出校验和匹配时才复用，修改或损坏文件不冒充成功。旧清单没有输出校验和时，需要新重算。旧 `Through Focus MTF` 映射可以消费新 Fourier 入口，Huygens/Geometric 不参与别名合并。

报告单独统计 `notRequested`、`reused`、`cancelled` 和 `fullRecalculation`；当前耗时排除复用项的旧时间。定向或复用运行不能建立全套性能结论，旧完整记录保留原范围。

重算目录为请求项保留原始 JSON。历史对比目录曾包含 30 组数值映射、30 张数值图、2 个明确排除的非等价映射及 69 组页面图像；这些是原阶段记录。旧报告仅提供声明的物理序列映射，实际数值由当前重算文件读取。

报告截图优先使用当前结构化 `plotPanes`。渲染器不得把多面板分析压平成单坐标轴：光线扇形、光程差和光瞳像差保留五视场/双方向布局，视场曲率与畸变保留曲率/畸变双面板布局。
