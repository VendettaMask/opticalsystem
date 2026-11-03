# 分析页与图注云门禁修复：2026-11-03

## 原失败与处理

[运行 41632316061](https://github.com/VendettaMask/opticalsystem/actions/runs/41632316061) 的源码为 `7ab4adc52cbb39107d094e1cd80b16f930f3c25a`。Windows x64、Windows ARM64 与 Apple Silicon Mac 均在分析页/图注检查中得到 **200 通过、3 失败、0 跳过**，后续安装包和发布未执行。

- `InterferogramFooterTests.FooterUsesTypedSummaryWithoutGenericRowsOrFabricatedFringeSettings(600)`：旧夹具把单独页脚直接放进窄视口，触发响应式堆叠，随后找不到原图签第二列。改为实际页面使用的 `UniformDiagramPage` 与 `ResponsiveAnalysisPlotHost` 组合。
- `FieldCurvatureFooterTests.ProductAndDocumentRegionKeepsTheSameBoundsAcrossAnalysisKinds(600, Light)`：相同夹具将堆叠后的整行品牌区与原图签三分之一列宽比较。改用完整固定源图，保留原有列宽比例、品牌/文档样式与跨分析一致性断言。
- `AnalysisGuiContractTests.MultiPanePlotsKeepConfiguredShapeWithoutExposingAUiToggle`：反射调用仍传三个参数；内部绘图工厂已增加可选的第四个 `alignTitleRows` 参数。显式传入 `false`，保留非方形绘图区和无额外界面切换控件的检查。

两个页脚用例同时检查源图 1200×900、图签宽 1200/高 160、无溢出，以及窄视口中的整体缩放。没有删除原断言、放宽公差、跳过测试或修改产品绘图/计算代码。

## 复验

独立稀疏检出基于上述失败源码，只应用这三份测试修改。初次沙箱还原被 NuGet 网络访问限制阻断；保留锁定模式与漏洞审计，联网重做还原后成功。正常 Release 构建零警告/错误，测试运行在后台 Avalonia Headless 会话；沙箱外执行用于本机测试进程通信，未操作或重启用户前台软件。

```powershell
dotnet restore tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj --locked-mode --force --disable-parallel -m:1 -nodeReuse:false
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter (Get-Content validation/rechecks/2026-11-03/analysis-release-contracts/analysis-filter.txt -Raw) --logger 'trx;LogFileName=analysis-contracts.trx' --results-directory validation/rechecks/2026-11-03/analysis-release-contracts
```

实际过滤器由该源码的 `release.yml` 中 `Verify readable analysis pages and captions` 步骤原文提取，保留为 [analysis-filter.txt](analysis-filter.txt)。

最终 **203 通过、0 失败、0 跳过**，约 1 分 33 秒；原始 [TRX](analysis-contracts.trx) 保留。与云端原门禁用例数一致，三个失败用例都得到通过记录，原 200 项保持通过。不扩大为全仓测试、后续绘图/导出阶段、三平台安装包或新的光学数值认证。

主工作区中其他任务正在编辑的 Core、示例精度和测试项目改动未进入隔离验证，也不包含在此次修复提交内。临时检出及编译载荷验证后清理，仅保留本轮小型结果与过滤器。
