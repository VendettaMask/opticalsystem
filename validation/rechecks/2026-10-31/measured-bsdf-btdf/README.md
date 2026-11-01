# Measured BSDF / BTDF — 2026-10-31

Scope: native measured-grid CSV import, spectral/incidence/outgoing interpolation, joint BRDF/BTDF sampling, NSC medium transitions, energy accounting, detector power and the existing RAW/ISP workflow. Synthetic controls are explicitly labelled; this is not measured-material validation or commercial certification.

## Final verification

- Default Release App build and default test-driver build: zero warnings/errors (`build-app-final.log`, `build-tests-final.log`). The App graph builds Core and both built-in modules; the test driver then uses `BuildProjectReferences=false` to consume those outputs without rebuilding product references between the audit and test start.
- `final-focused.trx`: **132 passed / 0 failed / 0 skipped**, including 28 new measured-grid/BTDF cases. Existing coverage includes Lambertian, NSC database/filter/session lifecycle, spectral detector/sensor conversion, legacy loss aliases, native format boundaries and frozen V5–V9 inputs.
- `before-final-run.json` and `after-final-run.json` record actual source/binary identities immediately around the test command. `manifest.json` records final per-class counts, artifact hashes and numerical controls. Shared working outputs may be rebuilt by other work; later copies are not automatically classified as tested. Binary identity is provenance, not a numerical acceptance threshold.
- All validation was background/headless/off-screen. No native desktop interaction, whole-repository test suite, packaging, publishing or push.

`combined-before-ui.trx` preserves an earlier 132/132 pass. The final run repeats the same cases after making the Lambertian-only reflectance field unavailable for measured models, bounding CSV column splitting, adding asymmetric-back-side checks and deriving moment gates from analytical variance. Repeated runs are not added together.

## Independent physical and statistical checks

- `distribution.json`: 32,768 fixed-seed samples from a spectral, incidence-dependent grid with nonuniform outgoing values. Independent off-grid formulas and Gauss/azimuth integration check f, R/T and solid-angle PDF. R/T proportions, cosine CDF/moments and an azimuthal probability use six analytical standard deviations. Front and back tables are separately queried; missing ranges/sides are rejected.
- `narrow-lobe.json`: a nonuniform 1°-scale transmission lobe with peak **100 sr⁻¹**. The analytical projected integral is retained, samples stay within the measured support and PDF can exceed 100 sr⁻¹. Neither BSDF values nor PDF are clamped to Lambertian bounds.
- `energy-*.json`: 8,192-ray controls with `(R,T)=(0.2,0.5),(0,1),(0,0),(0.4,0)`. Check source = detector + absorption + escape + truncation, absorption = `1−R−T`, R/T sampling counts and independent centered-square view factor `4/(π√2) atan(1/√2)`.
- Actual box entry/exit uses declared `n=1.7` and vacuum: segment optical path switches from 1× to 1.7× and back to 1×. All three Fresnel splitting settings preserve the measured-interface result without adding another reflection/refraction. A rigidly rotated unit-transmission scene checks grazing normalization/passivity. Native project v9 roundtrip freezes the full grid; database B/S/T/R filters and scene identity retain the actual BTDF model.
- `camera-chain.json`: selected BTDF detector power about `2.725830078125e-12 W`; independent per-pixel photon/CFA/row calculation differs by at most `1.4210854715202004e-14 e−`. Exposure doubles electrons; excluding scattering gives dark pixels. RAW/ISP reproduce exactly after deleting the original CSV and database and replacing the workspace. Previous V9 NSC and Lambertian fixtures retain their original fingerprints.

Roundoff guards and analytical/statistical tolerances are internal correctness checks, not changes to any original commercial-reference tolerance. Whole-trace sampling statistics include repeated interactions and zero-power proposals; per-interaction power sums are not unique source power, independent pixel uncertainties or sensor-noise estimates.

## Reviewable artifacts

- `synthetic-r02-t05.bsdf.csv`: complete, deliberately synthetic R=0.2/T=0.5 control; copied into `samples/non-sequential/measured-bsdf/` with placement instructions. Its original-byte SHA-256 and canonical data hash are embedded in captured inputs.
- `measured-btdf.camera-experiment.zip`: independent frozen 32×32 / 4 µm / 550 nm BTDF experiment with A/B exposure comparison. Open and redevelop without the source CSV or NSC database. Frozen power redevelopment is not a new optical trace.
- `measured-bsdf-editor.png` and `measured-btdf-camera.png`: visually reviewed off-screen import/preservation/error handling, model clearing, B path selection, R/T counters, provenance and RAW/ISP. The measured model does not expose an extra Lambertian reflectance multiplier.
- `camera-lambertian-v9-frozen.zip` in test fixtures was copied unchanged from the previous phase's experiment artifact. Old optional fields remain absent/default, protecting historical request fingerprints.

## Preserved failures

`core-development.trx`: first 18/21 pass. Three endpoint/rotated-normal checks exposed inconsistent direction normalization in grazing cosine/PDF evaluation, including an unnormalized normal in the test's independent angle calculation. The propagation cosine now uses the same normalized direction convention as the BSDF PDF, the independent angle is computed from unit vectors, and a rotated NSC unit-BTDF control was added. Original energy gates were not widened.

`chain-development.trx`: subsequent 28/28 pass, including the old measured-loss compatibility case. The added rotated NSC case brings the new case count to 28 (the earlier 28 included one pre-existing compatibility case).

Support is the documented **native isotropic grid CSV**, not arbitrary vendor BSDF files. Fixed material-axis anisotropy, scattered-point fitting, delta specular lobes, per-face overrides, volume scattering, polarization/coherence, targeted importance sampling and MIS remain future work. See [format, physics and operation](../../../../docs/MEASURED_BSDF_BTDF_2026-10-31.md).
