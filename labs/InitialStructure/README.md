# Initial Structure Lab

Development follows the [2026-09-07 flat-start proposal](../../docs/INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md). P0 freezes twelve future full-target specifications; P1 implements the independent `strict-flat-bootstrap/v1` engine, starting at exact zero curvature with target-derived entrance pupil and finite optical-power/real-ray residuals. The desktop still uses legacy version 3: full-target continuation, new candidate search and the new UI are not implemented. See [P0/P1 results and replay instructions](../../docs/INITIAL_STRUCTURE_P1_2026-09-07.md).

This directory contains the isolated experimental implementation of the intelligent initial-structure generator. Its assemblies remain outside the formal product solution, runtime, installer, compatibility surface, and release baseline. The formal desktop has a launch-only entry under **实验室 → AI 初始结构**; it starts this independent application in a separate process.

Current verification (2026-09-07, P0/P1): the complete laboratory suite passes **42/42**, with no failures/skips; the new bootstrap filter contributes **18/18**. Default Debug/Release builds have zero warnings/errors. All ten legacy specifications pass unchanged gates. The new 1/2/3-element startup proofs pass independent thick-lens and meridional Snell checks. They use an axial primary wavelength and 30% target pupil, not full-target acceptance. Evidence is in `artifacts/validation/initial-structure-p1-20260907`. Existing restored dependencies were used; no new online vulnerability audit was completed. Earlier restoration-only 24/24 results are historical.

Launch-entry verification (2026-09-07): the formal App's launcher/dependency/layering filter passes **33/33**, and the laboratory's unchanged project-isolation/responsive-source filter passes **2/2**. A separate Windows startup smoke check created the **智能初始结构实验室** window and closed only that owned process. The formal and laboratory solutions build independently in Debug and Release with zero warnings/errors. No search algorithm, frozen specification, installer or numerical test baseline changed; the full laboratory suite was not rerun for this launch-only change.

## Current implementation

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

Run data is written below the current user's local application-data directory at `OpticalSystemDesign/Labs/InitialStructure/runs`; resumable seed checkpoints use the sibling `checkpoints` directory. Each run ID includes UTC time, the specification fingerprint, and random entropy. A run is assembled in a hidden sibling staging directory and published only after every candidate and the manifest are complete; published run directories are immutable. The App exports only the explicitly selected candidate, writes through the Core STAROPT atomic store, reopens it, and verifies the optical snapshot before reporting success. Export does not modify the currently open formal workbench.

Implemented laboratory safety limits are 10,000 initial seeds, 100,000 evaluations, 256 workers, 64 wavelengths, 128 glass catalogs, a 24-hour run, an 89-degree maximum field angle, a 64 MiB manifest, and a 4 MiB candidate snapshot. Specification validation rejects non-finite or overflowing track and aperture calculations before allocation. Manifest and candidate serialization enforce the limits while writing; loading revalidates the exact sidecar set, canonical hashes, optical snapshots, evaluations, and lineage. These are current executable constraints, not future search capability claims.

See [the development plan](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md) for scope, isolation rules, acceptance gates, and later phases.
