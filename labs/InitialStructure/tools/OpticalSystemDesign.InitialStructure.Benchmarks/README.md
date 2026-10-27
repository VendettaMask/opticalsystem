# Fixed-protocol search evidence

The separate `--autolens-official <new output directory> [exact preset id]` command uses the same native presets and search service as the UI. The complete protocol has 24 runs: four single-seed targets and twenty locally chosen wide-angle seeds. Limits are 4000 evaluations/60 seconds per run; independent full-aperture physical and STAROPT replay follow outside the search budget. Exit 0 means all requested designs passed, 2 means at least one failed or errored, and 3 means cancellation. It never overwrites existing evidence, labels a subset as the complete catalog, or claims upstream numerical equivalence. See [source, adaptations and gates](../../../../docs/AUTOLENS_OFFICIAL_EXAMPLES.md).

This independent C# runner uses the formal Core. It records search acceptance separately from engineering tests, asset integrity, external numerical comparison and native UI acceptance. Current v11 supports automatic lens diameters; the frozen protocols omit this new flag and therefore retain their fixed-diameter meaning. See [v11 behavior and engineering verification](../../../../docs/INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md). The v10 complete protocol stopped after 43/60 runs (30 successful), before holdout; complete v11 acceptance has not been run. Complete v8 results below remain historical.

Completed v8 evidence (2026-09-26): original **30/60**, **6/12** specifications meeting 4/5; separate holdout **15/15**. Both protocols have zero failed trials or verification errors and identical selected-export reloads. The original 10/12 gate remains failed. See [all seeds, branch coverage and integrity records](../../benchmarks/dsearch-v8-20260926/README.md).

From the repository root, build the default Release outputs and run the original protocol:

```sh
dotnet build OpticalSystemDesign.slnx -c Release --no-restore
dotnet run --no-build -c Release --project labs/InitialStructure/tools/OpticalSystemDesign.InitialStructure.Benchmarks -- labs/InitialStructure/benchmarks/flat-start-v1 artifacts/flat-start-acceptance/new-original
```

The output directory must be new or empty. The original twelve specifications and seeds 101–105 retain 10000 evaluations/600 seconds, serial execution, six fields, 97 pupil points including the boundary, all positive-weight wavelengths, and every physical gate. `FrozenInputs.json` verifies the committed protocol/specification hashes after newline normalization. Raw input and binary hashes are also recorded. A diagnostic subset can append an exact specification filename and seed; it cannot pass the original 60-run gate.

A successful run requires an independently recalculated accepted candidate and no failed trial or verification mismatch. Each completed run saves a checksummed checkpoint, selected prescription and STAROPT file. Independent validation and exact export/reload checks run outside the search budget and are recorded separately. A full original gate requires at least ten specifications with at least four successful seeds each. Exit 0 means that search gate passed; exit 2 means it did not, including diagnostic subsets.

Separate supplementary commands:

```sh
dotnet run --no-build -c Release --project labs/InitialStructure/tools/OpticalSystemDesign.InitialStructure.Benchmarks -- --holdout labs/InitialStructure/benchmarks/holdout-v1 artifacts/flat-start-acceptance/new-holdout
dotnet run --no-build -c Release --project labs/InitialStructure/tools/OpticalSystemDesign.InitialStructure.Benchmarks -- --legacy labs/InitialStructure/benchmarks/flat-start-v1 artifacts/flat-start-acceptance/new-legacy
```

The holdout contains three new targets and seeds 201–205. Every seed is reported; it never changes the original denominator or threshold. The legacy command retains the historical hybrid objective and removes only the unsupported `FlatStart` API option. Every returned prescription is then independently evaluated against the full original target, including fixed back focus, edge thickness and per-field/per-wavelength gates. The historical engine does not acquire new optimization capabilities through this adapter. A cancelled legacy run's original evaluation diagnostic may omit interrupted refinement work; its new measured ray count still observes that work. Supplementary exit 0 means all requested runs were recorded, **not** that a search gate passed. Their `SearchAcceptanceGatePassed` is null.

`summary.json` is updated atomically after each completed run. `MeasuredSearchRays` counts rays submitted to actual sequential computation, including aiming, aperture diagnostics and failed rays, excluding cache hits. It is neither a surface-intersection count nor a hard ray budget. Original `SearchRays` is retained as the historical requested-analysis-sampling count; these two metrics must not be mixed. Wall time includes the same host's normal background load. Do not infer speed from evaluation counts alone or compare historical Windows timings directly with macOS timings.

`P5ReleaseAccepted` remains false: search, external precision and required platform UI evidence have separate conditions. Keep every failed version's output and source/binary hashes. Freeze code before a complete acceptance run; use independent mechanism tests to repair identified defects instead of manually tuning weights on the acceptance set.

## Qualified four-to-three recovery study (2026-10-27)

The separate `AutonomousTopologyV1` CPU runner supports `--four-to-three-prepare <qualified native> <new output directory>`, `--four-to-three-compare <frozen protocol> <new output directory>`, `--four-to-three-filter <checkpoint> <new output directory>` and `--four-to-three-audit <evidence root> <new output directory>`. All reject nonempty output directories. These commands use the shared C# Core; they do not invoke Python, native desktop windows, GPU, or the stopped AutoLens training.

Preparation pins the exact previously delivered qualified four-element source and records an independent physical assessment plus all four deletion failures. Comparison pins source-plan/native SHA, protocol, actual assembly hashes and every stage/checkpoint. The four frozen protocols under `benchmarks/autonomous-topology-v1/four-to-three-*-20261027.json` distinguish the initial 16 recovery ablations, four pupil-recovery cells, four conic-compensation cells and four final allocation cells. New flags are explicit nullable plan options; they do not upgrade old checkpoints or ordinary example defaults. Audit reimports exported three-element native files and compares the full physical endpoint, explicitly reusing identical optical fingerprints.

The final direct route qualifies a three-element design in the original 600-function/100-fixed-ray-gradient/2-million-ray/60-second global and 400,000-stage-ray quotas. It adds six conic freedoms and raises the Full update ceiling from 8 to 99 within those quotas; it is not the same parameter permission or per-stage update allocation as the original control. Both gradual routes and all preceding failures remain recorded. See the [complete report and 28-cell table](../../../../validation/rechecks/2026-10-27/four-to-three-recovery/README.md), [verification](../../../../validation/rechecks/2026-10-27/four-to-three-recovery/verification.json), and [implementation plan](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md).
