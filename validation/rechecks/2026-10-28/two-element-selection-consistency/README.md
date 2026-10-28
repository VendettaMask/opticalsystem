# Saved two-variable feasible-cone regression

Parameter-selection mismatch was tested and rejected at the saved v10 Local and v5 Full endpoints: both raw objective and gate-merit gradients select parameters[0,7]. Do not attribute the recorded stall to this hypothesis.

A different numerical limitation is confirmed. At the v10 Local stalled endpoint, an independently obtained witness satisfies every original bounded linear row (maximum raw violation8.674e−19) with normalized merit descent−0.00129332. For a requested decrease.001, the existing4096-sweep projection returns null despite this witness. The C# deterministic regression independently checks all1558 original rows and reproduces the failure without tracing. This proves a missed feasible linear direction, not a nonlinear optical improvement or global feasible prescription.

The independent SciPy diagnostic solves six saved-data linear programs, each capped at5seconds, and performs zero optical evaluations. Its outputs are[diagnosis.json](diagnosis.json). The portable[feasible-cone fixture](feasible-cone.json) contains only two selected coordinates and the original halfspaces; no parameter bounds or optical conditions changed. `BoundedProjectionTests` verifies it without SciPy. One focused background test passed.

Next candidate: a bounded two-dimensional polygon intersection fallback only when iterative projection exhausts. Preserve existing proposals when the solver succeeds, all nonlinear filters, phase budgets and default policies. No extra derivative or trace calls; numerical fallback wall time remains inside the original deadline. Require deterministic feasibility/empty-set/cancellation tests before one fresh original-budget v5/candidate pair. Full12-dimensional solves remain unchanged; no increase of local active parameter count.

Saved source is Library v4 version3, ID `libfile_b3b554cb381881918ebd21ba0e590728`; the diagnostic script requires that archive extracted at the repository root. The compact C# fixture does not. No Core changes.

## Implemented isolated fallback

Explicit policy v11 first runs the unchanged v5 projection. Only an exhausted two-dimensional solve may use a polygon clipped by the same halfspaces inside the original unit step box. The returned point is independently checked in all original rows at1e−10. Default and historical policies remain unchanged, and the nonlinear filter remains authoritative. Nine focused background tests passed (saved cone, infeasible sets, cancellation, original gate protection, axial mapping and serialized resume). Optical qualification is not implied; a fresh same-budget v5/v11 pair is pending at this stage.
