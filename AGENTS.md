# Project collaboration instructions

## Documentation synchronization

- Every completed code change must update the relevant repository documentation in the same task.
- Documentation must distinguish implemented behavior from planned or compatibility-only behavior.
- When verification changes the repository test baseline, update every document that states the current build date or passing-test count.
- A change is not complete until code, tests, documentation, and the reported verification result agree.

## Desktop build output locking

- If a running `Optical System Design` process locks the default App build output, close that specific application process and rerun the build or test against the default output directory so the runnable desktop binaries are updated. Do not work around the lock by treating an alternate output directory as the completed verification.

## Accuracy reference priority

- The default precision authority is the committed Zemax OpticStudio 2026 R1 baseline for `123456.ZMX` under `artifacts/zemax/123456-zemax-2026-r1-baseline`.
- That baseline validates only the captured file, analysis settings, and OpticStudio version. Never present those captured settings as universal Zemax defaults or specifications.
- Dispatch analysis result controls by `AnalysisPresentationKind`; localized names, visible titles, and imported labels are presentation text and must not select a control implementation.
- Treat `AnalysisAxisQuantity` and `AnalysisAxisUnit` as the authority for plot scaling, value formatting, and data export. Axis-label strings are presentation text and must never be parsed to determine a quantity or unit; every non-empty analysis axis must publish typed metadata.
- Treat scene ray segments as directed propagation data. Preserve the traced `RayInteractionKind`, publish typed segment and surface-interaction metadata through scene DTOs, and orient viewer arrows from the explicit direction vector; never infer reflection, transmission, diffraction, or TIR from point order, Z-axis order, labels, or colors.
- Share ray-trace results only through a bounded cache keyed by optic revision, numeric backend, exact input-ray state, retained surface indices, and result-affecting request options. Equivalent retention modes may share when they resolve to the same surfaces; analysis names and localized labels must never participate in cache identity. Any trace-relevant mutation of an analysis snapshot must detach that snapshot from the shared cache before further tracing.
- Treat analysis viewport, pan/zoom, hover, and surface-camera state as document-revision state. Reset interactive analysis controls when their displayed source revision becomes stale, and clear the old result content immediately on a file switch even when the analysis page is locked.
- Treat only a positive-infinite Object-surface thickness as an infinite object conjugate. A zero Object thickness is a finite object coincident with the first physical surface; never infer conjugate type from coordinates, labels, field type, or a small-distance tolerance.
- In RMS-vs-field Spot mode, sample `Field Density + 1` positions from zero through the maximum defined field magnitude along the selected orientation; do not substitute the discrete Field Editor rows. For wavefront RMS, chief-ray reference removes piston only, while centroid reference removes the weighted best-fit piston and both pupil tilts.
- Core analysis constructors and `AnalysisCatalog` must keep general-purpose defaults. Application presets may choose product defaults, while baseline tests must pass captured settings explicitly and name them as captured-file settings.
- Optiland 0.5.8 Cooke/Tessar golden tests are auxiliary regressions, not the primary accuracy conclusion, unless the user explicitly requests that reference.
- Reports must distinguish Zemax baseline integrity, current Workbench recalculation, numerical comparison, and screenshot-only review.
