# MS-L7 off-axis Huygens controls — 2026-10-11

Captured locally with OpticStudio 26.1.0, build 260127, EnterpriseEdition and a valid API license. The original source SHA-256 is `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`. The source is never saved by the native host. These controls are limited to MS-L7; independent Cooke and Tessar investigations remain outside the user-authorized scope.

| Directory | Captured control |
| --- | --- |
| `psf/field2-uniform-*`, `psf/field3-uniform-*` | Fields -10.6/-15 mm; wave 1; pupil/image 32; 0.25 µm; Linear/Real/Imaginary; Auto/Planar/Spherical. |
| `psf/field2-gaussian-*` | Field -10.6 mm; Gaussian factor 512; wave 1; pupil 32/image 64; 1 µm; Linear/Real/Imaginary; Planar. |
| `psf/field2-pupil`, `psf/field3-pupil` | Exactly 665 normalized pupil inputs per field; raw native image-local XYZ, full LMN, OPD, masks and intensity. |
| `mtf/positive-chief`, `mtf/positive-centroid`, `mtf/negative-centroid` | Isolated +15/-15 mm PSF controls; pupil/image 32; 0.25 µm; Auto/Planar/Spherical. The +15 mm override is explicit in the request and native text output. |
| `mtf/scan-plus` | Original +Y MTF scan; Field Density 10; six frequencies 10–60 cycles/mm; wave 1; pupil/image 32; 0.25 µm; Auto/Planar/Spherical. |
| `mtf/scan-three` | Independent requested Field Density 3 control. Native computation uses five positions, confirmed by the full curve; the actual API setting still reports 3. |
| `mfe/positive`, `mfe/negative` | Native MTHA/MTHS/MTHT/MTHN/MTHX at ±15 mm, Samp 1, wave 1, 60 cycles/mm, 0.25 µm, polarization/all-configurations off; actual column schemas included. |

Every capture retains its request, actual settings, environment and raw numeric output. Huygens captures also retain CFG, text output and method readback. `capture-file-hashes.json` identifies all saved bytes except itself.

The scalar plane projection uses the traced directions in the image-surface tangent frame. Native MTF scans and MTH operands agree with the transform of the geometric-centroid PSF window. Field Density counts positions rather than intervals, with a native effective minimum of five. These internal sampling observations are inferred from independently executed controls; they are not claimed to be a published universal specification.

Spherical outputs are diagnostic method controls, not certification of the current spherical product path. Polarization, polychromatic off-axis synthesis, tilted/complex pupils and other lenses are not certified by these captures. The wide Gaussian factor 512 field -15 mm capture had nonfinite native cells; the negative scan returned empty data. Those cases are excluded from certification and retained in the separate diagnostic exclusion report. No nonfinite or empty array is counted as a pass.

Capture sources: `tools/diagnostics/N02Layers20261009/NativeEnergyControl.cs`, the repository Zemax host helpers, `CaptureOffAxisControls.ps1` and `CaptureMtfOffAxisControls.ps1`. Compile against the locally licensed ZOS-API assemblies, then supply `-NativeHostDirectory` and a fresh output directory. The optional overrides affect only the isolated API application.

See [the repair and verification report](../../../../docs/HUYGENS_OFFAXIS_REPAIR_2026-10-11.md). Existing frozen captures and comparison tolerances were not changed.
