# v11 optical quality audit

This is a read-only investigation of actual completed searches, not a new runtime version or a release success-rate claim. The [report](../../../../docs/INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md) separates confirmed defects/limitations, literature, failed ablations and proposed repairs.

- `source-runs.json`: checksummed source runs, budget allocation, planned forms, retained and trial counts.
- `inputs.json`: twelve saved starting prescriptions, selected deterministically as the best candidate for each element count in two runs.
- `solver-ablation.json`: all 24 paired 600-evaluation local solves. The only experimental change is a relaxed trust-radius expansion boundary. It did not increase physical passes; two easier cases worsened. Best results were independently recalculated with identical formal Core metrics and snapshots.
- `inspection.json`: exact rejected derivative probes and fixed-prescription 41-field scans using legacy 97-point, hexapolar and Gaussian pupil plans.
- `quality.json`: three legacy-accepted prescriptions, Gaussian convergence (10×32, 16×48, 24×64), formal f-tan distortion, pupil aberration and unscaled geometric MTF at two pupil densities. These are not FFT/Huygens MTF or external Zemax results.
- `algebra.json`: numerical smoke and exact production/audit-copy equality for the unchanged generic solver.
- `verification.json`: source/binary/evidence hashes and unchanged prior engineering baseline.

The 50 mm / F2.8 / 40° run has 0/31 accepted trial candidates; the 50 mm / F8 / 20° run has 3/30. Neither is a collection of independent random-seed acceptance runs. For the latter, two of the three accepted prescriptions exceed 50 µm RMS under converged Gaussian pupil integration. The old discrete 97-ray statistic remains historically correct under its own declared measure.

`probe/AuditTrustRegion.cs` is an audit-only copy of the generic numerical solver, with a single optional boundary-policy switch. No optical tracing, geometry or metric formula is copied: every optical result uses the existing formal Core via the laboratory adapter or public Core analysis services. All projects remain outside both product solutions and introduce no package dependencies.

Raw files, saved source checkpoints, full solver trials and retained v11 assemblies are under `artifacts/validation/quality-audit-20260926`. Reproduce from the repository root, using those retained assemblies if current runtime sources have changed:

```sh
dotnet run --project labs/InitialStructure/benchmarks/quality-audit-20260926/probe/Probe.csproj -c Release -p:AuditBinaries=/absolute/path/to/artifacts/validation/quality-audit-20260926/frozen-v11 -- /tmp/quality-audit-replay /absolute/path/to/first.family.json /absolute/path/to/second.family.json
dotnet run --project labs/InitialStructure/benchmarks/quality-audit-20260926/inspection/Inspection.csproj -c Release -p:AuditBinaries=/absolute/path/to/artifacts/validation/quality-audit-20260926/frozen-v11 -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json /tmp/quality-inspection.json
dotnet run --project labs/InitialStructure/benchmarks/quality-audit-20260926/quality/Quality.csproj -c Release -p:AuditBinaries=/absolute/path/to/artifacts/validation/quality-audit-20260926/frozen-v11 -- labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json /tmp/quality-diagnostics.json
```

All three diagnostic programs completed successfully. Prior formal **1309/1309**, comparison-tool **104/104** and laboratory **200/200** remain the unchanged full-test baseline; full tests were not rerun for these isolated diagnostics. Runtime/test source hashes (380 files) match the v11 automatic-diameter freeze. The source checkpoints were not modified. No new original/holdout protocol, native UI or external numerical certification is claimed.
