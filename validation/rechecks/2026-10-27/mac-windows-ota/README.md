# Mac and Windows OTA delivery

Final source `6b4ccf3129b926ad82ae68de3d62b2310da34285` passed [validation 40468907100](https://github.com/VendettaMask/opticalsystem/actions/runs/40468907100), including native early-quit/save-choice regression. [Release 40469805561](https://github.com/VendettaMask/opticalsystem/actions/runs/40469805561) published all three packages and activated the signed Mac feed. All three actual downloads and both protocols were independently verified; see `public-verification.json` and `release-checks-summary.json`. No installer was executed locally. The earlier integration stage below is retained with its original scope.

# Earlier integration stage

Scope: [Actions 40464598219](https://github.com/VendettaMask/opticalsystem/actions/runs/40464598219),
source `6c56adc7f8d7db5949b88bf689c2e2c02c95427d`. Public release activation is a
separate step; see the [current ledger](../../../../docs/validation/MAC_WINDOWS_OTA_2026-10-27.json).

- Windows: 185 distinct scoped tests including the existing publisher, release
  parser, signed downloads and actual save/discard/cancel entry. Repeated Skia
  renders do not inflate the count. Mac: 86 distinct update/close UI cases.
- Native Mac bridge compilation, fixed C ABI, queued install reply, cancelled
  download and cancellation during extraction passed. Production never enables
  the test loopback URL policy.
- Existing RSA-root recovery and deterministic Mac key derivation: 40 throwaway
  cryptography checks, no new Secrets. The actual derived identity signs the
  disposable DMG and appcast; altered bytes are rejected.
- Full packaged app and read-only mounted DMG: ad-hoc signatures, both modules,
  bundled runtime/fonts, exact manifests and native bridge exports verified.
- Four native replacement cases use complete bundle copies with fixture-only
  build/key/network Info.plist changes: cancel download, cancel ready install,
  tampered archive, and actual replacement with test-driver relaunch. Complete
  target manifests and separate project/settings sentinels are compared. The
  replacement product then runs its real non-GUI module verification. It does
  not launch the product GUI or certify every Mac/OS configuration.

Small public summaries are retained here. Raw cloud TRX, rendered frames and
bounded fixture diagnostics remain in the Actions artifacts; no private keys,
system memory dumps or maintainer desktop data are committed. The derived Mac
public key is in `packaging/macos/ota-public-key.txt`.

Earlier failure records: 40461433050 had native compiler argument splitting;
40462525183 had five Windows-panel assumptions in actual entry tests on Mac.
All five now exercise the real Mac close delegates and none are skipped.
40463535364 passed the preceding UI/package/signature stage, before replacement
fixtures were added. The first local VSTest connection attempt was aborted, not
passed. Superseded automatic full CI runs on this task branch were cancelled
to release cloud resources, following the focused-regression preference.

No local installation, clipboard, focus, mouse, keyboard or native desktop
window was changed. The original checkout's unrelated edits remain separate.
