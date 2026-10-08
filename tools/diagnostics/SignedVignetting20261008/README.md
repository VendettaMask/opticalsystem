# Native factor inspection

`inspect-native.ps1` reads the negative, mixed and asymmetric Tessar capture artifacts from the repository root and writes `observed-factors.json` alongside them. It reconstructs observed affine pupil coefficients for inspection from paired native rays and their formal Core inputs. These calculations never supply product tracing, acceptance or analysis values. The acceptance tests compare original native directed launches directly; the inspection's reconstructed coefficients are not a reference fixture.

Full provenance, derived prescription changes, capture settings and limitations are in the [signed-vignetting fixture](../../../validation/zemax/2026-r1/signed-vignetting-2026-10-08/README.md) and [repair report](../../../docs/CONFIRMED_ISSUE_REPAIR_2026-10-08.md).
