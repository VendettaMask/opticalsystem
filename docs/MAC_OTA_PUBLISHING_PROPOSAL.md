# Proposed production OTA publishing change

The maintainer authorized this publishing change on 2026-10-27, conditional on
cloud regression success. The workflow implementation is prepared on the
isolated branch. Production main and public releases remain unchanged until
those checks pass.

The prepared code adds the native Sparkle bridge and Mac update panel, derives
the Mac Ed25519 identity from the existing protected Windows RSA key, verifies
signed archives and prepares a signed appcast. The existing Windows trust key,
release-body envelopes and installer helpers are unchanged. See [Mac OTA](MAC_OTA.md).

To activate the implementation, the remaining concrete publishing changes are:

1. Use an Apple Silicon Mac for the final publisher job, so it can run the
   pinned Sparkle signing tool. Keep the same existing three OTA Secrets scoped
   to the publisher job; create no new Secret.
2. Before changing release attachments, call
   `scripts/prepare-macos-ota-release.ps1` for the built Mac package and the
   current public feed. This validates the root identity, signs the DMG and
   verifies both archive and appcast signatures. Failure must stop publishing.
3. Keep `scripts/publish-github-exe.ps1` as the release attachment publisher,
   retaining its checks, previous-package backups and Windows OTA signatures.
   For the first bootstrap, rebuild the three supported runtimes from the same
   source and update the existing `v1.0.4-test.20261019` prerelease without moving
   its tag or making it Latest.
4. After verifying the published Mac asset's name, size and SHA-256 against the
   signed entry, commit only `updates/macos/appcast.xml` to the remote `main`
   branch using the previous file SHA as a concurrency check. Read it back and
   verify the public feed signature. A failure here is a failed release run,
   not a completed OTA activation.
5. Merge the verified implementation to `main`, put version history on the
   Releases page, and update installation guidance to describe the one-time Mac
   bootstrap. Do not install anything on the maintainer's computer.

Later release runs use the same signing and feed update sequence. Mac builds
use a monotonic numeric `CFBundleVersion`; test appcast items use Sparkle's
`test` channel. The feed remains at the fixed GitHub raw `main` URL embedded in
the bootstrap. Windows clients continue to read their existing signed metadata.

The old-to-new Mac replacement, cancellation and relaunch regression must run
in an isolated temporary cloud profile. First-run Gatekeeper behavior remains
separate from OTA authenticity; this path does not claim Developer ID signing
or notarization.
