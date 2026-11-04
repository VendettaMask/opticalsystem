# 图像模拟跨列拾取诊断

纯C#，调用共享导入器、Workbench执行及原比较器。源文件来自现有示例；原请求、原错误导入快照及旧数值从11月4日全量归档按SHA256恢复。`fresh`使用相同原ZMX重新导入，普通模式保留旧错误快照，便于同Core归因；不修改冻结数据。

后台示例，使用新的空输出目录：

```powershell
dotnet build tools/diagnostics/ImageSimulationPropagation20261104/ImageSimulationPropagation20261104.csproj -c Release -o artifacts/validation/image-pickup-runtime
$indices=@(579..608)+@(669..698)
dotnet artifacts/validation/image-pickup-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/image-pickup-before @indices
dotnet artifacts/validation/image-pickup-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/image-pickup-after fresh @indices
dotnet artifacts/validation/image-pickup-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/image-pickup-imports imports
dotnet artifacts/validation/image-pickup-runtime/OpticalSystemDesign.Tests.dll . artifacts/validation/image-pickup-phase phase
```

`imports`核对95份源文件及全部配置相对原导入快照的结构变化；`phase`仅诊断两份原FFT网格，不放开产品保护。所有模式都保留原源文件、请求及参考；它们不是新的原生收敛采集。

`Finalize.ps1`使用本批固定路径验证60项A/B、600个原生载荷及156个不同测试，再内容寻址封存、完整读回。首次诊断Job路径元数据的更正在证据README明示；脚本不覆盖已封存归档，不应直接重复运行。

[修复原因、验证与剩余问题](../../../docs/IMAGE_SIMULATION_PICKUP_REPAIR_2026-11-04.md)。
