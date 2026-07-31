# Zemax parity probe

`zosapi_export.py` uses the official Python ZOS-API Standalone connection to
load a sequential ZMX file and export an FFT MTF result. `zosapi_probe.m`
provides an equivalent MATLAB connection probe.

`zosapi_capture_baseline.py` captures the complete `AnalysisIDM` catalog for
one lens. Every analysis receives a status record. Applicable analyses retain
their native settings file, raw text, structured ZOS-API JSON, and either an
actual OpticStudio window screenshot exported by the companion
`capture_analysis_window.zpl` macro or a clearly identified plot rendered from
the ZOS-API data when no ZPL window code exists. Analyses that need
non-sequential data, external files, STAR data, or another unavailable module
remain in the manifest as not applicable; no substitute numbers are generated.

Before running it, close every visible and background OpticStudio instance.
The script deliberately refuses to launch when an existing `OpticStudio.exe`
process is present, because a stale or second instance can consume the
available license and make `IsValidLicenseForAPI` return false.

Run from MATLAB:

```matlab
addpath("D:\Projects\opticalsystem\tools\zemax_parity");
result = zosapi_probe("C:\Users\19851\Desktop\123456.ZMX");
```

Run the Python exporter with the Python distribution bundled with Ansys:

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_export.py"
```

Capture the current `123456.ZMX` baseline:

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_capture_baseline.py" `
  --zmx "C:\Users\19851\Desktop\123456.ZMX" `
  --output "D:\Projects\opticalsystem\artifacts\zemax\123456-zemax-2026-r1-baseline"
```

The collector refuses to start while another `OpticStudio.exe` process is
present. This protects the single API license and prevents the screenshot pass
from attaching to or closing an unrelated interactive session. Use
`--allow-existing` only when an intentional interactive session must remain
open and the installed license supports another OpticStudio instance; each
capture subprocess remains isolated and closes only itself.

If a serializer or external prerequisite is fixed after a run, use
`--retry-failed --data-only` to recalculate only failed manifest entries, then
use `--screenshots-only` to fill only the still-missing screenshots. Existing
native screenshots are not overwritten.

Verify the manifest, source hash, JSON outputs, settings/text references, and
every screenshot:

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\verify_baseline.py" `
  "D:\Projects\opticalsystem\artifacts\zemax\123456-zemax-2026-r1-baseline"
```

To export the Zemax FFT MTF frequency, tangential, and sagittal arrays as
JSON:

```matlab
result = zosapi_probe( ...
    "C:\Users\19851\Desktop\123456.ZMX", ...
    "D:\Projects\opticalsystem\artifacts\zemax\123456-fft-mtf.json");
```
## Current Workbench comparison

To recalculate every current Workbench analysis with the saved comparison
settings, then regenerate all numeric and screenshot comparisons:

```powershell
dotnet run --project tools/OptilandWorkbench.AccuracyCapture -- `
  artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-30/current-manifest.json `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31

python tools/zemax_parity/generate_workbench_comparison.py `
  artifacts/zemax/123456-zemax-2026-r1-baseline `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31 `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-30
```

The capture directory retains one raw JSON result for every Workbench analysis.
The comparison directory contains 32 machine-readable numeric comparisons,
32 numeric plots, and 69 Workbench/Zemax page images. A previous comparison is
used only for stable physical-series mappings; its Workbench values are never
reused.