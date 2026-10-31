# 镀膜进展续算证据（2026-10-31）

本轮47规格，94份新优化；所有比较以最终测试Core及anchored-band-domain/1冷复算。178/178镀膜测试通过。完整结论、失败试探和剩余未达标项见[报告](../../../../docs/COATING_PROGRESS_ALLOCATION_2026-10-31.md)。

- inputs逐字节复用上一阶段47份；baseline-source为本轮开始时Engine，control-source只把上一阶段旧额度控制的恢复上限改为N。
- final-matrix/after-raw与budget-only-raw保存新优化原输出；after、budget-only、before、prior-budget-only保存统一冷复算。冷复算保留原Run工作量，另列原指标/判定及逐点一致性；不是重复优化。
- final-matrix/*-runtime本地冻结运行时被gitignore排除，SHA-256见integrity及最终manifest；evaluator-runtime与最终测试三DLL相同。报告披露优化Core与最终测试Core的非镀膜二进制变化，未隐瞒身份差异。
- probe-1保存旧采样数值目标完成但未收敛的紫光；probe-2保存全程高反度量替换的退步；其余probe仅诊断，不替代最终矩阵。
- sampling-trace-final保留原网格及固定端点的五级差异；原阈值不变。tests保存未完成/失败聚焦TRX，final-tests保存完整测试；interrupted-runs明确取消、测试修正与输出锁。
- paired-results记录逐项超限、固定层、bounds、预算、判定及回归守卫。reference-results为54次固定上游DLS/Newton/SQP；reference-native按原Core门槛复验，目标定义不同。

复跑（仓库根目录；纯C#产品，Node仅离线对照）：

~~~powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run validation/rechecks/2026-10-31/coating-progress-allocation/inputs <new-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck validation/rechecks/2026-10-31/coating-progress-allocation/inputs <raw-output> <cold-output>
node tools/diagnostics/CoatingAudit20261030/reference-continuation.mjs validation/rechecks/2026-10-31/coating-progress-allocation/reference-materials.json <reference-results>
node tools/diagnostics/CoatingAudit20261030/summarize-allocation.mjs validation/rechecks/2026-10-31/coating-progress-allocation .tmp/coating-edge-audit-20261030/package
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj --no-restore
~~~

quality-resume仅复用已有完整实验文件，重新收集指导/验收身份，不把旧优化计作新执行；冷复算和优化计数分别披露。最终manifest列出本范围文件哈希，不包含自身、忽略的运行时/构建目录及已复制到正式目录的final2-matrix工作副本。源与旧报告保持可追溯，不从验证输出向产品注入候选。
