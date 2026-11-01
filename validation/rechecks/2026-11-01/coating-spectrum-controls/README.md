# 镀膜光学评价控件验证

产品行为见[实现报告](../../../../docs/COATING_SPECTRUM_CONTROLS_2026-11-01.md)。本批只覆盖偏振显示、独立评估设置、结果表与导出及相邻桌面流程，不重新认证优化达标或参考软件全部功能。

## 运行与范围

- [完整独立构建日志](isolated-build.log)：Release Win-x64，App 及完整项目依赖，零警告/错误。
- [定向测试日志](targeted-tests.log)：31/31，26 项桌面用例、3 项真实材料偏振对比、2 项独立采样/默认设置检查；前序试跑不累计。
- 离屏预览发现未编辑时“恢复默认”被延后文本事件错误启用，修正后[完整依赖再构建](final-ui-build.log)零警告/错误，[该交互与导出用例复跑](final-ui-tests.log)1/1 通过，并断言出厂时禁用、编辑角度后启用。同一身份不累计为32项。[修正过程编译失败](intermediate-ui-compile.log)仅为已修复的局部变量初始化顺序，保留失败记录，不计成功构建。
- 默认输出复制遇到用户程序锁定的记录见 [default-output-locked.log](default-output-locked.log)。验证改用 `artifacts/coating-spectrum-20261101/build`，没有关闭、启动或控制用户前台程序，不把独立输出计作最终默认输出替换。
- 原设计协议、真实 n/k、原验收角度/偏振、正式结果和达标门槛保持。新评估默认 0°、400–800 nm、步长 0.5 nm、手动 Y=0–100%，801 点。
- 真实材料 0°/35°/57° 的 S/P/非偏振每点核对共享 Core；UI 固定为原设计 45° S 与独立评估 0° 的不同设置，验证两者不会混淆。截图中三层膜系是交互夹具，其曲线为实算，不代表设计优化达标。
- 桌面测试为 Avalonia 安全离屏宿主；保存/导出菜单项派发 Click 事件，并核对实际文件全文。未完成原生窗口/系统文件选择器或系统剪贴板验收。
- [文档范围检查](documentation-scoped.json)核对5份相关文档的781个本地目标，全部存在。另一次[全仓文档审计摘要](documentation-summary.json)覆盖3332份文档，无代码围栏问题、当前指南断链或当前指南重复段落；留下1条并行非序列专题的历史证据链接，不计全仓文档完全通过。本批镀膜入口无断链，未修改其他任务的文档。

## 实际离屏预览

- [出厂设置](evaluation-settings-factory-1500.png)：0°、400–800 nm、0.5 nm、手动 0–100%。
- [偏振及悬停](polarization-controls-1500.png)：45° 平均和 s 分别计算，提示按曲线身份列值。
- [结果表](polarization-results-1500.png)：展开801行，表头显示当前量与偏振，导出保留全精度。
- [浅色](layers-Light-1200-13.png)、[深色](layers-Dark-1200-13.png)、[窄窗大字号](layers-Light-820-18.png)：32层布局夹具，光谱仍调用共享 Core。

## 复现

在仓库根目录使用 PowerShell 运行。`isolated-locks.props` 将 NuGet 锁文件放在独立目录，不改共享项目锁文件；离线还原源为本机 NuGet 缓存。日志绑定当次构建，后续共享工作区修改不能自动沿用该通过结论。

```powershell
$buildRoot = Join-Path $PWD 'artifacts/coating-spectrum-20261101/build'
$lockProps = Join-Path $PWD 'validation/rechecks/2026-11-01/coating-spectrum-controls/isolated-locks.props'
$project = 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj'
dotnet restore $project -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -p:CustomAfterMicrosoftCommonProps=$lockProps --source C:/Users/19851/.nuget/packages -p:NuGetAudit=false --ignore-failed-sources -m:1
dotnet build $project -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -p:CustomAfterMicrosoftCommonProps=$lockProps --no-restore -m:1
$env:COATING_UI_EVIDENCE = Join-Path $PWD 'artifacts/coating-spectrum-20261101/screenshots'
$env:OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY = Join-Path $PWD 'artifacts/coating-spectrum-20261101/test-profile'
dotnet test $project -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -p:CustomAfterMicrosoftCommonProps=$lockProps --no-build --no-restore -m:1 --filter 'FullyQualifiedName~DesktopTests|FullyQualifiedName~DisplayPolarizationTests|FullyQualifiedName~AnalysisDisplaySettingsTests' --logger 'console;verbosity=normal'
```

`source-hashes.json` 与 `runtime-hashes.json` 只记录本批源码及实际独立输出的执行文件身份；不重复封存整棵源码树或第三方依赖。PNG/日志为本批唯一有效副本，`artifacts` 是可再生的工作输出，不作为第二份冻结证据。
