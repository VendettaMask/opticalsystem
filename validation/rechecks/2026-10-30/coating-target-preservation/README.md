# 峰值T/截止OD逐项目标保护：冻结证据

2026-10-30，实现及完整限制见[报告](../../../../docs/COATING_TARGET_PRESERVATION_2026-10-30.md)。本轮保持真实n/k、原制造/固定层/验收门槛及默认协议。

## 最终账目

- [inputs](inputs/)：35份完整冻结输入；原24组、5份不同历史起点/继续计算状态、6组新合成规格。新组合在配对运行前一次冻结，之后未改目标、材料或预算。
- [paired-results.json](paired-results.json)：70/70次完成，旧版8组增加某项目标超限，新版0组；原22组合格全部保持，31组相对各自输入严格改善，8组相对旧版选优分数更好。完整逐项超限、原/旧/新OD、指标、成本及失败都在文件中。
- [最终旧臂](final-matrix/before/summary.json) / [新臂](final-matrix/after/summary.json)：对应完整`.coating.json`可打开复算。每对输入指纹、目标/设置、材料及固定层相同；连续预算相同，实际工作不等价，不宣称提速或全局最优。
- [final-tests/coating-target-preservation-final.trx](final-tests/coating-target-preservation-final.trx)：完整模块154/154，无失败/跳过。原140项加14项新增保护/物理/回归，分组不能重复累计。
- [1890点实际Core输入/输出](final-matrix/core-cases-all.json) / [独立tmmcore结果](final-matrix/tmmcore-comparison.json)：最大绝对差R`5.329070518200751e-15`、T`7.549516567451064e-15`、A`6.106226635438361e-15`，门槛仍`2e-11`。
- [integrity.json](integrity.json)：原验收、前向/膜厚导数、原测量、材料及默认协议保持；两臂Core/Application完全相同，新Engine与最终测试相同；归档、选定参考执行源码和实际tmmcore依赖保持。

保护针对原输入每个目标的超限量。合格指标可使用仍在原门槛内的裕量，未完成指标不能恶化；结果原始数值不要求在合格区内全部单调。原真实高反和窄带完整规格仍失败，结果没有重新标成通过。

## 固定参考

[reference-materials.json](reference-materials.json)从同一共享Core导出8个冻结起点的真实n/k，参考查询未导出波长时拒绝回退；[reference-results.json](reference-results.json)记录固定TFStudio DLS/Newton的16次实际运行。[reference-final-native](reference-final-native/summary.json)用最终运行时复验参考膜厚；[reference-comparison.json](reference-comparison.json)记录逐项目标：1份通过原规格，14份相对输入有至少一项超限增加。

参考使用自己的谱点平方目标，与本地实际FWHM/OD和逐项保护目标不同，不作同问题/同总成本排名。`reference-inputs`是上述8个完整输入；`reference-native`为较早同物理定义复验，最终以`reference-final-native`为准。JS仅作离线参考，不是产品依赖。

## 失败与中间版本

- [red/target-preservation-before.trx](red/target-preservation-before.trx)：原引擎3个保护用例1通过/2失败，明确复现真实单腔峰值、双腔OD退步。
- `probe-1`排名扫描错误使用必须含目标锚点的约束系统，异常后目录不完整，不算完成。随后改用共享Core普通光谱扫描。
- `probe-2/3/4`、`reflectance-probe-1`保留保护目标、尺度归一化和高反重启试验，不能混入最终矩阵。中间有本任务诊断进程占用DLL导致复制重试，进程完成后正常构建；未关闭用户应用。
- `scale-before`、`scale-grid`、`scale-trace`及相应TRX保存尺度失败、归一化谱点/Jacobian定位；`scale-and-protection*`、`target-and-general`、`scale-grid-and-peak`保留失败。最后固定指导网格、峰值起点、重复节点及精度邻近活动性，原尺度门槛不变。
- `initial-matrix`保存第一次35组对比：原22组通过降到20组，暴露斜入射单腔与固定层双腔的可行路径阻塞。`tests/initial-full-152.trx`是当时152项全通过，不能替代这两个后续新增控制。
- `continuation-probe-1`尝试放宽探索界并提高目标罚项，未恢复固定层控制，真实材料多个起点也无进展，未采用。最终按实际峰值/截止目标是否已完成，选择联合保护或原带形恢复方向，两者都执行逐项保留/密集保护。
- `restoration-probe-1`恢复两个新控制的通过并保留真实材料改善；`tests/restoration-and-protection.trx`16/16通过。随后增加来源/阶段记录并完成最终154项与70次运行。旧152项记录和最终154项记录均保留。

`baseline-source`/`baseline-runtime`是本轮之前的`worst-reflectance/2`阶段，不能用上一任务更早的质量基线代替。原反射率可行区15组的持续改善仍保持。

## 重放

```powershell
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release
dotnet validation/rechecks/2026-10-30/coating-target-preservation/final-matrix/before-runtime/CoatingAudit.dll quality-run validation/rechecks/2026-10-30/coating-target-preservation/inputs <新基线输出目录>
dotnet validation/rechecks/2026-10-30/coating-target-preservation/final-matrix/after-runtime/CoatingAudit.dll quality-run validation/rechecks/2026-10-30/coating-target-preservation/inputs <新修正输出目录>
node tools/diagnostics/CoatingAudit20261030/summarize-target-preservation.mjs validation/rechecks/2026-10-30/coating-target-preservation <固定tmmcore包目录>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll infeasible-export <参考输入目录> <n-k文件>
node tools/diagnostics/CoatingAudit20261030/reference-infeasible.mjs <n-k文件> <参考输出文件>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll infeasible-reference <参考输入目录> <参考输出文件> <复验目录>
```

固定参考先从仓库归档展开到离线脚本指定的`.tmp`目录，使用原tmmcore包，不执行网络安装。完整配对须使用已有`inputs`，不要重跑`target-freeze`重新生成GUID。比较运行时由同一诊断构建复制，并以最终测试的Core/Application/Engine覆盖；旧臂仅换成`baseline-runtime`的Engine及PDB。原始目标、失败和真实材料快照均可复核。没有全仓、原生桌面或发布验收。

## Git同步范围

仓库保存源码/基线源码快照、冻结输入、完整结果、测试记录和哈希；本机编译比较运行时 `*-runtime/` 保留本地，由本目录 `.gitignore` 排除。上述旧/新二进制配对命令需要已封存的本地运行时；普通当前引擎重算可从工程重新构建。哈希用于核对已封存二进制，不承诺从部分基线源码逐字节重建旧运行时。
