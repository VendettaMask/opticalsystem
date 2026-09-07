# Initial Structure Lab frozen specifications

This directory contains versioned, deterministic input specifications for the
experimental flat-to-usable search. They are repository regression inputs, not
commercial-software reference prescriptions and not universal optical-design
acceptance limits.

The committed checks require every specification to remain valid and uniquely
fingerprinted. Search regression tests cover deterministic lineage, bounded
evaluation use, second-generation refinement, and dense-validation status. The
`accepted-baselines` summary records minimum observed family counts for a named
engine version; improvements may exceed those minima, while regressions must be
reviewed instead of silently rewriting the baseline.

On 2026-09-05 algorithm version 3 passed all ten unchanged frozen gates as part of the complete 24/24 laboratory suite. Search fitness now uses density 2 consistently for parents and trials; acceptance still uses density 4. The historical minimum-result files were not rewritten.

On 2026-09-07 P0/P1 passed the then-complete 42/42 laboratory suite. The later P2 suite passes **63/63**, with all ten legacy gates unchanged. `flat-start-v1/specs` and `protocol.json` were frozen before the new solver and still define twelve full-target cases and five seeds per case. P2 runs one fixed element/material/stop family per case: **4/12 meet every target**, while eight report startup or full-target failures. See `flat-start-v1/design-results.json` for exact metrics, failures, prescriptions and evidence hashes. This is not the sixty-run multi-seed release gate, which remains pending. `flat-start-v1/bootstrap-results.json` preserves the original v1 axial/primary/30%-pupil proof; current v2 obtains its metrics through formal Core and counts only actually traced real rays. See [P2 evidence](../../../docs/INITIAL_STRUCTURE_P2_2026-09-07.md) and the [P1 historical record](../../../docs/INITIAL_STRUCTURE_P1_2026-09-07.md).
