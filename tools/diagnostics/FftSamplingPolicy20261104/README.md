# FFT采样规则独立诊断

纯C#，使用共享Core和原Workbench分析工厂。输入从11月4日全量归档按完整SHA256恢复；原产品路径保留采样保护。独立诊断额外读取有限傅里叶和并运行1/2/4/8倍名义采样，不能作为产品默认补采样或原生收敛认证。

在仓库根目录后台执行，输出使用新的空目录：

```powershell
dotnet build tools/diagnostics/FftSamplingPolicy20261104/FftSamplingPolicy20261104.csproj -c Release -o artifacts/validation/fft-rule-runtime
dotnet artifacts/validation/fft-rule-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/fft-rule-main
dotnet artifacts/validation/fft-rule-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/fft-rule-afocal afocal
dotnet artifacts/validation/fft-rule-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/fft-rule-comparison.json summarize artifacts/validation/fft-rule-main artifacts/validation/fft-rule-afocal
```

主范围默认30项；也可在输出目录后传整数请求编号以缩小范围。工具程序集沿用Core现有测试友元名，仅为诊断，无产品依赖。

`Verify-And-Seal.ps1`校验本批固定路径的31项、120份前置/最终数值和29项测试，随后内容寻址封存并完整读回；它不覆盖已有证据包，后续批次应使用新的输出身份。不可重复运行以覆盖本批归档。

原物理节点全部比较；PSF使用原固定像面，曲线使用原节点线性插值，不外推。判定调用原比较器和冻结配置，包含NRMSE组合分支；最大差不单独决定Pass。比较结果仅是Core自加密。

[本轮规则、结果和未完成项](../../../docs/FFT_SAMPLING_RULE_AUDIT_2026-11-04.md)。
