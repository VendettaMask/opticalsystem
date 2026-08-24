# Initial Structure Lab

This directory contains the isolated experimental implementation of the intelligent initial-structure generator. It is not part of the formal product solution, runtime, installer, compatibility surface, or release baseline.

## Current implementation

- versioned specification, run, candidate, evaluation, violation, and lineage contracts;
- exact plane-parallel roots with curvature `c = 0` and complete root snapshots;
- deterministic first-order power expansion using curvature variables;
- paraxial focal-length recovery and low-density real-ray reachability checks;
- cancellable CPU-parallel seed evaluation;
- atomic JSON run and candidate persistence in a separate data directory;
- standalone Avalonia laboratory application;
- architecture, reproducibility, snapshot-isolation, cancellation, and persistence tests.

The current engine is a feasibility prototype. It does not yet implement differential evolution, CMA-ES, staged real-ray optimization, discrete glass search, candidate-family clustering, SQLite indexing, database retrieval, machine learning, or a design agent. It never publishes `LabAccepted` candidates.

## Build and test

```bash
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj --no-restore /m:1 /nr:false
```

## Run

```bash
dotnet run --project labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/OptilandWorkbench.InitialStructure.App.csproj
```

Run data is written below the current user's local application-data directory at `OpticalSystemDesign/Labs/InitialStructure/runs`. No result enters the formal workbench unless a future explicit, validated export path is implemented.

See [the development plan](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md) for scope, isolation rules, acceptance gates, and later phases.
