# Afocal MTF versus field

Pure C# audit using the shared Core and the existing comparison registry. Product optical formulas are not implemented here. No foreground application or desktop control is used.

Build `AfocalMtfField20261031.csproj` in Release, then pass exactly three arguments:

```
AfocalMtfField20261031.dll prepare <repository> <fresh-run-root>
AfocalMtfField20261031.dll compute <repository> <run-root>
AfocalMtfField20261031.dll compare <repository> <run-root>
```

`prepare` reads the frozen original 170-request inputs, verifies the four files/five configurations, and writes 15 requests with explicit native angular units. Execute `native/jobs.json` with the background-only `FftProfileNative20261029` runner. Preserve captures; never substitute modified lens files or fitted frequency scales.

`compute` requires a fresh `core-<assembly-SHA-prefix>` directory and calls `WorkbenchRuntime.BuildAnalysisData`. Use a frozen private runner containing all managed and native dependencies, including Skia for offscreen comparison plots. `compare` uses the unchanged per-analysis tolerances and retains per-curve pointwise counts separately from the existing NRMSE conclusion. No reference is fabricated for empty native data.

Optional `probe` calls shared Huygens synthesis and frequency sampling by reflection to investigate window centering on the primary wavelength. Historical Fourier probe source is archived with its matching experimental runner identity; those candidates were rejected and are not product paths.

The committed reference ZIP supplies portable inputs and native readbacks for tests. Final results and outstanding gaps are in [the report](../../../docs/AFOCAL_MTF_FIELD_2026-10-31.md).
