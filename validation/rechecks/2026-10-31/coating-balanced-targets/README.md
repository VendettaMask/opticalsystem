# 镀膜目标方向与指导峰位一致性证据（2026-10-31）

实现、收益/代价及仍失败项见[报告](../../../../docs/COATING_TARGET_BALANCING_2026-10-31.md)。对照为提交`3b43bb7f`，保留原真实n/k和所有原门槛。

- `inputs/`：35份原输入逐字节复用，6份新规格在比较前冻结；合成材料来源明确。不要重复运行freeze覆盖其身份。
- `baseline-source/`：被改的旧源文件及旧验收编排快照，完整旧版可从提交恢复。
- `final-matrix/before/`、`after/`：同一Core/Application、不同Engine的41组完整结果、分项、工作量与RTA输入。
- `paired-results.json`、`integrity.json`：独立检查材料/输入/目标/制造界/固定层/逐项目标/旧达标/迭代配额/验收/二进制身份，以及5组收益、5组分数代价。
- `reference-inputs/`、`reference-materials.json`、`reference-results.json`、`reference-final-native/`、`reference-comparison.json`：12份固定参考起点、原样DLS/Newton的24次实际运行与原Core复验。平方点目标不同于本地带指标目标，不作同问题能力排名。
- `final-tests/coating-guidance-final.trx`：最终完整156项回归；`tests/`及另一个156项TRX保留早期阶段。各阶段重复运行不累加成不同用例数。
- `probe-*`、`single-grid-probe/`、`dense-rejection-probe/`、`initial-source/`、`initial-paired-results.json`、`initial-integrity.json`、`final-matrix/initial-after/`：探索、错误基线假设、构建未完成的无效试算及初始指导峰位不一致的证据。`probe-2`不计有效完成。
- `final-manifest.json`：最终代码、输入/结果和测试哈希及计数。

82次最终配对完成；旧22组达标保持22，新增完整达标为0，双方相对输入的目标超限退步均为0；新版33组相对输入改善，旧版35组。2214个R/T/A点最大差7.55e-15，原门槛2e-11。真实高反/窄带仍未全部达标。

## 重放

仓库保存源码、完整输入/结果、TRX、参考输入和哈希；`.gitignore`中的`*-runtime/`只保留本机封存二进制，不提交重复DLL或本机运行库。旧版本完整重建以提交`3b43bb7f`为源；部分源快照及哈希不承诺逐字节复建历史运行库。两臂须放入同一Core/Application，只替换Engine；当前引擎可从正常项目重新构建。

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release --no-restore
dotnet <before-runtime>/CoatingAudit.dll quality-run <root>/inputs <root>/final-matrix/before
dotnet <after-runtime>/CoatingAudit.dll quality-run <root>/inputs <root>/final-matrix/after
dotnet <after-runtime>/CoatingAudit.dll infeasible-export <root>/reference-inputs <root>/reference-materials.json
node tools/diagnostics/CoatingAudit20261030/reference-infeasible.mjs <root>/reference-materials.json <root>/reference-results.json
dotnet <after-runtime>/CoatingAudit.dll infeasible-reference <root>/reference-inputs <root>/reference-results.json <root>/reference-final-native
node tools/diagnostics/CoatingAudit20261030/summarize-balanced-targets.mjs <root> <fixed-tmmcore-package>
```

参考源码从仓库固定归档离线提取，不网络安装、不修改参考算法。原生界面操作未执行；运行不占用桌面。
