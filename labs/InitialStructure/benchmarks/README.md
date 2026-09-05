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
