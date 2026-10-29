# Background afocal native investigation (2026-10-29)

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
