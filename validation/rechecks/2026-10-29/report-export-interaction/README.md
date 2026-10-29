# 设计报告导出入口与显式执行

日期：2026-10-29。范围：顺序工作台数据库功能区、报告设置窗口和中英日文案。

## 实现

- “一键导出 Markdown”和“报告导出设置”合并为“导出设计报告…”；命令搜索也只注册这一个入口。
- 顶部入口只打开或显示同一个设置窗口；删除自动执行参数、窗口打开事件中的导出调用和供顶部入口使用的直接启动方法。
- 窗口内“开始导出”保留原有导出流程；确认目录、分析及参数、布局和配置范围后才捕获快照、计算和写入。
- 重复点击入口保留当前目录与选择；运行中不会新建窗口或追加导出任务。保留进度、取消、设置保存和既有输出保护。

## 验证

默认输出目录的 Release App 和测试项目编译完成；`MarkdownReportExportTests` 7 项、`MarkdownReportImageTests` 6 项，合计 **13/13 通过**。最终运行见 [TRX](report-export-interaction-final.trx) 和[控制台日志](test-final.log)。

新增三语言界面用例实际点击顶部入口与窗口内按钮，确认只有一个入口和一个归属主窗口的报告窗口，首次打开保持空闲，重复点击保留指定目录且不创建输出，点击“开始导出”后只在指定目录生成一份报告，运行中重复点击仍使用同一窗口。其余用例覆盖快照与未保存修改、配置、参数、失败继续、取消清理、真实 Skia 图片、设置界面及共享进度。

复现命令（后台、`DOTNET_PROCESSOR_COUNT=2`，最终执行使用低优先级）：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MarkdownReport'
# 受限环境中的 testhost 连接超时后，在独立后台环境使用同一编译结果执行：
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~MarkdownReport' --logger 'trx;LogFileName=report-export-interaction-final.trx' --results-directory validation/rechecks/2026-10-29/report-export-interaction
```

修改的六个 C# 文件通过 `dotnet format whitespace --folder --verify-no-changes --include ...`，退出码 0，无格式诊断（[日志](format.log)）；`git diff --check` 通过。

## 中间失败与边界

最初默认多进程构建返回 1，在项目引用框架查询阶段提前退出，没有给出编译错误，未执行测试；[原摘要](initial-build-failure.txt)保留，详细本地诊断放在忽略的 `tmp/report-export-interaction-2026-10-29/test-build.log`。改用单进程、关闭节点重用与共享编译后成功生成默认输出。该受限运行的 testhost 随后连接超时，**执行 0 项**，见[原 TRX](report-export-interaction.trx)和[构建/中止日志](test.log)，不计为通过。最终独立后台重跑同一程序集，13 项全部通过，无需修改产品或放宽断言。

全部界面验证使用 Avalonia Headless/Skia，没有打开或控制真实桌面窗口。没有运行全仓测试、跨平台原生交互或安装包发布；本记录不扩大既有光学数值验证范围。
