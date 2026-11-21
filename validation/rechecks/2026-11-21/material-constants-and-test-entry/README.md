# 材料常数、色散标签与主测试入口

2026-11-21，按用户指定顺序处理“计算 Nd/Vd”、色散系数标签和主测试入口。此前独立公式核查保留在[原始记录](../glass-formula-audit/README.md)。

## 已实现

- 按钮经 `IMaterialCatalogService.CalculateGlassConstants` 调用共享 C# Core 的实际材料色散模型/折射率表，以 F=486.1327 nm、d=587.5618 nm、C=656.2725 nm 计算折射率和 `(nd−1)/(nF−nC)`。不再只刷新优先采用目录参考值的 DTO。
- 主数值与复制报告显示计算结果，来源行保留目录参考值。APEL:APL5014CL 从目录 1.544500 / 56.000 切换为实算 1.544502 / 55.987；未舍入结果为 1.544501600284279 / 55.98699089030721。切换材料恢复该材料的浏览值，不修改材料、工程或事件修订号。
- 计算前核对材料波段和实际折射率表覆盖；范围外不以表格端点外推冒充可见谱线结果。不支持模型、数据无效、Vd 无定义分别返回明确状态；无选择时禁用按钮。零色散时 Nd 仍可用，Vd 显示“—”。
- 系数根据实际 `Formula` 区分；不靠展示名称或可能不一致的附加公式编号分派。保留额外 CD 源字段，包括零填充，标记 `*` 并说明不参与当前公式。通用模型保留常数项/幂次，数据超过十项时动态扩展，避免截断。
- 主测试项目直接引用相机与装调引擎，保留原类型命名空间和原测试。修复前实际编译出现 63 个缺失类型错误，见 [main-build-before.log](main-build-before.log)。
- 主入口构建还暴露两处当前 NSC 源码编译错误：补齐文档现有查找模式对应的 `FindObject(Guid)`；网格校验中的错误变量改为实际循环变量 `item.FaceNumber`。这两项仅为编译修复，不代表 NSC 全功能验收。
- 保留无热化图异常点检查含义，将 `Assert.True(Any(...))` 改为 `Assert.Contains`，消除 xUnit2012，使严格 CI 编译可用。

## AGF 系数映射

| 模型 | 显示符号（源顺序） |
| --- | --- |
| 1 Schott | A0…A5 |
| 2 Sellmeier 1 | K1, L1, K2, L2, K3, L3 |
| 3 Herzberger | A, B, C, D, E, F |
| 4 Sellmeier 2 | A, B1, λ1, B2, λ2 |
| 5 Conrady | n0, A, B |
| 6 Sellmeier 3 | K1/L1…K4/L4 |
| 7/8 Handbook of Optics 1/2 | A, B, C, D |
| 9 Sellmeier 4 | A, B, C, D, E |
| 10 Extended 1 | A0…A7 |
| 11 Sellmeier 5 | K1/L1…K5/L5 |
| 12 Extended 2 | A0…A7 |
| 13 Extended 3 | A0…A8 |

定义核对来源为 [OpticStudio 2026 R1 玻璃色散公式](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Glass_Dispersion_Formulas.html)，包括 [Sellmeier 1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Sellmeier_1_Formula.html)、[Sellmeier 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Sellmeier_2_Formula.html)和 [Conrady](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Conrady_Formula.html)。未修改这些既有光学色散公式。

## 验证与环境

默认路径 Release 主程序和主测试项目严格构建均通过，各为 0 警告、0 错误，见 [app-build-final.log](app-build-final.log) 与 [main-build-final.log](main-build-final.log)。`ContinuousIntegrationBuild=true` 保持警告即错误；本机关闭在线 NuGet 查询的范围另见下文。

标准 `dotnet test` 主入口实际执行 **53/53 通过，0 失败，0 跳过**，见 [原始日志](main-tests-final.log)、[TRX](results/main-final.trx) 与[按类统计](test-summary.json)。材料常数新增 21 项；已有材料详情 9 项、材料分析 9 项、相机 CFA 采样 4 项、实验室交付/共享 Core 7 项、NSC 文档集合 1 项、本地化 2 项。没有移除测试或放宽数值阈值。

新增用例覆盖真实 APEL 计算、修改系数会改变结果、实际表格/覆盖范围、无效/不支持/零色散状态、13 种 AGF 标签和源值顺序、通用模型常数及幂次、按钮与报告一致、材料切换清除重算状态、范围外材料显示原因、无选择时禁用及工程不被修改。

已实际查看三张离屏截图：

| 主题 / 语言 / 窗口 DIP / 实际渲染倍率 | 画面 |
| --- | --- |
| Light / 中文 / 1400×960 / 100% | [计算结果及目录参考值](captures/material-constants-Light.png) |
| Dark / 英文 / 1100×960 / 150% | [来源说明及详情滚动](captures/material-constants-Dark.png) |
| Isekai / 英文 / 900×1100 / 200% | [窄窗上下布局及来源说明](captures/material-constants-Isekai.png) |

新增来源说明、系数附注与按钮沿用三语翻译；截图中的既有状态值及部分波长标签仍有中文残留，本轮不声称整页本地化完成。此次保留现有主题字形与布局，没有用截图替代数值断言。

原受限环境标准测试运行在 testhost 连接阶段中止，见 [main-tests.log](main-tests.log)，不计通过。允许本地通信后，诊断运行已完成发现；初次诊断筛选拼写未命中用例，因此其零退出码也不计通过，见 [testhost-diagnostic.log](testhost-diagnostic.log)。最终运行使用上述真实匹配的筛选，核对了 53 条执行结果与 TRX。

复现命令（仓库根目录，先还原依赖；`NuGetAudit=false` 仅为本机离线覆盖）：

```powershell
dotnet restore tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -m:1 -p:NuGetAudit=false
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore -m:1 -p:ContinuousIntegrationBuild=true -p:NuGetAudit=false
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -p:BuildProjectReferences=false -p:ContinuousIntegrationBuild=true -p:NuGetAudit=false
$env:OPTICAL_MATERIAL_CONSTANTS_CAPTURE_DIR = Join-Path (Get-Location) 'validation/rechecks/2026-11-21/material-constants-and-test-entry/captures'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 --filter 'FullyQualifiedName~MaterialConstantsTests|FullyQualifiedName~MaterialLibraryPanelTests|FullyQualifiedName~MaterialAnalysisTests|FullyQualifiedName~CameraPixelAreaTests.AreaAveragePrecedesPhysicalPixelCfaSelection|FullyQualifiedName~BuiltInModuleDeliveryTests|FullyQualifiedName~NonSequentialDocumentTests.DocumentCollectionsDoNotExposeMutableBackingLists|FullyQualifiedName~UiLocalizationTests' --logger 'trx;LogFileName=main-final.trx' --results-directory validation/rechecks/2026-11-21/material-constants-and-test-entry/results
```

本地还原/构建使用 `-p:NuGetAudit=false` 避免不可达的漏洞数据查询；仓库 NuGetAudit/CI 设置未关闭，不能据此声称在线依赖漏洞审计通过。主项目夹具门禁实际校验/物化 524 份文件，清单 SHA-256 为 `bb5ad46409c5fc4fe5d7a7b11ee4fc89ab3f20fe791cada1c63845e40ff798ea`。

没有重启或控制用户正在运行的桌面应用；UI 检查使用离屏渲染。未运行完整解决方案测试、95 镜头精度矩阵、原生交互认证或远端 CI。本次仍是材料库只读浏览与重算，未增加目录系数编辑器。
