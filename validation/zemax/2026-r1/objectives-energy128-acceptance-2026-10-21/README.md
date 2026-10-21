# Additional 128-grid acceptance, 2026-10-21

The user selected: “新增 128 档验收，32 档继续记为未解决”. [acceptance.json](acceptance.json) registers three additional matched requests for Apochromat2, Apochromat4 and the MIL doublet. Both systems use pupil sampling 128, image sampling 256, configuration 1, the axial field, selected primary wavelength, centroid, 401 output points and 5 micrometers maximum radius. The original comparison policy is unchanged.

The native arrays and requests remain byte-for-byte in the earlier [residual captures](../objectives-residuals-2026-10-21/cases.json), originally labeled supplemental controls. This manifest records their subsequent acceptance role without modifying that acquisition history. Each original 32-grid result remains Difference / Unresolved. There is no change to product sampling defaults or full-corpus acceptance rules.

[ray-comparison.json](ray-comparison.json) records 405 actual energy-grid pupil nodes per lens and 6075 surface records from formal Core and an independent licensed OpticStudio 2026 R1 application. All coordinate/direction components pass the original Single Ray Trace limits, with identical trace/vignette status. The offline reconstructed pupil must reproduce the formal complete energy FFT array bit for bit before capture inputs are emitted. Coverage is limited to the same axial primary-wave requests.

The [task report](../../../../../docs/OBJECTIVES_RESIDUALS_2026-10-21.md) documents the PSF repair, unresolved original requests and focused verification. Raw ray captures, source/host identities, replay inputs and test results are in the [acceptance evidence archive](../../../../repairs/2026-10-21/objectives-energy128-acceptance-evidence.zip). Proprietary API binaries are excluded.
