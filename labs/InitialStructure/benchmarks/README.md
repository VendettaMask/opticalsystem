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

On 2026-09-07 P0/P1 passed the then-complete 42/42 laboratory suite, followed by P2 at 63/63. P3 now passes **91/91**, with a final 16/16 regression subset after near-duplicate filtering was tightened; the subset is not an additional test count. All ten legacy gates remain unchanged. `flat-start-v1/specs` and `protocol.json` were frozen before the new solver and still define twelve full-target cases and five seeds per case. P3 runs seed 101 with the original 10,000-evaluation budgets and real catalog-material/surface-stop families: **4/12 specifications produce accepted candidates**, with eight remaining unsuccessful. See `flat-start-v1/family-search-results.json` for actual per-field values, best prescriptions, lineage and evidence hashes. Some image-quality results are worse than P2; increased candidate diversity is not a universal quality improvement. This is not the sixty-run release gate, which remains pending.

`flat-start-v1/design-results.json` preserves the P2 fixed-family 4/12 survey. `flat-start-v1/bootstrap-results.json` preserves the original v1 axial/primary/30%-pupil proof; current P1/P2/P3 optical metrics all come from formal Core. See [P3 evidence](../../../docs/INITIAL_STRUCTURE_P3_2026-09-07.md), [P2 evidence](../../../docs/INITIAL_STRUCTURE_P2_2026-09-07.md) and the [P1 historical record](../../../docs/INITIAL_STRUCTURE_P1_2026-09-07.md).
