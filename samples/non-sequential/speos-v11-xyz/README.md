# Speos v11 / XYZ controls

These are self-authored synthetic format and physics controls, not measured materials.

- `synthetic-v11.anisotropicbsdf`: archive-3 LZ4, reflection, embedded cone and White Specular at 550 nm. Assign an opaque reflective object and explicitly choose the incident side.
- `synthetic-white.anisotropicbsdf`: v11 text, R/T; choose refractive interaction with AIR on both sides, explicitly declare identical front/back, enable the native explicit cone at 14 degrees, relative height 0.01, White Specular base 550 nm.
- `synthetic-xyz-r.BSDF` / `synthetic-xyz-t.BSDF`: three XYZ channels. Select `synthetic-xyz-calibration.json` in the object editor; do not enable the Monochrome radiometric checkbox as a substitute. The calibration is synthetic and is not valid for another material or measurement condition.

Wavelength support is 400–700 nm, with no extrapolation. Keep all scatter power on the same NSC -> detector watts/pixel -> RAW/ISP path. See [implementation and limits](../../../docs/SPEOS_V11_XYZ_2026-10-31.md).
