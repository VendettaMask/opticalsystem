# First-order convention and FFT validity, 2026-11-03

See the [scoped report](../../../../docs/FIRST_ORDER_FFT_VALIDITY_REPAIR_2026-11-03.md) and [machine summary](summary.json).

- [Five explicit first-order convention cases](first-order-convention-results.json) pass and all reject the mixed legacy native reference. [Native definition controls](crystal-definition-controls.json) preserve aperture/thickness variants, the corrected EFFL wavelength slot and 60 unchanged real-ray operands. Original 125 Difference remains historical.
- [Four original FFT requests](fft-availability-results.json) now yield typed `Unavailable` with unwrapped pupil-phase sampling diagnostics. Their original native references remain invalid; no numerical Pass is claimed.
- [Huygens controls](huygens-converged-results.json): PSF/MTF at 256/512 and cross section at 512/1024, 21 checks pass within the complete declared finite windows and original tolerances. [256/512 cross-section convergence](huygens-cross-lower-sampling.json) remains Close. The earlier capture with an actual 32-sample pupil is excluded and preserved in the archive.
- [Prior convergence controls](prior-converged-results.json): all 38 pass again under the final Core. [156 cross-analysis prior Passes](cross-analysis-results.json) remain Pass. Original 252 requests retain 239/1/1/11/0; overlapping scopes are not summed.

611 scoped plus 134 distinct existing operand tests pass, zero failures/skips. The normal default dual-module Release App builds without warnings/errors. All use Core SHA256 `1ce484a713e50d89ccb534fc5ed3f7c19f9a3d471d94c7800d0fce4aae41ed20`. No new full 95-example matrix, whole-repository tests or remote synchronization is implied.

`archive-index.json` resolves each logical numerical run, original failure, native setting, owned binary and source by content hash, reusing previous verified archives. `archive-verification.json` records complete payload readback before retiring expanded owned runs; the final runnable diagnostic output is retained. `source-verification.json` binds actual final sources to portable PDB documents and checks App/Core identity. `documentation-check.json` and `final-review.json` record final documentation and review checks. No user's application or source prescription was changed.
