# Wide-field stage audit — 2026-09-27

Local storage review (2026-11-08): this directory describes a historical experiment, not a maintained default replay command. The related old build output and the checked bulk restart files have been retired; source, regression inputs, original summaries, exported prescriptions and unique failures remain. See the [scoped deletion and preservation record](../../../../validation/rechecks/2026-11-08/remaining-task-cleanup/README.md). This is not a new numerical acceptance result.

The [report](../../../../docs/INITIAL_STRUCTURE_STAGE_AUDIT_2026-09-27.md) distinguishes the shipped v15 joint-phase change, the rejected curvature-recovery experiment and an unchanged-v14 FULL-mode diagnostic. All optical results use the shared C# Core. No external optical engine or analytical fallback supplies candidate results.

## Frozen inputs and decisions

- `probe-inputs.json`: 11 v14 failed full-target entries, their original templates, stages and accounting positions. `roots.json` preserves the 24 original root definitions and fingerprints.
- `joint-inputs.json`: five v14 stage entries with finite complete rays and violated geometry bounds.
- `bridge-probe.json`, `curvature-probe.json`: two phase variants per failure, using frozen v14 binaries. Intermediate-stage bridging restores 0/22 full-target domains; curvature contraction restores 22/22, but these are not accepted designs.
- `joint-local-v14.json`, `joint-local-proposed.json`: 90 local evaluations per input. Joint optimization improves the same-stage objective in four cases and worsens one relative to v14.
- `comparison.json`: all ten search records, including three reused v14 QUICK baseline records and seven searches run in this task. At 50000 evaluations, best worst-field RMS is 161.145 μm for v14 QUICK, 230.469 μm for rejected curvature recovery, 134.444 μm for v15 QUICK, and 203.049 μm for v14 FULL. All four fail the original complete specification.
- `*-summary.json` and `*-recalculation.json`: raw published metrics and fresh/export/convergence checks. The final v15 files contain 30 retained records. The other 40 records document rejected recovery and unchanged-v14 FULL experiments. These are records, not 70 distinct successful designs.

The candidate objectives, area sampling, 21-field independent validation, automatic diameter policy and final acceptance limits were unchanged. `joint-phase.patch` is the numerical orchestration change retained in v15. `rejected-recovery.patch` is an experiment to apply to the frozen v14 service source; it must not be mistaken for a current product feature. Its recorded binaries used the original v14 phase selection. Experimental checkpoints carrying version 15 are scoped by their source/binary hashes and are not release artifacts.

## Historical replay scope

The probe, joint solver and FULL-mode runner sources remain historical records. Their former default test-output dependencies and the `frozen-v14` runtime were retired in the earlier 2026-11-08 cleanup; those paths are no longer maintained replay commands. The saved source/binary identities, input snapshots, summaries, STAROPT exports and rejected-experiment diagnostics remain. Recorded v14 probes must not be relabeled as results from a later engine.

The original protocol recalculated each probe at its recorded evaluation number and kept its prefix costs separate. Failed-ray diagnostic RMS values must still be interpreted with `HasContinuousSearchResiduals`, never as valid full-aperture image quality.

See `source-freeze.json` and `verification.json` for the final source and binary hashes, test counts, build logs and the distinction between reused formal tests and new laboratory checks. Frozen Optiland history and the committed Zemax baseline were not regenerated or used as runtime candidate evaluators.

Repository sync on 2026-09-27 normalized the rejected patch's blank context line into an equivalent remove/add pair so the committed patch passes whitespace checks. Only the patch representation and its recorded SHA-256 changed; applying it produces the same experimental source. The v15 runtime sources and the 242/242 verification baseline are unchanged.
