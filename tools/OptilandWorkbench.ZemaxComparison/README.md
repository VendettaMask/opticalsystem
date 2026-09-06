# C# Zemax / Workbench comparison

The 2026-09-06 MS-L7 rerun has 44 Pass, 6 Close and 2 Difference within 52 numerical contracts; API and image-contract limitations remain explicit. Geometric energy, line/edge bin integration and extended-source output sampling are repaired. Contrast Loss exports unshifted pupil phase separately from the preserved mean-ray GUI indicator and is explicitly partially comparable. See the [current Huygens postprocessing evidence](../../docs/ZEMAX_HUYGENS_REPAIR_2026-09-06.md).

This independent validation tool audits all 72 public Core canonical analysis keys. It uses the existing Application request factory and the real Core calculations. It does not run in the desktop product. No product project references this tool or ZOS-API.

**Current numerical coverage is 52 explicit mappings; registration is not a numerical pass.** Four further spot layouts are captured and audited for numeric/text channels. Eight analyses receive native capability inspection: three have a documented physical-definition mismatch and five image/source adapters still need a common source and detector contract. Six keys have no verified native equivalent (Classified Data is also unavailable for the selected sequential model), and two non-sequential entries are not applicable to a sequential snapshot. Every entry remains in the report. Native IDs without a Workbench counterpart are enumerated separately as `ZemaxOnly`.

`AdapterNotImplemented`, `PhysicalDefinitionMismatch` and `UnsupportedByZosApi` mean different things. Empty structured channels alone cannot establish API unavailability: geometric image analysis may expose text, and Image Simulation may export a bitmap. Capability inspection records actual reset settings and does not compare them to unrelated Workbench defaults. See the [MS-L7 expansion evidence and remaining differences](../../docs/ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md).

The original ten numerical contracts:

| Canonical key | Native AnalysisIDM | Numerical boundary |
|---|---|---|
| First Order | SystemData + EFFL/EPDI operands | EFL and `abs(EFL)/EPDI`; no claim for working F-number or TotalTrack |
| Spot Diagram | StandardSpot | Native RMS/GEO radii; one field/wavelength; hexapolar; chief reference |
| Ray Fan | RayFan | Tangential Y and sagittal X, selected field/wavelength |
| Pupil Aberration | PupilAberrationFan | Selected field/wavelength extracted from the canonical all-field output |
| Optical Path Difference | OpticalPathFan | Tangential/sagittal OPD; reference differences remain measurable |
| MTF | FftMtf | Modulation, tangential/sagittal, explicit frequency range |
| Huygens MTF | HuygensMtf | Explicit pupil/image sampling and image pitch |
| PSF | FftPsf | Native unnormalized irradiance; incompatible pixel origins are reported, not fitted |
| Huygens PSF | HuygensPsf | Native unnormalized irradiance, chief reference, explicit pitch |
| Wavefront | WavefrontMap | Signed OPD, wavelength reference sphere, no tilt removal; explicit even-grid pupil convention |

Use `--list-analyses` for the complete machine-stable mapping and skip reasons. Display titles and translations never select an analysis or an axis unit.

The 42 additional contracts cover Seidel tables/diagram, single rays, field curvature/distortion and chromatic scans, cardinal data, Y-Ybar, three angle/height analyses, prescription/system reports, four RMS scans and RMS field map, eight additional MTF scans/methods, three PSF/line profiles, four energy analyses, relative illumination, Fringe Zernike, footprint extents, contrast loss and Y-input Jones pupil magnitudes. The registry records partial scopes separately: a scalar footprint extent is not a point-cloud comparison; native batch-ray angle scans are not the built-in three-curve IHT plot; image-plane Ex/Ey magnitudes are not a full complex Jones matrix.

## Requirements and setup

- .NET 10 SDK; Windows x64 for live capture.
- Installed Ansys Zemax OpticStudio 2026 R1 with a valid ZOS-API license. The tool checks actual version and `IsValidLicenseForAPI` before analysis execution.
- Windows .NET Framework 4.x compiler/runtime. OpticStudio's assemblies depend on .NET Framework/WPF/remoting; a small C# host is compiled against the locally installed assemblies. Proprietary assemblies are neither copied into the repository nor downloaded through NuGet.
- ZOS paths are resolved from `--zos-api-path`, `ZOS_API_PATH`, `ZEMAX_ROOT`, the registry and installed Ansys version directories on fixed drives. Use the install root containing `ZOSAPI.dll`, `ZOSAPI_Interfaces.dll` and `ZOSAPI_NetHelper.dll`; the `ZOS-API` subfolder is also accepted. No local absolute installation path is compiled in.
- Ordinary CI builds/tests the portable .NET tool and offline fixtures. It does not launch Zemax or compile the local .NET Framework host.

Each live analysis runs in an isolated worker. Windows job objects terminate only that worker's descendants on timeout/cancellation. Existing interactive OpticStudio sessions are never terminated. A license permitting only one instance may require closing that interactive session yourself before capture.

## Commands

From the repository root:

```powershell
dotnet restore tools/OptilandWorkbench.ZemaxComparison --locked-mode
dotnet run -c Release --project tools/OptilandWorkbench.ZemaxComparison -- --input "C:\Users\19851\Desktop\[MS-L7](10X大NA大视场).ZMX" --all --capture-screenshots --keep-raw

# A different lens requires no source changes.
scripts/compare-zemax.ps1 -InputFile "D:\Optics\AnotherLens.ZMX" -OutputDirectory "D:\Optics\comparison-results" -Configuration "tools\OptilandWorkbench.ZemaxComparison\comparison-settings.json"

# Stable keys, repeatable selection, all optical configurations.
dotnet run --project tools/OptilandWorkbench.ZemaxComparison -- --input "D:\Optics\AnotherLens.ZMX" --analysis MTF --analysis "Huygens PSF" --configuration all

dotnet run --project tools/OptilandWorkbench.ZemaxComparison -- --list-analyses
```

The PowerShell wrapper performs environment checks, locked restore, build and argument forwarding only. `-Configuration` denotes the JSON file; `-OpticalConfiguration` maps to the CLI's one-based optical configuration number or `all`.

CLI options: `--input`, `--output`, `--config`, repeated `--analysis`, `--all`, `--configuration`, `--zemax-version`, `--zos-api-path`, `--overwrite`, `--fail-on none|error|difference`, `--timeout` (per worker, seconds), `--list-analyses`, `--capture-screenshots`, `--keep-raw`, `--report-language zh-CN|en-US`. `--all` and `--analysis` are mutually exclusive. The default timeout is 120 seconds. Input is required except for listing.

Default output names include lens name, expected version, UTC timestamp and source SHA-256. A nonempty output is refused by default. `--overwrite` is accepted only for an existing tool manifest with identical source/configuration hashes. After acquiring an exclusive run lock, the previous generated evidence is moved together into a `previous-run-<UTC>` subdirectory; unrelated user files are retained. This prevents stale plots or arrays from appearing in a subset rerun. A different tolerance file requires a new output directory. Output cannot contain the input file. The original file is never saved by either executor; hashing and modification time are checked at completion.

## Settings and tolerances

Edit `comparison-settings.json`. Its version and exact byte SHA-256 are recorded. `analyses.<canonical key>` provides explicit field, wavelength, pupil/image sampling, hexapolar ray density or fan count, image delta in micrometers, maximum frequency and **per-quantity** tolerances. Unknown configuration members, invalid ranges, missing tolerances and unmatched Workbench-only overrides for comparable adapters are rejected.

Each contract specifies its field/wavelength scope: selected, primary, all defined or a continuous scan. It does not exhaust all possible settings. Configure/MapWorkbench expand that scope before execution; unused generic request fields do not override a contract's explicit selectors. First Order follows the native primary wavelength. Configuration `all` repeats the selected analyses from separate imported immutable snapshots. Canonical requests, expanded Workbench parameters, native CFG and final selector/property readbacks are saved. These are **CapturedSettings**, not universal Zemax defaults. Extended-source energy uses the owned `assets/uniform-square.IMA`, hashed and copied into run input before either worker starts. Models retain their own apodization, aiming, materials, fields and spectra. Numerical adapters require focal image space and MM lens units; unsupported field mappings remain incomparable.

RMS contracts use radial Gaussian order 6 and explicitly select 12 azimuthal samples in Workbench for the MS-L7 convergence experiment. The native angular rule is not exposed by the typed settings interface; 12 is not asserted to be a universal Zemax rule. Core's general default remains 6. Native RMS selector enum names disagree with the installed 26.1 report's physical reference and density; the report is checked in addition to enum readback.

Scalar pass: absolute error <= absolute tolerance + relative tolerance × absolute reference. Curve/grid pass: all points satisfy that bound, or peak-reference NRMSE satisfies the per-quantity threshold; `Close` has its own NRMSE threshold. Minimum physical coverage is independently enforced. The scale floor is the absolute tolerance, avoiding division by an almost-zero pupil-aberration reference. Raw pointwise errors, worst point and percentiles remain in the report even when aggregate NRMSE passes. These thresholds are editable validation policy, not software specifications or a claim of physical accuracy.

## Data and normalization

The tool preserves raw and normalized data separately, with scalar, 1D series, 2D grid/invalid mask, structured text, image and complex-field schemas. Schema availability does not establish full image or complex-field equivalence. The required raw arrays/text/CFG are always retained for audit; `--keep-raw` explicitly requests that retention. There is no destructive cleanup mode.

Curves convert only compatible typed quantities/units, sort physical coordinates and interpolate on the union of knots inside the common interval. There is no extrapolation or axis stretching. Grid comparison currently requires equal physical grids after unit conversion; an origin/pitch mismatch is `Incomparable`, with both normalized grids preserved. Explicit orientation utilities move axes, values and masks together; no value-based orientation search is performed.

Wavefront normalizes the known even-grid pupil convention shared with the existing golden tests. Workbench subtracts its own minimum for display; the normalizer recovers signed physical OPD from its unrounded optical-path mean and wavelength, independent of any Zemax value. This is a documented display conversion, not a fitted piston. No phase wrapping, tilt removal, reference-sphere replacement, intensity scaling, peak normalization or chief-to-centroid fitting is silently applied. Unsupported conventions stay incomparable.

Curve metrics include overlap, absolute/relative errors, RMSE/NRMSE, percentiles, Pearson and worst coordinates, with ordinary CSV and overlay/difference PNG files on Windows (metric-ID colons must not create alternate data streams). Wavefront grids add RMS/PV; irradiance grids add common-mask energy, peak, centroids, marginal FWHM and encircled-energy radii. These refer to the captured finite window, not infinite-image energy. Frequency-domain MTF curves add DC and 10/20/30/50 cycles/mm statistics; field/focus axes do not receive frequency statistics. Strehl is omitted without a verified common definition. Zernike compares 37 Fringe coefficients in waves with explicit pupil sampling; Standard full-field decomposition is a separate contract.

## Outputs and exit codes

`analysis-registry.json` records the complete canonical mapping, typed result contracts and default per-quantity tolerances. Every executed entry also records the actual configured tolerances next to its request and metrics. The registry is an audit inventory; a listed native ID does not imply that an adapter has been implemented or that the model is supported.

`manifest.json` contains source integrity, source header version, immutable model summaries/preflight issues, source revision and assembly hashes, .NET/OS, actual Zemax/license information and configuration hash. `run-summary.json` and `analysis-matrix.csv` distinguish enumeration, capture, actual comparable results and all conclusion/support counts. `COMPARISON_REPORT.md` includes the full matrix, settings, transforms, plots and worst differences. Every entry has `comparisons/<key>-cN/comparison.json`; successful comparisons also have values CSV and plots. Logs and failed analysis details are retained incrementally. One failure never discards completed results.

Native screenshots use the existing OpticStudio ZPL window-export mechanism, orchestrated by C# without UI clicks. The native macro loads a disposable Zemax-saved copy and the exact captured CFG. It does not participate in numerical computation. JPEG signature, lens/configuration/image hashes and capture status are recorded. Numerical redraws are labelled separately. Text-only or unavailable windows never count as numeric passes. `IA_.ToFile` exports text and is deliberately not used as a screenshot API.

| Code | Meaning |
|---:|---|
| 0 | Run completed under the selected failure policy; inspect skip and incomparable counts |
| 1 | At least one Close/Difference with `--fail-on difference` (default) |
| 2 | Setup/configuration/file/API failure, or an analysis error under `error`/`difference` policy |
| 3 | Unhandled internal tool failure |
| 4 | Cancellation or numeric worker timeout; completed results are retained |

`--fail-on error` ignores numerical differences; `none` also ignores per-analysis failures. Setup errors, cancellation and timeout remain nonzero under every policy. A zero exit with no comparable results does **not** certify agreement. Screenshot failure is a separate status and does not rewrite a completed numerical conclusion.

## Troubleshooting and extending

- Missing source: provide the actual path. The tool never substitutes a similarly named historical file.
- API not found: supply `--zos-api-path` or `ZOS_API_PATH`.
- License/connection failure: inspect `raw/zemax/probe/environment.json`, `error.json` and `logs/zemax.log`; check whether another instance consumes the available API license.
- Unresolved material/opaque geometry: inspect Workbench preflight and native model warnings. Resolve the model deliberately, then create a new run; never silently substitute a glass or plane.
- Timeout: increase `--timeout` for that run; retain the old report. Ctrl+C terminates owned workers and writes remaining statuses.
- Pixel-grid mismatch/reference mismatch: inspect both raw results and captured requests. Do not fit a shift, scale or flip to reduce the score.

To implement another mapping, edit the unique `AnalysisComparisonRegistry`, add a typed `Adapter` in `ZemaxHost/Host.cs`, and add its explicit canonical settings and result normalization contract. Reuse `WorkbenchRuntime.BuildAnalysisData`; do not duplicate its analysis factory. Add quantity tolerances, fixed-raw offline tests, and a licensed-machine test. Only promote `AdapterNotImplemented` after settings, physical axes and result definitions are verified. Historic `123456.ZMX` scripts contain file-specific assumptions and must not be promoted into defaults for arbitrary lenses.

Run portable tests with `dotnet test tests/OptilandWorkbench.ZemaxComparison.Tests`. See [audit and integration evidence](../../docs/ZEMAX_COMPARISON_TOOL.md) and the [2026-09-06 numerical repairs and fresh comparisons](../../docs/NUMERICAL_REPAIR_2026-09-06.md). The original reports remain historical evidence; the product fixes preserve the comparison tolerances and do not fit results in the reporting layer.
