# C# Zemax / Workbench comparison

The independent `NativeExplicitFanNormalizer` requires `NativeExplicitPointsV1`, millimeter units, the selected wavelength, a valid chief, complete ordered pupil axes and explicit error/vignette masks. It never fills or relabels a built-in plot. The [Objectives continuation](../../docs/OBJECTIVES_COMPLETION_2026-10-23.md) records matched source, primary wavelength and frozen-thickness readbacks, with separate every-surface physical gates. F-Theta full-field controls and higher-sampling energy controls retain different request identities from the original matrix.

## Afocal comparisons

The main runner and Sequential corpus runner dispatch afocal support per analysis, rather than excluding every afocal system. MM lenses have audited unit contracts for Ray Fan, FFT MTF, Field Curvature/Distortion, and the limited scalar FFT/Huygens PSF contract below. Other afocal contracts remain explicitly unverified. Native `AFocalImageSpace`, `AfocalModeUnits`, selected field/wavelength and angular MTF frequency must agree with the request and captured settings; localized axis captions never select a unit.

2026-10-28: linear scalar PSF/Huygens PSF requires one selected field/wavelength, the final surface, chief reference, zero rotation and no peak normalization. The historical canonical property `ImageDeltaMicrometers` carries **mrad in afocal mode**; only the native payload converts it to the model's declared angular unit. Zero explicitly requests automatic sampling; negative pitch, all-wavelength and polarized requests remain outside this adapter contract. Captured selectors, sampling, numbered configuration, reference, normalization and pitch must agree. Native grid axes are decoded to mrad without changing origin, ordering, intensity or the strict coordinate gate. Coordinate mismatch is Incomparable, not a fitted Pass. Twenty existing frozen PSF grids now run through the formal normalizer: automatic 4 Pass/6 Incomparable, fixed-angle 10 Pass. Live native dispatch was not executed. See [repair and evidence](../../docs/AFOCAL_PSF_CONTRACT_REPAIR_2026-10-28.md).

For afocal FFT MTF, `MaximumFrequency` is **cycles/mrad** in canonical configuration and Workbench requests. The native request converts to the source's angle unit (for example cycles/arcminute); normalized outputs return to cycles/mrad. `Field=0` and `Wavelength=0` explicitly select all fields and all defined wavelengths for FFT MTF. Every field retains separate T/S curves.

The native `captured-settings.json/request` is the converted native adapter payload. For offline Workbench replay use the canonical request in `run-summary.json` or `raw/workbench/.../request.json`, not the converted native frequency as a canonical frequency.

Afocal Ray Fan normalizes to **degrees** and requires an explicit per-analysis `AfocalQuantities` object containing `IncidentAngle` tolerances. Focal `Quantities.ImageHeight` limits remain unchanged and cannot become angular limits. The scoped Riflescope configuration explicitly copies the existing Single Ray Trace/IncidentAngle degree limits; this choice is recorded separately, not presented as a universal angular tolerance. FFT MTF modulation and percent distortion retain their original limits.

Afocal native field curvature is defocus in diopters. The current Core field-curvature output remains millimeters and is retained with that physical metadata; it is never relabeled as diopters. A mixed result can report a percent-distortion Difference while identifying the two incompatible field-curvature curves separately. Passing available components cannot close missing physical coverage. Current scope, real capture results and remaining product errors are in the [Riflescope report](../../docs/AFOCAL_COMPARISON_REPAIR_2026-10-19.md).

Explicit ray audits also limit the total number of surface records to the shared Core retained-sample budget (2,000,000), checked both before tracing and in the isolated native host. Dense arrays over many fields/surfaces require smaller separate diagnostic jobs.

2026-10-08: one-sided pure Y tables now interpolate squared radial distance in either sign, with native mixed/asymmetric tables retaining nearest-row selection and exact physical-unit midpoint ties. Three derived official Tessar controls certify 384 native launches; an independent finite-object Tessar adds 128. Native ray-audit preflight updates the system, records EPDI/ENPP and rejects zero/non-finite entrance pupils. The six unchanged official sources now replay as 100 Pass / 9 Close / 14 Difference / 8 Incomparable / 1 Error, with all 95 previous passes retained. Fresh MS-L7 Huygens and DEE captures still leave numerical differences; no existing baseline, setting or tolerance was changed. See [current fixes, evidence and limitations](../../docs/CONFIRMED_ISSUE_REPAIR_2026-10-08.md). The next paragraph is the earlier stage.

2026-10-07 RA256 and Single Ray Trace repair: the original six-source 132-case matrix has 95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error in both unchanged-snapshot and fresh-source-import replay; all 17 prior and 6 new independent five-source RMS controls pass, separately counted. Six full edge pupils contain 308808 rays and 2521932 surface results. Correctly imported automatic stops now match native acceptance and first clipping; signed first-surface report paths repair ten original Differences. Same-grid agreement does not certify convergence: native Tessar RA128 to RA256 still changes by about 0.164328 micrometer. GQ6 integration and finite Relay aiming residuals remain measured. See [the current repair and boundaries](../../docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md). The preceding shared Gaussian-pupil integral and squared-radius positive-Y vignetting interpolation remain in use; general field tables retain nearest-row selection.

`--ray-audit job.json` is a standalone explicit-ray diagnostic. Required job fields are `source`, its `sourceSha256`, and a fresh `output` directory. Optional fields include `configuration`, `wavelength`, `method` (`GQ` or `RA`), `rayDensity`, `removeVignettingFactors`, normalized `fields` (`[[hx,hy],...]`, at most 16), `zemaxVersion`, `zosApiPath`, and `timeoutSeconds`. GQ orders 1..20 and RA 32/64/128/256 use formal Core samplers. Unknown members and unmatched source hashes fail; the output is exclusively locked. Native coordinates/error/vignette flags, model/settings/version, input weights and source/snapshot/code hashes are retained. These batches do not produce a comparison conclusion or establish a native RMS angular rule. Existing canonical analysis requests and fingerprints are unchanged.

The historical 2026-09-06 MS-L7 rerun had 44 Pass, 6 Close and 2 Difference within 52 numerical contracts; API and image-contract limitations remain explicit. Geometric energy, line/edge bin integration and extended-source output sampling are repaired. Contrast Loss exports unshifted pupil phase separately from the preserved mean-ray GUI indicator and is explicitly partially comparable. See the [historical Huygens postprocessing evidence](../../docs/ZEMAX_HUYGENS_REPAIR_2026-09-06.md).

This independent validation tool audits every public Core canonical analysis key. It uses the existing Application request factory and the real Core calculations. It does not run in the desktop product. No product project references this tool or ZOS-API.

The historical 2026-09-06 coverage was 52 explicit numerical mappings; registration is not a numerical pass. Four further spot layouts were captured and audited for numeric/text channels. Eight analyses received native capability inspection: three had a documented physical-definition mismatch and five image/source adapters needed a common source and detector contract. Six keys had no verified native equivalent (Classified Data was also unavailable for the selected sequential model), and two non-sequential entries were not applicable to a sequential snapshot. Native IDs without a Workbench counterpart are enumerated separately as `ZemaxOnly`; current entries and limitations come from `--list-analyses`.

2026-10-13: the retired `Through Focus MTF` machine key resolves to `Fourier Through Focus MTF` for old capture replay and explicit CLI selection. The canonical registry and all-analysis execution contain only the Fourier entry, and selecting both names runs it once. The historical default tolerance file is part of the frozen fingerprint ledger and keeps its original alias row; that row does not create another executable analysis. Localized display names remain invalid tool keys. Current verification and the restored configuration identity are recorded in [the project status](../../docs/CURRENT_STATUS.md).

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

The historical 42 additional contracts covered Seidel tables/diagram, single rays, field curvature/distortion and chromatic scans, cardinal data, Y-Ybar, three angle/height analyses, prescription/system reports, four RMS scans and RMS field map, eight additional MTF scans/methods, three PSF/line profiles, four energy analyses, relative illumination, Fringe Zernike, footprint extents, contrast loss and Y-input Jones pupil magnitudes. The registry records partial scopes separately: a scalar footprint extent is not a point-cloud comparison; native batch-ray angle scans are not the built-in three-curve IHT plot; image-plane Ex/Ey magnitudes are not a full complex Jones matrix.

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
dotnet restore tools/OpticalSystemDesign.ZemaxComparison --locked-mode
dotnet run -c Release --project tools/OpticalSystemDesign.ZemaxComparison -- --input "C:\Users\19851\Desktop\[MS-L7](10X大NA大视场).ZMX" --all --capture-screenshots --keep-raw

# A different lens requires no source changes.
scripts/compare-zemax.ps1 -InputFile "D:\Optics\AnotherLens.ZMX" -OutputDirectory "D:\Optics\comparison-results" -Configuration "tools\OpticalSystemDesign.ZemaxComparison\comparison-settings.json"

# Stable keys, repeatable selection, all optical configurations.
dotnet run --project tools/OpticalSystemDesign.ZemaxComparison -- --input "D:\Optics\AnotherLens.ZMX" --analysis MTF --analysis "Huygens PSF" --configuration all

dotnet run --project tools/OpticalSystemDesign.ZemaxComparison -- --list-analyses
```

The PowerShell wrapper performs environment checks, locked restore, build and argument forwarding only. `-Configuration` denotes the JSON file; `-OpticalConfiguration` maps to the CLI's one-based optical configuration number or `all`.

CLI options: `--input`, `--output`, `--config`, repeated `--analysis`, `--all`, `--configuration`, `--zemax-version`, `--zos-api-path`, `--overwrite`, `--fail-on none|error|difference`, `--timeout` (per worker, seconds), `--list-analyses`, `--capture-screenshots`, `--keep-raw`, `--report-language zh-CN|en-US`. `--all` and `--analysis` are mutually exclusive. The default timeout is 120 seconds. Input is required except for listing.

Default output names include lens name, expected version, UTC timestamp and source SHA-256. A nonempty output is refused by default. `--overwrite` is accepted only for an existing tool manifest with identical source/configuration hashes. After acquiring an exclusive run lock, the previous generated evidence is moved together into a `previous-run-<UTC>` subdirectory; unrelated user files are retained. This prevents stale plots or arrays from appearing in a subset rerun. A different tolerance file requires a new output directory. Output cannot contain the input file. The original file is never saved by either executor; hashing and modification time are checked at completion.

### Historical Workbench recapture

The former `OpticalSystemDesign.AccuracyCapture` project was removed on 2026-10-13. Its historical settings-manifest replay is now an offline subcommand of this tool and is covered by the root solution's build and tests:

```powershell
dotnet run -c Release --project tools/OpticalSystemDesign.ZemaxComparison -- --recapture-workbench `
  artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  artifacts/workbench-recapture/new-run
```

The three positional arguments are source ZMX, saved settings manifest and output directory; optional fourth/fifth arguments are inclusive, positive one-based start/end indices in the **current** Core catalog. For a partial fresh run, unselected analyses are `not-requested`; no additional calculations are silently run. Read current indices from `--list-analyses` order or the new `current-manifest.json`, rather than an old fixed index table. A retired `Through Focus MTF` setting resolves to the Fourier key, with an explicit canonical setting taking precedence if both exist.

Outputs keep `current-manifest.json`, `current/NNN-name.json`, AnalysisView fields, camel-case enum values and the existing captured/unavailable/not-applicable/failed distinction used by the historical Python report. New fields record settings/output hashes and cancellation. This mode imports the source and calls `WorkbenchRuntime.BuildAnalysisView`, which already uses the shared `BuildAnalysisData` factory; it needs no Zemax installation or license.

Use a fresh output directory. Source/settings files and recognized native baseline directories are protected. An existing recapture can be updated only with matching source and computation fingerprints; unchanged outside-range results additionally need matching settings and an output checksum. Old manifests without output checksums require fresh recalculation. Modified reusable files are retained and reported as failures, and concurrent output writers are rejected. Partial/reused runs do not establish full-run performance; the report counts them separately and excludes reused elapsed times from the current run total.

Cancellation stops new computation and publishes a complete current catalog. Unvisited previous results retain their identity only after settings, output bytes and checksum are verified; retained rows are marked reused without rewriting result files. Changed settings produce `not-requested`, and tampered saved data produce `failed`. Fresh unvisited entries are `not-requested`. Cancellation before output creation still creates nothing. Older already-truncated manifests without the missing rows' provenance cannot certify those orphaned files; recalculate them explicitly.

## Settings and tolerances

Edit `comparison-settings.json`. Its version and exact byte SHA-256 are recorded. `analyses.<canonical key>` provides explicit field, wavelength, pupil/image sampling, hexapolar ray density or fan count, image delta in micrometers, maximum frequency and **per-quantity** tolerances. Unknown configuration members, invalid ranges, missing tolerances and unmatched Workbench-only overrides for comparable adapters are rejected.

Each contract specifies its field/wavelength scope: selected, primary, all defined or a continuous scan. It does not exhaust all possible settings. Configure/MapWorkbench expand that scope before execution; unused generic request fields do not override a contract's explicit selectors. First Order follows the native primary wavelength. Configuration `all` repeats the selected analyses from separate imported immutable snapshots. Canonical requests, expanded Workbench parameters, native CFG and final selector/property readbacks are saved. These are **CapturedSettings**, not universal Zemax defaults. Extended-source energy uses the owned `assets/uniform-square.IMA`, hashed and copied into run input before either worker starts. Models retain their own apodization, aiming, materials, fields and spectra. Numerical adapters require focal image space and MM lens units; unsupported field mappings remain incomparable.

RMS contracts use radial Gaussian order 6 and explicitly select 12 azimuthal samples in Workbench for the MS-L7 convergence experiment. The native angular rule is not exposed by the typed settings interface; 12 is not asserted to be a universal Zemax rule. Core's general default remains 6. Native RMS selector enum names disagree with the installed 26.1 report's physical reference and density; the report is checked in addition to enum readback.

For `RMS vs Field` spot controls, the typed configuration also accepts `rmsFieldMethod` (`GQ` or `RA`), `rmsFieldRayDensity` (GQ 1..20 or RA 32/64/128/256), and `rmsFieldRemoveVignettingFactors` (boolean). Defaults remain GQ/6/true and preserve existing canonical request fingerprints. These controls are rejected on other analyses. Both executors receive the same method, density and vignetting choice; Workbench uses `2 * density` angular samples for the selected GQ experiment, with native numerical evidence at orders 6 and 12. Other orders are configurable but not certified by these captures. RA spot scans use equal-weight square-cell centers restricted to the circular pupil through the shared Core sampler; generic endpoint grids and RA wavefront sampling retain their existing conventions. The five-file, 17-case native study and remaining Tessar differences are documented in [the RMS sampling report](../../docs/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md). No original GQ reference or tolerance is replaced by an RA control.

The later [RA256 and Single Ray Trace study](../../docs/ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md) adds six independent field curves and six complete edge-field pupil audits across five sources. It separates unchanged snapshot replay from a fresh import of identical ZMX bytes: automatic stop DIAM is an estimated footprint and no longer becomes a hard circular clipping aperture. User-defined stop DIAM and powered-surface DIAM retain clipping. Native captures, original comparison settings and tolerances stay frozen. Single Ray Trace reports use the native signed first-surface vertex-plane geometric segment for infinite objects; physical histories and weighted optical paths remain unchanged.

Scalar pass: absolute error <= absolute tolerance + relative tolerance × absolute reference. Curve/grid pass: all points satisfy that bound, or peak-reference NRMSE satisfies the per-quantity threshold; `Close` has its own NRMSE threshold. Minimum physical coverage is independently enforced. The scale floor is the absolute tolerance, avoiding division by an almost-zero pupil-aberration reference. Raw pointwise errors, worst point and percentiles remain in the report even when aggregate NRMSE passes. These thresholds are editable validation policy, not software specifications or a claim of physical accuracy.

## Data and normalization

The tool preserves raw and normalized data separately, with scalar, 1D series, 2D grid/invalid mask, structured text, image and complex-field schemas. Schema availability does not establish full image or complex-field equivalence. The required raw arrays/text/CFG are always retained for audit; `--keep-raw` explicitly requests that retention. There is no destructive cleanup mode.

Curves convert only compatible typed quantities/units, sort physical coordinates and interpolate on the union of knots inside the common interval. There is no extrapolation or axis stretching. Grid comparison currently requires equal physical grids after unit conversion; an origin/pitch mismatch is `Incomparable`, with both normalized grids preserved. Explicit orientation utilities move axes, values and masks together; no value-based orientation search is performed.

A zero-extent field request has an explicit point domain: repeated software samples must agree at exactly zero, and only the native sample at exactly zero belongs to that request. Both sides must declare the same point domain; this cannot certify a nonzero interval. Checked ray/OPD fans retain their full physical lattice and invalid-sample masks. Masked comparison requires identical coordinates and validity on every knot, with at least two shared finite pairs; it never interpolates across invalid samples. A mask mismatch remains incomparable even if an independent batch-ray trace finds a native plot omission. See the [Objectives repair and scoped evidence](../../docs/OBJECTIVES_PRECISION_REPAIR_2026-10-23.md).

Wavefront normalizes the known even-grid pupil convention shared with the existing golden tests. Workbench subtracts its own minimum for display; the normalizer recovers signed physical OPD from its unrounded optical-path mean and wavelength, independent of any Zemax value. This is a documented display conversion, not a fitted piston. No phase wrapping, tilt removal, reference-sphere replacement, intensity scaling, peak normalization or chief-to-centroid fitting is silently applied. Unsupported conventions stay incomparable.

Curve metrics include overlap, absolute/relative errors, RMSE/NRMSE, percentiles, Pearson and worst coordinates, with ordinary CSV and overlay/difference PNG files on Windows (metric-ID colons must not create alternate data streams). Wavefront grids add RMS/PV; irradiance grids add common-mask energy, peak, centroids, marginal FWHM and encircled-energy radii. These refer to the captured finite window, not infinite-image energy. Frequency-domain MTF curves add DC and 10/20/30/50 cycles/mm statistics; field/focus axes do not receive frequency statistics. Strehl is omitted without a verified common definition. Zernike compares 37 Fringe coefficients in waves with explicit pupil sampling; Standard full-field decomposition is a separate contract.

## Outputs and exit codes

`analysis-registry.json` records the complete canonical mapping, typed result contracts and default per-quantity tolerances. Every executed entry also records the actual configured tolerances next to its request and metrics. The registry is an audit inventory; a listed native ID does not imply that an adapter has been implemented or that the model is supported.

`manifest.json` contains source integrity, source header version, immutable model summaries/preflight issues, source revision and assembly hashes, .NET/OS, actual Zemax/license information and configuration hash. `run-summary.json` and `analysis-matrix.csv` distinguish enumeration, capture, actual comparable results and all conclusion/support counts. `COMPARISON_REPORT.md` includes the full matrix, settings, transforms, plots and worst differences. Every entry has `comparisons/<key>-cN/comparison.json`; successful comparisons also have values CSV and plots. Logs and failed analysis details are retained incrementally. One failure never discards completed results.

JSON files stream directly to an owned temporary file, retaining the existing UTF-8 serialization options and bytes. Only a completed serialization replaces the destination; a failed serialization retains its previous bytes and removes the owned temporary file. This avoids the full UTF-16 string allocation that failed on the 1.37 GB Tessar RA256 diagnostic. It does not remove the ray-trace retained-sample budget or promise unlimited memory use.

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

The selected Objectives corpus run opts into `NativeLengthUnitContract`: original centimeter/inch/meter source bytes are preserved, and OpticStudio's own `Tools.OpenScale` converts the loaded copy physically to millimeters before each analysis. The native source-unit request, conversion method and actual model-unit readback are mandatory; missing or mismatched records cannot certify a result. The default non-MM gate stays closed unless this explicit contract is requested. Raw ray audits keep their original native lens units and convert coordinates with the original fixed unit factors in the reporting layer. Native signed-index reflection directions, report normals and segment lengths are converted using explicit `MIRROR` model rows; original arrays/reports remain intact. Null native series/grid buffers are `NativeInvalidResult`, an unavailable reference, never a precision pass.

The bounded [Objectives workflow](../diagnostics/Objectives20261020/README.md) retains per-lens differences and open controls separately from repaired precision regressions. Its current native FFT MTF contract uses the even pupil lattice, endpoint frequency interval and natural cubic complex OTF display interpolation. That is a verified finite sampling convention for the captured requests, not universal convergence or an afocal certification claim.

- Missing source: provide the actual path. The tool never substitutes a similarly named historical file.
- API not found: supply `--zos-api-path` or `ZOS_API_PATH`.
- License/connection failure: inspect `raw/zemax/probe/environment.json`, `error.json` and `logs/zemax.log`; check whether another instance consumes the available API license.
- Unresolved material/opaque geometry: inspect Workbench preflight and native model warnings. Resolve the model deliberately, then create a new run; never silently substitute a glass or plane.
- Timeout: increase `--timeout` for that run; retain the old report. Ctrl+C terminates owned workers and writes remaining statuses.
- Pixel-grid mismatch/reference mismatch: inspect both raw results and captured requests. Do not fit a shift, scale or flip to reduce the score.

To implement another mapping, edit the unique `AnalysisComparisonRegistry`, add a typed `Adapter` in `ZemaxHost/Host.cs`, and add its explicit canonical settings and result normalization contract. Reuse `WorkbenchRuntime.BuildAnalysisData`; do not duplicate its analysis factory. Add quantity tolerances, fixed-raw offline tests, and a licensed-machine test. Only promote `AdapterNotImplemented` after settings, physical axes and result definitions are verified. Historic `123456.ZMX` scripts contain file-specific assumptions and must not be promoted into defaults for arbitrary lenses.

Run portable tests with `dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests`. See [audit and integration evidence](../../docs/ZEMAX_COMPARISON_TOOL.md) and the [2026-09-06 numerical repairs and fresh comparisons](../../docs/NUMERICAL_REPAIR_2026-09-06.md). The original reports remain historical evidence; the product fixes preserve the comparison tolerances and do not fit results in the reporting layer.
