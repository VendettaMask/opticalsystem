# Pupil comparison diagnostic · 2026-10-08

This standalone diagnostic references the formal shared Core only. It compares wavefront-map aiming on frozen native OPD fan knots, checks mirrored signed-Y vignetting continuity, and measures endpoint/cell-center RA sensitivity. It does not fix product behavior, capture Zemax, classify the original 132 analyses, or certify native RMS-RA nodes.

From the repository root, supply a fresh output directory and request the repaired source-aiming contract:

```powershell
dotnet run -c Release --project tools/diagnostics/PupilComparison20261008 -- "." "artifacts/validation/pupil-comparison-20261008/new-run" --expect-source-aiming
```

The six native fan cases verify manifest hashes before comparison. Every input is hashed again afterward. Tessar controls freshly import unchanged ZMX with the current importer, not the pre-repair automatic-STOP snapshot. Only exact common fan/map coordinates are compared; display offset recovery uses product optical-path metadata, independently of native values.

`summary.json` retains scope, hashes, actual product shared-knot values, valid/candidate counts, errors and assembly/source fingerprints. Nonempty output is refused. With `--expect-source-aiming`, the map must match system aiming and both exit-pupil display settings must return identical physical samples. Source/forced path errors are both recorded; a missing corresponding valid pupil is reported as infinity, not silently dropped.

Omitting the optional flag retains the historical forced-aiming assertion and is expected to reject the repaired product. The preserved `run-03/summary.json` is the pre-repair diagnosis, while `repair-01/summary.json` verifies the repaired product. Neither run is a complete native Wavefront Map recapture. Fixed aiming in the zero-defocus diffraction helper remains a separately recorded, unrepaired path.

See [the diagnosis and evidence boundaries](../../../docs/ZEMAX_PUPIL_DIAGNOSIS_2026-10-08.md).

See [the product repair and regression scope](../../../docs/WAVEFRONT_AIMING_REPAIR_2026-10-08.md).
