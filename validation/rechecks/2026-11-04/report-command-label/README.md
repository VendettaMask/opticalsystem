# 报告导出入口名称简化

日期：2026-11-04。

原“导出设计报告…”末尾是名称中显式写入的省略号，表示后续还有设置。按用户要求简化为“导出报告”，英文为“Export Report”，日文为“レポート出力”。主Ribbon、命令和悬浮提示同步；保留原动作ID、图标、先设置后导出的行为。更新已有流程断言、使用指南及当前图标目录，没有新增测试或光学逻辑。

默认Release编译通过。测试项目保留材料库测试的CS0618、xUnit2031两条既有警告。第一次沙箱运行在执行用例前因testhost通信90秒超时中止，记录在 `test.log` 和 `report-command-label.trx`；随后只重跑同一已构建程序集中的定向用例。

最终 **4/4通过，0失败，0跳过**，见 `test-final.log`、`report-command-label-final.trx`：

- 中文 / Word、英文 / PDF、日文 / Markdown、中文 / HTML。
- 原入口可访问名称、单一闲置设置窗口、重复点击保留状态。
- 明确点击“开始导出”后生成对应文件、进度恢复和设置回读。

构建和执行命令：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MarkdownReportImageTests.ReportRibbonOpensOneIdleSettingsWindowAndExportsOnlyOnStart' --logger 'trx;LogFileName=report-command-label.trx' --results-directory validation/rechecks/2026-11-04/report-command-label -m:1 -v:minimal
dotnet vstest tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll '/TestCaseFilter:FullyQualifiedName~MarkdownReportImageTests.ReportRibbonOpensOneIdleSettingsWindowAndExportsOnlyOnStart' '/Logger:trx;LogFileName=report-command-label-final.trx' '/ResultsDirectory:validation/rechecks/2026-11-04/report-command-label'
```

全程后台离屏，未控制或重启用户程序。此记录只覆盖名称修改及原有报告入口流程，不是全仓或数值验证。
