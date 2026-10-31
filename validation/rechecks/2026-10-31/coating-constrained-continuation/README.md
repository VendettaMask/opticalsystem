# 镀膜约束续算与预处理证据

本轮对应[专项报告](../../../../docs/COATING_CONSTRAINED_CONTINUATION_2026-10-31.md)。原[权衡修复证据](../coating-tradeoff-repair/README.md)及其数值保持；本轮机器结果见[三臂汇总](paired-results.json)、[完整性](integrity.json)、[参考复验](reference-comparison.json)、[实际损失分解](physical-loss-witnesses.json)和[封存清单](final-manifest.json)。

## 输入、对照与物理边界

- `inputs/`共47份：原41份逐字节保留，另6份在首次查看其新结果前由`continuation-freeze`冻结，来源见`holdout-inputs/`。四份新窄带是明确合成控制，两个新高反使用原SiO2/Ta2O5实测快照。近可行控制后来用于发现阶段切换退步，因此最终六份作为公开交叉验证，不称未见盲验。
- `final-matrix/before/`为上轮Engine，`budget-only/`为上轮源码仅将恢复额度改为3ceil(sqrt(N))，`after/`为本轮；各47次。三臂使用字节相同的Core/Application，仅Engine不同。`control-source/`保留独立控制项目及源码，逐份核对除额度外无算法变化。
- 保留真实n/k、原目标、层顺序和变量标记、厚度界及Checks/Converged。新结果的逐项目标超限同时检查输入和上轮输出；完整通过仍要求采样收敛及所有原检查。
- 新反射率预处理与质量阶段仍合计≤N。窄带除主调用≤N、方向比较另≤N外，恢复含续算≤min(N,2ceil(sqrt(N)))，硬约束LS另≤ceil(sqrt(N))。续算次数是恢复的子集。额度控制的最大非线性轮数相同，但实际查询/QP/密集调用不同，不宣称同计算量优胜。

## 可复算数据

每个最终臂保存完整`.coating.json`、`summary.json`及`core-cases.json`。独立tmmcore点除原均匀网格外还包含实际峰值、半高边缘、最差截止和反射率点，仍使用原2e-11阈值。`physical-loss-witnesses.json`仅分解当前膜系的R/T/A，不作为全局吸收下界或无解证明。

结果保留两方向候选、续算触发时的正式最好膜厚/上界、每步正式指标/超限/对应上界/恢复误差/是否保留、恢复及硬约束LS停止原因和工作计数。候选可用原快照与膜厚交回`DesignService.Evaluate`复算。旧记录缺新字段保持空/零，输入指纹和schema1不变。

`reference-materials.json`从原快照经Core导出实际n/k；固定TFStudio的原DLS/Newton/SQP在18份相同起点上运行，54份输出由最终Core按原门槛重验，见`reference-results.json`及`reference-native/`。平方点目标和参考SQP的硬厚度箱界不等同于本地带宽/OD约束，不作同问题排名。原源码归档和MIT声明保持，Node仅用于离线参考。

## 中间诊断

`probe-1/`只把旧恢复窗口加倍，确认原第10步被额度截断，第11步得到可保留的prior-double-96结果。`probe-2/`为收缩续算和硬约束LS；`probe-3/4`比较不同高反预处理收缩条件。`probe-5-partial/`保留一组近可行高反质量轻微退步的中止新臂，随后加入按初始目标违约尺度判断的通用切换条件，完整新臂重跑。早期失败/中止不计为最终通过，部分原型只有局部源码快照，不补造全部字节重建声明。

`baseline-source/`是本任务开始时的Engine源码与共享不等式步快照。`tests/`保存定向试跑；`final-tests/coating-continuation-final.trx`为最终完整镀膜范围，不与重复试跑累计。

## 后台重放

从仓库根目录执行，使用独立输出目录，不覆盖封存结果：

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release --no-restore
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release --no-restore
$taskRoot = 'validation/rechecks/2026-10-31/coating-constrained-continuation'
dotnet "$taskRoot/final-matrix/after-runtime/CoatingAudit.dll" quality-run "$taskRoot/inputs" "$taskRoot/replay"
node tools/diagnostics/CoatingAudit20261030/reference-continuation.mjs "$taskRoot/reference-materials.json" "$taskRoot/replay-reference.json"
dotnet "$taskRoot/final-matrix/after-runtime/CoatingAudit.dll" infeasible-reference "$taskRoot/reference-inputs" "$taskRoot/replay-reference.json" "$taskRoot/replay-reference-native"
```

`*-runtime/`为本机已封存、Git忽略的产物，DLL哈希保留于清单。跨机器重建旧Engine或额度控制时须核对源版本与共同运行时；不能拿新DLL冒称旧版或追认相同产物。`summarize-continuation.mjs`、`write-continuation-report.mjs`、`seal-continuation.mjs`在三臂、参考和最终测试完成后生成汇总及清单。

全部后台运行，未操作用户桌面；未运行全仓或新增实测材料认证。其余真实材料高反及窄带未通过项按原门槛继续开放。
