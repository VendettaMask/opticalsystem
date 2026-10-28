# Saved two-variable feasible-cone regression

Parameter-selection mismatch was tested and rejected at the saved v10 Local and v5 Full endpoints: both raw objective and gate-merit gradients select parameters[0,7]. Do not attribute the recorded stall to this hypothesis.

A different numerical limitation is confirmed. At the v10 Local stalled endpoint, an independently obtained witness satisfies every original bounded linear row (maximum raw violation8.674e−19) with normalized merit descent−0.00129332. For a requested decrease.001, the existing4096-sweep projection returns null despite this witness. The C# deterministic regression independently checks all1558 original rows and reproduces the failure without tracing. This proves a missed feasible linear direction, not a nonlinear optical improvement or global feasible prescription.

The independent SciPy diagnostic solves six saved-data linear programs, each capped at5seconds, and performs zero optical evaluations. Its outputs are[diagnosis.json](diagnosis.json). The portable[feasible-cone fixture](feasible-cone.json) contains only two selected coordinates and the original halfspaces; no parameter bounds or optical conditions changed. `BoundedProjectionTests` verifies it without SciPy. One focused background test passed.

Next candidate: a bounded two-dimensional polygon intersection fallback only when iterative projection exhausts. Preserve existing proposals when the solver succeeds, all nonlinear filters, phase budgets and default policies. No extra derivative or trace calls; numerical fallback wall time remains inside the original deadline. Require deterministic feasibility/empty-set/cancellation tests before one fresh original-budget v5/candidate pair. Full12-dimensional solves remain unchanged; no increase of local active parameter count.

Saved source is Library v4 version3, ID `libfile_b3b554cb381881918ebd21ba0e590728`; the diagnostic script requires that archive extracted at the repository root. The compact C# fixture does not. No Core changes.

## Implemented isolated fallback

Explicit policy v11 first runs the unchanged v5 projection. Only an exhausted two-dimensional solve may use a polygon clipped by the same halfspaces inside the original unit step box. The returned point is independently checked in all original rows at1e−10. Default and historical policies remain unchanged, and the nonlinear filter remains authoritative. Nine focused background tests passed (saved cone, infeasible sets, cancellation, original gate protection, axial mapping and serialized resume). Optical qualification is not implied; the completed fresh same-budget v5/v11 pair below shows no image-quality change.

## Fresh same-budget comparison and remaining limit

Both arms start at the exact58.752088/167.340368µm endpoint with the original12 absolute variable bounds, aperture,21°field, wavelengths/sampling and50/150µm plus3% distortion gates. Each arm retains600 functions,100 gradients,2,000,000 rays,60seconds,400,000-ray training stages and800,000 final reserve. No cumulative continuation or added optical probes.

|Arm|RMS µm|Maximum µm|Distortion %|Functions/gradients|Rays|Seconds|
|---|---:|---:|---:|---:|---:|---:|
|v5|55.925893|161.461275|2.999882642|31/29|1,333,862|13.162|
|v11|55.925893|161.461275|2.999882642|31/29|1,333,862|10.284|

The final assessments are exactly equal, both unqualified; Full exhausts its original400,000-ray cap. All35 fields have960/960 integration and99/99 check rays, while edge RMS/maximum gates fail. Elapsed times are observations, not a benchmark speedup claim. Two arms consume2,667,724 rays across two separate budgets. See[verification](verification.json) and[offline verifier](verify.py).

The fallback repairs an actual saved two-variable linear omission, but does not change this original-start search trajectory. It remains explicit v11, not default, and is not advertised as an image-quality improvement. The outstanding obstacle remains progress in the actual12-variable nonlinear constrained Full phase within its original ray cap; this local two-variable repair does not establish a remedy. A further candidate must first demonstrate a changed, useful nonlinear step on the control's rejected trajectory, with every evaluation charged, rather than infer improvement from a different stalled state or repeat failed curvature strategies. No physical impossibility or convergence is claimed.

Independent work: six LP checks and the deterministic reproduction use zero rays. The nine focused fallback regressions include mapping/serialized-resume optical tests; their exact ray counters were not persisted, so they are explicitly unquantified separate test work, not zero or part of the search budget. Original finite per-stage function/gradient/update limits were enforced. Release build passed with zero warnings/errors. Shared Core untouched.
