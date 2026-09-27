# Pupil strategy and progressive recovery, 2026-09-27

This is a fixed-start diagnostic and single-seed search comparison, not release acceptance or a Zemax comparison. Optical values, ray validity and geometry come from the formal shared C# Core. The v18 baseline engine is commit `c69a1106db12c86a8b80836a1e588eeba6a678a6`; the final v19 source closure is recorded in `source-freeze.json`. The 164 frozen Core source files are unchanged.

The two specifications are reused from `../physical-stop-search-20260927/specifications.json`: f = 50 mm, F/8 with 20° half-field, and F/2.8 with 40° half-field, monochromatic 587.6 nm, automatic lens diameters, 3–8 elements, seed 101. Final evaluation always uses physical-stop aiming. RMS and maximum-radius limits remain 50 and 150 μm.

- `summary.json`: all 16 v18 start comparisons, four v19 recovery cases, and the three wide-angle search summaries. A rejected partial-ray result must not be ranked as good quality using its RMS. `BestWorstRmsAmongPhysicallyFeasible` filters to `TraceValid`/`LabAccepted`; it is not necessarily the first archive-ranked candidate and does not imply the focal-length or F-number targets pass.
- `retained-inputs.json`: all 40 final snapshots from the 16 + 4 local runs and 10 + 10 retained candidates from the new v18 FULL and v19 QUICK searches. Previously frozen v18 QUICK candidates stay in the preceding evidence directory. Residual arrays are omitted as in the checkpoint contract; the optical evaluations and snapshots are retained for exact replay.
- `replay.json`: every snapshot recalculates identically at its original settings and reloads identically after STAROPT export. An additional Core check uses 41 fields, 16×48 Gaussian pupil samples and 72 boundary rays plus a center ray. It records loss explicitly: 31 snapshots have zero lost rays on this grid; nine retain failures. Identity is not acceptance.
- `verification.json` and `source-freeze.json`: final test/build scope and hashes. Raw logs and full search checkpoints are under `artifacts/validation/pupil-continuation-20260927`, outside the committed evidence package.

The direct/deferred starts share an 800-evaluation ceiling, not identical actual work. Deferred starts use at most 400 implicit-pupil evaluations, then spend the remaining budget with the physical stop and `improveBeyondTargets: true`; direct starts stop when their targets are met. F/8 RMS differences therefore do not isolate the causal effect of pupil switching. Wide-angle results improve for one tested start and worsen for another; neither strategy reliably rescues the failed starts.

All wide-angle searches have the same 10,000-evaluation ceiling. The best worst-field RMS among physically feasible retained candidates is 377.789369 μm for v18 QUICK, 378.773375 μm for v18 FULL, and 377.789369 μm for v19 QUICK. None meets all targets. FULL explores six roots; QUICK explores 24. Some runs overlapped other verification work, so recorded wall time is not a controlled speed benchmark.

From the repository root:

```sh
dotnet run --project labs/InitialStructure/benchmarks/pupil-continuation-20260927/runner/PupilContinuation.csproj -c Release -- --replay labs/InitialStructure/benchmarks/pupil-continuation-20260927/retained-inputs.json artifacts/validation/pupil-continuation-replay
```

Other runner modes take the specification path, an output directory, and `--starts`, `--recovery`, `--quick`, or `--full`. They execute the engine currently built from the checkout; reproducing the historical v18 comparisons requires the v18 source, not relabeling a v19 run. The replay mode reuses the earlier `FrozenReplay.cs` to avoid a second analysis implementation.

See [the implementation and literature review](../../../../docs/INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md) for limits and remaining work. No native UI certification, multiseed release protocol, new Zemax capture, baseline-integrity audit or complete external numerical comparison was performed in this change.
