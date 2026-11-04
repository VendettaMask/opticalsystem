# Off-axis OPD and sampling audit

Build this scoped .NET 10 diagnostic with the SourceRevisionId in the evidence record and `CopyLocalLockFileAssemblies=false`. Run `OpticalSystemDesign.Tests.dll REPOSITORY FRESH_OUTPUT MODE [OPTIONS]` through `dotnet exec --additionalprobingpath` using the existing package cache.

- `review`: six original OPD requests, separately declared perfect-collimation controls, 60 original image-pair requests and 156 archived cross-system Pass controls.
- `native [mode0|mode3] [example5] [plots] [cache-off]`: bounded independent native phase/ray controls. Mode changes affect owned source copies only. Cache claims require actual readback.
- `sampling-native [8x] [INDICES...]`: 2/4倍，或显式8倍的独立原生采样捕获；保留原像面设置、源文件和请求身份。
- `sampling-audit CAPTURE_ROOT [EXTRA_8X_ROOTS...]`: execute all 19 guarded original requests, compare diagnostic finite sums and hash-verified historical self-refinement, validate actual capture settings, then compare all original physical nodes. Recompute 831/835 high-sampling controls on the current Core.

Native captures hold the shared exclusive lease, check the process slot, use a new background instance with screenshots disabled and a 120-second limit. Never attach a user application. Audit modes do not disable or change production sampling protection.

`Write-Report.ps1` checks exact controls, verdicts, tolerances, 691 tests, 44 native readbacks and the shared Core. `Seal-Evidence.ps1`/`Seal-Reuse.ps1` bind final sources, read back all new/reused payloads, then retire only this task's checked expanded paths. The final runtime stays available; packages remain in the shared cache.

See [the scoped report](../../../docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md). No new full-matrix or remote-delivery claim is implied.
