# 镀膜物理余量与连续状态验证（2026-10-31）

55规格、三臂同一最终采样冷复算；本轮新优化71次，历史控制复用94份。完整镀膜181/181通过。结论、代价和剩余未达标项见[报告](../../../../docs/COATING_MARGIN_REFINEMENT_2026-10-31.md)。

- inputs含原47份字节不变的输入和8份预冻结新规格；input-provenance核对n/k快照完全不变，初始相位/新目标/固定变量/匹配厚度界明确声明。
- baseline-source和baseline-runtime冻结本轮前Engine；control-source完全复用前阶段旧N控制；final-source保存本轮全部Engine源码。*-runtime和bin/obj忽略，SHA-256留在integrity/manifest。
- final-matrix/*-raw保留原优化输出，三个非raw目录统一冷复算，原Run工作量、原通过状态另存。before/budget-only各47份历史加8份新跑；after全部55份新跑。不能把历史复算称作新优化。
- probe-1定位恢复峰值锚点耦合并恢复旧N控制轨迹；probe-2记录余量目标；probe-3记录高反状态窗口。最终矩阵替代探针作为结论。
- PassbandQualityTrials逐点保存实际T、宽度误差、每带OD和原超限边界；ReflectanceMetricWindows保存实际损失、接受/收敛、起止阻尼与额度。拒绝点不推进正式保留点。
- paired-results保存所有精确分数和逐项差异，包含仍未达标或末位较差项。reference-*为26规格×3固定参考方法，点目标与本地带指标目标不同。
- tests保存初次非镀膜编译阻塞及3项聚焦回归；final-tests是冻结默认输出的完整TRX。不得把未完成构建或探针冒充完整通过。

仓库根目录复跑：

~~~powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run validation/rechecks/2026-10-31/coating-margin-refinement/inputs <new-raw-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck validation/rechecks/2026-10-31/coating-margin-refinement/inputs <raw-output> <cold-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-physics <cold-output> <physics.json>
node tools/diagnostics/CoatingAudit20261030/reference-continuation.mjs validation/rechecks/2026-10-31/coating-margin-refinement/reference-materials.json <reference-results>
node tools/diagnostics/CoatingAudit20261030/summarize-margin.mjs validation/rechecks/2026-10-31/coating-margin-refinement .tmp/coating-edge-audit-20261030/package
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj --no-restore
~~~

产品仍是共享Core的纯C#/.NET；Node仅离线参考/取证。最终manifest排除自身与忽略的运行时/构建目录，其余范围文件逐项SHA-256，不向产品运行路径注入验证候选。
