# 初始结构（产品内置模块）

更新：2026-10-14。本页维护操作流程、运行记录和历史方法入口；计算隔离、支持范围与后续计划见 [模块契约](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md)。初始结构和镀膜设计均在主程序同一进程内运行，由根解决方案和标准安装包统一交付，不是独立软件。

当前桌面调用 `FlatStartSearchService` 的 v19 形式搜索与渐进恢复流程。所有光学计算复用正式共享 Core；模型不因“AI”名称而具有机器学习或设计智能体能力。生成有效候选不等于满足所有规格，更不等于可制造成品。当前工程测试和已知失败只在 [状态页](../../docs/CURRENT_STATUS.md) 汇总。

## Current desktop workflow

Enter focal length, F/# or entrance-pupil diameter, maximum half-field angle, an element-count range, maximum track and editable wavelengths/weights/primary selection. The Goals tab exposes geometry limits, permitted catalog glasses and full quality gates. The Search tab exposes QUICK/FULL mode, initial radius/thickness/gap, a + / - / * initial-form filter, starting stop and stop exploration, trial/candidate limits and budgets. A blank sign filter covers all directions within the explicit trial cap. Invalid or fractional count inputs are rejected; invalid numeric edits remain visible after focus changes rather than silently reverting to an old target.

Generate, stop/save and resume use the v19 form-search engine. The Search plan shows actual planned root counts and the budget reserved for later operators; the requested seed count is an upper limit. Each binary direction starts from a recorded flat root followed by an explicit curvature initialization, not an assumed optical result. The Search plan/history tab distinguishes requested, planned and fully evaluated forms; a budget-limited root without initialization is not completed coverage. Inputs lock during a run, and editing parameters clears old candidate tables and scenes immediately. Candidate details show full targets, prescription, per-field/per-wavelength transmission and original flat-root provenance. `Layout2DBuilder` supplies all lens geometry and directed ray segments; A/B uses one physical scale. Layout requests explicitly use the primary wavelength, all defined fields and seven pupil samples per field across [-1, 1], retaining truncated vignetted paths. This is a display request, not a replacement for dense final analysis.

Selected refinement creates a new `strict-flat-selected-refinement/v19` run with an explicit additional evaluation budget and the source time limit. `Origin` preserves the source run, charged work, candidate and original strict-flat proof. Targets and material contents remain unchanged, the source file is not rewritten, and the source snapshot is reevaluated with the current dense objective and retained if no better physical class / objective value is found. Its historical record remains in Origin; legacy summary scores and current dense objective scores are never mixed. Physical-stop mode is part of score identity and is retained from the source specification; scores from different modes cannot mix. Completed evaluations count actual Core propagation work, including aiming and circular-aperture diagnostics, excluding cache hits. Historical work counts retain their original sampling meaning. The legacy objective retains its historical meaning; the area rule publishes a separate V2 objective, sampling identity and independent-validation flag. Coarse and final candidate scores are not mixed. Resume restores the current run's remaining budget; it never silently adds more work.

## Build and test

```bash
dotnet build OptilandWorkbench.slnx /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj --no-restore /m:1 /nr:false
```

## Run

Install the standard product once, start Optical System Design, and choose **实验室 → AI 初始结构**. No additional download, build, environment override or separate executable is used. For development, start the main application:

```bash
dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release
```

Both module windows are owned by the main window and share its theme and display settings. Search records and coating experiments retain their own editing state. Closing the product stops/awaits module operations and preserves unsaved-data safeguards.

The entry's “AI” label names the experimental feature requested by the user; this change adds no model, training, retrieval or agent implementation. The implemented generator remains the deterministic optical search described above.

Current desktop data is written below the user's local application-data directory at `OpticalSystemDesign/Labs/InitialStructure/flat-start/runs`. Each unique run has a checksummed atomic `<run-id>.family.json` file; `last-run.json` is the bounded last-run pointer. Open a saved record to inspect it, then explicitly resume if its algorithm is current and budget remains. Historical strict-flat checkpoints can instead seed a separate current-version refinement. Legacy `runs` and `checkpoints` directories stay separate; the desktop does not reinterpret hybrid v3 records as strict-flat runs. The App exports only the selected candidate through the Core STAROPT store and verifies the exact snapshot after reopening it. Export remains available after stopping a search and does not modify the currently open formal workbench.

Shared legacy specification limits include 10,000 initial seeds, 100,000 evaluations, 256 workers, 64 wavelengths, 128 glass catalogs, a 24-hour run, an 89-degree maximum field angle, a 64 MiB manifest, and a 4 MiB candidate snapshot. The current P3/P4 path additionally restricts initial families to 128 and workers to four. Validation rejects non-finite or overflowing track and aperture calculations before allocation. These are executable bounds, not guarantees that arbitrary requested optics can be generated.

See [the development plan](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md) for scope, isolation rules, acceptance gates, and later phases.

## 历史方法与验证

早期独立 App、混合 v3 和 P0–P5 命令只描述当时实现，不是当前启动方式。旧运行保持算法、采样、预算和失败身份；不能直接改写为 v19 或把不同协议的成功率相加。当前算法可显式从受支持旧快照创建一个新的细化运行，源记录不被覆盖。

- [混合/形式搜索冻结协议](benchmarks/README.md) 与 [验收工具](tools/OptilandWorkbench.InitialStructure.Benchmarks/README.md)：固定目标、种子、预算和未达到的发布门槛。
- [从平板生成提案](../../docs/INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md)、[P3 数值结果](../../docs/INITIAL_STRUCTURE_P3_2026-09-07.md)、[P4 历史 UI 验证](../../docs/INITIAL_STRUCTURE_P4_2026-09-07.md)。
- [v8 形式搜索](../../docs/INITIAL_STRUCTURE_DSEARCH_2026-09-26.md)、[路径审计](../../docs/INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md)、[R2 收敛修正](../../docs/INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md)。
- [质量审计](../../docs/INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md)、[实体光阑](../../docs/INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md)、[v19 渐进恢复](../../docs/INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)：包括仍未达到像质目标的宽角案例，历史失败保留。

## 同一产品中的镀膜模块

[光学镀膜设计](../CoatingDesign/README.md) 提供减反、高反和 1–5 腔设计。两个模块各自保存实验状态，但由同一个主程序构建、安装和管理，均不隐式修改当前镜头。
