# Initial Structure regression inputs and historical records

Current product work follows managed AutoLens and native retrieval/topology/SPC, as described in the [current scope](../../../docs/INITIAL_STRUCTURE_LAB_PLAN.md). This directory retains specifications and fixed inputs still consumed by regression tests, together with historical results and diagnostic source. September search results are not the current product acceptance gate.

On 2026-11-08, obsolete local S4/DSearch bulk restart files, hybrid full candidate histories and legacy execution/preview binaries were retired at the user's request. Per-seed summaries, selected exports, failure/interruption records, sources and current regression inputs remain. Historical console projects and audit scripts are preserved as source records; their former local binary/checkpoint defaults are no longer maintained replay commands. Do not rebuild the retired storage trees to satisfy a historical path or hash listing. The [retirement ledger](../../../validation/rechecks/2026-11-08/legacy-task-retirement/summary.json) records the deletion and verification scope. Product support for viewing/exporting historical saved records is unchanged.

## Historical protocol and results

The historical v4 sixty-run result recorded 52 successes and 9/12 qualifying specifications. The abandoned v5 weight experiments did not count toward that gate. The [automatic strategy revision](../../../docs/INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md), [S1 solver report](../../../docs/INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md) and [P5 aperture report](../../../docs/INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md) describe their original stages, not present-day pending integration or release requirements. The former full protocol assessed a fixed strategy, not a manual parameter-tuning loop.

P5 recorded input fingerprints, independent candidate revalidation and STAROPT reload checks. Search v3 added Core aperture diagnostics and the specification's throughput gate for incumbent retention; v4 used three full-pupil, all-wavelength field stages. Current benchmark-tool behavior is described in its [maintained guide](../tools/OpticalSystemDesign.InitialStructure.Benchmarks/README.md); historical acceptance counts below retain their original scope.

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

On 2026-09-07 P0/P1 passed the then-complete 42/42 laboratory suite, followed by P2 at 63/63 and P3 at 91/91. At P4, verification was **104/104**, plus a final **14/14** relevant replay after the numeric-input correction (already included in that total). All ten legacy gates remain unchanged. `flat-start-v1/specs` and `protocol.json` were frozen before the new solver and still define twelve full-target cases and five seeds per case. The P3 survey uses seed 101, the original 10,000-evaluation budgets and real catalog-material/surface-stop families: **4/12 specifications produce accepted candidates**, with eight remaining unsuccessful. See `flat-start-v1/family-search-results.json` for actual per-field values, best prescriptions, lineage and evidence hashes. P4 adds the independent UI and source-preserving refinement budget; it does not revise that survey or its targets. Some image-quality results are worse than P2; increased candidate diversity is not a universal quality improvement. This is not the sixty-run release gate, which remains pending. See [P4 verification](../../../docs/INITIAL_STRUCTURE_P4_2026-09-07.md).

`flat-start-v1/design-results.json` preserves the P2 fixed-family 4/12 survey. `flat-start-v1/bootstrap-results.json` preserves the original v1 axial/primary/30%-pupil proof; current P1/P2/P3 optical metrics all come from formal Core. See [P3 evidence](../../../docs/INITIAL_STRUCTURE_P3_2026-09-07.md), [P2 evidence](../../../docs/INITIAL_STRUCTURE_P2_2026-09-07.md) and the [P1 historical record](../../../docs/INITIAL_STRUCTURE_P1_2026-09-07.md).
