# Diffraction energy window controls

Run from the repository root with a fresh output directory:

```powershell
dotnet build tools/diagnostics/IssueRepair20261008/IssueRepair20261008.csproj -c Release
dotnet tools/diagnostics/IssueRepair20261008/bin/Release/net10.0/IssueRepair20261008.dll D:/Projects/opticalsystem D:/Projects/opticalsystem/artifacts/validation/issue-repair-20261008/new-energy-output --energy
```

The tool reads the immutable MS-L7 Zemax capture and its canonical settings. It uses the formal Application/Core analysis, PSF, centroid and pixel-area energy implementations, then the comparison tool's ordinary physical alignment and NRMSE. It does not implement another optical engine, fit a normalization or change tolerances. Density 32 reproduces the frozen captured request; densities 64 and 128 are Workbench controls against that same native 32-sample curve, and do not certify native convergence.

The alternative integrates the full FFT image window instead of the existing central window. Both ideal and actual PSFs are reported separately. See `artifacts/validation/issue-repair-20261008/energy-window-canonical/summary.json` and the [repair report](../../../docs/CONFIRMED_ISSUE_REPAIR_2026-10-08.md). The earlier `energy-window` artifact contains raw pointwise RMSE and is superseded by the canonical-metric report. Failed constant-index finite-system export experiments were not certified and are excluded from accepted native counts; the finite-object certification uses a derived official Tessar control instead.

The first restore/build encountered NU1900 because the online vulnerability endpoint failed. A later locked, forced reevaluation succeeded after network recovery, and the final build has zero warnings/errors. Auditing remained enabled and dependency versions were unchanged; earlier failed attempts remain process evidence.
