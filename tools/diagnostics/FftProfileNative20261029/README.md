# Background afocal native investigation (2026-10-29)

The remaining-frequency investigation permits explicit `budgetSeconds`, bounded to 120–600 seconds with the historical 120-second default. This changes execution time only, never comparison tolerances. Execution records include the budget, elapsed time and native-host hash. A `frozenHost` inside the repository requires a matching SHA256. Historical failures remain unchanged. Correction: the late live-buffer control actually completed successfully; the earlier narrative missed its final execution and buffers. The [direct-reference investigation](../../../docs/BINOCULAR_FOURIER_DIRECT_REFERENCE_2026-10-31.md) confirms direct captures with all three waiting modes and the frozen host, without claiming a proven underlying timeout trigger.

The current background runner holds an exclusive OS file handle at `artifacts/validation/.background-native-capture.lock` while executing each native job. Only participating diagnostic callers queue; existing user applications are untouched. `queueSeconds` is separate from the unchanged worker budget. OS handle cleanup releases the lease if its caller exits. Two simultaneous native callers were verified to execute without overlap. Host stage timelines and final `FftFieldCaptureAudit` distinguish transient null polls, completed direct buffers, genuine timeouts and jobs not started. No intermediate/live buffer is accepted as a final reference.

The [2026-10-30 repair](../../../docs/AFOCAL_FFT_SEAM_REPAIR_2026-10-30.md)
closes the original 335 FFT profile violations with unchanged requests and gates.
The older sections below retain their historical execution scope. The new
`02-tagged` / `02-tagged-reverse` controls assign one face group per original
triangle. `nsc-tagged-paths` reads those explicit diagnostic tags, not proprietary
internal IDs. Tagged batch paths match the original 66 paths bit for bit; the
separate single-ray analysis has small recorded interface rounding differences.
`diagnosticFlipTriangleWinding` applies only to the disposable control POB.

New offline modes are `--tagged-facets REPOSITORY CAPTURE OUTPUT_JSON`,
`--score-tags PREDICATE_JSON OUTPUT_JSON`, and `--pupil-projection REPOSITORY
OUTPUT_JSON`. Candidate scores and the standard median BVH are falsification
tools, never product dependencies or a fitted selection rule. `--gaussian-pairs`
also reports support changes relative to the original frozen complex fields.
`Seal-SeamRepair.ps1 -RepositoryRoot REPOSITORY` verifies the final five profiles,
original ray gates, both pupil supports, 82 distinct latest tests, 16 native
receipts, original assets, cleanup and six matching Core DLLs before sealing a
new byte-verified archive. It does not execute native tasks or tests. The full
native traversal and physical launch mapping remain explicitly unproven.

This independent C# diagnostic resumes the frozen binocular FFT cross-section
investigation. It is not a product dependency or an optical acceptance override.
Use only a licensed Windows OpticStudio 2026 R1 installation. All live workers use
the existing `ProcessIsolation` hidden-process/job-object implementation, with a
120-second limit per capture and at most 16 captures per manifest. Existing
interactive applications are neither attached nor terminated. Screenshots are
explicitly forbidden by this runner.

Prepare exact frozen inputs with the existing offline-only
[`Capture-Remaining.py`](../AfocalPsfContract20261028/Capture-Remaining.py).
Select the desired jobs into a new `jobs.json`; their request files and source
hashes must accompany it. This preparation script has no product runtime role.
The runner compiles the repository Framework host against the installed API
assemblies, never copying proprietary DLLs:

```powershell
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- REPOSITORY PREPARED_DIRECTORY INSTALLED_API_ROOT
```

Each job requires an unused destination and unchanged input SHA-256. Execution
receipts are saved incrementally. Source ZMX/POB files and existing references are
never overwritten. A valid native process exit is a capture success, not an
optical pass. The preparation-time `existing-evidence.json` retains its original
`nativeExecuted=false`; actual executed subsets are exclusively in `execution.json`.

`nsc-facet-paths` is a deliberately restricted native adapter for surface 6 of the
frozen binocular prescription. It obtains launches at surface 5, shifts their
local Z by that standard surface's thickness, and records actual NSC batch
segments. The two surfaces share orientation in this specific frozen input.
Typed API surface enums are used in every UI language. The records expose object
IDs and directions, **not internal triangle IDs**. Reflection normals can be
inferred independently; a nearest normal match must not be labelled an API facet
ID. The optional diagnostic glue control changes only the disposable in-memory
system, records both values, and never substitutes for the original request.

Two offline modes use the original frozen archive and the current shared Core:

```powershell
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --compare REPOSITORY CAPTURE_DIRECTORY NEW_OUTPUT_DIRECTORY
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --assess REPOSITORY ARTIFACT_VALIDATION_ROOT OUTPUT_JSON
```

`--compare` keeps the original five request fingerprints and tolerance ledgers.
The four outside-[-1,1] native pupil probes per configuration are explicitly
unavailable through Core's normalized-pupil API; they are never clamped or counted
as matching. Its exit code reports successful diagnostic execution; `violations`
in the summary determines pointwise success. `--assess` verifies the 20 completed
captures, reference selectors, frozen FFT channels and final pointwise results.
Its report deliberately retains `allProfilePointsPassed=false`.

The investigation found a real native/Core seam-plane difference, but the tested
normalized-edge/file-order hypothesis improved configuration 2 while regressing
configuration 1 and did not close the FFT residual. That product change was
reverted. The current deliverable changes only diagnostic tooling and status
documentation. See the [native report](../../../docs/AFOCAL_NATIVE_RESUME_2026-10-29.md).

The subsequent [grid investigation](../../../docs/AFOCAL_FFT_GRID_INVESTIGATION_2026-10-29.md)
retains **335 unresolved violations**, 10 valid native controls, 6 invalid results,
1 failed native attempt and 11 rejected Core experiments. None is a runtime
dependency or an accepted product change. New controls are compiled C# and run
with the same hidden, owned, bounded standalone workers.

Additional offline modes (each uses the shared Core):

```powershell
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --grid-diagnose REPOSITORY NEW_OUTPUT_DIRECTORY
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --gaussian-grid REPOSITORY GAUSSIAN_CAPTURE OUTPUT_JSON
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --pupil-lines REPOSITORY OUTPUT_JSON
```

The Gaussian inference subtracts a same-axis |k|=1 normalization reference;
center-normalized amplitude alone does not identify absolute pupil radius.
Earlier uncorrected/stale-binary results remain explicitly invalid in the evidence.
Neither inferred stretches nor native masks may supply product inputs.

`diagnosticPolygonTriangles` supports only `02` / `02-reverse` in the declared
binocular control. It creates a short, unique temporary POB filename, verifies
native readback and full physical paths, then removes its own file after closing
the owned application. The original installed polygons and source ZMX remain
unchanged. Record reversal changes configuration 2 seam paths, so identical object
sequences or segment counts cannot establish identical tracing.

`nsc-normals` copies the original group to an independent in-memory NSC system.
It requires `IsValid`, real segment data and a saved text result. Its direct
normals are not internal triangle IDs or full original sequential captures.

`OPTICAL_FFT_SOURCE_IMPORT=1` opts into a diagnostic reimport for binoculars only;
it cannot replace original frozen-snapshot acceptance. Leave it unset for final
baseline verification. `--assess` still belongs to the **earlier 20-capture stage**.
For this stage, run `Assess-GridInvestigation.ps1` with the restored isolated
checkout, then `Collect-GridEvidence.ps1`. The collector writes a separate,
byte-verified archive and deliberately retains `objectiveAchieved=false`.

The [subsequent rule audit](../../../docs/AFOCAL_FFT_RULES_2026-10-29.md)
still has 335 violations. Its additional offline modes are `--normal-formulas
REPOSITORY OUTPUT_JSON`, `--pupil-map REPOSITORY OUTPUT_JSON`, and
`--gaussian-pairs REPOSITORY CAPTURE_DIRECTORY OUTPUT_JSON`. The last mode reads
matching `binocular-c{1|2}-uniform-{Real|Imaginary}` and Gaussian directories.
The nonzero-field controls show that the inferred Gaussian **envelope** stretch
does not, by itself, prove the physical launch-coordinate mapping. Retain this
limitation despite the historical `inferredCoordinateStretch` JSON property name.
Do not use the inferred values as runtime coordinates or calibration constants.

The attempted independent single-facet adapter returned zero segments or native
ray errors, was rejected, and has been removed from the current host. Its source
and failures survive only as evidence. `Seal-RuleInvestigation.ps1` audits and
seals this separate stage after restoring the isolated Core and recording the
final five original requests. It does not rerun live native tasks or tests.

Current follow-up (2026-10-30): the accepted shared Core seam repair closes the
original **335** profile violations; the subsequent automatic-diameter repair
closes **four binocular grids**. The latest
[finite-probe repair](../../../docs/AFOCAL_RIFLESCOPE_PUPIL_REPAIR_2026-10-30.md)
also closes the final **two Riflescope grids**, yielding **20/20** original grids
pointwise within unchanged gates. That stage's focused test count is **156**.
The historical investigations above retain their own failed outcomes.
See the [current automatic-grid report](../../../docs/AFOCAL_AUTOMATIC_GRID_REPAIR_2026-10-30.md).

Offline follow-up modes, all using the shared Core and fresh outputs:

```powershell
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --automatic-grids REPOSITORY NEW_OUTPUT_DIRECTORY
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --working-pupils REPOSITORY OUTPUT_JSON
dotnet run --project tools/diagnostics/FftProfileNative20261029 -c Release -- --native-pupil-check REPOSITORY NINE_RAY_CAPTURE OUTPUT_JSON
```

`--automatic-grids` verifies frozen references, replays all original 20 grid
requests and applies unchanged formal alignment and pointwise gates. Inferred
diameters are diagnostic only. `--working-pupils` distinguishes non-applicable
mixed-NSC paraxial compatibility values. `--native-pupil-check` retains raw native
coordinates and explicitly projects the centered afocal final reference onto
the Core image plane; earlier unprojected JSON is not a same-plane error result.
`Seal-AutomaticGridRepair.ps1` verifies final identities, original five profiles,
133 distinct tests, six identical Core copies and the separate evidence archive.
`Seal-SeamRepair.ps1` belongs to the preceding sealed 335 stage and must not be
rerun over its immutable archive. Neither collector runs native jobs or tests.

`--launch-study REPOSITORY CAPTURE OUTPUT_JSON` compares precise launches with
the three original/cache-off/aiming-off controls. `--parabasal-rule-study` takes
the same arguments and retains the offline rule investigation, including wider
or negative-pupil residuals outside the product's three-probe scope. Neither
mode supplies native coordinates or inferred constants to the product.
`NativePupilLaunchControls` changes only declared non-acceptance in-memory pupil
diagnostics, reads back cache/mode/aperture controls, and never saves the source.
Primary-wavelength controls must freeze physical thickness solves first to
retain an identical prescription. `Seal-RiflescopePupilRepair.ps1` seals this
new stage, preserving the original/earlier archives and all failed controls.

The latest [centroid-energy stage](../../../docs/AFOCAL_CENTROID_ENERGY_REPAIR_2026-10-30.md)
closes the three original geometric-energy differences with explicit angular
plot reference and zero-radius boundary conventions. Physical angular means
and empirical CDFs retain their independent default. That stage passed 169 distinct tests.
The modes `--centroid-energy REPOSITORY NEW_OUTPUT_DIRECTORY` and
`--energy-reference-controls REPOSITORY NEW_OUTPUT_DIRECTORY` replay the five
original requests and 19 valid native controls respectively, retaining the
original gates. The latter makes only declared field selection and typed-unit
conversion; it does not fit centers or ordinates. `NativeEnergyReferenceControls`
freezes thickness before toggling afocal/focal units in owned, non-acceptance,
background systems. Middle is absent from this installed energy API enum; its
failed request survives in the evidence. `Seal-CentroidEnergyRepair.ps1` verifies
and seals this stage without rerunning tests or native applications.

`--afocal-ri REPOSITORY NEW_OUTPUT_DIRECTORY` replays the five immutable RI
requests, retaining exceptions or structured outcomes without inventing native
arrays or assigning numerical Pass. The [RI applicability stage](../../../docs/AFOCAL_RELATIVE_ILLUMINATION_APPLICABILITY_2026-10-30.md)
records five background native rejections and a genuine focal buffer-positive
control. `NativeRelativeIlluminationCapture` is explicitly diagnostic-only and
never writes `data.json` for missing buffers; its exit zero is not optical Pass.
`Seal-RelativeIllumination.ps1` verifies input/settings identities, tests and all
archive entry hashes without rerunning native applications.
