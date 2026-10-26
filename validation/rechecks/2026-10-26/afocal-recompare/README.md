# 四无焦系统复采证据（2026-10-26）

[报告](../../../../docs/AFOCAL_RECOMPARE_2026-10-26.md)说明剩余问题和范围。[汇总](../../../../docs/validation/AFOCAL_RECOMPARE_2026-10-26.json)与[150 项账目](analysis-results.json)分别保存结论和逐项指标。原范围是 73 Pass / 4 Close / 20 Difference / 53 Incomparable，额外控制单列。

`evidence.zip` 保存本轮原始结果、设置读回、诊断、失败日志、模型快照、源文件及冻结运行二进制。`evidence-manifest.json` 逐文件记录 SHA-256；`archive-integrity.json` 记录压缩包和解压读取的逐项校验。压缩包中的文件使用仓库相对路径，原生报告自身保留实际运行时的绝对路径与时间，未重写原始证据。

本地原工作目录为 `artifacts/validation/afocal-recompare-20261026`。最终有效结果为 `baseline-v4/analysis-results.json` 中原 30 项，加 `expanded/compare-v3/summary.json` 的 120 项；不把基线中相同 120 个阻断再次相加。`expanded/compare-v1` 的绘图依赖失败、`compare-v2` 的 Windows 换行解析失败，以及最终汇总脚本首次字典分组失败均保留，不计为有效结论。

复现入口位于 `tools/diagnostics/AfocalRecompare20261026/Program.cs`。此运行器明确引用本轮冻结 `runners-v4` DLL；不能以重新编译的其他 Core 版本悄悄替换。准备/运行顺序：

```powershell
$root='artifacts/validation/afocal-recompare-20261026'
# 下列输出目录必须不存在；原始目录为已完成证据，不应覆盖。
dotnet "$root/expanded-runner-v6/AfocalRecompare20261026.dll" prepare-fixed-grid "$root/baseline-v4" "$root/new-fixed-grid-controls"
& tools/diagnostics/StandardMatrix20261015/Run-Native.ps1 -RunDirectory "$root/new-fixed-grid-controls"
dotnet "$root/expanded-runner-v6/AfocalRecompare20261026.dll" workbench "$root/baseline-v4" "$root/new-fixed-grid-controls"
dotnet "$root/expanded-runner-v6/AfocalRecompare20261026.dll" compare-v1 "$root/baseline-v4" "$root/new-fixed-grid-controls"
```

原生运行需要本机 OpticStudio 2026 R1 许可及原始 POB 资源。快照和原请求绑定捕获时的路径；在另一目录重算时，应生成新请求并记录路径变化，不修改已捕获请求。没有许可证时仅可核对已保存证据，不可宣称完成新原生运行。

定向测试命令：

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --filter 'FullyQualifiedName~RegistryCatalogSynchronizationTests|FullyQualifiedName~Afocal'
```

实际 52/52 通过。此次只重新比较和补足证据，产品剩余问题未在本轮修复，历史有焦 1159 + 52 请求也未在本轮重算。
