# 2026-10-31 受控代码审查复现

诊断工具仅使用产品现有 Application/Core，UI 通过离屏 Headless 运行。它不修复或改变产品代码，不读取用户当前处方。合成凹面镜的输入与聚焦后输出写入指定目录；结果以 JSON 保留。

```powershell
dotnet run --project tools/diagnostics/CodeReview20261031/CodeReview20261031.csproj -c Release -- <new-evidence-directory>
dotnet run --project tools/diagnostics/CodeReview20261031/CodeReview20261031.csproj -c Release -- <evidence-directory> --inventory
```

普通模式核对负间隔镜头聚焦、实际写入后的 RMS、非法数值文本和相机按钮三语覆盖。`--inventory` 读取实际原生操作数注册表并排除官方 Unused 项。运行结果描述当前缺陷，不是要求产品保留这些错误行为的默认回归测试。

结论和建议见[审查报告](../../../docs/CODE_REVIEW_2026-10-31.md)，本批原始数据见[证据](../../../validation/rechecks/2026-10-31/code-review/)；再次运行使用新目录以保留旧结果。

后续修复重放及 54 项去重回归见[修复证据](../../../validation/rechecks/2026-10-31/review-fixes/README.md)。`Verify-Fixes.ps1` 只读核对这些封存结果，不代替重新编译/执行。并行构建时可在诊断命令中传入 `-p:DirectoryBuildPropsPath=<absolute IsolatedVerification.props>`，隔离当前调用的中间与输出目录；仍需单独核实默认 App 构建，不能把隔离结果当作默认输出验证。
