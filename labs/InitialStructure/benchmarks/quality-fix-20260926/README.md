# v12 optical-quality repair evidence

The input is the unchanged twelve prescriptions in `../quality-audit-20260926/inputs.json`. This folder records the area-integrated evaluation and constrained local solver repair. Runtime optics are exclusively the formal shared C# Core.

`runner/QualityFix.csproj` references the normal Release test-output assemblies. Build the laboratory Release solution first, then run:

```sh
dotnet run --project labs/InitialStructure/benchmarks/quality-fix-20260926/runner/QualityFix.csproj -c Release -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json labs/InitialStructure/benchmarks/quality-fix-20260926/local-results-final.json
```

`local-results-final.json` records 600 charged solver evaluations per input, entry/best values, all model trials, actual accepted steps, final 21-field/10×32 Gaussian validation and a separate 41-field/16×48 convergence check. Entry, fresh recalculation and convergence checks are diagnostic calls outside the 600-call local solver budget; this is not an end-to-end search budget claim. The denser check uses 72 boundary directions plus the center, and never resizes the independently validated snapshot.

`local-results.json` is the same optical implementation before additional archive-identity validation; `local-results-final.json` uses the final runtime binary. `local-results-without-field-rms-penalty.json` preserves the first implementation experiment. It exposed a lower mean objective with worse worst-field RMS, motivating explicit per-field RMS penalties. It is not the final runtime result.

Passing an output directory and `--search` runs two complete searches derived from the same source specifications (F/2.8, 40° and F/8, 20°), with 10000 evaluations, serial execution and a 10-minute time cap. New checkpoints are separate from the source files. This is a two-case regression experiment, not the historical 60+15 acceptance protocol.

Raw TRXs, full search checkpoints and build logs are under `artifacts/validation/quality-fix-20260926`. `verification.json` records final hashes, all raw TRX counters, default Debug/Release binaries and build logs. `source-freeze.json` records 381 runtime/test/build source identities. Final full regressions: formal 1311/1311, comparison tool 104/104, laboratory 212/212, all without failures/skips. Both solutions built in default Debug/Release outputs with zero warnings/errors. Initial failing diagnostic attempts remain distinguishable from final passing runs. See [implementation and limits](../../../../docs/INITIAL_STRUCTURE_QUALITY_FIX_2026-09-26.md).

Final local results: all twelve solved through valid models with no `DerivativeUnavailable`; all retained geometries pass. New-rule acceptance increases from 1/12 initial prescriptions to 2/12 after local work. Worst-field RMS decreases in ten, is retained in one, and increases in one. The larger convergence request has no missing rays and differs in worst RMS by at most about 4.11e-15 mm; this applies only to these prescriptions. The two full searches retain 0/10 accepted at F/2.8, 40° and 1/10 at F/8, 20°, each with 48 actual reduced stages. Neither executes a glass/stop neighborhood in its remaining budget.

`search-summary.json` mirrors the final two complete searches. `search-recalculation.json` records identical published Core results, prescriptions and STAROPT export round trips for all twenty retained candidates. Run `--verify` with the search-output directory to repeat this check. The first verification attempt compared fresh solve-local residual arrays with intentionally empty checkpoint arrays; the retained mismatch evidence shows no optical or snapshot difference. The corrected verifier checks the regenerated residual sum and compares every published result after applying the existing checkpoint compaction contract.
