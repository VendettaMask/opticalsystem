# v13 neighborhood allocation evidence — 2026-09-26

Local storage review (2026-11-08): this directory describes a historical experiment, not a maintained default replay command. The related old build output and the checked bulk restart files have been retired; source, regression inputs, original summaries, exported prescriptions and unique failures remain. See the [scoped deletion and preservation record](../../../../validation/rechecks/2026-11-08/remaining-task-cleanup/README.md). This is not a new numerical acceptance result.

Final build/verification record updated 2026-09-27; the directory date identifies the start of this experiment. Final full tests: formal 1311/1311, comparison 104/104, lab 224/224. The first formal run had one headless-session disposal exception; the unchanged-binary full recheck passed. Both TRX files are preserved.

Scope: two complete searches compared with the frozen v12 searches, using identical target specifications, seeds, initial prescriptions and unchanged Core optical settings. This is a scheduling regression, not the 60+15 acceptance protocol or new Zemax certification. See the [report](../../../../docs/INITIAL_STRUCTURE_NEIGHBORHOODS_2026-09-26.md).

- `search-summary.json`: new complete 10000-evaluation searches; F/2.8 has 0/10 retained passes and F/8 has 3/10. These are retained-candidate counts, not independent-run success rates.
- `comparison.json`: original/new file hashes, equality of 48 root snapshots and final evaluations, every new neighborhood allocation, and lineage of the three passes (one common four-element root).
- `search-recalculation.json`: 20 exact fresh Core recalculations, 20 exact STAROPT round trips and independent 41-field / Gaussian 16×48 / center plus 72 boundary-ray convergence samples, evaluated at the saved physical lens sizes.
- `source-freeze.json`: 383 source/configuration hashes. Optical calculation, sampling and objective implementation are unchanged relative to v12.
- `verification.json`: actual TRX totals, default binary/build hashes and evidence identities.

Exports, TRX, build logs and the unchanged summaries remain under `artifacts/validation/neighborhood-budget-20260926/`. The two original checkpoint hashes were verified against their summaries, with all twenty exported prescriptions present, before retiring those bulk files and the auxiliary frozen-v12 runtime. Source and recorded binary identities remain.

The diagnostic runner remains historical source, with no maintained default execution environment. Its original two-case protocol is described above; repeated runs were compared by optical fingerprints and evaluations rather than randomized run IDs or whole-file hashes.

The complete F/8 search improves best worst-field RMS from 48.113 to 44.422 μm; the F/2.8 target still fails. All five enabled neighborhood types complete in both new runs. A lower joint objective can worsen an individual optical measure, and three accepted descendants do not imply three independent starting forms.
