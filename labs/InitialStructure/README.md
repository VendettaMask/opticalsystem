# Initial Structure Lab

Development follows the [2026-09-07 flat-start proposal](../../docs/INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md). P0 freezes twelve full-target specifications; P1/P2 provide strict-flat startup and full-target continuation through formal Core. P3 adds `FlatStartSearchService.RunAsync` (`strict-flat-family-search/v1`): real catalog materials, discrete surface stops, element-count families, parent-linked refinement, diversity filtering and budget-preserving checkpoints. P4 connects this engine to the independent desktop with formal layout/rays, target tables, resume, selected-candidate refinement and validated export. The P3 survey remains four accepted specifications out of twelve; eight retain explicit gaps. The P5 release gate remains pending. See [P4 workflow and verification](../../docs/INITIAL_STRUCTURE_P4_2026-09-07.md) and [P3 numerical results](../../docs/INITIAL_STRUCTURE_P3_2026-09-07.md).

This directory contains the isolated experimental implementation of the intelligent initial-structure generator. Its assemblies remain outside the formal product solution, runtime, installer, compatibility surface, and release baseline. The formal desktop has a launch-only entry under **实验室 → AI 初始结构**; it starts this independent application in a separate process.

Mandatory shared computation boundary (user clarification, 2026-09-07): every optical trace and analysis calls the formal product's shared Core. Laboratory isolation applies to features, search orchestration, processes, and data, never a separate optical engine. P1/P2 now call formal `SpotMetricEvaluator.EvaluatePupilSamples` and `Paraxial.EstimateOpticalPower`, with identical snapshot/settings checks. Missing capabilities belong in the formal shared layer, with no laboratory formula copies or approximation fallbacks. Independent analytical/Snell code remains test-only and cannot supply runtime results. P2 adds no separate tracer or optical analysis engine.

Current verification (2026-09-07, P4): the complete laboratory suite passes **104/104**, with no failures/skips (previous 91 plus 13 cases). After the final numeric-input correction, the relevant **14/14** subset passes on the final Release code; it is included in the 104 cases, not an additional total. The solver did not change during the full run. Default Debug/Release builds have zero warnings/errors; formatting and diff checks pass. Actual Avalonia mouse/keyboard interactions, Skia renders at 1240/600/480 px, formal-scene equality, cancellation/export/resume, source-preserving additional refinement and stale-scene clearing are verified. A separate native Windows launch/close check passes; this is not native manual or cross-platform UI certification. Evidence: `artifacts/validation/initial-structure-p4-20260907`. All ten legacy gates and the twelve full-target specifications remain unchanged. The P3 seed-101 survey is not the sixty-run release protocol or new-candidate external certification. Formal Core was unchanged and formal tests were not rerun for P4; the formal 46/46 and laboratory 63/63 counts belong to P2. Existing cached dependencies were used; no new online vulnerability audit was completed.

The P3 API accepts `FlatStartSearchOptions` and a separate `FlatStartSearchCheckpoint`; `FlatStartSearchCheckpointStore` saves checksummed atomic JSON. It supports at most 128 initial families, 256 trials, four simultaneous root workers, 64 allowed material names and a 64 MiB checkpoint. Cancellation completes the current bounded batch. Resume preserves consumed budget; uncertain in-flight work keeps its whole reserved quota and reserved wall-time charge. This does not restore an unfinished internal Jacobian. Independent stop surfaces inserted into air gaps remain a later extension.

Launch-entry verification (2026-09-07): the formal App's launcher/dependency/layering filter passes **33/33**, and the laboratory's unchanged project-isolation/responsive-source filter passes **2/2**. A separate Windows startup smoke check created the **智能初始结构实验室** window and closed only that owned process. The formal and laboratory solutions build independently in Debug and Release with zero warnings/errors. No search algorithm, frozen specification, installer or numerical test baseline changed; the full laboratory suite was not rerun for this launch-only change.

## Current desktop workflow

Enter focal length, F/# or entrance-pupil diameter, maximum half-field angle, an element-count range, maximum track and editable wavelengths/weights/primary selection. Advanced inputs expose geometry limits, permitted catalog glasses, full quality gates and budgets. Invalid or fractional count inputs are rejected; invalid numeric edits remain visible after focus changes rather than silently reverting to an old target.

Generate, stop/save and resume use the P3 engine. Inputs lock during a run, and editing parameters clears old candidate tables and scenes immediately. Candidate details show full targets, prescription, per-field/per-wavelength transmission and original flat-root provenance. `Layout2DBuilder` supplies all lens geometry and directed ray segments; A/B uses one physical scale. Layout requests explicitly use the primary wavelength, all defined fields and seven pupil samples per field across [-1, 1], retaining truncated vignetted paths. This is a display request, not a replacement for dense final analysis.

Selected refinement creates a new `strict-flat-selected-refinement/v1` run with an explicit additional evaluation budget and the source time limit. `Origin` preserves the source run, charged work, candidate and original strict-flat proof. Targets and material contents remain unchanged, the source file is not rewritten, and the source candidate stays available if refinement does not improve it. Resume restores the current run's remaining budget; it never silently adds more work.

## Historical v3 implementation

The following describes the retained legacy engine and its historical validation; the current desktop uses the workflow above.

- versioned specification, run, candidate, evaluation, violation, and lineage contracts;
- exact plane-parallel roots with curvature `c = 0` and complete root snapshots;
- deterministic first-order power expansion using curvature variables;
- paraxial focal-length recovery plus deterministic multi-wavelength real-ray reachability and spot checks;
- specification-level RMS and maximum spot-radius gates, with `Refinable` reserved for candidates that satisfy focal, F/#, trace, and image-quality limits;
- cancellable CPU-parallel seed evaluation;
- bounded curvature, center-thickness, and air-gap parameterization followed by deterministic differential evolution, budget-dependent damped least-squares refinement, and a separate density-4 acceptance evaluation;
- structural-family quotas keyed by element count and stop placement, followed by exact optic-fingerprint deduplication;
- strict `MaximumEvaluations` enforcement across seed, global, local, and dense-validation evaluations, including exact-budget boundary handling and a structured consumption diagnostic;
- bounded JSON run and candidate persistence through a complete staging tree and one immutable directory publish, with canonical-hash and exact-sidecar-set validation on load;
- atomic seed-stage checkpoints that preserve run identity and deterministically skip completed seeds on resume; interruption during refinement restarts the deterministic refinement from the completed-seed checkpoint;
- ten frozen synthetic benchmark specifications plus a versioned minimum-result baseline;
- standalone Avalonia laboratory application with structured preflight, responsive table/geometry layout, A/B candidate comparison, resume/cancel controls, and validated STAROPT export;
- architecture, reproducibility, snapshot-isolation, cancellation, and persistence tests.

The L3 engine gate and L4 core desktop workflow are implemented. `LabAccepted` is published only by the separate dense-validation request after every structured constraint passes; it means the candidate meets the frozen laboratory initial-structure threshold, not that the lens is a finished or manufacturable design. The engine does not yet implement CMA-ES, discrete glass search, optical-distance/Pareto clustering within a structural family, SQLite indexing, database retrieval, machine learning, or a design agent. Checkpoints resume completed seeds, not an in-progress differential-evolution population.

On 2026-08-30, the four-test L3 contract filter passed `4/4`, the separate frozen-benchmark gate passed `1/1` in about four seconds, and the L4 checkpoint/export/responsive-source filter passed `4/4`. The final combined key-path replay passed `5/5`, covering the frozen gate, dense acceptance, deterministic checkpoint resume, validated STAROPT export, and responsive accessible App source. Eight of ten frozen specifications produced three distinct accepted element/stop families; the 85 mm and 100 mm specifications produced two each. The laboratory solution build and format verification passed with `0` warnings and `0` errors, and the online direct/transitive dependency audit reported no known vulnerable package. A wide macOS screenshot showed no overlap; the unpackaged Avalonia process was not discoverable by the Computer Use accessibility driver, so automated narrow-window interaction remains a cross-platform UI follow-up. These targeted runs overlap, are not a full-suite total, and remain independent of the formal product baseline.

Repository CI now restores, builds, tests, and verifies formatting for this isolated solution on Linux, macOS, and Windows. This does not merge the laboratory into the formal product solution or release baseline.

## Build and test

```bash
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj --no-restore /m:1 /nr:false
```

## Run

Build the laboratory separately, then use the desktop entry or the command below. In a source checkout, the entry locates this App's `bin/Debug/net10.0` or `bin/Release/net10.0` output to match the formal desktop build. It never builds the laboratory automatically or substitutes the other configuration's binaries.

```bash
dotnet run --project labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/OptilandWorkbench.InitialStructure.App.csproj
```

For a separately deployed laboratory, set `OPTILAND_INITIAL_STRUCTURE_LAB_PATH` to the absolute path of its executable (or its `.dll`, requiring the .NET runtime), or place its complete independent publish output under the formal desktop's `labs/InitialStructure/` directory. The standard installer is unchanged and does not bundle it. Missing laboratory files produce a normal launch error; opening the formal desktop does not require the laboratory. Paths are passed directly to the process API, without a command shell. No current document, optimization state or application settings are sent to the laboratory.

The entry's “AI” label names the experimental feature requested by the user; this change adds no model, training, retrieval or agent implementation. The implemented generator remains the deterministic optical search described above.

Current desktop data is written below the user's local application-data directory at `OpticalSystemDesign/Labs/InitialStructure/flat-start/runs`. Each unique run has a checksummed atomic `<run-id>.family.json` file; `last-run.json` is the bounded last-run pointer. Open a saved record to inspect it, then explicitly resume if budget remains. Legacy `runs` and `checkpoints` directories stay separate; the new desktop does not reinterpret v3 records as P3 runs. The App exports only the selected candidate through the Core STAROPT store and verifies the exact snapshot after reopening it. Export remains available after stopping a search and does not modify the currently open formal workbench.

Shared legacy specification limits include 10,000 initial seeds, 100,000 evaluations, 256 workers, 64 wavelengths, 128 glass catalogs, a 24-hour run, an 89-degree maximum field angle, a 64 MiB manifest, and a 4 MiB candidate snapshot. The current P3/P4 path additionally restricts initial families to 128 and workers to four. Validation rejects non-finite or overflowing track and aperture calculations before allocation. These are executable bounds, not guarantees that arbitrary requested optics can be generated.

See [the development plan](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md) for scope, isolation rules, acceptance gates, and later phases.
