# Full-field restart and early-field continuation evidence — 2026-09-27

See the [implementation, references and measured limits](../../../../docs/INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md).

The retained runtime change is the full-target restart gate: complete formal Core ray data may enter the joint constrained objective while geometry bounds are violated. Hard final acceptance is unchanged. Algorithm v16 preserves the existing root plan, reduced stages and area scheduling policy 2.

- `restart-inputs.json` freezes three real inputs for automated regression, including a missing-propagation control. Their source checkpoint SHA-256 is recorded.
- `restart-comparison.json` contains 16 same-budget replays selected by a Core audit of 151 v15 continuation entries. All baseline final evaluations and evaluation counts reproduce the saved checkpoint exactly. The joint objective improves in all 16, RMS in 15, and final physical feasibility changes from 11 to 12. These are not search-success rates.
- `root-comparison.json` compares all 24 identical roots under the original and experimental early-field stages; original v15 merits, EFL, F-number and evaluation counts match the saved search exactly.
- `early-field-experiment.patch` is **not** a runtime feature. It modifies only the two reduced field fractions. Its three complete searches improve best RMS but reduce the F/8 retained pass count from 10/10 to 5/10; the default path was restored.
- Search summaries and recalculations distinguish early-field experiments, restart-only prototypes and final v16 runs. Prototype files carry the original temporary algorithm identity and must not be silently treated as release checkpoints.
- `raw-evidence.json` records raw local probe files, including discarded setup attempts. The stale-binary partial root run and the exploratory `improveBeyondTargets=true` replay are excluded from comparisons.

All runtime optical quantities, dense 21-field acceptance and 41-field convergence checks call shared Core. No historical Optiland assets or Zemax captures were regenerated. Raw checkpoints, binaries, logs and TRX files are under `artifacts/validation/field-continuation-20260927/` and intentionally ignored by Git.

## Reproduction

Build the laboratory solution in Release first. The small C# runners default to its test-output dependency DLLs and accept `-p:AuditBinaries=/absolute/path` for frozen dependencies. Baseline v15 sources are at commit `7d68ed4`; compare published optical values and fingerprints, not elapsed times or randomized run IDs.

```sh
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Release --no-restore
dotnet run -c Release --project labs/InitialStructure/benchmarks/field-continuation-20260927/runner/FieldContinuation.csproj -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json artifacts/validation/field-continuation-repeat/short --search
dotnet run -c Release --project labs/InitialStructure/benchmarks/field-continuation-20260927/runner/FieldContinuation.csproj -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json artifacts/validation/field-continuation-repeat/wide --wide-budget 50000
dotnet run -c Release --project labs/InitialStructure/benchmarks/field-continuation-20260927/runner/FieldContinuation.csproj -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json artifacts/validation/field-continuation-repeat/wide --verify
```

Once the baseline wide search has created `f2.8.family.json`, `runner --roots <checkpoint> <output.json>` reproduces all root solves. `restart-runner <checkpoint> <output.json> --baseline` audits all restart inputs and asserts exact replay for the affected entries. Remove `--baseline` when using the changed runtime. The runners perform orchestration and comparisons only; they contain no second optical engine.

## Final verification

Final v16 laboratory Release tests: **247/247**, zero failed/skipped; default Debug/Release builds and final format checks pass. The 18 focused cases overlap the full suite. `verification.json` and `source-freeze.json` identify the exact scope. All 30 final-v16 records reproduce prototype optical values and work counts, pass exact Core recalculation and STAROPT round-trip comparison, and have zero lost rays in denser convergence sampling. Ten retained F/8 passes keep their limits. An already-failing short-budget wide-field record increases RMS by 1.077 μm; the large-budget maximum-radius increase reaches 1.085 μm. No wide-field candidate meets the optical targets.

Core sources are unchanged. Default DLL hashes change after rebuilding with the new Git informational version; `core-audit` directly compares PE method definitions/signatures/bodies and embedded resources. All 11216 Release methods and resources match v15 (`core-binary-audit.json`). Debug IL was not separately compared. This is not byte-identical binary verification or a new formal-product/Zemax test run.
