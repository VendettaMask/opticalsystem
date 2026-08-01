# Project collaboration instructions

## Documentation synchronization

- Every completed code change must update the relevant repository documentation in the same task.
- Documentation must distinguish implemented behavior from planned or compatibility-only behavior.
- When verification changes the repository test baseline, update every document that states the current build date or passing-test count.
- A change is not complete until code, tests, documentation, and the reported verification result agree.

## Accuracy reference priority

- The default precision authority is the committed Zemax OpticStudio 2026 R1 baseline for `123456.ZMX` under `artifacts/zemax/123456-zemax-2026-r1-baseline`.
- That baseline validates only the captured file, analysis settings, and OpticStudio version. Never present those captured settings as universal Zemax defaults or specifications.
- Core analysis constructors and `AnalysisCatalog` must keep general-purpose defaults. Application presets may choose product defaults, while baseline tests must pass captured settings explicitly and name them as captured-file settings.
- Optiland 0.5.8 Cooke/Tessar golden tests are auxiliary regressions, not the primary accuracy conclusion, unless the user explicitly requests that reference.
- Reports must distinguish Zemax baseline integrity, current Workbench recalculation, numerical comparison, and screenshot-only review.
