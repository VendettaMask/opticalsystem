# Wide-field stage audit — 2026-09-27

The [report](../../../../docs/INITIAL_STRUCTURE_STAGE_AUDIT_2026-09-27.md) distinguishes the shipped v15 joint-phase change, the rejected curvature-recovery experiment and an unchanged-v14 FULL-mode diagnostic. All optical results use the shared C# Core. No external optical engine or analytical fallback supplies candidate results.

## Frozen inputs and decisions

- `probe-inputs.json`: 11 v14 failed full-target entries, their original templates, stages and accounting positions. `roots.json` preserves the 24 original root definitions and fingerprints.
- `joint-inputs.json`: five v14 stage entries with finite complete rays and violated geometry bounds.
- `bridge-probe.json`, `curvature-probe.json`: two phase variants per failure, using frozen v14 binaries. Intermediate-stage bridging restores 0/22 full-target domains; curvature contraction restores 22/22, but these are not accepted designs.
- `joint-local-v14.json`, `joint-local-proposed.json`: 90 local evaluations per input. Joint optimization improves the same-stage objective in four cases and worsens one relative to v14.
- `comparison.json`: all ten search records, including three reused v14 QUICK baseline records and seven searches run in this task. At 50000 evaluations, best worst-field RMS is 161.145 μm for v14 QUICK, 230.469 μm for rejected curvature recovery, 134.444 μm for v15 QUICK, and 203.049 μm for v14 FULL. All four fail the original complete specification.
- `*-summary.json` and `*-recalculation.json`: raw published metrics and fresh/export/convergence checks. The final v15 files contain 30 retained records. The other 40 records document rejected recovery and unchanged-v14 FULL experiments. These are records, not 70 distinct successful designs.

The candidate objectives, area sampling, 21-field independent validation, automatic diameter policy and final acceptance limits were unchanged. `joint-phase.patch` is the numerical orchestration change retained in v15. `rejected-recovery.patch` is an experiment to apply to the frozen v14 service source; it must not be mistaken for a current product feature. Its recorded binaries used the original v14 phase selection. Experimental checkpoints carrying version 15 are scoped by their source/binary hashes and are not release artifacts.

## Reproduction

Build the normal laboratory Release output first. The default `AuditBinaries` references its test-output dependencies. To reproduce v14 comparisons, set this property to the absolute path of the recorded `artifacts/validation/stage-recovery-20260927/frozen-v14` directory. Hashes are in `verification.json`; these binaries were copied before source changes. Do not substitute v15 when reproducing the v14 probe results.

Run the same program with `--bridge` for intermediate-stage probing, without that flag for curvature probing, or with `joint-inputs.json` and `--joint` for the five geometry entries:

```sh
dotnet run --project labs/InitialStructure/benchmarks/stage-recovery-20260927/runner/StageRecovery.csproj -c Release -p:AuditBinaries="<absolute frozen-v14 directory>" -- labs/InitialStructure/benchmarks/stage-recovery-20260927/probe-inputs.json /tmp/bridge.json --bridge
dotnet run --project labs/InitialStructure/benchmarks/stage-recovery-20260927/runner/StageRecovery.csproj -c Release -- labs/InitialStructure/benchmarks/stage-recovery-20260927/joint-inputs.json /tmp/joint-current.json --joint
```

The probe entry is recalculated at its recorded evaluation number; recorded prefix work is not silently counted as new optimization. Probe comparison outputs intentionally retain failed-ray diagnostic RMS values. They must be interpreted alongside `HasContinuousSearchResiduals`, never as valid full-aperture image quality.

The unchanged secant-solver benchmark runner provides the final current-version QUICK search and verification:

```sh
dotnet run --project labs/InitialStructure/benchmarks/secant-solver-20260927/runner/SecantSolver.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json /tmp/v15-searches --search
dotnet run --project labs/InitialStructure/benchmarks/secant-solver-20260927/runner/SecantSolver.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json /tmp/v15-wide --wide-budget 50000
dotnet run --project labs/InitialStructure/benchmarks/secant-solver-20260927/runner/SecantSolver.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json /tmp/v15-wide --verify
```

`full-runner/FullSearch.csproj` uses the same search/recalculation protocol, changing only `DesignSearch.Mode` to FULL. Bind it to frozen v14 binaries for the recorded FULL comparison. All process work uses normal benchmark outputs; the desktop application was built and verified in its default Debug/Release output directories.

See `source-freeze.json` and `verification.json` for the final source and binary hashes, test counts, build logs and the distinction between reused formal tests and new laboratory checks. Frozen Optiland history and the committed Zemax baseline were not regenerated or used as runtime candidate evaluators.

Repository sync on 2026-09-27 normalized the rejected patch's blank context line into an equivalent remove/add pair so the committed patch passes whitespace checks. Only the patch representation and its recorded SHA-256 changed; applying it produces the same experimental source. The v15 runtime sources and the 242/242 verification baseline are unchanged.
