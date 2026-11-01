# Full-incidence log interpolation and absolute XYZ reconstruction

2026-11-01. Product changes and detailed limits: [report](../../../../docs/XYZ_LOG_RECONSTRUCTION_2026-11-01.md).

The current result is **two of the remaining three XYZ calibrations passing the original development holdout gates**. Mystic and Cayman qualify; Blue Acrylic remains rejected. This phase does not complete the entire requested XYZ goal or commercial certification.

- [Final verification summary](final-verification-summary.json): 332 focused tests, 43 measured angular chains, 4 calibrated XYZ synthetic-shape controls, 23 previous White power controls.
- [Six C# RBF fits](final-rbf-fit/summary.json), including both general configurations on all three materials. Two approved calibration JSON files are next to their fit reports. No failed calibration is exported.
- [All 40 Cornell powers and original-knot checks](final-log-import/summary.json), [independent original-ring quadrature](diagnosis.json), [43 complete chains](final-angular-chain/summary.json), with 1,376 histogram bins.
- [Independent unscaled XYZ witnesses](final-rbf-witnesses.json), [four calibrated chains](final-calibrated-chain/summary.json). The angular shapes here are synthetic, not additional measured BRDF/BTDF data.
- [White controls](final-white/runtime6-native-power-audit.json), [source/binary integrity](final-run-integrity.json), [compiled-source checksums](compiled-source-audit.json), [source/input map](source-snapshot-map.json).
- [Finite exploratory results](exploration-finite.json) retain failed/nonfinite diagnostic outputs without promoting an exploratory model to product qualification. [MAT precision diagnostic](rbf-full-precision.json) still rejects Blue Acrylic.
- [Previous-binary Grid/9 capture](legacy-grid9-capture/capture.json) records the independently frozen pre-change Core/Application and pure synthetic fixture hashes. Current replay is part of the focused regression.

Log interpolation is opt-in, scale 1e-6 sr^-1 for all five Cornell materials in this evidence. The scale is an interpolation convention, not measurement uncertainty. Original measured knots/amplitudes are retained. Only the original eight incidence nodes at 550 nm and measured outgoing polar support are qualified here, not all 31 wavelengths or unmeasured directions. Grid/9 linear behavior and its previous nonpassive verdicts remain unchanged.

The absolute-XYZ RBF model uses finite declared strength/color domains, training-only nodes/scales, fixed observer/illuminant and a positive response offset. It is not homogeneous under arbitrary XYZ brightness scaling. Original 959/480 development split and 1% NRMSE / 0.01 sr^-1 maximum absolute gates remain; repeated model exploration is not a new blind test. Published original Cornell XYZ observer identity remains unverified. Low-signal relative errors are retained in each report.

The measured chains use fixed source power 5e-12 W, gain 2 DN/e, 10 ms, analytic QE and no sensor noise. These are validation scene/sensor controls, not altered measurement/calibration values. Every measured chain has 32,768 independent direction samples and 8,192 actual NSC rays. VIAVI data retain all 31,046 angular nodes and the previous explicitly declared wavelengths/back-side assumptions: front normal incidence is measured; the declared identical back side is not independently measured.

Final execution uses task-owned copies under `tools/diagnostics/XyzReconstruction20261101/bin/FrozenReconstruction20261101`, external to the evidence manifest. Nine critical execution assemblies are bound by before/after hashes; 164 source/input snapshots are in this manifest, and 94 source documents across six PDBs match their actual compilation checksums. Subsequent shared-bin changes are not the executed bytes. Full default Release App including both built-in modules completed with zero warnings/errors before copying.

Five earlier evidence packages, totaling 1,694 frozen files, are verified read-only. Their documentation-at-seal hashes are historical identities, not a request to overwrite current maintained docs. No current manifest rewrites earlier data.

Earlier attempts are retained: a zero-match test launched before its build finished, default sandbox testhost connection abort, one PowerShell logger argument split, initial original-incidence rounding mismatch, sandbox temporary ray-database publication denial, and the deliberately strict synthetic-offset test failure before its analytical reference explicitly accounted for the model's declared positive bias. None are counted as successful validation. Automatic approval review for an early background diagnostic timed out; the subsequent authorized background execution succeeded. No native GUI or desktop control was performed.

Reproduction requires .NET 10; Python/SciPy is used only for independent offline diagnostics. Product computation is pure managed C# through shared Core. `prepare-isolation.ps1` creates a new execution/source snapshot and refuses to overwrite it; `run-isolated.ps1` runs the scoped tests and diagnostics; `verify-final.ps1` checks final counts, gates, compiled source, execution hashes and previous evidence. Sealed evidence must not be regenerated in place: use a new output directory and identities. [Seal](evidence-manifest.json) lists every retained evidence file by size and SHA256.
