# Lambertian BSDF verification — 2026-10-31

Scope: formal `IBsdf` and opaque Lambertian reflection in shared C# Core; NSC energy accounting, typed scattering paths, sampling diagnostics, detector capture, RAW/ISP and frozen replay. This is analytical/statistical regression evidence, not a commercial software or measured-material certification.

## Final acceptance

- Default Release test-project build and App build: 0 warnings / 0 errors; see `build-tests-final.log` and `build-app-final.log`.
- `final-focused.trx`: **121 passed / 0 failed / 0 skipped**, including 22 new BSDF/chain/off-screen cases. Existing coverage includes NSC filters/database/session lifecycle, detector-to-sensor conversion, old scattering-loss aliases, native format boundaries, V5–V9 experiments, three-language camera UI and the original 3D camera controls. Counts are test cases, not assertions; repeated stages are not added together.
- `manifest.json` records the scoped sources, verified default App/test Core identity, artifact SHA-256 hashes, per-class case counts and numerical energy checks. All six Core copies matched at test start; App and test copies retained the same verified hash in the completion audit (`post-final-core-copies.json`). Shared working outputs changed during and again after that audit, including App/test directories. The manifest labels the current working binaries separately and does not claim those later outputs were tested. `seal.ps1` checks the captured pre/post identities, scoped code timestamps and verified build time.
- All tests ran in the background. Avalonia UI verification and screenshots are headless/off-screen; no native desktop interaction, full-repository suite, packaging, publishing or push.

## Physical checks

- `samples-*.json`: 65,536 fixed-seed samples for each of three normals. Check `P(cosθ≤z)=z²`, mean `2/3`, second moment `1/2`, uniform azimuth, normalized directions and `f cosθ / pdf = ρ`. Statistical gates use six analytical standard deviations. Numerical integration, reciprocity and invalid input checks are in `BsdfTests`.
- `energy-*.json`: 8,192 NSC source rays at `ρ=0, 0.25, 1`. Reference differential-patch-to-square view factor is `0.5541264239795719`, with half-width/distance = half-height/distance = 1. The sampled unit-reflectance detector fraction is about `0.560669`, within the six-sigma binomial interval. Relative energy closure is below `1e-12` for these controls. This is an internal analytical guard, not a new universal product tolerance.
- The regression also checks four diffuse bounces with absorption/truncation in the original budgets; all three Fresnel splitting options give the same opaque-BSDF result. Zero reflected power terminates as absorption without inventing an R/S event.
- `camera-chain.json`: selected scattered power `2.8033447265624977e-12 W`; independent per-pixel photon/CFA/row calculation differs by at most `5.684341886080802e-14 e−`. Exposure doubles the expected electrons; excluding S paths produces dark pixels. RAW and ISP are nonempty and reproduce exactly after clearing the database and replacing the workspace.

Whole-trace statistics count sampled interactions before path filtering, including repeated bounces and zero-albedo proposals. Per-interaction power sums are not unique source power and not pixel uncertainties. Imported external databases do not manufacture a missing statistics summary.

## Reviewable artifacts

- `lambertian.camera-experiment.zip`: frozen 32×32, 4 µm detector, 550 nm, `ρ=0.5`, selected `S2 & D3`, absolute QE and A/B exposure comparison. Reopen in the Camera laboratory and recompute without the original NSC database. It freezes power and provenance; it does not rerun NSC geometry.
- `lambertian-editor.png`: valid albedo application after an invalid value was rejected; `lambertian-camera.png`: scatter selection, sampling diagnostics and RAW/ISP off-screen UI. Both were visually inspected.
- The old V9 fixture `tests/OpticalSystemDesign.Tests/Fixtures/camera-nsc-v9-frozen.zip` is byte-identical to the prior phase's `nsc-camera-chain/examples/nsc-main-ghost.camera-experiment.zip` (SHA-256 `c153c6610c01a9b35031afa678c4d1988aea31cbf122419bb711b2e2556aec7d`). New optional fields stay absent and original replay/fingerprint validation passes.

## Development history

`development.trx` preserves the first 18/21 result: two new assertions incorrectly required bit equality between sums in different orders (about `5e-14` relative detector-power difference); the UI also set a control Name after styling and threw. Assertions now use the existing `1e-12` relative energy guard and names are assigned before mounting. No physical/statistical acceptance threshold was widened to hide a failure.

`focused.trx` preserves the subsequent 104/104 pass. `expanded-pre-zero.trx` preserves the 121/121 pass after the direct-camera boundary and visible sampling-summary changes; `expanded-pre-zero-core.sha256` identifies that intermediate binary. The final run repeats the same 121 cases after correcting zero-albedo event semantics. Only `final-focused.trx` is the final acceptance run.

`build-zero-test-import-failure.log` preserves a test-only missing-namespace compile failure while adding the zero-albedo assertion; the import was corrected before the final build/run.

`pre-output-sync.trx` preserves the first final-code 121/121 pass, against `pre-output-sync-core.sha256`. The sealing audit then found a later Core/Application output alongside earlier App/module/test copies (`output-sync-audit.json`). The default graph was rebuilt and the same final cases repeated after synchronization (`pre-final-core-copies.json`). During that run other output folders changed again; `post-final-core-copies.json` preserves the differences. Acceptance binds the App/test Core `e569f05015c6993bc3cd96dbcdbb5613b26a89912f80d880b2ed3078d580ec55` observed before and immediately after the run. Subsequent working builds also replaced App/test copies with `fb6d2b94cb461da4eef88ba6ead5e57bc7da4dc27419e6bedc005de70e11af22`; those later copies are not represented as another pass. No scoped Core source change was found during the run, and no other task's processes or artifacts were overwritten to force global agreement. Binary differences alone are provenance differences, not evidence of a numerical failure.

Final build order is the full default App graph, then the default test driver with `-p:BuildProjectReferences=false`, then `dotnet test --no-build --no-restore`. This avoids rebuilding the already-built product references between the output audit and test start. It does not skip the App/Core build; see `build-app-final.log` and `build-tests-final.log`.

Limits: achromatic opaque Lambertian reflection only. Measured/spectral/angle-dependent BSDF, BTDF, mixed lobes, per-face overrides, volume scattering, targeted importance sampling, MIS, polarization and coherent NSC transport remain future work. See [implementation and format contract](../../../../docs/LAMBERTIAN_BSDF_2026-10-31.md).
