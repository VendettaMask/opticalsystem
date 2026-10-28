# Afocal PSF reference manifest

`manifest.json` identifies exact members of the already committed
`validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip`.
Tests verify its archive SHA-256 and every selected member SHA-256 before reading.
No new binary reference is committed. The optional `Prepare-Fixture.py` output
`native-grids.zip` is an offline derived copy retained in Library, not required by CI.
