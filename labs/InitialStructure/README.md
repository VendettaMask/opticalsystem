# 初始结构（产品内置模块）

系统页 **从预设开始 → 载入预设** 提供 AutoLens 官方目标：覆盖广角、全画幅、20 种子协议和 F/1.2 / 120° 两种极端目标适配。只载入原生参数，不自动运行，不伪装成上游成功处方；“来源与适配说明”中逐项注明补充几何、材料、波长、阈值和预算。使用与固定来源见 [原生预设指南](../../docs/AUTOLENS_OFFICIAL_EXAMPLES.md)，实际验证见 [当前状态](../../docs/CURRENT_STATUS.md)。

更新：2026-10-23。本页维护操作流程、运行记录和历史方法入口；计算隔离、支持范围与后续计划见 [模块契约](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md)。初始结构和镀膜设计均在主程序同一进程内运行，由根解决方案和标准安装包统一交付，不是独立软件。

当前桌面调用 `FlatStartSearchService` 的 v21 形式搜索与渐进恢复流程。所有光学计算复用正式共享 Core；模型不因“AI”名称而具有机器学习或设计智能体能力。生成有效候选不等于满足所有规格，更不等于可制造成品。当前工程测试和已知失败只在 [状态页](../../docs/CURRENT_STATUS.md) 汇总。

AI 初始结构现只运行 AutoLens 的 C# 自动求导实现。旧原生信赖域、启发式及原生目标精修选项已隐藏，原生目标精修预算固定为零；实体光阑和光线瞄准固定启用。旧算法与混合算法记录可查看、比较和导出，不能通过桌面继续运行或细化。原 [C# 集成评估](../../docs/INITIAL_STRUCTURE_AUTOLENS_CSHARP_2026-10-19.md)保留当时的试验边界，不是当前界面操作指南。

搜索页的“计算设备”提供自动优先 GPU、强制 GPU 和 CPU。新窗口默认自动，目标预设保留当前设备选择，保存记录则保留自己的设备模式；切换设备会创建新实验。CUDA 内核只执行共享 Core 记录的双精度链式反传，光学公式、正式光线、瞄准及独立物理验收继续使用 Core CPU。实际设备、成功 GPU 求导批次及自动回退原因进入运行记录。详情和测量见 [GPU 说明](../../docs/AUTOLENS_GPU.md)。

## Current desktop workflow

The reorganized desktop groups settings into System, Targets and Search cards. Geometry/back focus, glasses/catalogs and aperture/stop are separate collapsible Target sections; AutoLens settings now belong to Search, alongside separate starting-form and refinement-budget sections. Collapsing a group never drops its inputs or resets values. Run/record commands stay above the workspace; refine/export belong beside the candidate table, with layout and inspection tabs below. Narrow windows stack the two panels with scrolling and wrapping tab headings. Colors, card chrome and icons use existing theme resources; live Chinese/English/Japanese changes reflow tab labels. The 2026-10-22 reorganization was presentation-only; the subsequent AutoLens-only execution policy is described below. See the [UI walkthrough](../../docs/INITIAL_STRUCTURE_UI_2026-10-22.md).

Enter focal length, F/# or entrance-pupil diameter, maximum half-field angle, an element-count range, maximum track and editable wavelengths/weights/primary selection. The Goals tab exposes geometry limits, permitted catalog glasses and full quality gates. The Search tab exposes QUICK/FULL mode, initial radius/thickness/gap, a + / - / * initial-form filter, starting stop and stop exploration, trial/candidate limits and budgets. A blank sign filter covers all directions within the explicit trial cap. Invalid or fractional count inputs are rejected; invalid numeric edits remain visible after focus changes rather than silently reverting to an old target.

Generate, stop/save and resume use the v21 form-search engine. The Search plan shows actual planned root counts and the budget reserved for later operators; the requested seed count is an upper limit. Each binary direction starts from a recorded flat root followed by an explicit curvature initialization, not an assumed optical result. The Search plan/history tab distinguishes requested, planned and fully evaluated forms; a budget-limited root without initialization is not completed coverage. Inputs lock during a run, and editing parameters clears old candidate tables and scenes immediately. Candidate details show full targets, prescription, per-field/per-wavelength transmission and original flat-root provenance. `Layout2DBuilder` supplies all lens geometry and directed ray segments; A/B uses one physical scale. Layout requests explicitly use the primary wavelength, all defined fields and seven pupil samples per field across [-1, 1], retaining truncated vignetted paths. This is a display request, not a replacement for dense final analysis.

Selected refinement creates a new `strict-flat-selected-refinement/v21` run with an explicit additional evaluation budget and the source time limit. `Origin` preserves the source run, charged work, candidate and original strict-flat proof. Targets and material contents remain unchanged, the source file is not rewritten, and the source snapshot is reevaluated with the current dense objective and retained if no better physical class / objective value is found. Its historical record remains in Origin; legacy summary scores and current dense objective scores are never mixed. Physical-stop mode is part of score identity and is retained from the source specification; scores from different modes cannot mix. Completed evaluations count actual Core propagation work, including aiming and circular-aperture diagnostics, excluding cache hits. Historical work counts retain their original sampling meaning. The legacy objective retains its historical meaning; the area rule publishes a separate V2 objective, sampling identity and independent-validation flag. Coarse and final candidate scores are not mixed. Resume restores the current run's remaining budget; it never silently adds more work.

v21 retains v20's normal projected search. If its first nonstationary model cannot produce a usable step, it retries that same model with explicit crossed-bound elimination, the actual fixed-step residual contribution and remaining trust radius before declaring a projection stall. This safeguard consumes the existing evaluation budget; it does not restart or add work. The Core optical objective and physical gates are unchanged. Older supported records, including v20 trust-region and experimental records, remain readable and can start a separate refinement; silently resuming them as v21 is rejected.

## Build and test

The desktop uses only **AutoLens · C# 自动求导** (`autolens-managed-v1`), with native target polish fixed to zero. This path uses shared Core automatic tangents and implicit physical-stop aiming, a half-to-full cosine aperture curriculum, explicit-state Adam with cosine restarts, seeded area-stratified pupils and symmetric full fields, continuous n/V relaxation followed by projection onto the permitted real glasses, then fixed-catalog AutoLens refinement. Advanced controls choose spherical/even-aspheric profiles through r¹⁰ and optionally cemented gap numbers (UI starts at 1, stored indices at 0). The r² term stays zero. A cemented pair ties its coincident profile and shared medium; its zero-power reference row is retained in the native fixed-row prescription.

Every accepted Adam step measures a loss decrease on identical samples; non-descent momentum resets its bias-correction age without rewinding curriculum progress. Real-ray body sizing is a projection outside differentiation, with body radii held fixed within a phase. The desktop always disables the historical numeric-Jacobian target-polish handoff. Its engine implementation and separately counted work remain available only to interpret historical validation protocols; they are not selectable desktop behavior.

Every trial ends, budget/time permitting, with a fresh dense full-aperture physical evaluation and the existing independent edge checks. Unclipped training coordinates and relaxed glasses cannot establish acceptance or become exported candidate materials. A separately verified better full-target incumbent is retained. Evaluation/time budgets override requested iteration counts. Results retain actual work, Adam moments/age, lineage and the evaluated snapshot; `ResumeAutoLens` validates exact specification, glass-list and strict-flat-root identities and does not reset consumed resources. Search-file saves remain at batch boundaries, not crash-safe per-iteration journaling. Historical v21 and finite-difference experiments retain their original identities without becoming desktop execution options.

This is a C# workflow with shared Core CPU/CUDA execution. Eligible rotational systems use the same Core intersection, normal, refraction and aiming functions on CUDA, plus double-precision derivative tapes. Automatic diameter envelopes and independent dense physical validation batch all fields without changing quadrature, clipping, throughput gates or target tolerances. Statistics, material selection, Adam and manufacturing constraints remain on CPU. Bounded device buffers are reused; extended aiming recovery and actual GPU work are counted separately. Automatic prefers a real CUDA device and records CPU fallback reasons; explicit GPU never masquerades as CPU execution. Historical omitted compute modes preserve CPU specification identities. This does not establish upstream numerical equivalence, an entirely GPU-resident search or a pretrained specification-to-lens network. See the [implementation scope](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md), [GPU scope](../../docs/AUTOLENS_GPU.md) and [historical native rework ledger](../../docs/validation/AUTOLENS_MANAGED_2026-10-19.json).

```bash
dotnet build OpticalSystemDesign.slnx /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj --no-restore /m:1 /nr:false
```

## Run

Install the standard product once, start Optical System Design, and choose **实验室 → AI 初始结构**. No additional download, build, environment override or separate executable is used. For development, start the main application:

```bash
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release
```

Both module windows are owned by the main window and share its theme and display settings. Search records and coating experiments retain their own editing state. Closing the product stops/awaits module operations and preserves unsaved-data safeguards.

The entry's “AI” label does not imply a pretrained network, retrieval system or autonomous design agent. Managed AutoLens performs gradient-based optical optimization; historical strategies are not exposed by the desktop.

Current desktop data is written below the user's local application-data directory at `OpticalSystemDesign/Labs/InitialStructure/flat-start/runs`. Each unique run has a checksummed atomic `<run-id>.family.json` file; `last-run.json` is the bounded last-run pointer. Only pure managed-AutoLens records with a current algorithm version and remaining budget can resume. Old-algorithm and hybrid records retain their original identities for viewing, comparison and export, but cannot resume or refine through this UI. Starting again uses their target inputs for a new pure-AutoLens run and preserves the source record. Legacy `runs` and `checkpoints` directories stay separate; the desktop does not reinterpret hybrid v3 records as strict-flat runs. The App exports only the selected candidate through the Core STAROPT store and verifies the exact snapshot after reopening it. Export remains available after stopping a search and does not modify the currently open formal workbench.

Shared legacy specification limits include 10,000 initial seeds, 100,000 evaluations, 256 workers, 64 wavelengths, 128 glass catalogs, a 24-hour run, an 89-degree maximum field angle, a 64 MiB manifest, and a 4 MiB candidate snapshot. The current P3/P4 path additionally restricts initial families to 128 and workers to four. Validation rejects non-finite or overflowing track and aperture calculations before allocation. These are executable bounds, not guarantees that arbitrary requested optics can be generated.

See [the development plan](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md) for scope, isolation rules, acceptance gates, and later phases.

## 历史方法与验证

早期独立 App、混合 v3 和 P0–P5 命令只描述当时实现，不是当前启动方式。旧运行保持算法、采样、预算和失败身份；不能直接改写为 v21 或把不同协议的成功率相加。旧算法及混合算法记录不能通过当前桌面继续运行或创建细化；按原目标重新开始会建立纯 AutoLens 新实验，源记录不被覆盖。历史引擎的旧协议回归不代表当前界面能力。

- [混合/形式搜索冻结协议](benchmarks/README.md) 与 [验收工具](tools/OpticalSystemDesign.InitialStructure.Benchmarks/README.md)：固定目标、种子、预算和未达到的发布门槛。
- [从平板生成提案](../../docs/INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md)、[P3 数值结果](../../docs/INITIAL_STRUCTURE_P3_2026-09-07.md)、[P4 历史 UI 验证](../../docs/INITIAL_STRUCTURE_P4_2026-09-07.md)。
- [v8 形式搜索](../../docs/INITIAL_STRUCTURE_DSEARCH_2026-09-26.md)、[路径审计](../../docs/INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md)、[R2 收敛修正](../../docs/INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md)。
- [质量审计](../../docs/INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md)、[实体光阑](../../docs/INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md)、[v19 渐进恢复](../../docs/INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)：包括仍未达到像质目标的宽角案例，历史失败保留。

## 同一产品中的镀膜模块

[光学镀膜设计](../CoatingDesign/README.md) 提供减反、高反和 1–5 腔设计。两个模块各自保存实验状态，但由同一个主程序构建、安装和管理，均不隐式修改当前镜头。
