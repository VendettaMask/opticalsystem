# Zemax定义核查诊断

使用原图像模拟示例2与共享Core，在独立复制对象上诊断有焦参考模式、中间面选择和无效面号；不修改源文件、原比较请求或产品代码。

```powershell
dotnet run --project tools/diagnostics/ZemaxDefinitionReview20261104/ZemaxDefinitionReview20261104.csproj -c Release -- . artifacts/validation/zemax-definition-review-new
```

输出含20次Workbench分析请求及4组共享波前/像面光程探针。Absolute公式仅是依据官方说明、用共享追迹光程构造的独立诊断，不提供产品结果，不代表原生数值认证；不要将其作为其他参考模式的正确值。

`Seal.ps1`验证本批固定输出的两处复现与95份源哈希后封存、完整读回；不覆盖已存在的归档。缺口尚未修复，因此没有添加会让主测试集合持续失败的回归测试，也没有把诊断执行计成通过测试。

[结论及官方来源](../../../docs/ZEMAX_DEFINITION_REVIEW_2026-11-04.md)。
