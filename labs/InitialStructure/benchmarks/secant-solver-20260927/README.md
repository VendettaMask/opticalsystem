# v14 guarded secant solver evidence — 2026-09-27

Local storage review (2026-11-08): this directory describes a historical experiment, not a maintained default replay command. The related old build output and the checked bulk restart files have been retired; source, regression inputs, original summaries, exported prescriptions and unique failures remain. See the [scoped deletion and preservation record](../../../../validation/rechecks/2026-11-08/remaining-task-cleanup/README.md). This is not a new numerical acceptance result.

See the [implementation, papers and measured limits](../../../../docs/INITIAL_STRUCTURE_SECANT_SOLVER_2026-09-27.md). All runtime optics remain in formal Core; this change is generic numerical algebra in the laboratory solver. Full laboratory Release tests pass 234/234. Formal 1311/1311 and comparison 104/104 are reused from the preceding same-day complete verification, with unchanged Core/App binary hashes; they were not rerun for this laboratory-only change.

- `local-ablation.json`: twelve frozen inputs, each with zero or four maximum consecutive Broyden updates, 600 solver evaluations per case/method. The zero-update snapshots match the prior frozen results exactly. Local final passes improve from 2/12 to 4/12; this is not a search success rate.
- `search-summary.json`: two complete 10000-evaluation searches. F/2.8 remains 0/10 retained passes; F/8 improves from v13's 3/10 to 10/10 across six root branches.
- `search-recalculation.json`: 20 fresh Core comparisons, exact STAROPT round trips and independent convergence sampling. All ten accepted records retain their limits; one already-failing wide-field record has a larger RMS at the denser field grid.
- `wide-50000-summary.json` and `wide-50000-recalculation.json`: separate larger-budget wide-field search (50000 evaluations / 175 trials), still 0/10 retained passes; best RMS 161.145 μm. All ten exports/recalculations match.
- `wide-diagnostics.json`: 11/24 root trials lack full continuous residuals, including eight with six to eight elements; neighborhood counts by element count.
- `source-freeze.json` and `verification.json`: source, binary, raw evidence and test identities, including which formal-product checks were reused unchanged.

Original summaries, recalculations, thirty STAROPT exports, logs and TRX files remain under `artifacts/validation/secant-solver-20260927/`. Each retired search checkpoint matched the SHA256 and work counts in its original summary. Frozen-v13 DLLs are also retired; source and the recorded binary hashes remain. No historical Optiland or Zemax capture data was regenerated.

The runner is retained as historical source, not a current default replay command.

The isolated C# diagnostic runner requires no additional NuGet packages and calls only shared Core for optical quantities. Input/start snapshots are fixed; run IDs and elapsed times vary, so compare optical fingerprints and published metrics rather than whole checkpoint files across repetitions. Entry, final validation and convergence costs outside each local solver are explicitly separate from its 600-evaluation budget.
