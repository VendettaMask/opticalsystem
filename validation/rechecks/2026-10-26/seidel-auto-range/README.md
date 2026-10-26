# 赛德尔柱形图自动量程

日期：2026-10-26。用户范围：只增加自动量程，保留现有显示样式。

## 实现

- 桌面 `MaximumAberration` 默认 0，参数最小值允许 0，并说明 `0=自动`。输入正数保持手动范围，已有保存设置不被自动覆盖。
- 自动范围取全部七类系数、各表面及总和的最大绝对值，再留 10% 对称边距。既考虑未舍入系列，又覆盖实际用于绘制的表格数值；零值回退到有限范围。
- 通过类型化 `PlotOptions.YMaximum` 向现有控件传递完整精度；旧结果没有此值时保留原说明行回退。
- 保留 Core 通用构造默认 0.1。未修改光学系数、网格间隔、柱形渲染、语义配色、图例、坐标字样和布局。

## 验证

执行默认 Release 输出的完整依赖构建及定向测试：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~SeidelDiagramAutoRangeTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests' --logger 'trx;LogFileName=seidel-auto-range.trx' --results-directory validation/rechecks/2026-10-26/seidel-auto-range
```

最终：[seidel-auto-range.trx](seidel-auto-range.trx)，15 通过、0 失败、0 跳过。包括既有系数/报告回归和新自动量程回归；新用例使用已有 123456 夹具，三种视场尺度仅检查量程覆盖与系数保持，不表示新增外部数值认证。

首轮：[first-range-binding-failed.trx](first-range-binding-failed.trx)，14 通过、1 失败。控件从格式化说明文字读到了 0.084，实际类型化上限为 0.084282494173010924。修复为直接读取类型化范围后重新完整构建并全部通过，未放宽精度断言。

此前构建阶段曾因工作区同时进行的界面改动暂缺 `CompanyLogoControl` 而停止；该引用恢复后完整构建成功。本轮新增测试也曾出现 `AnalysisOutcome` 命名空间歧义，已限定类型；这些编译中止不计为通过。

没有运行全仓测试、操作前台窗口/系统剪贴板、替换已安装应用或发布安装包。
