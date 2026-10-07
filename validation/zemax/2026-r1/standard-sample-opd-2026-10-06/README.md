# Official standard-sample OPD regression evidence

Immutable fixtures copied from the initial OpticStudio 2026 R1 captures on 2026-10-06 (26.1.0, build 260127, EnterpriseEdition, valid license). They cover Cooke 40 degree field, Double Gauss 28 degree field and Relay lens, with on-axis/primary-wavelength and last-field/wavelength-1 requests. Relay has a finite 250 mm object distance and source ray aiming enabled; the other two have infinite object distance and ray aiming disabled.

Each `snapshot.json` is the exact imported configuration saved during the original comparison. Each `*-native.json` is an unchanged original native OPD result, with both 41-point fans. `manifest.json` retains original source identity/hash, snapshot/native hashes, canonical request, unchanged captured tolerances, capture manifest path and original metrics. No original ZMX file is required on the test machine. Captures establish only these files/settings/version; they do not establish universal Zemax defaults or product-wide certification.

`StandardSampleOpdParityTests` verifies fixture hashes and full native fan parity, then uses the same Relay snapshot for independent physical-equivalence checks of finite angle versus object-height fields. Only test code constructs that equivalence; runtime analyses remain in the formal shared Core. Existing `123456` captures and `validation/history` remain unchanged.

See [the repair report](../../../../docs/ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.md) for current recomputation, numerical conclusions and verification limitations.
