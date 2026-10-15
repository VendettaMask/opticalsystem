# Standard-lens accuracy matrix

Offline validation only. The executable references the formal comparison tool, Application request factory and shared Core; it implements no optical formulas and is not part of the product solution or runtime. Python only audits saved evidence. The source inventory is the original 2026-10-06 plan: 27 installed official files, with independent Cooke/Tessar excluded by the user's later decision, plus the separately retained `123456.ZMX` baseline.

Use a fresh output directory. Preparation checks every original source hash, preserves original bytes, imports each configuration separately, and freezes the existing comparison policy before native capture. Do not edit the historical plan or overwrite captures to update their status.

```powershell
dotnet restore tools/diagnostics/StandardMatrix20261015 --locked-mode
dotnet build tools/diagnostics/StandardMatrix20261015 -c Release --no-restore
$run='artifacts/zemax-standard-samples/another-fresh-run'
$tool='tools/diagnostics/StandardMatrix20261015/bin/Release/net10.0/StandardMatrix20261015.dll'
dotnet $tool prepare . $run
tools/diagnostics/StandardMatrix20261015/Run-Native.ps1 -RunDirectory $run
dotnet $tool workbench . $run
dotnet $tool compare . $run
python tools/diagnostics/StandardMatrix20261015/AuditResults.py $run
dotnet $tool isolated-controls . $run
```

Native capture requires the locally licensed Windows x64 OpticStudio 2026 R1 installation and .NET Framework compiler. No API binaries are copied. The script retains the exact helper sources and their hashes; its two mechanical changes reuse the native application and leave application closing to the batch wrapper. Each request reloads the immutable lens copy, selects its configuration, and closes its analysis/tool. Existing user applications are untouched. `isolated-controls` uses the unmodified original host in four fresh native processes, in reverse file/configuration order, and requires identical normalized Spot results.

This is a local diagnostic, not a background service: monitor `native.log` during execution. The shared native process has no per-request watchdog or automatic resume. A hung API request requires stopping only this task's verified host and selecting a new run directory; do not combine unfinished outputs. Workbench calculations run in two isolated processes with a 900-second per-request timeout. The earlier 180-second attempt timed out on the 210-degree illumination calculation under concurrent verification load; that attempt is retained. This process deadline does not change optical settings, sampling, optimization budgets or numerical tolerances. `compare` reports missing/failed captures explicitly.

The first matrix uses the prior 18 primary-field analysis keys and four edge-field controls at every positive-weight wavelength, for every imported configuration. Settings are the explicitly saved comparison policy (64 pupil/image samples where applicable); continuous/all-field contracts retain their own formal selectors. These controls do not exhaust every field/wavelength combination or certify sampling convergence. No implicit source-field edits, optical gain fit, baseline changes or tolerance relaxation are allowed. Unit/afocal analysis-adapter limits remain Incomparable; their sources, native models and applicable explicit rays remain in the inventory.

GQ6 explicit rays retain the source vignetting and cover defined radial-normalized fields and positive-weight wavelengths. The saved pre-capture policy uses the existing Single Ray Trace pointwise coordinate/direction tolerances and separate exact acceptance/first-clip checks. Native length units are converted to millimeters only in this explicit-ray audit. Unsupported surfaces cannot be certified by diagnostic traces. Coordinate-break row folding requires a separate surface-index correspondence; reflection direction conventions need review before raw local-direction residuals are interpreted physically. The audit does not infer a native RMS integration rule or optical-path equivalence from these ray batches.

Results preserve Pass, Close, Difference, Incomparable and Error separately. One file being imported, a finite output, a unit conversion or a successful process exit never establishes full numerical equivalence. See [the dated result](../../../docs/ZEMAX_STANDARD_MATRIX_2026-10-15.md) and [current status](../../../docs/CURRENT_STATUS.md).

The [Zoom/wide-angle repair](../../../docs/ZEMAX_ZOOM_WIDE_THROUGHPUT_REPAIR_2026-10-15.md) separates fresh native acquisition from final Core recalculation. Its final rerun reuses this task's completed native captures only after checking every numerical request value and source SHA-256; the changed copied input path, original executed request, copied raw-file hashes and reference-reuse ledger remain explicit. Native configuration selectors for Spot/Huygens reserve entries before numbered configurations and are distinct from the one-based MCE configuration index. Original single-configuration frozen captures are not rewritten.
