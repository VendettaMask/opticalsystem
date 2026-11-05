# F/8 cold-start paired study, 2026-10-31

Ten completed runs: candidate **1/5**, control **0/5**. This is a new cold study, not a continuation or a merge with the earlier fixed-layout or reduced-lens results. No DeepSeek, native desktop control, Git commit or publishing.

- [Protocol](paired-v1/protocol.json), [source checksums](paired-v1/source-sha256.json), [runtime checksums](paired-v1/runtime-sha256.json), [source archive](paired-v1/source.zip).
- Same original seed44 task specification, RNG seeds 42–46, fresh process per run; new thicknesses/positions/shapes, no repaired endpoint, checkpoint or optimizer-state imports.
- Each arm/seed cap: 600 functions, 600 gradients, 10,000,000 actual CPU rays, 600 seconds. Construction, screening, compensation and complete original acceptance are charged. No budget or schedule changes after the study started.
- [All paired summaries](paired-summary.json); each arm/seed directory retains input, progress, result checksum, phase bounds and all original assessments. All ten runs are within budget.
- [Frozen final audit](audit-final-v1/audit.json): 94 distinct complete assessment replays, plus repeated qualified-endpoint validation and native readback = **96**, zero errors. Each run retains the independently computed full replays.
- [Current product audit](audit-current-product-v1/audit.json): another **96** exact replays/acceptance/readback checks, zero errors; post-search costs only, no added optimization. [Openable product export](audit-final-v1/candidate/seed44/qualified.staropt).

Seed44 is the sole qualified cold design: RMS 49.7601785755 µm, maximum 117.165252952 µm, all 21 fields fully transmitted, zero lost independent boundary/center checks; original image/manufacturing/physical gates pass. Cost 313 functions, 264 gradients, 1,436,330 rays, 540.289913 seconds. It retains eight lenses, 16 even-asphere faces and 32 original-permitted A4/A6 coordinates.

Failed candidate seeds: 42 RMS 52.195236 µm, rim loss 0; 43 RMS 50.080776 µm, maximum 150.140194 µm, rim loss 1; 45 RMS 51.578214 µm, rim loss 1; 46 RMS 53.487119 µm, rim loss 1. Every candidate has 100% Gaussian integration transmission. Near misses remain failures. The result does not establish robust success, statistical superiority or optimality. F/4 retains the original 6.25 mm stop / 5 mm face-cap conflict without a new optimization run.

The automatic entry was prepared only as an isolated draft while the paired study ran; it was applied to the active checkout after the full final frozen audit passed. [Applied draft](automatic-entry-draft.patch) records that reviewable change. The desktop option is explicit and off by default. Failed/cancelled preparation saves evidence and never starts the old seed; qualified fresh seeds enter the autonomous search; resume does not repeat preparation. Preparation and subsequent search budgets remain separate.

[Loadable original-spec cold-start plan](F8-cold-start.topology-plan.json) uses seed44 and the same preparation limits. It imports the task specification, not the qualified endpoint, and has passed save/read checksum verification. Creating this file did not start another search. Final implementation source and product binary inventories are retained alongside the immutable study snapshot.

Test records are under `tests`. `isolated-entry-complete.trx` contains seven isolated flow/compatibility checks. `current-entry-restored.trx` is the authoritative seven-check current-product result. The earlier `current-entry-complete.trx` used stale restore references to both source trees and is superseded, not counted. The first cold preflight recorded a cancellation exception, fixed by the explicit zero-work pre-cancel return; its verified follow-up passed. Loose synthetic positive controls test workflow only, never count as real F/8 passes.

Default Release App build: success, 0 warnings/errors. **109 distinct current-product tests passed**: 102 in `tests/cold-entry-regression.trx`, seven in `tests/current-entry-restored.trx`. `tests/cold-desktop-focused.trx` repeats the three new UI checks and is not added to that count; isolated/preflight repeats are not added either. Whole-repository documentation audit recorded three links to the other coating task's missing `COATING_NONLINEAR_CORRECTION_2026-10-31.md`; none is in this task's documentation. Scoped links/fences pass, and the original global failure remains recorded. No full-suite, native desktop or GPU certification.

Implementation, usage, limitations and the complete seed table: [task report](../../../../docs/INITIAL_STRUCTURE_F8_COLD_START_2026-10-31.md).
