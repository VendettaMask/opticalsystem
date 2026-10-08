# Calculation path diagnosis 2026年10月8日

The current tool also verifies post-repair behavior. Prepared aimed and unaimed controls now pass explicit matching aiming, and a separate mismatch probe must be rejected. Unsupported Foucault polarization must raise an error. Pre-repair JSON below is immutable historical evidence; current records are under `artifacts/validation/calculation-path-repair-20261008/matrix-release-final` and `translation-release-final`. See [修复与结果对比](../../../docs/CALCULATION_PATH_REPAIR_2026-10-08.md).

This diagnostic calls only the formal Core and Application engines. It does not change product calculations, frozen references, tolerances or formal tests. A completed run means that the controls and integrity checks executed, not that all optical paths passed acceptance.

From the repository root, use a fresh output directory:

```powershell
dotnet run -c Release --project tools/diagnostics/CalculationPaths20261008 -- . artifacts/validation/calculation-paths-20261008/new-run
```

The matrix checks three immutable snapshots, two captured field/wavelength settings and both system-aiming states. It compares generic hex Zernike fits, uniform Fringe and ZERN controls, the Zernike field sweep, focal reference spheres, synthetic afocal branches, zero-defocus wavefront, FFT, Jones pupil, fast MTF, FFT display dispatch and compatibility-only settings. Internal controls reuse exact pupil nodes; native OPD fan comparisons retain validity/missing counts and are authoritative only at the captured aiming state. Synthetic afocal controls are not native afocal lens captures.

To inspect rigid global axial translation without changing the optical prescription:

```powershell
dotnet run -c Release --project tools/diagnostics/CalculationPaths20261008 -- . artifacts/validation/calculation-paths-20261008/new-translation --translation-only
```

Translation controls shift every surface frame by the same Z displacement, then check formal wavefront OPD, pupil position, intensity, image direction, image frame, Huygens PSF and FFT PSF. They do not implement an alternative diffraction formula. Original files are hashed before and after.

The pre-repair final matrix records are `release-03/summary.json` and `debug-01/summary.json`. Their optical results are identical. The pre-repair final translation controls are `translation-02/translation-summary.json` and `translation-debug-01/translation-summary.json`; earlier run folders are retained as intermediate evidence. Product-source fingerprints cover ten explicitly listed files, not the entire repository. The historical `native-regressions/frozen-diffraction-related.trx` preserves 45 existing comparison tests, including the two failures. Post-repair complete formal and tool test results are in `calculation-path-repair-20261008/full-release-01/full-formal.trx` and `full-tool-release-02/full-comparison.trx`; the report describes their scope separately.

Findings and repair boundaries are in [计算路径问题定位报告](../../../docs/CALCULATION_PATH_DIAGNOSIS_2026-10-08.md). No new native capture, complete solution build, complete test run or release certification is performed by this tool.
