# Physical-stop search integration, v18

This experiment compares `UsePhysicalStop=false` and `true` on the same two specifications, seed 101, one worker and a 10,000-evaluation budget. Each run permits 30 minutes; see the actual charged work and termination state in `summary.json`. Retained-candidate acceptance counts are not independent-run success rates. The physical-stop mode changes the sampled rays and must not be treated as the same optical objective with a faster optimizer.

The runner validates checksummed checkpoint round trips, independently recalculates every retained candidate with the exact final settings, and verifies each exported STAROPT snapshot. `retained-inputs.json` freezes those candidates and their source specifications/families/final evaluations so replay does not require another optimization. `replay.json` also records a separate 41-field, 16×48 Gaussian-area pupil grid with 72 boundary rays and a chief ray. This convergence check keeps each saved prescription fixed; it does not resize or optimize the design.

`source-freeze.json` identifies the source closure. `verification.json` records the test/build evidence and its scope. Full raw checkpoints, TRX files, logs and exports are under the ignored `artifacts/validation/physical-stop-search-20260927` directory; their hashes are retained. Binary hashes are from the recorded machine/build and are not promised to match another SDK/platform.

```bash
dotnet build labs/InitialStructure/benchmarks/physical-stop-search-20260927/runner/PhysicalStopSearch.csproj -c Release --disable-build-servers -p:NuGetAudit=false
dotnet labs/InitialStructure/benchmarks/physical-stop-search-20260927/runner/bin/Release/net10.0/OptilandWorkbench.InitialStructure.Tests.dll labs/InitialStructure/benchmarks/physical-stop-search-20260927/specifications.json artifacts/validation/physical-stop-search-reproduction
dotnet labs/InitialStructure/benchmarks/physical-stop-search-20260927/runner/bin/Release/net10.0/OptilandWorkbench.InitialStructure.Tests.dll labs/InitialStructure/benchmarks/physical-stop-search-20260927/retained-inputs.json artifacts/validation/physical-stop-replay --replay
```

The runner's assembly name grants access to existing test-visible orchestration APIs. It is outside all product projects and uses the formal Core for every optical computation. No new external simulator, Python optical runtime, or regenerated Optiland reference is involved. No Zemax capture, full external comparison or continuous-pupil/manufacturing certification was performed.

See the [implementation and measured results](../../../../docs/INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md).
