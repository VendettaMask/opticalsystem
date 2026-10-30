# 真实 n/k 未达标高反/窄带通用修正证据

2026-10-30。本轮实现与完整数值见[报告](../../../../docs/COATING_INFEASIBLE_REPAIR_2026-10-30.md)。保持真实材料、原验收和默认协议；没有按材料名、膜层编号或例子签名修正。

## 最终结果

- [all-inputs](all-inputs/)：24份完整冻结输入。前17份来自上一阶段可行性质量对照；另外7份来自既有高反、单/双腔与4组跨规格窄带结果，仅预先指定新的迭代上限和结果清空。各配对目标/材料/预算完全一致。
- [paired-results.json](paired-results.json)：24对、48次完成，原19组通过全部保持；原5个失败配对的选优指标均改善，仍失败。单腔峰值 T 和双腔左截止 OD 的退步如实保留。
- [final-matrix/before](final-matrix/before/summary.json) / [after](final-matrix/after/summary.json)：最终旧/新引擎结果、实际迭代/评价/光谱/灵敏度/约束与复验计数、墙钟及完整可重开处方。两臂最大预算相同，实际工作不同，不作等价总成本或全局最优声明。
- [final-tests/coating-infeasible-final.trx](final-tests/coating-infeasible-final.trx)：完整模块140/140通过，含原物理/120组固定参考/导数/保存/公差/离屏流程和新增13例。
- [1296点实际输入/输出](final-matrix/core-cases-all.json) / [独立 tmmcore 比较](final-matrix/tmmcore-comparison.json)：最大绝对差 R `5.329070518200751e-15`、T `7.549516567451064e-15`、A `6.106226635438361e-15`，原门槛 `2e-11` 不变。
- [integrity.json](integrity.json)：原验收源码、原材料及前向/膜厚导数保持；两臂 Core/Application 相同，新 Engine 与最终测试字节一致，固定参考归档和执行源码保持。

真实高反最低 R：24次 `95.9562848%→95.9691731%`，96次 `95.9562848%→96.0308505%`，均未达到98%。单腔24次 FWHM `17.1700274→16.9340019 nm`，峰值 T `71.0554998%→70.0640329%`；双腔96次宽度 `17.4691029→17.1359173 nm`，峰值 T `66.4573104%→66.5191588%`。原峰值/宽度/左截止 OD 仍有失败，不改判为通过。

## 参考实现

[reference-input.json](reference-input.json) 导出7份冻结输入的真实材料 n/k；[reference-results.json](reference-results.json) 是未改动固定 TFStudio DLS/Newton 的14次真实运行。参考谱点目标与本产品的实际 FWHM/OD/最差指标目标不同；保留差异，不宣称同问题比较。每次用相同初始膜厚、固定层、厚度界和最大迭代次数，不清零吸收。

[reference-final-native/summary.json](reference-final-native/summary.json) 使用最终运行时 Core 对参考膜厚作原密集复验。真实 HR、单腔和双腔参考均未达原规格；14份参考处方中2份通过各自原门槛。`reference-native` 是较早同定义复验，最终以 `reference-final-native` 为准。参考源归档及选定执行文件完整性已核验。

## 历史探针与失败

`baseline-source`、`baseline-runtime` 为本轮修改前快照，包含此前已有的半高宽修复和 `feasible-worst-reflectance/1`。`before`、`extended-before` 是初次复现；最终旧臂使用共同最终 Core 加基线 Engine 重跑。

`probe-*`、`extended-probe-*` 保留目标/初始化试验，不能与最终矩阵混合挑选。实际宽度单独代替形状引导曾使吸收红外对照退步；最大超限辅助变量未改善跨规格结果；无条件对偶初始化使多组已可行反射率停滞；保留原可行性预处理后才恢复 AR 短筛选与既有可行质量行为。`extended-probe-6` 的真实单腔遇到约束维度切换异常，因此目录不完整，该次不算完成；最终固定物理约束行并用边缘消失控制覆盖此问题。

`tests/band-and-reflectance.trx` 为20项初始通过；`targeted-integrated.trx` 为35通过/1项 AR 模板失败的中间版本；`infeasible-regressions.trx` 的3项失败来自比较含数组目标的引用身份，物理改进断言已通过，改为完整 JSON 内容比较后最终通过。以上分组与最终140项重叠，不能累计。

## 重放

```powershell
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release
dotnet validation/rechecks/2026-10-30/coating-infeasible-repair/final-matrix/before-runtime/CoatingAudit.dll quality-run validation/rechecks/2026-10-30/coating-infeasible-repair/all-inputs <新旧臂输出目录>
dotnet validation/rechecks/2026-10-30/coating-infeasible-repair/final-matrix/after-runtime/CoatingAudit.dll quality-run validation/rechecks/2026-10-30/coating-infeasible-repair/all-inputs <新修正臂输出目录>
node tools/diagnostics/CoatingAudit20261030/summarize-infeasible.mjs validation/rechecks/2026-10-30/coating-infeasible-repair <固定tmmcore的package目录>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll infeasible-export <冻结输入目录> <n-k输出文件>
node tools/diagnostics/CoatingAudit20261030/reference-infeasible.mjs <n-k输出文件> <参考结果文件>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll infeasible-reference <冻结输入目录> <参考结果文件> <新复验目录>
```

重放配对必须使用已有 `all-inputs`，不要重新生成 GUID 或更改来源处方。准备旧/新运行时的方法为：复制同一当前诊断构建，然后仅在旧臂替换 `baseline-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll` 及PDB。Node/归档参考和旧 DLL 仅为离线验证，不进入产品运行依赖。没有操作桌面、提交、推送或发布。

## Git同步范围

仓库保存源码/基线源码快照、冻结输入、完整结果、测试记录和哈希；本机编译比较运行时 `*-runtime/` 保留本地，由本目录 `.gitignore` 排除。上述旧/新二进制配对命令需要已封存的本地运行时；普通当前引擎重算可从工程重新构建。哈希用于核对已封存二进制，不承诺从部分基线源码逐字节重建旧运行时。
