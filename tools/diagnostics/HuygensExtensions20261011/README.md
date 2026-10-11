# Huygens spherical, spectral and experimental polarization controls

These tools capture only MS-L7 on the locally licensed OpticStudio 26.1.0 API. Independent Cooke and Tessar follow-up is excluded. Native helpers open isolated applications and never save source files. Use fresh output directories; failed and superseded runs remain diagnostic evidence.

```powershell
pwsh -NoProfile -File tools/diagnostics/HuygensExtensions20261011/CaptureControls.ps1 -OutputDirectory D:\fresh\native
pwsh -NoProfile -File tools/diagnostics/HuygensExtensions20261011/CaptureControls.ps1 -OutputDirectory D:\fresh\extra -ExtraScalarControls
dotnet build tools/diagnostics/HuygensExtensions20261011/HuygensExtensions20261011.csproj -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tools/diagnostics/HuygensExtensions20261011/bin/Release/net10.0/HuygensExtensions20261011.dll D:\Projects\opticalsystem D:\combined\native D:\fresh\comparison
```

The native host defaults to the preceding local `huygens-offaxis-20261011/host-pupil` build; override it with `-NativeHostDirectory`. Build the repository host helpers and `../N02Layers20261009/NativeEnergyControl.cs` against the licensed ZOS-API assemblies when that build is absent. ZOS-API IPC requires normal local permissions on this host; access-denied captures are excluded, never counted as passes.

Combine the captured case directories without editing their request paths or raw values. The comparison imports the original source and applies only explicit saved Gaussian overrides. It calls WorkbenchRuntime and round-trips the product AnalysisData through JSON. Auto acceptance remains distinct from forced native method diagnostics. Offline reference-sphere/exit-plane/chief-plane propagation and weight hypotheses never feed product runtime, acceptance or display calculations.

The Jones hypothesis uses the existing approximate Jones pupil and scalar power. It is not a completed vector or absorbing-interface implementation: the formal full polarized ray transport explicitly rejects absorbing interfaces. The current PSF polarization switch is already labeled Experimental scalar weighting. Both native polarization residuals remain nonpassing evidence.

Wide-window native complex grids contain nonfinite cells and are excluded as whole-grid certificates; linear intensity grids remain independently complete. Missing complex captures are not zero-filled. See [the saved capture scope](../../../validation/zemax/2026-r1/huygens-extensions-2026-10-11/README.md) and [the repair report](../../../docs/HUYGENS_SPHERICAL_REPAIR_2026-10-11.md).
