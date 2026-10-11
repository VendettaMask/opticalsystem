# Verified MS-L7 scalar Huygens spherical repair evidence

This bundle preserves the measured before/after data and completed verification for the scalar subset. Experimental polarization and nonfinite wide complex controls remain explicit nonpassing evidence. Independent Cooke and Tessar follow-up is excluded.

- `comparison-before.json` and `provenance-before.json`: actual pre-repair product output from a fresh build of the unchanged preceding committed source. Its binary bytes differ from the earlier saved build; the report preserves both hashes and verifies the preceding diffraction source identity. Only the original two wide-window, two polarization and two polychromatic cases were measured before repair; supplementary cases have no fabricated before values.
- `comparison-after.json` and `provenance-after.json`: final WorkbenchRuntime and JSON results compared with every saved Auto/Planar/Spherical method output. Forced methods are diagnostic comparisons; ten nonpolarized Auto cases form scalar acceptance.
- `*-workbench.json`: final product data, including the two experimental polarization outputs preserved as nonpassing controls.
- `hypotheses.json`: offline origin and amplitude investigations. Approximate Jones hypotheses do not certify full polarization transport; existing absorbing-interface rejection is recorded separately.
- `nonpassing-controls.json`: the measured polarization residuals and all wide complex grid availability counts. No nonfinite grid or failed scalar approximation is counted as a pass.
- `formal-original.json` and its provenance: all fifteen original N01/N02/MTF comparison metrics remain exactly equal to the preceding report, with unchanged original requests and tolerances.
- Final completed TRX gzip files, build logs and `debug-filter.txt`: actual test identities, multiplicities and verification selection. Gzip decompression is checked against the original bytes.
- `frozen-head.json`: all original 2668 frozen files retain the original Git and working-tree byte identities.
- `capture-and-report-hashes.json`: saved evidence hashes. The native fixture has its own exact-byte capture manifest.

Initial sandbox IPC failures, testhost timeout and superseded diagnostic runs are retained in the local run directory and excluded. Full Debug, laboratories, installer, manual desktop and the complete six-file matrix were not rerun.

See [the repair report](../../../docs/HUYGENS_SPHERICAL_REPAIR_2026-10-11.md) and its JSON for actual completed counts and remaining scopes. Native evidence is committed under `validation/zemax/2026-r1/huygens-extensions-2026-10-11`.
