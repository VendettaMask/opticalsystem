# 123456.ZMX — Zemax 2026 R1 analysis baseline

This directory is the captured reference for the current `123456.ZMX`
sequential lens. It was produced with Ansys Zemax OpticStudio 2026 R1 through
ZOS-API and the companion ZPL screenshot exporter.

Open [`BASELINE_REPORT.md`](BASELINE_REPORT.md) for the complete human-readable
baseline dashboard, all 165 analysis entries, and all 148 captured GUI images.
The self-contained Workbench comparison is in
[`comparison-reports/workbench-vs-zemax-2026-07-30/`](comparison-reports/workbench-vs-zemax-2026-07-30/).

- Source SHA-256:
  `0CD65A2F823BAF5079F20F91D8310765899A182A6BE72DDAC53EDE943F2BF75B`
- Lens structure: 23 surfaces, 5 fields, and 3 wavelengths.
- Analysis catalog: all 165 `AnalysisIDM` entries were attempted.
- Captured: 148 analyses, all with structured data and screenshots.
- Screenshots: 106 native OpticStudio window exports and 42 plots rendered
  directly from captured ZOS-API data.
- Not applicable: 17 entries for which OpticStudio did not create an analysis
  for this sequential lens.

`manifest.json` is the machine-readable index and records the status,
provenance, settings, and output paths for every analysis. Each numbered
directory under `analyses/` contains `status.json`, `data.json`, the native
settings file when exposed by OpticStudio, raw text when exposed, and the
corresponding screenshot.

The 17 non-applicable entries remain in the manifest rather than being
silently omitted:

`DetectorViewer`, `ReverseRadianceAnalysis`, `PathAnalysis`,
`FluxvsWavelength`, `RoadwayLighting`, `SourceIlluminationMap`,
`NSCShadedModel`, `NSC3DLayout`, `NSCObjectViewer`, `RayDatabaseViewer`,
`NSCSurfaceSag`, `NSCSingleRayTrace`, `NSCGeometricMtf`, `UserDefinedCOM`,
`NEST`, `NSCSpotStandardNative`, and `XXXTemplateXXX`.

Regenerate or verify this baseline with the commands documented in
`tools/zemax_parity/README.md`.
