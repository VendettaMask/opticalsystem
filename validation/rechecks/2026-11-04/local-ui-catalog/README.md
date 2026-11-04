# 中英日本地词库与自动提取验证

日期：2026-11-04。

实现见[本地化说明](../../../../docs/LOCALIZATION.md)。本轮把运行时译文切换到程序集内置JSON资源，保留原三语词条作为生成输入，补齐发现的标签、选项与动态提示，统一术语并保护实际数据。没有运行时网络翻译、第三方光学计算或用户文档扫描。

## 最终结果

- `catalog-check.log`：3049个本地三语词条，2825处静态UI文字引用，0项缺失，0项资源/术语/格式错误。
- `build-final.log`：默认输出Release构建成功，0警告、0错误，包括App、两个内置模块和主测试程序集。
- `language-final.trx`、`test-final.log`：最终单次45/45通过，0失败、0跳过。
- `command-controls/results.json`：8个CLI控制通过，覆盖正常同步/检查、漏译拒绝、拒绝覆盖有效词库、过期资源、单位篡改、术语冲突及恢复后检查。它们是命令控制，未与45项xUnit用例相加。

45项覆盖新词库完整性与提取器、损坏输入、原样保留材料/路径/公式/前导零与单位、动态提示双向切换、真实主窗口语言菜单与处方修订保持、三语命令搜索、真实系统属性、官方材料库详情、异世界固定English及四格式报告入口。材料名称特意使用与“保存”按钮相同的文字，验证绑定、选择器及编辑内容保持原样；英文中新建的预翻译控件仍能切回中文/日文。

## 失败记录与修复

`language-first.trx`、`test-first.log`的43过/2失败定位到短模板被旧长度门槛跳过；修复后重跑同一定向集合45项全部通过，没有放宽数据相等断言。初次`build.log`中另一个并行字体工作出现临时async编译错误，随后工作区修正，`build-retry.log`及最终构建通过；本轮没有修改那个测试。

早期`schema-audit-*`日志记录验证器对中文词语数字与日文相邻数字的误报，以及随后实际补齐的显式数值。最终检查保留原始数值、单位和公式要求，不把中文“三维”翻译成“3D”误当成改变测量值。原始运行记录保留，不覆盖为成功。

`extraction.json`保留当前提取的来源和行号。`verified-identities.json`记录收尾时资源、实现和程序集的文件身份；没有复制整套源树、运行时或历史数据。静态提取不穷举任意运行时字符串和不透明辅助函数，需显式标记界面文字与数据。全程后台离屏，未控制或重启用户程序，未运行全仓数值矩阵。

## 复验

```powershell
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- check .
./tools/OpticalSystemDesign.Localization/VerifyPipeline.ps1 -OutputDirectory <output-directory>
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1
dotnet vstest tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll '/TestCaseFilter:FullyQualifiedName~LocalUiCatalogTests|FullyQualifiedName~UiLocalizationTests|FullyQualifiedName~SystemPropertiesLocalizationTests|FullyQualifiedName~LocalizedCommandSearchTests|FullyQualifiedName~IsekaiTypographyPolicyTests|FullyQualifiedName~MaterialLibraryPanelTests|FullyQualifiedName~MarkdownReportImageTests.ReportRibbonOpensOneIdleSettingsWindowAndExportsOnlyOnStart'
```

本机测试宿主需后台本机通信权限；不启动原生桌面窗口。CI新增三平台词库及失败控制检查；此处没有宣称远端CI已经执行通过。
