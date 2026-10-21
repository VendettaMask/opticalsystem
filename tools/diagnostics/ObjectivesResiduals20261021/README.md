# Objectives residual controls, 2026-10-21

These offline controls investigate the three remaining 32-grid diffraction-energy differences and the Double Gauss 5° FFT PSF profile. Product computation stays in the formal C# Core. No captured phase, amplitude, energy curve or lens identifier supplies product results.

`NativeConeControl.cs` uses a separate licensed OpticStudio 2026 R1 application. It reloads each immutable source copy and never saves a lens or merit function. Requests may capture marginal/diagonal rays, or explicitly selected analyses with an optional diagnostic aperture multiplier. Non-millimeter models use the same native `OpenScale.ScaleByUnits` conversion as the formal comparison host. Raw settings, unit/model readbacks, curves and completion are retained. `WFNO` is a system-primary-wave quantity: its inactive operand columns do not select individual wavelengths, even in the older raw captures where the field is repeated in each wave record. Per-wave ray directions are separate measured channels.

Use the prepared JSON requests in the evidence archive with a fresh output directory:

```powershell
./tools/diagnostics/ObjectivesResiduals20261021/Build-And-Capture.ps1 -RequestPath <request.json> -OutputDirectory <fresh-run>
```

The script compiles against the licensed machine's installed API assemblies, fingerprints source inputs and compiler sources, and checks that lens bytes remain unchanged. It does not distribute proprietary API binaries or interact with the user's existing application.

The existing [Objectives diagnostic project](../Objectives20261020/README.md) has explicitly scoped replay modes: `precision-controls`, `psf-plane-controls`, `energy-grid-controls`, `energy-convergence-controls`, `native-complex-controls`, and `raw-snell-controls`, each followed by a fresh output directory. They use the saved 2026-10-20 matrix and this task's local evidence paths. The complex control calls the formal Core inverse FFT to inspect native pupil support. The raw-Snell control calls the existing private Snell operation while isolating its final normalization, on disposable optics only; it is a rejected diagnostic, not a product ray-tracing option.

The 57-request PSF/profile/energy subset preserves all original requests and tolerances. The 20 energy convergence controls include unchanged 32/64/128 settings and independent ±1e-12 aperture multipliers. Higher-sampling passes never replace failed original 32-grid requests. One additional Apochromat4 64-grid and one Cooke 128-grid control remain Close; these are explicit diagnostic coverage, not new independent Cooke certification. The three original 32-grid differences remain open.

See the [task report](../../../docs/OBJECTIVES_RESIDUALS_2026-10-21.md) and its machine ledger for source identities, exact scope, rejected candidates and verification results.

The user subsequently selected additional 128-grid acceptance for Apochromat2, Apochromat4 and the MIL doublet, while keeping all three original 32-grid differences unresolved. The separate [acceptance manifest](../../../validation/zemax/2026-r1/objectives-energy128-acceptance-2026-10-21/acceptance.json) selects those existing immutable captures and retains the original policy hash. Captured-file settings and product sampling defaults are unchanged.

`energy128-rays-prepare <fresh-output>` in the diagnostic project reconstructs only these three expanded pupil grids, requires bit-identical full FFT output from formal Core, and exports their actual 405 pupil nodes per lens. Its `native-request.json` can be passed to `Build-And-Capture.ps1 -OutputDirectory <output>/native`. The optional `rayAuditRequest` job delegates all native surface captures to the formal `NativeRayAuditCapture`; it does not change the cone or analysis modes. `energy128-rays-compare <output>` compares all physical surfaces at the original coordinate/direction component limits and requires identical error/vignette status. These axial primary-wave records supplement the energy curves; they do not certify the unresolved 32-grid requests or other fields/wavelengths.
