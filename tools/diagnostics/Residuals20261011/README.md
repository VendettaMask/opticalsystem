# N01/N02 offline residual controls

This diagnostic references the formal Core, Application runtime and original Zemax comparison registry. It changes no product calculations or frozen references. Independent plane-wave and interpolation formulas are diagnostic hypotheses only; none feed runtime candidate evaluation or displayed product results.

Build from the repository root:

```powershell
dotnet build tools/diagnostics/Residuals20261011/Residuals20261011.csproj -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tools/diagnostics/Residuals20261011 -c Release --no-build -- D:\Projects\opticalsystem artifacts\validation\residual-controls-new\formal reproduce
```

Supply exactly three arguments: repository root, a fresh output directory, and a mode. The tool verifies the frozen MS-L7 source SHA and writes the source/Core/tool/native-data hashes to `provenance.json`.

| Mode | Measurements |
| --- | --- |
| `reproduce` | Original Huygens cross section and ideal/actual DEE, using captured settings and unchanged comparison tolerances through the product JSON boundary. |
| `huygens` | Five pupil node families, aiming on/off, nine fixed amplitude/quadrature hypotheses; 90 original-grid controls. Exact circular pupil-cell area uses the formal Core integrator. |
| `knots` | Native display-polynomial reconstruction and 32-node natural-spline check. Clipped intervals do not reveal unmodified physical integration knots. Native values never enter product calculations. |
| `energy` | 96 bilinear range/grid resampling controls with formal area integration. |
| `dense` | 60 original-pupil integration controls; changed FFT image density is diagnostic, not original-setting certification. The 32-image case has a different physical window and is explicitly a separate hypothesis. |
| `full-dense` | 54 full-window controls, including original versus interpolated pupil phase. No alternate ray tracer. |
| `energy-grids` | 72 pupil-grid/density controls; alternate settings are not acceptance replacements. |
| `cubic-grid` | 48 standard linear, Catmull-Rom, Keys and Lagrange intensity interpolation controls. The observed first-node quarter rule is a diagnostic hypothesis, not a runtime compensation. |
| `inputs` | Exact five-family normalized pupil coordinates for subsequent native tracing. |
| `rays` | The 709 frozen native pupil inputs traced through formal Core; compare image-local XYZ, full LMN, reference-sphere OPD and traced intensity. This does not compare absolute native cumulative optical path. |

`metric.Item1` is relative L2 and `metric.Item2` is maximum absolute difference in the candidate JSON files. These are distinct from the original comparison registry's NRMSE, `Close` and `Difference` classifications. Diagnostic completion does not mean native precision passed.

## Licensed-machine capture

The current computer cannot run Zemax. Preparation is supported here without starting a native process:

```powershell
pwsh -NoProfile -File tools/diagnostics/Residuals20261011/CaptureControls.ps1 -OutputDirectory artifacts\validation\residual-capture-prepared-new -PrepareOnly
```

On the user-designated Zemax 2026 R1 computer, use its existing native host directory containing `ZemaxHost.exe` and `NativeEnergyControl.exe`, compiled from the repository's host helpers and `../N02Layers20261009/NativeEnergyControl.cs` against that computer's API assemblies:

```powershell
pwsh -NoProfile -File tools/diagnostics/Residuals20261011/CaptureControls.ps1 -OutputDirectory artifacts\validation\residual-native-new -NativeHostDirectory D:\path\to\existing\host -ZosApiPath D:\path\to\Zemax\OpticStudio
```

The nine jobs retain the original source bytes and captured settings: endpoint and Zemax pupil traces, the three original EE ranges and X-only control, and three Huygens output types across Auto/Planar/Spherical. An explicit chief is added when absent from the endpoint family. The helper records actual settings/version and restores the original advanced setting. Real/Imaginary support is determined by execution; preparation does not certify API support. A failed native job leaves its diagnostic output for inspection and stops execution.

DEE ideal 2D data, its private integration grid and total-energy denominator remain unexposed by the audited public API. This script does not modify the lens to create an ideal reference. Native batch OPD is `CurrentAndChief`, not absolute cumulative optical path; verify the unit/reference before propagation comparison.

Pre-repair measurements and limitations: [2026-10-11 diagnosis](../../../docs/RESIDUAL_DIAGNOSIS_2026-10-11.md). The subsequent scalar FFT amplitude repair and current residuals are recorded separately in [the repair report](../../../docs/FFT_SCALAR_AMPLITUDE_REPAIR_2026-10-11.md). The executable supports ten modes; the 420 hypotheses remain diagnostic controls, not passing tests.
