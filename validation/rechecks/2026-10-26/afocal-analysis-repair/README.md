# 无焦修正停止点

用户要求停止进一步运行并同步所有本地修正。代码范围、仍开放项目见[修正记录](../../../../docs/AFOCAL_ANALYSIS_REPAIR_2026-10-26.md)。

- `evidence.zip`：第一次定向测试失败日志、最终已完成 79/79 测试日志/TRX、随后诊断 Release 构建日志，以及 `fixed-v6` 的 20 份原始 Workbench 输出、请求与阶段比较摘要。
- `evidence-manifest.json` / `archive.json`：归档文件身份；不修改原始捕获。原生参考来自[完整复采包](../afocal-recompare/README.md)。
- `identity.json`：阶段 Core/Application 二进制指纹及未变更的全局策略指纹。`fixed-v6` 对应 runner-v8；已完成的测试阶段对应 runner-v13；最后构建为 runner-v14，不能将这些阶段合并为最后源码的全范围通过结论。

固定网格阶段 FFT PSF、Huygens PSF、Huygens MTF 各 5 Pass；Huygens Through Focus 为 2 Close / 3 Difference。最后源码的 150 项统一复跑、有焦 1159 + 52 守卫和实际安装验收均未完成。没有因停止而把剩余问题标成已解决。

原生数值回归夹具位于 `validation/zemax/2026-r1/afocal-analysis-repair-2026-10-26`。可复现命令（停止后未再次运行）：

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Afocal'
dotnet build tools/diagnostics/AfocalRepair20261026/AfocalRepair20261026.csproj -c Release --no-restore -m:2 -p:UseSharedCompilation=false
```

详细本机迭代仍在被忽略的 `artifacts/validation/afocal-repair-20261026` 中保留。实际出瞳坐标、完整圆边界和缓存瞄准实验已撤回；将参考中心改成主光线的诊断不是同设置认证。没有继续启动完整回归或安装包发布。
