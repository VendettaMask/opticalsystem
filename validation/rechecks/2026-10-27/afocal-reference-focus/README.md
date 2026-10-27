# 无焦参考中心与惠更斯离焦

范围、剩余差异及原生参考定义见[专题报告](../../../../docs/AFOCAL_REFERENCE_FOCUS_REPAIR_2026-10-27.md)。机器账目为 `summary.json`，原 150 项原始结论为 `analysis-results.json`，不覆盖上一轮数据。

- 原 150 项：129 Pass / 3 Close / 3 Difference / 15 Incomparable。两项 Core 相对照度明确不可用，未超时，不计通过。
- 固定角网格 20/20 Pass，其中五配置离焦 MTF 全部通过原门槛。
- 有焦 1159 + 52 原请求全部 Pass，参考/源/请求/容差保持；Core 385、比较 89、独立原生角质心 3，共 477 个不同定向测试通过。
- 原生图表参考切换的 3 个质心 Difference 保留；另以 10361 条独立原生光线、13 条视场 CDF 检查角质心，三组通过。原生内置图没有被改写或标成质心通过。

`evidence.zip` 保存当前无焦/固定角网格 Workbench 原始结果与请求、数值比较、25 个新原生诊断请求及结果、测试 TRX、阶段诊断、冻结证据校验和实际运行的三个自有数值程序集。`evidence-manifest.json` 与 `archive-integrity.json` 核对归档各文件字节。此前 150 项原生基准从[10 月 26 日完整归档](../../2026-10-26/afocal-recompare/README.md)读取，没有重写。

有焦完整当前输出约 1.5 GB 留在本机 `artifacts/validation/afocal-reference-focus-20261027`；归档保存逐请求完整指标摘要、二进制身份、原引用及全部输出的 SHA-256 清单，不声称 ZIP 包含全部有焦原始数组。`focal-output-hashes.json` 在 ZIP 中提供这些文件的身份。

复现入口为 `tools/diagnostics/AfocalReferenceFocus20261027`，命令从仓库根目录运行，新运行必须使用新输出目录：

```powershell
dotnet build tools/diagnostics/AfocalReferenceFocus20261027/AfocalReferenceFocus20261027.csproj -c Release
# audit 是原复采包解压后的 artifacts/validation/afocal-recompare-20261026
# runner 是本次构建的 AfocalReferenceFocus20261027.dll
dotnet $runner prepare $audit $fresh
dotnet $runner workbench $audit $fresh
dotnet $runner compare $audit $fresh
dotnet $runner prepare-fixed $audit $freshFixed
dotnet $runner workbench $audit $freshFixed
dotnet $runner compare $audit $freshFixed
dotnet $runner focal-guards $audit $freshFocal
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --filter 'FullyQualifiedName~Afocal'
```

通过同样构建的 Objectives20261020 运行 `objectives-sampling-controls` 和 `objectives-completion` 可复现 22 + 30 补充守卫；原冻结引用与请求身份保存在摘要中。新原生捕获使用仓库 C# Framework 后台 Host 和录入请求清单，需本机受许可的 OpticStudio 2026 R1，不能把生成的绝对路径请求直接当作跨机器配置。原生角质心期望值由 `Reduce-NativeCentroid.ps1` 独立从方向余弦计算经验 CDF，不调用产品积分或插值。

`Confirm-Evidence.ps1` 验证各集合、不可用项、原生参考切换、原引用、同一 Core 与 2668 冻结文件；`Pack-Evidence.ps1` 创建且逐文件读回校验归档。初版证明脚本曾错误地要求所有 150 个 Core 分析可用，随后明确检查两项无焦相对照度不可用；原计算未因此重跑或改成通过。频率插值和参考坐标诊断只用于定位，没有将这些实验植入运行时。

本轮只后台计算及无窗口构建，未控制用户桌面、安装、发布或推送；并行的自主设计界面本地修改保持。
