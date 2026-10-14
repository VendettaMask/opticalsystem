# Native curvature and illumination matrix

Captured on the local licensed OpticStudio 2026 R1 installation, build 260127, using `NativeAccuracyCapture` and unchanged committed 123456, Double Gauss 28 degree field, Relay Lens, and Even Asphere sources. See `manifest.json` for source references and SHA-256 identities. The older frozen references and original comparison targets are preserved.

Each case retains environment/license metadata, model settings, operand schemas, 101 +Y normalized field positions per active wavelength for FCGS/FCGT, and Samp 5/10/20/40 × all active wavelengths × all defined fields, Pol=0, for EFNO/RELI. The first matrices contain 2020 native curvature values and 256 native illumination operand values. A separate signed-axis capture adds 140 curvature values at axis, negative, and oblique coordinates, bringing the curvature scope to 2160 values. These scopes do not certify other fields, wavelengths, polarization states, aperture masks, or image poses.

The 123456 directory additionally contains a recapture using the original Relative Illumination CFG. Its full-precision values reproduce the existing 21-field baseline; this verifies repeatability of that particular configuration rather than inferring universal defaults. Ray controls are a distinct channel; their `wave` and normalized inputs are recorded explicitly.

Curvature regression checks retain the original `1e-5 mm` target. EFNO/RELI are retained as unresolved differences, not passing gates. The comparison command and capture instructions are in [the diagnostic guide](../../../../tools/diagnostics/Accuracy20261014/README.md), and the current conclusion is in [CURRENT_STATUS](../../../../docs/CURRENT_STATUS.md).

No Cooke/Tessar independent case, Optiland regeneration, API binary, or screenshot-only precision result is included.
