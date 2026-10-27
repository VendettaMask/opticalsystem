# Mac OTA

Mac OTA integration is in progress. The existing Windows RSA-4096 signing
identity remains pinned and unchanged. The Mac signing seed is deterministically
derived with HKDF-SHA256 from its canonical PKCS#8 private encoding, with separate
product salt and `OpticalSystemDesign/macOS/Sparkle/Ed25519/v1` context. The
existing encrypted recovery path restores the same derived key. No additional
GitHub Secret, Apple Developer ID or notarization is required for this path.

Sparkle 2.10.1 is pinned to the official archive SHA-256
`1a856d2a9edb8e628de88121d7be04cf0e2167f8641650b78fca00b14244cf27`.
The cloud identity check publishes only the derived public key and tests native
bridge compilation plus disposable archive signing/verification. This check
does not by itself establish an installed product OTA regression.

The planned product flow is user-initiated check, download, Ed25519 verification,
save/discard/cancel, installation and restart. Existing Mac users need one manual
bootstrap installation. Gatekeeper approval remains the user's decision.
The bootstrap package and native replacement regression are not yet published.
