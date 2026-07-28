# Large-scale ray tracing performance

## Scope

The first performance phase keeps the public scalar backend, legacy tracing calls, plugin contracts, file formats, and .NET 10 target. It adds a managed CPU bulk path without introducing GPU, automatic-differentiation, or benchmark-framework dependencies.

Correctness is a prerequisite for the fast path:

- interaction results explicitly report transmission, ordinary reflection, or total internal reflection;
- only transmission moves the ray into `MaterialAfter`;
- ordinary reflection and total internal reflection retain `MaterialBefore`;
- thin-lens output is normalized before the next surface;
- path length, OPL, absorption, and subsequent refraction therefore use the correct direction magnitude and medium.

## Retention API

Use `TraceRequest` when the consumer does not require every surface:

```csharp
using var finalTrace = tracer.Trace(bundle, TraceRequest.FinalOnly());
using var stopTrace = tracer.Trace(
    bundle,
    TraceRequest.Selected(new[] { stopSurfaceIndex }));
using var history = tracer.Trace(bundle, TraceRequest.FullHistory());
```

The modes are:

| Mode | Retained samples | Typical consumers |
| --- | --- | --- |
| `FinalOnly` | Final surface only | PSF, MTF, spot, irradiance, optimization operands |
| `SelectedSurfaces` | Explicit surface indices | Footprints, pupil/wavefront planes, targeted diagnostics |
| `FullHistory` | Every surface | Single-ray reports, layout rendering, Jones propagation |

`Trace()` and `TraceFinalSamples()` remain compatibility wrappers. Surface recording is opt-in on the request; a final-only or selected-surface trace does not mutate `SurfaceGroup.RecordedTrace`.

`RequestedTrace` is disposable because it rents storage from `ArrayPool<T>`. Its surface and ray views share one flat sample buffer, and both views become invalid after disposal. Copy or materialize samples before leaving the owning `using` scope if they must outlive the result.

Memory for retained results is `O(ray count × retained surface count)`. Full history retains one flat backing store instead of parallel ray-history and surface-history object graphs.

## Execution model

The tracer snapshots surfaces into a read-only context and uses a surface-major loop. Current state is stored in pooled SoA arrays:

- origin and direction components;
- wavelength and intensity;
- geometric path, OPL, and OPD;
- polarization;
- active/vignetted and normalized flags;
- current material.

Small bundles run serially. Larger bundles are divided into deterministic ray-index ranges and processed with `Parallel.ForEach`; results are always written at the original ray index. The final OPD reference is reduced in ray-index order and applied in place, so serial and parallel modes use the same ordering.

`TraceRequest` exposes the parallel threshold, maximum degree of parallelism, and batched-backend switch. Parallel optimization Jacobians and outer Monte Carlo trials suppress nested tracing parallelism to avoid oversubscription.

## Batched backend and SIMD

`IBatchedNumericBackend` is optional. The managed implementation vectorizes:

- direction normalization;
- homogeneous propagation;
- plane and standard/conic intersection;
- circular-aperture tests;
- refraction;
- ordinary reflection;
- total internal reflection.

The implementation uses `System.Numerics.Vector<double>` and handles a non-vector-width tail. Common homogeneous, centered sequential surfaces use this path. Unsupported geometry, GRIN propagation, custom apertures, complex coatings, scattering, or plugin components fall back to scalar state tracing for that batch.

Existing `INumericBackend` implementations remain valid. `NumericBackendProvider` supplies a cached scalar batch adapter when a backend does not implement `IBatchedNumericBackend`.

## Analysis and tolerancing use

Core PSF/MTF sampling, spots, wavefronts, radiometry, field analyses, image simulation, and optimization operands request only their final or targeted surfaces. Full history remains limited to consumers that use every interaction, notably single-ray diagnostics, layout display, and Jones-pupil accumulation.

Monte Carlo creates an independent `Optic` for every trial from the nominal snapshot. A trial seed is derived from the global seed and trial number, and results are stored by trial number. This makes the trial sequence independent of scheduler order and maximum parallelism.

## Verification

The regression suite compares:

- serial and parallel tracing;
- scalar and SIMD backends;
- final-only, selected-surface, and full-history retention;
- position, direction, intensity, OPL, OPD, and vignetting;
- total internal reflection and ordinary-reflection material/absorption state;
- thin-lens OPL;
- early termination, non-finite object distance, cancellation, and exceptional surfaces;
- Monte Carlo sequences across seeds and parallelism levels.

The validated 2026-07-28 baseline is 532 passing tests with a zero-warning solution build.

## Benchmark

Run the built-in benchmark:

```bash
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj
```

By default it measures 10,000 and 100,000 rays through 20 surfaces for final-only, selected-surface, full-history, PSF/MTF-related sampling, and 100 Monte Carlo trials. It reports elapsed milliseconds, rays per second, allocated bytes, managed heap size, and peak working set.

Treat timing as a local comparison rather than a CI contract. Allocation and retained-buffer dimensions are the stable architectural checks: final-only and selected-surface storage scale with the number of retained surfaces, while full history intentionally scales with all surfaces.

## Deferred work

GPU execution is a later backend phase after the CPU batch interface and benchmarks stabilize. CUDA, DirectML, and cross-platform compute choices are not committed yet. Automatic differentiation will require a separate parameter/state derivative model and is not coupled to this refactor.
