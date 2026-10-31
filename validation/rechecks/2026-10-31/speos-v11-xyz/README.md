# Speos v11 / native peak / White Specular / calibrated XYZ

Final focused run: **229 / 229 passed**, **38 new cases**, zero failed/skipped. Default Release App and test driver builds: zero warnings/errors. No full suite, native desktop acceptance, installer rebuild or commercial numerical certification.

See the [implementation contract](../../../../docs/SPEOS_V11_XYZ_2026-10-31.md). This is one shared, dirty checkout; unrelated afocal/coating/initial-structure work was preserved. Fingerprints identify this run, not a clean commit or a numerical acceptance criterion.

## Reproduction and identities

Build the default App Release output, then build the test driver with `-p:BuildProjectReferences=false`, as recorded in `build-final-app.log` and `build-final-tests.log`. Run `run-focused.ps1` from the repository; it sets only this stage's evidence variable, includes the previous 191 cases and the new 38, and does not rewrite earlier stage evidence. `seal.ps1` records TRX counters and hashes. `update-docs.ps1` is the one-time documentation edit record, not a repeatable migration.

`before-final-run.json` / `after-final-run.json`: **57** scoped sources/fixtures, **9** binaries, no drift. Application copies agree. `manifest.json` seals artifacts and records the base commit; the source checkout contains uncommitted changes. Default build output was used throughout successful final validation.

## New test scope

| Class | Cases | Evidence |
| --- | ---: | --- |
| SpeosV11BinaryTests | 6 | Synthetic LZ4 v11, embedded cones/white, corruption/unknown variant rejection, actual installed 81/125-sample materials |
| SpeosV11XyzTests | 11 | v11 quantities/terminal marker, peak position, white/background spectra, XYZ calibration/mixture/TIS, metadata, unchanged official text |
| SpeosV11StatisticsTests | 11 | Independent slope Jacobian and recovered-source CDF/moments at 15/45/85 degrees; fixed-axis rotation; joint R/T at 400/550/700 nm; nonpassivity and index mismatch |
| SpeosV11NscTests | 2 | Speos and XYZ BTDF at n=1 -> 1.5, energy/path length, protocol-4 database, STAROPT v12 and downgrade rejection |
| SpeosV11ChainTests | 5 | Four three-band NSC -> RAW/ISP paths, one photon conversion, project and frozen replay; original grid3 frozen experiment |
| SpeosV11WindowTests | 3 | Off-screen embedded v11, explicit white cone and calibrated XYZ controls -> camera |

Analytic slope controls use 16,384 samples per case (seed 71289), including source CDF P(mu>0.5)=0.75 and Lambertian first/second moments. Maximum independent density relative error is about **1.26e-11**, within the declared 2e-10 internal density guard. Joint reflection/transmission tests use 16,384 samples per band, seed 7223, independent trapezoidal integrals of the retained projected grid and a six-sigma binomial gate. XYZ constant-channel controls use 8,192 samples per band and six-sigma analytic moment gates. Peak-width controls are separate bounded-shape checks; they are not a statistical estimate of a vendor reference.

Four `chain-*.json`/experiment ZIPs retain three actual detector bands with deliberately unequal wavelength-table weights; no second area/throughput/weight multiplication occurs. Maximum pixel photon-conversion errors: v11 binary 5.69e-14 e-, white 9.10e-13 e-, XYZ R/T 1.14e-13 e-. RAW/ISP replays are exactly equal after deleting BSDF/calibration and clearing live state. Five historical frozen identities pass, including new `camera-projected-grid3-frozen.zip`; old artifacts were not regenerated.

The two new medium tests close the energy ledger within 1e-12 relative; observed balance residuals are 4.45e-16 and 1.80e-14. XYZ constant distribution additionally uses the independent square-detector view factor. Speos peaks use their separate density/CDF guards. No captured commercial radiometric results were used as a runtime correction.

## Corpus, license and limits

`installed-v11-False.json` and `installed-v11-True.json` record full paths, SHA-256, 125/81 spectral samples, 40,002 retained angular nodes and current 30-degree/550-nm reflectance. Installed material bytes are not redistributed. `flags.json` and `v11-budgets.json` retain the read-only corpus inspection. Local-corpus tests return without a corpus read on machines lacking those installed files; both actual paths were present and exercised in this run. Deterministic archive tests always run.

The unchanged official `speos-gaussian-v11.anisotropicbsdf` fixture is from ansys/pyspeos commit `bc5f75644f568c6b1c9b9fa1b53d22d18170cf99`; its full MIT notice is copied beside the fixture. `official-v11-text.json` records its unchanged source digest and positive import. `official-texture.anisotropicbsdf` is a public text-format inspection download from the same commit, not a product dependency. Public XYZ examples were downloaded from the official [Zemax format article](https://optics.ansys.com/hc/en-us/articles/43071107687699-Zemax-Tabular-BSDF-data-file-format) into `official-xyz-examples.zip`; no license for redistribution as a product material is assumed. Its XYZ example's filename says BTDF while the file header says BRDF: import always uses the header. These are format examples, not a certified material/calibration pair.

All installed v11 samples inspected had enhancement disabled. Active v7/v11 cone/White Specular archive handling is verified with independently written synthetic records; no active commercial file plus native Speos calculation was available. The native `SlopePowerTransport/1` and node-based cone split are documented physical models, not a claim to reproduce a private vendor interpolation formula. Active v8/v9 layouts, unknown v11 variants, retroreflection, delta lobes and uncalibrated XYZ spectral recovery remain outside scope.

## Development history retained

- `development-unit.trx`: 8/11. Full public fixture exceeded the original expanded-grid budget; the initial near-peak mass check ignored the retained diffuse background. Compact spectra and a meaningful separate analytic CDF/density test resolved the scope, without changing a vendor precision gate.
- `development-unit2.trx`: 10/11. The public two-endpoint rotational file still exceeded the both-side frozen budget; its 0/360-only isotropic meaning was corrected and the public format case explicitly imports Front.
- `development-unit3.trx`: 22/23. Fixed-axis PDFs near 1.46e4 straddled a decimal rounding boundary despite a roughly 1e-14 relative rotation difference. The rotation test now uses its declared relative-density guard, not fixed decimal places.
- `development-chain.trx`: 31/31; `development-boundaries.trx`: 38/38; `final-focused.trx`: **229/229** on final code.
- The initial restricted build call produced no successful compilation or compiler diagnostics. Subsequent permitted background builds used the default output and succeeded; testhost required local process communication. No foreground application was launched or closed.

Final off-screen captures are `editor/camera-False-False.png` (embedded v11), `editor/camera-False-True.png` (explicit cone), `editor/camera-True-False.png` (XYZ). They were visually inspected. Earlier shorter-name captures and superseded JSON records remain as development evidence; final TRX and final identity snapshots are the acceptance record.
