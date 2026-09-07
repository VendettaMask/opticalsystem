# Frozen flat-start acceptance runner

This independent C# command exercises `strict-flat-family-search/v4` through the formal Core. It has no product runtime entry or external numerical engine dependency.

From the repository root:

```powershell
dotnet restore labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx --locked-mode
dotnet run -c Release --no-restore --project labs/InitialStructure/tools/OptilandWorkbench.InitialStructure.Benchmarks -- labs/InitialStructure/benchmarks/flat-start-v1 artifacts/flat-start-acceptance/new-run
```

The output directory must be empty or new. The full command runs all twelve specifications with seeds 101–105, sequentially, under their frozen 10,000-evaluation/600-second budgets. For a diagnostic subset, append an exact filename and seed, for example `06-150mm-visible.json 101`. A subset cannot pass the full search gate.

`FrozenInputs.json` is embedded in the executable and checks the original protocol/specification content after CRLF-to-LF normalization. Inputs, tolerances, seed choices and protocol sampling cannot be changed under the same acceptance identity. Raw input and binary SHA-256 values are also recorded in `summary.json`.

Each completed run stores a checksummed checkpoint, a selected prescription and a STAROPT export. All distinct candidates claiming acceptance are recalculated from new Optic instances with the full six-field/97-pupil-point grid; mismatched acceptance or a failed trial prevents success. Export reload requires exact snapshot identity and identical spot metrics. These independent checks run outside the search budget, with their ray count and elapsed time reported separately.

`summary.json` is replaced atomically after each completed run. It records every run, first completed batch reporting acceptance, actual search ray count/time, accepted family count, per-field/per-wavelength gaps, failed trials and export reload error. Ctrl+C finishes the current bounded search batch and preserves the resulting completed records; a partial run cannot pass. Use the desktop/checkpoint API to resume a saved search; this command does not aggregate separately run subsets into a release result.

A full **search gate** requires at least ten specifications with at least four successful seeds each. Exit zero means that gate passed; exit 2 means it did not pass, including successful diagnostic subsets. Exceptions also return nonzero: inspect the summary/error to distinguish them. `P5ReleaseAccepted` stays false because the runner cannot certify the separate historical-prototype comparison, external Zemax evidence or engineering/UI checks.

Use the same machine and report background load when comparing time. Dense refinement and v3 aperture diagnostics trace additional rays per evaluation; every diagnostic ray is counted. v4 changes polychromatic continuation to three full-pupil, all-wavelength field stages without changing the final sampling or gates. Evaluation counts alone do not establish a speed improvement. The original v1–v3 evidence remains frozen; current results and limitations are recorded in [the aperture recovery report](../../../../docs/INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md).
