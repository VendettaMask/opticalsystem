# 镀膜权衡退步修复证据

本轮针对上一阶段明确列出的五组旧版退步，保留真实 n/k、固定层、制造界、原验收及默认协议。结果与边界见[专项报告](../../../../docs/COATING_TRADEOFF_REPAIR_2026-10-31.md)，机器汇总见[三臂结果](paired-results.json)、[完整性](integrity.json)、[最终清单](final-manifest.json)。

## 最终范围

- `inputs/`：41份上一阶段冻结输入，SHA-256逐份一致。含实际SiO2/Ta2O5快照及明确标为合成的跨材料控制；六份上一阶段新规格本轮不再称新盲测。
- `final-matrix/legacy-v1/`：3b43bb7f引擎；`previous-v2/`：上轮目标方向引擎；`after/`：本轮。各41次，全部保存完整实验、summary和独立物理点。
- 三臂同一Core/Application，仅Engine不同。`integrity.json`核对三臂与最终测试DLL、原验收源段、Core薄膜/材料/测量源码及固定参考。
- `reference-materials.json`：从原快照通过Core导出的实际n/k。`reference-results.json`为固定TFStudio原DLS/Newton的24份实际输出，`reference-final-native/`是最终Core按原门槛重验；`reference-comparison.json`明确两种指导目标及计算量不同。
- `final-matrix/tmmcore-comparison.json`：三臂3321组R/T/A与固定tmmcore对照，使用原2e-11阈值。
- `final-tests/coating-tradeoffs-final.trx`：最终完整镀膜168项；不与中间重复执行累计。

## 计算量和可重算结果

本轮不是相同总预算对照。DLS未完成窄带幅度目标除主调用≤N外，额外比较另一通用方向≤N，再校正≤ceil(sqrt(N))。高反预处理与质量阶段仍合计≤N。比较、校正、线性子问题、活动集限次、切空间试探、指导查询、密集调用及实际耗时均保留；同时后台运行的耗时不是受控速度基准。

实验的 `TargetDirectionTrials`保存两方向实际结果；`TargetRepairStartLayers` / `TargetRepairViolationCeilings`保存校正基线；`TargetRepairTrials`保存每步实际膜厚、正式指标、超限、收敛和是否保留。使用同一材料/目标/设置，将候选膜厚交给 `DesignService.Evaluate`可复算。新五组测试覆盖保存重开及物理复算；它们检查相对旧版不退步，允许明确恢复原水平，不放宽原峰值/截止收益回归或物理门槛。

## 失败与中间阶段

`probe-1`至`probe-18`、`tests/`保留辅助残差、不同归一化、独立替换优化轨迹、活动集条件数和截止极值诊断。早期方案损失原截止收益；probe 15展示引导OD到1而正式OD仍约0.999971的漏采。`hr-probe-1/2`是早期高反阶段，不能代替最终结果。`initial-matrix/`的两旧臂完成，新臂因宽SVD性能问题中止；probe 17触及5分钟资源时限，均记录为未完成。并行相机代码造成的构建中断、App复制重试和终止旧测试也不计为通过。

`baseline-source/`是本任务开始时的完整Engine C#源码快照（v2）。部分试验另保存局部源码，但不是每个失败都有完整构建快照；不补造所有中间版本的字节复现声明。所有历史阶段和原失败结果保留。

## 后台复放

从仓库根目录执行；Node仅用于离线参考和汇总，产品计算是C#/.NET。

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release --no-restore
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release --no-restore
```

建议将诊断输出中的根文件复制到独立运行目录，避免编译输出锁。下面以本机封存的新运行时重新生成一个独立输出目录，不覆盖已封存结果：

```powershell
$taskRoot = 'validation/rechecks/2026-10-31/coating-tradeoff-repair'
dotnet "$taskRoot/final-matrix/after-runtime/CoatingAudit.dll" quality-run "$taskRoot/inputs" "$taskRoot/replay-new"
dotnet "$taskRoot/final-matrix/after-runtime/CoatingAudit.dll" infeasible-export "$taskRoot/reference-inputs" "$taskRoot/replay-materials.json"
node tools/diagnostics/CoatingAudit20261030/reference-infeasible.mjs "$taskRoot/replay-materials.json" "$taskRoot/replay-reference.json"
dotnet "$taskRoot/final-matrix/after-runtime/CoatingAudit.dll" infeasible-reference "$taskRoot/reference-inputs" "$taskRoot/replay-reference.json" "$taskRoot/replay-reference-native"
```

`*-runtime/`是本机产物，Git忽略；清单保留实际DLL哈希。跨机器需用相应源版本重新构建旧Engine及共同运行时，并核对来源，不可拿新DLL冒称旧臂。v1源码来自3b43bb7f，v2来自本目录起点快照；两者历史物理结果也保留于[上一阶段](../coating-balanced-targets/README.md)。重新构建带入其他共享代码变化时，应视作新运行时重新验证，不能追认相同DLL。

最终汇总及封存脚本为 `summarize-tradeoff-repair.mjs`、`write-tradeoff-report.mjs`、`seal-tradeoff-repair.mjs`；先完成三臂、参考复验和最终测试。汇总同时检查各输入目标、层结构/固定层、厚度界、原门槛、通过状态、额外工作上限和二进制来源。未运行全仓、原生桌面或新材料实测认证。
