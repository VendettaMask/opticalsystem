# Afocal relative illumination applicability

The five original requests now return structured Core `NotApplicable`, without
numerical RI curves. All five original native captures and their new background
recaptures lack numerical buffers. This resolves applicability and failure
classification, **adds zero numerical Passes**, and does not rewrite the old
170-case run. See the [report](../../../../docs/AFOCAL_RELATIVE_ILLUMINATION_APPLICABILITY_2026-10-30.md).

`original-requests.json` retains original request fingerprints, snapshot/source
SHA-256 identities, settings and the original (empty) effective-tolerance fields.
`assessment.json` records before/after replay, native diagnostics, positive
control scope, test runs and shared Core identities. `verification.json` seals
the evidence archive and verifies every entry length and SHA-256.

`evidence.zip` uses `entry-map.json` for content deduplication: resolve each logical
`path` to its actual `member`, then verify `length` and `sha256`. It includes
raw native text, missing-buffer diagnostics, requests, model/settings readback,
old native failures, before/after output, test/build logs, relevant source and
source hashes. Proprietary native DLLs and executables are excluded. Dependencies
remain in their original immutable archives and are pinned by SHA-256.

The final focal native control only verifies buffer availability; it is not a new
formal optical comparison. Earlier metadata-heavy control, missing-version
control failure and simultaneous-build lock failures remain as superseded
diagnostic records. Diagnostic process exit zero is not numerical acceptance.

Pure C# product and shared Core; no changed prescriptions, afocal flags, request
settings or gates; no desktop interaction, full-corpus rerun, commit or push.

Final focused tests: **298/298** (237 + 61), no failures/skips; earlier runs are
superseded, not added. Default App Release and diagnostic builds have zero
warnings/errors. All six Core copies share SHA-256
`21d9a68915ecc34556e9c4f5d88f04e9f9b425a1fdeac6edf71b172c7458ff89`.

The sealed archive is 529,254 bytes with 225 logical entries and 156 unique
members; every entry was verified. Archive SHA-256:
`89aeb7953c4d03e2948a3db5b48e090fa9a1ee190e395c12ff029200524f471c`.
The five dependency archives, including the immediately preceding centroid-energy
stage, remain unchanged. Sealing path/active-log failures were corrected before
this seal; the incomplete earlier archive is not the sealed evidence.
