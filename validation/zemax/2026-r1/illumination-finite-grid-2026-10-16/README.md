# Independent finite illumination grid controls

These synthetic controls were captured on licensed OpticStudio 2026 R1 build 260127 Enterprise API on 2026-10-16. `controls.json` preserves the native values and source SHA-256, not a fitted output. GetOperandValue before and after saving/reloading a new diagnostic copy and actual MFE row values agreed exactly. Readback clamps requested Samp 101/128 to 100.

`slow-ideal-lens.ZMX` is the native-only 1000 mm PARAXIAL lens with infinite object, ENPD 2 mm, 1000 mm back focus, wave 1=0.55 µm, field 1=axis, Pol=0 and configuration 1. Each control adds a square stop aperture of the stated half-width. Core tests explicitly construct its existing ThinLensInteractionModel and physical rectangular aperture. This does not claim PARAXIAL import support or general native EFNO equivalence.

Source, settings and failed/successful native probes are preserved in the [evidence archive](../../../repairs/2026-10-16/illumination-finite-grid-evidence.zip). Product behavior, original four-file comparison and remaining fine residuals are described in the [repair report](../../../../docs/ILLUMINATION_FINITE_GRID_REPAIR_2026-10-16.md). The original four-file captures, acceptance targets and independent Cooke/Tessar exclusions remain unchanged.
