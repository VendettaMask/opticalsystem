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
The application now has a Mac-specific update panel using the native Sparkle
driver, with no automatic startup network checks. Installation replies are held
until document and module decisions plus session persistence succeed. Cancel
uses Sparkle's cancel-install choice, including during extraction, to avoid
implicitly approving installation on quit. Windows continues to use its
existing signed manifest, verified download and guarded helper unchanged.

`prepare-macos-ota-release.ps1` signs the DMG and a channel-aware appcast locally
in the cloud; it performs no GitHub writes. Private derived seeds are held in
owner-only temporary files and removed in `finally`. The `ota-security.yml`
workflow validates the native bridge, throwaway signed/tampered archives,
headless UI contracts and a disposable packaged application/DMG. It does not
publish to Releases or update `main`.

Release wiring is prepared, with archive/feed signing before release mutation,
the existing Windows attachment publisher, and a final archive-identity gate
before activating the appcast. The maintainer authorized main integration and
cloud publication after successful regression. An installed old-to-new
replacement regression remains pending; neither the bootstrap release nor the
production feed has yet been changed.

The native installation fixture uses copies of the complete packaged product,
with only fixture build/key/local-network Info.plist values changed and re-signed.
A test-only AppKit driver permits its loopback server; the shipped bridge accepts
only the product's HTTPS GitHub DMGs. The fixture checks cancelled download,
cancelled install, a modified archive and actual bundle replacement/relaunch,
compares complete bundle manifests and separate user-data sentinels, then runs
the updated product's non-GUI module/bridge verification. It relaunches the
test driver rather than opening the actual product GUI; that distinction must
remain explicit in verification records.
