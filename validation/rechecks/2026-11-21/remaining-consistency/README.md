# 剩余一致性问题的按序修复

接续[材料常数及主测试入口](../material-constants-and-test-entry/README.md)，按此前评估的顺序核对分析参数传递、支持边界、材料数据，以及主题/语言/导出一致性。保留其他任务正在进行的改动；不改变光学公式、原物理门槛或用户文件。

## 分析参数与来源

- 分析服务原先冻结工程后，把调用方的 `IReadOnlyDictionary` 原对象交给后台。只读接口不保证底层字典不再被界面修改。现在提交时同步复制参数，计算和请求指纹共享同一参数快照。
- 新回归在提交后把原字段改为非法视场/不同密度和参考，核对后台仍使用原请求、相同指纹和点数据，工程/修订号不变。
- [参数扫描工具](../../../../tools/diagnostics/AnalysisParameterAudit20261121/Program.cs)解析实际 C# 语法，记录 76 个参数分派分支、622 个静态声明键及共用工厂/包装器读取路径。[最初清单](parameters-before.json)的 12 项候选均为 Fourier 验证包装器读取，不是遗漏；[最终清单](parameters-final.json)明确这些读取，未消费候选为 0。动态构造的频率键和物理语义仍由现有行为回归核对，扫描本身不认证数值精度。
- 沿已有瞄准审查复验 270 处敏感调用，省略关闭默认值为 0，见[扫描日志](aiming-audit.log)和[逐调用记录](aiming-final.json)。保留专用接口的显式策略及原 API 默认值。

## 功能入口与支持边界

非序列示例按钮、分类和条目的提示与无障碍说明在打开前明确“先导入检查，全部语义和资产通过后才能应用”。搜索动作使用“检查非序列示例”。原始文件名、106 个动作身份和 16 类分组保持；实际检查仍逐文件判断，不把菜单收录当作完全支持，也不把已有可用案例误标为全部阻断。

## 材料数据与模型一致性

- 浏览缺失参考值的材料时，不再在无效波段外求 Nd/Vd；折射率表同时检查实际采样覆盖。部分波段只覆盖 d 时可显示 Nd，Vd 显示不可用。无效/未知公式、非有限参考值不再使浏览崩溃或显示伪有效常数。
- Vd 回退使用同一公式的未舍入 nF/nd/nC。有效目录参考值独立保留，不与公式求值混合。
- 复验发现部分材料的工程辅助定义与完整外部目录不同。材料库按钮显式选用 `GetGlasses` 展示的完整目录，避免重算另一组系数；`FindGlass` 和默认计算 API 保留工程解析、目录顺序和已保存模型，不修改追迹所用材料。
- DTO 发布 F-d-C 状态与参考温度/d 谱线热导数检查。详情与复制报告提示需核对的极值，使用与无热化图同一检查函数；这是检查提示，不是删除、缩放或修复原记录的规则。复制报告补齐详情中的原始 TD 七项。

### 厂商来源核对

从 [OHARA 官方下载页](https://www.ohara-inc.co.jp/en/product/catalog/)取得 2026-11-18 的全产品目录，原件为 [OHARA_261118.zip](vendor/OHARA_261118.zip)，解压的 [AGF](vendor/ohara/OHARA_261118.AGF) 第 15387 行为 TPH55。该版标为 discontinued，TD 六项为零、参考温度 25°C；这与旧内置 OHARA-A 的异常 TD 不同，但零记录不提供可确认的实测热系数，未据此猜测替换值或更新整个厂家目录。

| 文件 | SHA-256 |
| --- | --- |
| ZIP | `589cd0210d74d2bfd82d4fb5f4441677b693c143d35c03a26bd9a2b86b78e1ac` |
| AGF | `712c4921596764855a23feb8cbfb28a921f8fbd73c1340d49150fd25c5651a71` |

[HIKARI 官方目录页](https://www.hikari-g.co.jp/optical_glass/catalog/)已核对可用来源，但未取得对旧 7054 TD 的可确认替换记录；ANGSTROMLINK 的异常同样保留为需核对。不能把本轮提示改进称作全目录数据已认证。

## 界面与导出

材料状态使用共享语言绑定，波长标签补齐三语翻译。复制报告只翻译标签和应用说明，保留厂家原文和数值。Light/中文、Dark/日文、Isekai/英文及实际 100%/150%/200% 离屏检查覆盖新增提示、语言往返与报告源数据。

实际画面检查还发现Isekai材料波长字段的希腊μ缺字。Almendra包含微米前缀µ（U+00B5），不含希腊μ（U+03BC）；现将这两个字段与复制报告统一为µm，保留原单位和数值，不更换字体或使用机器字体回退。末轮三项界面检查专门核对单位文本和新画面。

四格式比较原测试混用了 Auto 来源图纸和旧 4:3 的 1440×1080 断言；现在明确冻结 4:3，并让普通 PNG 与报告使用同一设置。保留完整边框、原尺寸与逐像素相等断言，不以改变容差处理失败。最终结果见后续验证段。

## 最终验证

- 默认路径完整依赖图Release App严格构建通过，0警告、0错误：[最终产品构建](build-app-delivery.log)。主测试项目严格构建通过：[主测试构建](build-main-recheck.log)及[末轮构建](build-main-ui-delivery.log)。
- 标准 `dotnet test` 实际执行 **101/101通过、0失败/跳过**：[日志](tests-recheck.log)、[TRX](results/remaining-recheck.trx)。最后三项微米符号/界面复验全部通过：[日志](tests-ui-delivery.log)、[TRX](results/ui-delivery.trx)。[按最终案例去重统计](test-summary.json)仍为101，不把重复项加成104。
- 4项Word/PDF/Markdown/HTML测试各包含离焦点列与非相干照度的完整绘图、1440×1080原图纸、逐像素及报告包内图像核对；普通导出画布仍为1920×1080，外部留白不进入报告原图纸。6项边框检查覆盖Light/Dark的100%/125%/200%。这不是所有图表/Auto/主题的完整导出认证。
- 三张材料窗口画面已实际查看：[Light](captures/material-review-Light.png)、[Dark](captures/material-review-Dark.png)、[Isekai](captures/material-review-Isekai.png)。示例菜单/检查窗口与逐文件审核结果见[nsc](nsc/)，四格式实际报告和图像见[exports](exports/)。

复现标准主入口（先完成相同配置构建；需要本地testhost通信）：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR = Join-Path (Get-Location) '.tmp/remaining-frame-check'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 --filter 'FullyQualifiedName~AnalysisRequestSnapshotTests|FullyQualifiedName~MaterialDataQualityTests|FullyQualifiedName~MaterialLibraryConsistencyTests|FullyQualifiedName~MaterialConstantsTests|FullyQualifiedName~MaterialLibraryPanelTests|FullyQualifiedName~MaterialAnalysisTests|FullyQualifiedName~AthermalGlassMapTests|FullyQualifiedName~AnalysisAimingPropagationTests|FullyQualifiedName~ThroughFocusAimingTests|FullyQualifiedName~NonSequentialExampleTests|FullyQualifiedName~AnalysisCrossFormatExportTests|FullyQualifiedName~AnalysisImageExportTests.BorderStaysOutsideOriginalContentAcrossThemesAndPixelScales'
```

## 验证边界

首次运行 93 项，85 通过、8 失败，保留 [日志](tests-first.log) 与 [TRX](results/remaining-first.trx)。四项涉及目录/工程来源混淆，另四项涉及上述导出夹具；最终修复后须实际复验，不能把首次结果计为全通过。

中途主测试编译遇到同工作区其他测试尚在编辑的语法错误；再次编译后还发现两处 xUnit2031。后者仅改为等价的 `Assert.Single(collection, predicate)`，不改变数值或断言条件。默认构建另遇 SourceLink 文件并发占用，原日志保留，未关闭其他进程或用户程序。

只做后台/离屏定向验证，不运行完整解决方案、95 镜头精度矩阵、远端 CI 或原生交互认证。NuGetAudit 的本机离线覆盖不修改仓库/CI 策略，也不算在线漏洞检查通过。
