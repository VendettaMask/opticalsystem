# R1 runtime mechanism verification

This supports the [R1 implementation report](../../../../docs/INITIAL_STRUCTURE_DSEARCH_R1_2026-09-26.md). Algorithm v9 uses the existing joint real-ray residual vector throughout optical optimization and admits complete Core aperture diagnostics below the physical throughput threshold. Final physical acceptance remains unchanged. This directory is not a new 60-run acceptance or 15-run holdout result.

`inputs.json` contains all eight independent starting snapshots. They were regenerated with retained v8 binaries and checked against every starting fingerprint in the earlier [32-row audit](../dsearch-route-audit-20260926/residual-ablation.json). Targets, initial forms, materials and starting snapshots are unchanged. The capture executable fails if a hash differs.

`results.json` runs the actual v9 local adapter for 600 solve evaluations per start, plus separate entry/final analyses. All eight results exactly match the earlier isolated continuous-domain variant in final merit, focal length, physical acceptance and solve evaluation count. Total solve evaluations: **4772**; invalid evaluations: **0**; continuous but physically clipped evaluations: **1143**. Derivative-unavailable exits: **0/8**. Full physical successes: **0/8**. All failures are retained.

The original target-hinge audit had 4/8 derivative failures; it differs in both objective and domain. The audit's controlled joint-vector domain comparison was 2/8 to 0/8. These observations establish the mechanism, not convergence to a successful lens or a search success-rate improvement. The probe does not measure total aiming-inclusive physical-ray cost or claim a speed improvement.

The same executable imports the real frozen v8 `03-50mm-monochrome/101` checkpoint through its checksum-verifying store, creates a separate v9 run, spends its 120-evaluation budget, verifies current objective metadata and checks that the source file hash remains unchanged.

Both small console projects stay outside the product and laboratory solutions, use the existing test friend-assembly identity, and add no package or runtime optical implementation. Default reference paths expect retained v8 binaries and the current Release test output. Override either with `-p:AuditBinaries=/absolute/path` if needed. From the repository root:

```sh
dotnet run --project labs/InitialStructure/benchmarks/dsearch-r1-20260926/capture-v8/Capture.csproj -c Release -- labs/InitialStructure/benchmarks/dsearch-route-audit-20260926/residual-ablation.json labs/InitialStructure/benchmarks/dsearch-r1-20260926/inputs.json
dotnet run --project labs/InitialStructure/benchmarks/dsearch-r1-20260926/replay-v9/Replay.csproj -c Release -- labs/InitialStructure/benchmarks/dsearch-r1-20260926/inputs.json artifacts/validation/initial-structure-s4-v8-20260926/original/03-50mm-monochrome/101/checkpoint.json labs/InitialStructure/benchmarks/dsearch-r1-20260926/results.json
```

TRX files live under `artifacts/validation/dsearch-r1-20260926`. Final full laboratory tests pass **183/183**; the final objective subset passes **4/4** and is included in that total. Both default laboratory Debug/Release builds have zero warnings/errors. `verification.json` records final source/binary hashes and test evidence. Formal Core is unchanged; its frozen assembly identity is included in the result. The v8 acceptance evidence and prior audit are preserved separately.
