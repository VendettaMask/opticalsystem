# 38668 导入后的公共 Working F/# 修复

核验日期：2026-09-07。此记录针对用户截图中的 `zmax_38668.ZMX`，不使用此前的扩束镜代替复现文件。

## 实际输入与复现

原文件位于本机 Downloads；回归夹具 `tests/OptilandWorkbench.Tests/Fixtures/zmax_38668.ZMX` 保留原始字节。SHA-256 为 `8407918B31B4ADDD03BC0207368AC3571BE43549AADE2F5D395EFE703BC22684`。

文件包含 4 个表面、1 个轴上视场、C79-80 玻璃，入瞳直径 12.7 mm，固定净半口径 5.85 mm，机械半直径 6.35 mm，像方有焦。实际 `WAVM` 波长为 **355 nm**；备注中的“266nm Coated”是镀膜描述，不能覆盖波长记录。

修复前，通过 `WorkbenchApplication.Documents.OpenAsync` 导入原文件，再经 `IAnalysisService` 的产品默认设置运行 12 个分析，11 个因同一条 `Working-F-number ray did not reach the image surface.` 异常失败；已修复的全视场像差入口正常。证据保存在本地 `artifacts/validation/38668-working-f-number/38668-before.trx`。

此前修复仅把点列图的艾里斑计算分流到忽略表面口径的专用方法，MTF、PSF 及其它分析仍调用公共旧方法。入瞳边缘高度 6.35 mm 超过净半口径 5.85 mm，公共探测光线被真实口径截断，导致整项分析中止。这是公共计算规则不一致和实际文件回归覆盖不足，不是用户操作错误。

## 已实现的统一规则

- 公共 `WorkingFNumber(s)` 计算尺度时忽略表面口径，保留视场渐晕因子；采用主光线与四条边缘光线的数值孔径。几何追迹仍失败时，在更小瞳孔区域探测并外推至完整瞳孔。这里的区域序列是本软件的数值策略，不声称与 Zemax 内部步长相同。
- 点列图艾里斑、FFT/Huygens PSF、MTF 和关联分析共用该实现，删除点列图专用分支。根据 [Ansys 官方 Working F/# 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Working_F.html)，工作 F 数的尺度探测与物理口径截光需要分别处理。
- 真实波前、偏振光瞳和几何光线仍按物理口径截光。不会放大有效口径、替换玻璃，或让原本被挡住的光线参与衍射计算。
- FFT PSF 遵循系统瞄准设置；高 NA 发射几何失败时仍整体切换波前和偏振采样的瞄准方式。预计算光瞳与瞄准方式不符时要求重新生成，不能仅更换尺度。
- FFT MTF 沿用输入 PSF 的视场尺度，兼容缺少子午/弧矢独立尺度的结果时使用该 PSF 自身的工作 F 数，避免重新用轴上探测覆盖离轴尺度。
- 实际光瞳无任何通光样本时明确返回分析不可用；不把全零 PSF 作为成功。光瞳计数平方使用浮点数，避免高采样时整数溢出。

## 数值与入口验证

`Imported38668AnalysisTests` 从真实导入、文档快照和产品默认参数运行以下 12 个入口，均得到有效结果：MTF、PSF、FFT PSF 截面、Huygens MTF、Huygens PSF、Fourier 离焦 MTF、Fourier MTF 随视场、对比度损失图、色焦移、横向色差、圈入能量、全视场像差。对比度损失图的瞳孔外/截光区域保留无效值掩码，不计为有限结果点。

独立物理检查包括：

- 球面折射加平面出射的斯涅尔定律计算得到工作 F 数 `23.60628011984837`，公共引擎得到 `23.606280119854798`。此值与状态栏的近轴 `EFL / ENPD` 属于不同定义。
- 去掉 OPD 后，以 `5.85 / 6.35` 的实际通光瞳孔比例计算解析圆孔 MTF，在五个非零频率位置检查子午/弧矢方向，绝对误差不超过 0.005。该检查验证截光仍参与衍射，并非仅验证窗口不报错。
- 有/无瞄准均保留物理截光和镜头原始口径；全部光线被挡时返回不可用；512 × 512 光瞳的零 OPD 峰值归一化保持为 1。
- 高 NA 夹具保留自动瞄准与显式瞄准逐像素一致性、预计算相位、取消及失败路径检查。离轴 PSF 转 MTF 保留原空间频率尺度。

## 验证边界

本次定向测试 **86/86 通过，0 失败、0 跳过**（耗时 7 分 15 秒）：原文件 18 项、公共 Working F/#/高 NA/离轴尺度 14 项、MTF 设置与频率 15 项、Huygens 采样 14 项、无焦 10 项、圈入能量变体 6 项、固定 Zemax Huygens/能量数值回归 9 项。默认目录的完整解决方案 Debug、Release 构建均为 0 警告、0 错误；格式及差异检查通过。不更新历史全量测试基线。

可复验命令：

```powershell
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore --filter 'FullyQualifiedName~Imported38668AnalysisTests|FullyQualifiedName~PsfWorkingFNumberRegressionTests|FullyQualifiedName~MtfMaximumFrequencyTests|FullyQualifiedName~ZemaxHuygens|FullyQualifiedName~ZemaxEncircledEnergyParityTests|FullyQualifiedName~HuygensMtfSamplingTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~EncircledEnergyVariantTests' --logger 'trx;LogFileName=38668-final-regressions.trx' --results-directory artifacts/validation/38668-working-f-number /m:1 /nr:false -v:q
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet build OptilandWorkbench.slnx -c Release --no-restore /m:1 /nr:false
dotnet format OptilandWorkbench.slnx --no-restore --verify-no-changes
```

默认 Debug 桌面程序通过现有 `--capture-analysis-gui` 入口导入原文件，以空覆盖设置清单运行产品默认 MTF。真实 Avalonia 分析面板已显示完整曲线和“已同步”状态；截图及捕获清单在本地 `artifacts/validation/38668-working-f-number/gui/`。这是实际界面验收，不能代替数值比较。

上述理论校验不是新的 Zemax 实机数值捕获。已尝试比较工具对原文件的 MTF 捕获，但本机未找到 ZOS-API，报告为 `Zemax captured: 0`、`Workbench captured: 1`、`Error: 1`；该结果不能记为 Zemax 一致性通过。详情保留在本地 `artifacts/validation/38668-working-f-number/live-zemax/COMPARISON_REPORT.md`。

已有 `123456.ZMX` 的 Zemax 2026 R1 固定捕获及容差保持不变；其回归只代表已捕获镜头及设置，不代表所有导入文件、所有分析均已对齐 Zemax。当前定向测试与默认桌面构建记录见 [构建与发布](BUILD_AND_RELEASE.md)。
