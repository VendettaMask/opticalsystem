# All 95 product examples: current-Core accuracy audit

The scope is `src/OpticalSystemDesign.App/Assets/SequentialExamples/index.json`: 95 ZMX sources in 14 categories. Historical `samples/lenses/*.staropt` fixtures are a different inventory.

This diagnostic uses the product's shared Core/Application and existing comparison normalizers/gates. It reimports all sources, replays the exact executable canonical requests from the frozen 2026-10-21 corpus, and preserves missing requests, unsupported models and invalid references. No product optical formulas, Python, UI interaction, tolerance changes or new native captures are introduced.

Build to a fresh directory, then invoke `ExampleAccuracy20261031.dll <mode> <repository> <fresh-study-root>`:

- `prepare`: verify every source against the product index and native corpus, import all configurations, write fresh snapshots and original requests.
- `compute`: two owned worker processes at a time, grouped by lens; each analysis has a 900-second cooperative cancellation budget. `worker` is internal. Per-request failures remain failures; successful worker completion does not mean numerical Pass.
- `compare`: existing registry, normalizers, comparison runner and original per-request policy. No completed worker may be absent. `Close` and unavailable Core results with numeric native references do not become Pass. Original non-executable requests are coverage gaps.
- `rays`: fixed recorded normalized input rays through the shared ray generator/tracer, exact semantic input identity, explicit native signed-index direction conversion, original coordinate/direction/acceptance/first-clip limits. Folded surface mapping and missing native reference remain incomparable.
- `regressions`: diagnose original Pass rows that were not retained. Unclipped chief rays are counterfactual diagnostics only and cannot supply acceptance or displayed results.

`Verify-References.ps1` verifies all used legacy files against the immutable native ZIP manifest. `Write-Report.ps1` writes the dated 95-lens table, per-analysis/request CSV/JSON, old-Pass preservation, capability taxonomy and independent afocal scope. It rejects mixed Core identities and incomplete execution. The companion `FinalAfocalRecheck20261029` replays the established 170 afocal requests using this same frozen Core; preserve its distinct request and reference identities.

A zero/one-point domain, incomplete native field range or unsupported model cannot be certified by an empty comparison. Result-level agreement is the acceptance goal; private sample-node equality is not. Fixed-ray controls use their recorded explicit input coordinates, independently of current analysis quadrature nodes. Reference convergence is not established merely by replaying a fixed-sampling capture.

See `docs/ALL_EXAMPLE_ACCURACY_2026-10-31.md` and `validation/rechecks/2026-10-31/all-example-accuracy/`. Early compiler, ray-identity and native-library failures are retained and excluded from final totals.
