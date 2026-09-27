# Physical-stop diagnostic, 2026-09-27

This is a diagnostic conversion of the 30 frozen v16 prescriptions from
`../literature-audit-20260927/inputs.json`, not a new search or accepted candidate set.
All optical computations use the formal shared Core. Product code remains C#/.NET.

The runner first recalculates the original prescriptions with 21 fields and 10×32
Gaussian pupil quadrature, without altering their settings or snapshots. It then
calibrates each circular stop from its declared pupil and enables ray aiming on a
private optic. Lens bodies are sized from 11 fields, 6×24 Gaussian pupil samples,
the pupil center and 24 boundary samples, using margin 1.15. Physical stop clear
radius remains fixed; mechanical lens radius may grow or shrink. Another private
trace removing circular apertures is diagnostic only and never supplies a score.

`results.json` records the final conversion, same-settings original RMS check,
input/binary hashes and actual incomplete-ray counts. A converted RMS is published
only for a complete envelope and complete physical interior/boundary samples;
this still does not establish geometry feasibility or full design acceptance.
`before-aiming-fix.json` is the intermediate pre-fix diagnostic, identified by its
Core binary hash, with 0/30 complete envelopes. Its older RMS column covered only
interior samples and is **not** an acceptance metric. The final result has 22/30
complete envelopes; eight remain failures, totaling 45 incomplete rays even with all circular apertures removed. All generated rays have positive intensity, so these remaining failures occur during real propagation after aiming, not rejected aiming launches. This is not a throughput certification.

The pre-fix runtime was parent commit `b8910c2f` plus `pre-fix-core.patch` (the two
new stop/envelope capabilities, before changing aiming and circular boundaries).
The patch is evidence for an isolated historical reconstruction, not a patch to
apply on the current checkout. It is a zero-context patch (`git apply --unidiff-zero` in an isolated historical checkout). The final runner can also use those APIs; newer
diagnostic columns were not present in the original pre-fix output.

Reproduce from the repository root (requires the .NET 10 SDK):

```sh
dotnet build labs/InitialStructure/benchmarks/physical-stop-20260927/runner/PhysicalStopAudit.csproj -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet labs/InitialStructure/benchmarks/physical-stop-20260927/runner/bin/Release/net10.0/PhysicalStopAudit.dll labs/InitialStructure/benchmarks/literature-audit-20260927/inputs.json artifacts/validation/physical-stop-20260927/replay.json
```

Compare numerical rows and the input hash. Assembly hashes also include build
metadata and can change after committing; they are recorded, not universal values.
`source-freeze.json` identifies the actual source files used for final verification;
`verification.json` records test counts, build outputs, evidence hashes and limits.
Raw TRX and build logs live in the ignored `artifacts/validation/physical-stop-20260927`.

v17 isolates shared Core numerical changes from v16 search checkpoints. It has not
connected stop calibration/preservation to the laboratory search loop or enabled
aiming by default. See the [implementation and remaining work](../../../../docs/INITIAL_STRUCTURE_PHYSICAL_STOP_2026-09-27.md).

Existing Zemax capture regression tests, current Workbench recalculation and this
diagnostic conversion are separate evidence. No new Zemax capture, full external
comparison, screenshot validation or concurrent main-UI certification was made.
