# Verified Huygens off-axis repair evidence

2026-10-11: MS-L7 original Huygens MTF vs Field curves all pass with unchanged captured settings and registry tolerances. The original N01 and N02 curves retain their passing values. Independent Cooke and Tessar follow-up is excluded by user scope.

- `formal.json` and `provenance.json`: all fifteen original curve metrics through WorkbenchRuntime and the product JSON boundary; twelve MTF curves plus the original Huygens section and two DEE curves.
- `huygens-mtf-vs-field-workbench.json`: actual final product output, including traced field positions and displayed curve data.
- `offaxis.json`: independently reconstructed planar complex fields, formal product intensity errors, and two 665-ray position/direction audits. Raw batch OPD differences have not been converted to a common wavefront reference and are explicitly excluded from direct OPD-equivalence gates.
- `mtf-offaxis.json`: independent transforms of native chief/centroid PSFs and native Auto/Planar/Spherical scan endpoints. Only a global intensity-invariant complex phase is removed in the complex-field diagnostic.
- `field-spline-before.json` and provenance: preserved intermediate eleven-position investigation. No inferred native knots or fitted coefficients enter product calculations.
- Final TRX gzip files: product Release 4655/4655, comparison Release 182/182, Debug related 386/386, no skipped tests. Compression is lossless and the decompressed bytes are independently checked.
- `frozen-head.json`, `frozen-index.json`: original 2668 frozen files match original aggregate bytes in Git and the working tree.
- `excluded-native.json` and `excluded-native/`: wide field -15 mm Gaussian controls with nonfinite complex cells and the empty native negative scan. These are diagnostic exclusions, not passes.
- `capture-and-report-hashes.json`: saved evidence byte hashes; the separate native fixture manifest identifies all 254 saved fixture files except the manifest itself.

The earlier aborted/failed/connection-timeout TRX remain under the local diagnostic run directory and are not counted as completed verification. Debug sandbox host connections failed; the unchanged Debug assembly passed under normal local permissions. The full Debug suite, laboratories, installer, manual desktop and complete six-file numerical matrix were not rerun.

See [the repair report](../../../docs/HUYGENS_OFFAXIS_REPAIR_2026-10-11.md) and its JSON for test identities, baseline multiplicities, binary and source hashes, native environment and remaining scopes. The native capture files are saved under `validation/zemax/2026-r1/huygens-offaxis-2026-10-11`.
