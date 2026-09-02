# Zemax 一致性采集工具

本目录保存 OpticStudio 基线采集、Workbench 结果捕获和对比报告工具。
当前保留的入口均为 Python 或 ZPL 工具；历史外部探针和临时接口探针已删除，不属于当前基线采集链路。

本目录同时处理分析数据、设置、截图和 Merit Function Editor golden 基线。`[MS-L7]` 的评价函数目录与导入边界由 Core 测试和 [Zemax 顺序模式操作数支持规范](../../docs/ZEMAX_OPERAND_SUPPORT.md) 管理；MFE golden 只能证明指定文件、版本和行参数下的数值，不得用它替代 383 项操作数的完整验收。

## 工具用途

- `zosapi_export.py`：通过官方 Python ZOS-API Standalone 连接加载顺序模式 ZMX，并导出 FFT MTF。
- `zosapi_through_focus_export.py`：通过官方 Python ZOS-API 导出 FFT Through Focus MTF 与相关波前/追迹参考数据。
- `zosapi_merit_function_export.py`：加载指定 ZMX，导出 MFE 行顺序、六个原始参数槽、目标、权重、当前值、贡献和总评价函数，并记录源文件 SHA-256。
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

采集当前 `123456.ZMX` 基线：

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_capture_baseline.py" `
  --zmx "C:\Users\19851\Desktop\123456.ZMX" `
  --output "D:\Projects\opticalsystem\artifacts\zemax\123456-zemax-2026-r1-baseline"
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

## Workbench 图像口径

两类图像用途不同，不得混用：

- `images/current/*.png`：由结构化结果 JSON 离线绘制的 Matplotlib 图，只用于数据形状诊断，不能证明 GUI 一致。
- `images/gui-current/*.png`：由真实 Avalonia `AnalysisPanel` 渲染，包含导入镜头、保存的分析设置、明亮主题、工具栏、图表/数据/文本页签和报告页脚；只有这类图像可用于 Workbench 与 Zemax 的 GUI 对比。

构建桌面应用后捕获固定基线清单中的 69 个真实 GUI 分析页。这里的 69 是 `current-manifest.json` 的历史捕获项数，不是当前 Workbench 分析目录总数；当前 Core 目录为 70 项，新增报告类入口以及独立畸变入口退场不自动改写这份 Zemax 图像基线：

```powershell
dotnet src/OptilandWorkbench.App/bin/Debug/net10.0/OptilandWorkbench.App.dll `
  --capture-analysis-gui `
  --source=artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  --settings-manifest=artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  --output=artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/images/gui-current `
  --start=1 --end=69
```

捕获清单为每页记录 `captured`、`analysis-error` 或 `failed`。`analysis-error` 也可能生成截图，但它只记录真实错误界面，不代表数值计算成功。

生成并排图像报告：

```powershell
python tools/zemax_parity/generate_gui_image_report.py `
  --capture-manifest artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/images/gui-current/capture-manifest.json `
  --baseline-root artifacts/zemax/123456-zemax-2026-r1-baseline `
  --output artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31
```

报告会区分直接参考、仅最近似参考、来源/设置不同以及没有等价 Zemax 截图的分析，不会从图像尺寸或像素相似度推断视觉一致。

## 重算与定向采集

使用保存的对比设置重算全部 Workbench 分析，并重新生成数值和截图对比：

```powershell
dotnet run --project tools/OptilandWorkbench.AccuracyCapture -- `
  artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31

python tools/zemax_parity/generate_workbench_comparison.py `
  artifacts/zemax/123456-zemax-2026-r1-baseline `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31 `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-30
```

捕获命令还接受从 1 开始的 `start-index` 和 `end-index`。传入相同索引可只重算一项并复用其他已有结果。常用索引：视场曲率与畸变 `11`、畸变 `12`、包围能量 `19`、扩展源包围能量 `22`、光瞳像差 `23`、Huygens 离焦 MTF `32`、Huygens MTF `52`、对比度损失图 `55`、光程差 `56`、波前 `58`。

定向重算不能提供有效的全套性能总计，因为复用项没有重新计时；在固定清单全部 69 项重算前，应保留最近一次完整运行的时间数据。

捕获目录为每项 Workbench 分析保留原始 JSON。对比目录当前包含 30 组可机读的等价数值比较、30 张数值图、2 个明确排除的非等价映射，以及 69 组 Workbench/Zemax 页面图像。旧报告只用于稳定物理序列映射，不能复用其中的 Workbench 数值。

报告截图优先使用当前结构化 `plotPanes`。渲染器不得把多面板分析压平成单坐标轴：光线扇形、光程差和光瞳像差保留五视场/双方向布局，视场曲率与畸变保留曲率/畸变双面板布局。
