# N01/N02 residual controls and captured-settings verification

This diagnostic references the formal Core, Application runtime and original Zemax comparison registry. It changes no product calculations or frozen references. Independent plane-wave and interpolation formulas are diagnostic hypotheses only; none feed runtime candidate evaluation or displayed product results.

Build from the repository root:

```powershell
dotnet build tools/diagnostics/Residuals20261011/Residuals20261011.csproj -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tools/diagnostics/Residuals20261011 -c Release --no-build -- D:\Projects\opticalsystem artifacts\validation\residual-controls-new\formal reproduce
```

Supply exactly three arguments: repository root, a fresh output directory, and a mode. The tool verifies the frozen MS-L7 source SHA and writes the source/Core/tool/native-data hashes to `provenance.json`.

| Mode | Measurements |
| --- | --- |
| `reproduce` | Original Huygens cross section, ideal/actual DEE and all twelve Huygens MTF field curves, using captured settings and unchanged comparison tolerances through the product JSON boundary. |
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

The user confirmed on 2026-10-11 that this is the Zemax computer. Local API capture succeeded using OpticStudio 26.1.0 build 260127, EnterpriseEdition and a valid license at `D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio`. The earlier contrary assumption is superseded. Preparation without launching a native process is still supported:

```powershell
pwsh -NoProfile -File tools/diagnostics/Residuals20261011/CaptureControls.ps1 -OutputDirectory artifacts\validation\residual-capture-prepared-new -PrepareOnly
```

Use the existing local native host directory containing `ZemaxHost.exe` and `NativeEnergyControl.exe`, compiled from the repository's host helpers and `../N02Layers20261009/NativeEnergyControl.cs` against its API assemblies:

```powershell
pwsh -NoProfile -File tools/diagnostics/Residuals20261011/CaptureControls.ps1 -OutputDirectory artifacts\validation\residual-native-new -NativeHostDirectory D:\path\to\existing\host -ZosApiPath D:\path\to\Zemax\OpticStudio
```

The nine jobs retain the original source bytes and captured settings: endpoint and Zemax pupil traces, the three original EE ranges and X-only control, and three Huygens output types across Auto/Planar/Spherical. An explicit chief is added when absent from the endpoint family. The helper records actual settings/version and restores the original advanced setting. Real/Imaginary support is determined by execution; preparation does not certify API support. A failed native job leaves its diagnostic output for inspection and stops execution.

DEE ideal 2D data, its private integration grid and total-energy denominator remain unexposed by the audited public API. This script does not modify the lens to create an ideal reference. Native batch OPD is `CurrentAndChief`, not absolute cumulative optical path; verify the unit/reference before propagation comparison.

Pre-repair measurements and limitations: [2026-10-11 diagnosis](../../../docs/RESIDUAL_DIAGNOSIS_2026-10-11.md) and [the preceding scalar option](../../../docs/FFT_SCALAR_AMPLITUDE_REPAIR_2026-10-11.md). The completed axial Huygens and original FFT DEE repair, current metrics and remaining limits are in [the integration repair](../../../docs/HUYGENS_FFT_ENERGY_REPAIR_2026-10-11.md).

Additional modes are `airy`, `plane-phase`, `integer-grid`, `native-huygens`, `geo-nodes`, `gaussian-controls`, `kernel-controls`, `geometry-controls`, `finite-cells`, `dee-native-grid`, `dee-phase-controls` and `dee-grid-scale` (22 modes total). These compare formal Core output, pupil-node families, finite-cell kernels, complex fields and internal energy-grid hypotheses. Native-dependent modes expect the retained local `artifacts/validation/residuals-20261011/followup` capture directories; they fail explicitly if those inputs are absent. Hypothesis sweeps do not count as regression tests.

`CaptureGeometryControls.ps1`, `CaptureKernelControls.ps1` and `CaptureDeeControls.ps1` record independent geometry, illumination, sampling and lens controls. They require the prepared local host/capture layout and a fresh output directory. They open isolated API applications, close only those applications, and never save the input prescription. Diagnostic overrides are preserved in requests and applied system settings. Failures and sampling warnings remain evidence, not passing cases. Python recovery scripts are offline investigations only; product computation remains pure C#/.NET.

The additional `offaxis` and `mtf-offaxis` modes read the committed MS-L7 off-axis captures in `validation/zemax/2026-r1/huygens-offaxis-2026-10-11` (24 modes total). `offaxis` compares formal product and plane fields, while `mtf-offaxis` independently transforms native chief/centroid PSFs. `CaptureOffAxisControls.ps1` and `CaptureMtfOffAxisControls.ps1` accept a native host directory and fresh output directory. The latter also records MTH operands and a requested density below the effective native minimum. Independent Cooke/Tessar follow-up is excluded; existing historical modes and assets are preserved.

`InspectFieldSpline.py` records an offline pre-repair interpolation investigation; inferred values never enter Core. The completed projection, MTF window and position-count repair is documented in [the off-axis repair](../../../docs/HUYGENS_OFFAXIS_REPAIR_2026-10-11.md). `FinalizeOffAxisEvidence.py` packages completed TRX, identity counts, binary hashes, native evidence and frozen-integrity checks; it refuses failed or incomplete runs.
