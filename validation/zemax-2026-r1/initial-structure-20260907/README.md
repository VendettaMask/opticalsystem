# Generated initial structures: Zemax 2026 R1 reference

Captured 2026-09-07 with OpticStudio 26.1 SP0, build 260127, Enterprise API license. These three prescriptions were generated from exact flat roots using the laboratory's C# design v2. They use 50 mm target EFL, F/8, 5° maximum half-field, monochromatic 587.6 nm light, SCHOTT N-BK7 and one/two/three spherical elements. `generation.json` retains the specification, zero-curvature root, updates and final Core validation. These separate representatives do not enter the twelve-specification search success denominator.

Each directory contains the original C# snapshot, validated STAROPT export, Zemax-saved `candidate.ZMX`, native environment readback and `native-rays.json`. The native batch traces real unpolarized rays at six normalized fields (0, .25, .5, .7, .85, 1), using the center plus six rings of sixteen azimuths including the pupil boundary: 582 rays per prescription. Lens/image coordinates are millimeters; ZMX wavelengths are micrometers. Native absolute image coordinates are compared directly with formal Core. Independent centroid/RMS/maximum-radius statistics are calculated only in the offline validation program.

Before capture, absolute ray-coordinate, spot-radius and EFL tolerances were fixed at `1e-8 mm`. All three representatives pass. The largest coordinate difference is `1.34e-14 mm`, largest spot-radius difference `7.31e-15 mm`, and largest EFL difference `2.14e-14 mm`. See `dense-ray-comparison.json` for unrounded values and every field.

The standard comparison tool also executed First Order, Spot Diagram, MTF and Wavefront for each lens: **12/12 selected comparisons Pass**, no Close/Difference/Error. Each comparison folder includes captured requests, CFG settings, native text/arrays, Workbench recalculation, tolerances and plots. These analyses have their own explicit captured settings (including chief reference for standard spot), distinct from the dense centroid check. The 173 unselected registry rows per lens remain marked Skipped in the matrix and summary; their empty per-analysis folders are omitted. No screenshot is used as numerical evidence. These settings are specific captures, not universal Zemax defaults. This evidence does not certify other designs, spectra or analyses.

`manifest.json` records SHA-256 for the retained files. The lab's three offline reference tests verify snapshot/lens/native-array integrity and compare the frozen absolute coordinates through the current formal Core. No production or laboratory runtime project reads this directory.

## Replay

Offline, from the repository root:

```powershell
dotnet run -c Release --project validation/zemax-2026-r1/initial-structure-20260907/capture-source/Comparer -- validation/zemax-2026-r1/initial-structure-20260907 artifacts/initial-structure-offline-recheck
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests --filter FullyQualifiedName~FlatStartZemaxReferenceTests
```

The comparer reads the frozen inputs and writes `dense-ray-comparison.json` only to the new output directory. The offline test is read-only.

To generate new representatives, run `capture-source/Generator` with `labs/InitialStructure/benchmarks/flat-start-v1/specs/03-50mm-monochrome.json` and a new artifact directory. It changes only name and fixed element count for these explicitly separate examples. Do not overwrite the frozen files when the search changes.

On a licensed Windows machine, compile `capture-source/ExportLens.cs` and `CaptureRays.cs` with the installed .NET Framework x64 C# compiler, referencing `System.Web.Extensions.dll` and the installed `ZOSAPI.dll`, `ZOSAPI_Interfaces.dll`, `ZOSAPI_NetHelper.dll`. The exporter arguments are API directory followed by generated `candidate.json` paths; the ray capturer takes API directory followed by resulting `candidate.ZMX` paths. Both verify version 26.1 and close their owned API application. Source is retained for audit; these hosts are validation tools only.

For the four standard analyses, run the existing `tools/OptilandWorkbench.ZemaxComparison` command separately on each ZMX with a new output directory and `--analysis "First Order" --analysis "Spot Diagram" --analysis MTF --analysis Wavefront --keep-raw --timeout 300 --zos-api-path <installed API directory>`. Preserve and inspect each captured settings file and conclusion. A newly generated prescription is new evidence, not an update to this frozen capture.
