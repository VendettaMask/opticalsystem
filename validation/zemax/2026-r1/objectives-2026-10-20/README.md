# Objectives comparison, OpticStudio 2026 R1

This is the complete 19-lens **Objectives** product-example matrix for 2026-10-20. All original ZMX/ZOS bytes remain unchanged. The calculation uses the formal Application and shared C# Core. The original comparison policy is unchanged; Close and unavailable physical output are not numerical passes.

- [逐镜头结果](BY_LENS.md): 19 rows, analysis and explicit-ray counts.
- [逐分析结果](analysis-matrix.csv): all 638 requests.
- [光线比较](ray-results.json): all 74 GQ6 groups, 145,841 physical surface comparisons.
- [原通过项保护](previous-pass-preservation.json): all 391 previously passing analyses remain Pass.
- [V4 通过项保护](v4-pass-preservation.json): all 562 passes from the preceding repaired matrix remain Pass; [逐镜头前后计数](before-after-by-lens.csv).
- [机器汇总](summary.json) and [完整修正/边界说明](../../../../docs/OBJECTIVES_COMPARISON_REPAIR_2026-10-20.md).
- [V4 基础原始证据](../../../repairs/2026-10-20/objectives-matrix-evidence.zip) and [基础包身份](archive.json); the repository stores this archive through Git LFS.
- [最终 V7 增量证据](../../../repairs/2026-10-20/objectives-v7-integration-evidence.zip) and [最终包身份](integration-archive.json). Both archives are required: unchanged payloads inherit exact SHA-256 identities from the sealed V4 base, and changed payloads are stored in V7; every inherited and fresh payload passed readback verification.

The final v7 replay has **564 Pass, 1 Close, 3 Difference, 70 Incomparable, 0 Error**; all 74 ray groups pass. The three open differences are Apochromat2/Apochromat4/MIL doublet 32-grid diffraction energy. Double Gauss 5-degree FFT PSF cross section remains Close. Petzval relative illumination and RMS-vs-field now pass. One native 200-degree FFT PSF profile again returns a null data buffer and remains an unavailable reference.

`materials/` preserves the independent material-only API capture and 1,400 held-out points for the two bounded product MIL profiles. A fresh capture using the final helper source is byte-identical to both original datasets; see `reproduction-identity.json`. Product input material data lives in Core, not in this validation directory.

`captures/` retains 17 native precision controls: 15 repaired cases are required-pass regressions; the two open Apochromat energy controls are explicitly marked open and remain in the complete matrix. The new MIL energy difference is also retained in the complete matrix. A rejected 32-grid candidate improved one lens while regressing a previously passing Cooke result; it was withdrawn. No old test, native reference or tolerance was weakened to accept a candidate.

Scope is 18 primary analyses, four edge-field controls per positive-weight wavelength and GQ6 rays over every defined field. Final Release Core SHA-256 is `cf3647652213542dae90d0ed1c16b9df5290e740a0b204f1b4a6e0a9f40a6b2c`. This does not certify arbitrary settings or all ZOS numerical outputs. V4 is preserved in the sealed base ZIP; rejected broad-root and error-bound candidates are distinguished from the final fallback that restores only previously rejected roots. Final engineering checks are targeted at the user's request; the earlier complete main-suite result is not attributed to the final Core. See the [scoped diagnostic workflow](../../../../tools/diagnostics/Objectives20261020/README.md).
