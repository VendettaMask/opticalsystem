# v13 neighborhood allocation evidence — 2026-09-26

Final build/verification record updated 2026-09-27; the directory date identifies the start of this experiment. Final full tests: formal 1311/1311, comparison 104/104, lab 224/224. The first formal run had one headless-session disposal exception; the unchanged-binary full recheck passed. Both TRX files are preserved.

Scope: two complete searches compared with the frozen v12 searches, using identical target specifications, seeds, initial prescriptions and unchanged Core optical settings. This is a scheduling regression, not the 60+15 acceptance protocol or new Zemax certification. See the [report](../../../../docs/INITIAL_STRUCTURE_NEIGHBORHOODS_2026-09-26.md).

- `search-summary.json`: new complete 10000-evaluation searches; F/2.8 has 0/10 retained passes and F/8 has 3/10. These are retained-candidate counts, not independent-run success rates.
- `comparison.json`: original/new file hashes, equality of 48 root snapshots and final evaluations, every new neighborhood allocation, and lineage of the three passes (one common four-element root).
- `search-recalculation.json`: 20 exact fresh Core recalculations, 20 exact STAROPT round trips and independent 41-field / Gaussian 16×48 / center plus 72 boundary-ray convergence samples, evaluated at the saved physical lens sizes.
- `source-freeze.json`: 383 source/configuration hashes. Optical calculation, sampling and objective implementation are unchanged relative to v12.
- `verification.json`: actual TRX totals, default binary/build hashes and evidence identities.

Raw checkpoints, exports, TRX and build logs are under `artifacts/validation/neighborhood-budget-20260926/`. The old checkpoints remain under `artifacts/validation/quality-fix-20260926/searches-final/`; their hashes match the previous evidence. Frozen v12 Core and Engine DLLs were copied before editing and verified against the prior manifest. Those DLLs are auxiliary local artifacts, not product dependencies. The old result files are not overwritten.

Reproduce with the normal Release binaries:

```sh
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Release --no-restore
dotnet run --project labs/InitialStructure/benchmarks/neighborhood-budget-20260926/runner/NeighborhoodBudget.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json artifacts/validation/neighborhood-budget-20260926/reproduction --search
dotnet run --project labs/InitialStructure/benchmarks/neighborhood-budget-20260926/runner/NeighborhoodBudget.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json artifacts/validation/neighborhood-budget-20260926/reproduction --verify
```

The diagnostic runner references the default laboratory Release binaries, requires no extra NuGet packages, and does not calculate optical quantities itself. It requests formal Core spot analyses and compares returned data. Its inherited local-solve mode is not part of this v13 experiment. Repeated runs produce new run IDs and timestamps, so compare optical fingerprints and evaluations rather than whole-file hashes for reproduction.

The complete F/8 search improves best worst-field RMS from 48.113 to 44.422 μm; the F/2.8 target still fails. All five enabled neighborhood types complete in both new runs. A lower joint objective can worsen an individual optical measure, and three accepted descendants do not imply three independent starting forms.
