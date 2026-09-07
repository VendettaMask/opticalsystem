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

On 2026-09-07 the complete laboratory suite passes 42/42 after adding the independent flat-start P0/P1 path; all ten old gates remain unchanged. `flat-start-v1/specs` and `protocol.json` were committed before the new solver and freeze twelve future full-target cases, five seeds per case, and explicit pass criteria. Those full-target runs have not been executed. `flat-start-v1/bootstrap-results.json` records only the 1/2/3-element axial, primary-wavelength, 30%-pupil startup proofs and their final prescriptions. It does not satisfy or replace the full-target protocol. See [P0/P1 evidence](../../../docs/INITIAL_STRUCTURE_P1_2026-09-07.md).
