# 扩束镜 FFT PSF 逐点形状残差修复（2026-10-29）

沿用[先行峰值修复](../beam-fft-psf/README.md)的原处方、自动/固定两份请求及冻结OpticStudio 2026 R1参考。本轮关闭其剩余48/140点超限。机器记录为[verification.json](verification.json)；[evidence.zip](evidence.zip)只包含红/绿TRX、拆分摘要与完整性报告，不复制原生数组。未启动Zemax或操作用户桌面。

## 误差拆分

对两份原请求分别导出相同追迹节点的功率和原/已处理相位，离线按已知坐标与傅里叶约定重建PSF。重建与修复前产品输出的最大差为3.89e-15/4.00e-15，先确认诊断重现的是同一计算，未做方向搜索、峰值拟合、网格移动或参考数组重采样。原接纳节点分别1489/933个。

| 仅替换的分量 | 自动网格最大绝对差 / 超限点 | 固定角网格最大绝对差 / 超限点 |
| --- | --- | --- |
| 原产品振幅与相位 | 3.14182e-4 / 48 | 5.14197e-4 / 140 |
| 相位置零，振幅保持 | 3.20063e-4 / 48 | 5.19091e-4 / 148 |
| 原追迹相位，振幅保持 | 3.13957e-4 / 48 | 5.13898e-4 / 140 |
| 四邻点振幅算术平均，相位保持 | 7.21867e-13 / 0 | 4.81871e-9 / 0 |
| 先平均功率再开方，相位保持 | 2.71762e-4 / 48 | 4.36057e-4 / 140 |

置零相位未解决形状残差，功率平均也不解决；振幅平均独立解释两份原请求。平均只在内部偶棋盘节点、四个方向的邻点全有效时应用，包含中心；保持所有边缘/受阻节点的原值和同一组相位。另一个保留中心振幅的控制仍留下3.11e-6/8.37e-6残差；完整控制见证据内`decomposition.json`，未据该控制选择宽松判据。

## 冻结复数场独立核验

沿用既有归档中的另一份32/64扩束镜Real/Imaginary控制，不将其冒充两份原64/128请求。按捕获的固定傅里叶约定逆变换，得到405个非零瞳孔节点，与独立圆瞳枚举的405节点完全对应。以同一已知G=2照明计算：直接高斯振幅的最大差为0.0155036；四邻点振幅算术平均后为1.03337e-8；功率平均后仍为0.00577811。C#回归读取原归档、校验归档SHA、采样和选择器，并验证完整接纳掩码及振幅，不使用拟合高斯因子。

该控制的32节点间隔来自其单独请求的`1/(2 sqrt(32))`；精确闭圆枚举使用整数半径平方128，避免重写原网格时引入圆边界舍入分支。没有据辅助控制改动两份原请求的间隔。32/64与两份原64/128结果互相支持，而非以一份捕获外推任意系统。

网络依据：[Ansys关于FFT/Huygens的说明](https://optics.ansys.com/hc/en-us/articles/42661795535251-What-is-the-difference-between-the-FFT-and-Huygens-PSF)将FFT PSF描述为出瞳复振幅的傅里叶传播；[2026 R1 FFT PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)及[高斯照明定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Apodization_Type.html)支持振幅、强度和无像差参考的区分。**具体棋盘插值规则来自冻结复数场和原请求控制，不声称官方公开了全部内部实现。**

## 产品修复

[共享Core](../../../../src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs)新增显式`interpolateScalarPupilAmplitude`选项，默认false。它在原生FFT采样的32及以上二次幂标量网格上，把符合条件节点的振幅作四邻点算术平均后恢复功率；不修改源波前对象、发射功率记录、相位、样本坐标或接纳集合。缺邻点或零功率不跨边界补值，偏振/非原生采样的错误组合明确拒绝。

[PsfAnalysis](../../../../src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs)仅在兼容模式且未使用偏振时选择该采样模型。其二维图、截图/数据及既有截面共享修复；同一处理后的复瞳孔用于计算无像差参考，包括ignoreOpd理想控制。低层通用FFT默认、已有准备波前调用、偏振、MTF和圈入能量路径没有被隐式切换。没有增加界面开关、镜头名称分支、拟合系数或第二个计算引擎。

## 正式产品逐点复验

两份原请求仍为同一扩束镜、配置1、视场1、波长1、Sampling64/Display128、Normalize=false、UsePolarization=false，分别使用原自动角间距和原固定0.005 mrad。每份16384点、严格坐标对齐、覆盖率1。

| 指标 | 自动网格 | 固定角网格 |
| --- | ---: | ---: |
| 原剩余超限点 → 修复后 | 48 → **0** | 140 → **0** |
| 全图最大绝对差 | 7.26863e-13 | 4.81871e-9 |
| 峰值绝对差 | 7.26863e-13 | 5.20239e-12 |
| 全图NRMSE | 1.91036e-14 | 2.56642e-10 |
| 有限窗口积分相对差 | −5.0e-15 | +1.25708e-8 |

全部点分别满足原逐点门槛`|ΔI| ≤ 1e-5 + 0.001 × |Iref|`，没有依靠NRMSE替代逐点验收。原策略LF身份仍为`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。这证明限定请求下的PSF一致，不是任意镜头、绝对辐射功率或整个无焦矩阵的认证。

## 验证与复现

- `shape-before.trx`：新增全逐点断言实际2失败，复现48/140点；`shape-after.trx`：相同断言2通过。
- `shape-targeted.trx`：73通过、0失败/跳过。涵盖正式PSF契约、两份完整逐点比较、冻结复数场、中心/边缘/零功率与输入不变性、独立高斯振幅闭式期望/Parseval、相位与多波长及无焦能量保护。
- `shape-protection.trx`：直接编译5份原测试源码，46通过、0失败/跳过，覆盖原有有焦、原生输入、偏振、采样和取消。两组去重为119个不同用例，不与先行116项累计为235项。
- 默认Release App构建零警告、零错误；App与两组测试使用同一Core SHA-256。原2668份冻结文件HEAD和工作副本完整性通过。文档链接与差异空白检查另存机器账目。

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AfocalPsfContractTests|FullyQualifiedName~FftPsfNormalizationTests|FullyQualifiedName~AfocalIndependentPhysicsTests|FullyQualifiedName~AfocalFftEnergyTests|FullyQualifiedName~FftPupilAmplitudeInterpolationTests'
dotnet test tools/diagnostics/BeamFftPsf20261029/FocusedFftRegression.csproj -c Release --no-restore
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore /m:1 /nr:false
```

测试源码为[逐点比较](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPsfContractTests.cs)、[振幅控制](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPupilAmplitudeInterpolationTests.cs)、[独立理想参考](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPsfNormalizationTests.cs)。[诊断导出器](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/BeamPsfShapeDiagnostics.cs)仅在显式设置`OPTICAL_FFT_PSF_DIAGNOSTICS`时写入指定生成目录，不影响正式结果。原始导出含复用的参考数组，仅留本机生成目录；仓库保留摘要/原请求身份与既有归档引用。

[离线拆分工具](../../../../tools/diagnostics/BeamFftPsf20261029/Diagnose-Shape.py)读取本轮修复前的生成文件，验证独立傅里叶重建及控制；[汇总器](../../../../tools/diagnostics/BeamFftPsf20261029/Collect-ShapeEvidence.py)只整理已完成结果和哈希。这些Python工具不参与产品运行。未重跑完整150项/95文件、重新采集Zemax、运行原生GUI/CUDA、安装或发布；共享工作区其他功能不在本轮光学认证范围。
