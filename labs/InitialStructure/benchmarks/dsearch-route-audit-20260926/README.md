# DSEARCH route audit — diagnostic evidence

This directory supports the [2026-09-26 implementation-path review](../../../../docs/INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md). It is **not** a new search release or acceptance run. All runtime sources match final v8, and the calculation assemblies are unchanged. The existing laboratory **178/178**, formal **1304/1304**, and tool **104/104** baselines were not rerun or increased by this audit.

The standalone C# probe uses the existing Core-backed optical adapter and bounded least-squares solver. It defines two independent targets (62 mm / F6 / 7° and 113 mm / F7 / 4°), four elements, fixed N-BK7/N-F2/N-BK7/N-F2 materials, and four specified initial directions. None is a frozen acceptance or holdout specification. Each of the eight starts receives the same 256-evaluation warmup; its resulting snapshot is shared by all four diagnostic variants. Each solve has 600 evaluations, identical coordinates and solver settings, and the existing full six-field/97-pupil/three-wavelength analysis. Entry and final validation costs are separate.

The four variants are:

1. **current-target-hinges**: current Targets-phase residuals and feasible trial domain. It isolates that phase; it does not reproduce the full scheduler or continuation policy.
2. **joint-ray-vector**: the existing adapter's joint ray-residual vector, using the same feasible trial domain. This changes the optimization objective relative to variant 1.
3. **joint-ray-norm**: the scalar Euclidean norm of variant 2's residuals. Its squared objective equals variant 2 at every valid point, while its Gauss–Newton model loses residual-direction information.
4. **joint-ray-vector-continuous**: variant 2's objective and existing Core aperture-clearance penalties, allowing geometrically valid continuous Core diagnostics even below the physical throughput threshold. Its final acceptance still uses every original physical gate.

All variants use real Core results; the probe contains no optical tracing, dispersion or spot-analysis formulas. Invalid propagation remains invalid. Requested analysis ray samples in the output do not include all aiming/diagnostic work and are not a total physical-ray cost measurement.

| Variant | Full-target successes | Derivative-unavailable exits | Solve evaluations |
| --- | ---: | ---: | ---: |
| Current target hinges | 0/8 | 4/8 | 4317 |
| Joint ray vector | 0/8 | 2/8 | 4464 |
| Joint ray norm | 0/8 | 2/8 | 4460 |
| Joint ray vector, continuous domain | 0/8 | 0/8 | 4772 |

The results show implementation sensitivity and a concrete derivative-domain failure mechanism, not a successful replacement algorithm. Some starts improve and others do not. The continuous-domain variant still fails focal, maximum-spot and/or physical transmission gates on individual cases. Preserve all 32 rows, including failures, in `residual-ablation.json`.

`verification.json` records source/result hashes, unchanged v8 numerical identity and exact reproduction of the preceding 24 cases after adding the fourth variant. Runtime/test counts remain unchanged. The probe uses the existing test friend-assembly name only to inspect internal APIs; it is an isolated console project outside either solution and adds no product or test dependency.

From the repository root, with the matching v8 default Release benchmark assemblies already built:

```sh
dotnet run --project labs/InitialStructure/benchmarks/dsearch-route-audit-20260926/probe/RouteAudit.csproj -c Release -- artifacts/validation/dsearch-route-audit-replay/results.json
```

The project can also reference an explicitly retained binary directory with `-p:AuditBinaries=/absolute/path/to/frozen-v8-bin` before `--`. Check the emitted assembly hashes against the archived results before comparing runs. Future runtime revisions are different experiments. No NuGet packages are added. Raw console output and preceding diagnostic versions remain under `artifacts/validation/dsearch-route-audit-20260926`.
