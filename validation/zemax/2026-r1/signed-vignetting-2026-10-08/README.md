# Signed vignetting and finite-object native launch controls

Captured on 2026-10-08 with OpticStudio 2026 R1 (26.1.0, build 260127) through the installed native ZOS-API. `manifest.json` records each control's native environment, valid API license and hashes. These are derived controls of the official `Sequential/Objectives/Tessar lens using vignetting factors.zmx`, parent SHA-256 `c7ed96a7688abdf2fae197cd67be96bc69f397826a036a7dab424be6394f2644`; they are not unchanged standard lenses.

| Control | Deliberate prescription change | Native launches |
| --- | --- | --- |
| negative | Negate the original Y field positions and VDY; retain the other original factors. | 128 |
| mixed | Y = -25, -10, 10, 25, with distinct X/Y decenter, compression and rotation factors. | 128 |
| asymmetric | Y = -25, -6, 0, 14, with distinct factors. | 128 |
| finite-tessar | Change Object thickness from infinity to 100 mm; retain the original field table and materials. | 128 |

Each job uses 16 explicit normalized fields, eight formal Core GQ2 pupil inputs per field, wavelength index 2, aiming off and retained vignetting. The compact JSON files copy the native `inputs` and `launches` arrays without fitting, interpolation or reference regeneration. Full native ray audits and environments remain in the hash-addressed local paths recorded by the manifest. They also retain propagation failures and aperture flags; successful launch agreement is not certification of every subsequent ray interaction.

The selected full ray audits and environments are also preserved byte-for-byte in the [synchronization gzip archive](../../../../artifacts/validation/issue-repair-20261008/remote-evidence/README.md); its inventory maps the original local paths and both compressed/uncompressed SHA-256 values. This archive supplements the compact fixtures without changing their bytes.

The three signed tables certify 384 launch records: one-sided negative Y uses squared radial interpolation, while the captured mixed/asymmetric tables retain nearest-row selection. The finite-object control contributes another 128 records. Tests map native Object-origin Z to Core coordinates before comparing a physical directed ray; tolerances are 2e-8 mm in XY and 1e-10 for each direction cosine. These are new independent Zemax controls, separate from the unchanged primary `123456.ZMX` baseline and frozen auxiliary Optiland history.
