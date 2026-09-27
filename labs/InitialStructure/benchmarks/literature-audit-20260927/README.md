# v16 literature and candidate-quality audit

This is a **Workbench Core recalculation**, not a new optimization run, Zemax comparison, Zemax baseline-integrity audit, or screenshot review. Product Core now honors ray aiming in shared sampled-spot, aggregate-spot, aperture-diagnostic and envelope-sizing entry points; normalized-pupil generation also shares its resolved vignetting scale with stop targets. The laboratory search policy remains v16 and its generated snapshots still have aiming disabled.

`inputs.json` freezes three prior v16 runs' 30 retained snapshots plus actual charged neighborhood allocation by element count and original checkpoint SHA-256 hashes. `results.json` contains the new Core analyses and the loaded Core binary hash. Each candidate is checked for unchanged snapshot fingerprint after analysis. `verification.json` distinguishes new focused formal checks from the laboratory suite and historical full formal/comparison records. Source and input hashes are in `source-freeze.json`.

Measurements:

- Formal `DistortionAnalysis`: f-tan, +Y, 41 points, reference field 1, all original wavelengths, percent, ignore field vignetting factors.
- Formal `SingleRayTraceAnalysis`: source aiming setting, 11 equally spaced normalized fields, center plus 24 boundary pupil samples, all original wavelengths; physical refracting surfaces only. 275 rays per candidate, 8250 total. A sampled maximum is not a global incidence bound or coating assessment.
- Formal `FootprintDiagramAnalysis`: density 20, original designated stop, axis and outermost defined fields, all original wavelengths, delete vignetted rays. Its transmission percentage applies to that sampled bundle, not total physical throughput or relative illumination.
- RMS/status values are **frozen v16 results**, not recalculated by this runner. All inputs are monochromatic 587.6 nm; no multicolor or multiseed success-rate claim.

Run from repository root with .NET 10:

```sh
dotnet run --project labs/InitialStructure/benchmarks/literature-audit-20260927/runner/LiteratureAudit.csproj -c Release -- --audit labs/InitialStructure/benchmarks/literature-audit-20260927/inputs.json /tmp/literature-audit-results.json
```

The runner references current formal Core and laboratory projects; it implements no ray tracing, refraction, distortion or incidence-angle formula. The raw capture mode additionally accepts `--capture output.json checkpoint1 checkpoint2 ...`; it is not required for replay because the snapshots are frozen here. Original full checkpoints/TRX/build logs stay under ignored `artifacts/validation` and are described by hashes, not committed as new binaries.

Findings, primary literature, implemented fixes and pending work: [Chinese audit report](../../../../docs/INITIAL_STRUCTURE_LITERATURE_AUDIT_2026-09-27.md).
