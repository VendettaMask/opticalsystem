# v11 actual-path audit

Predeclared scope: replay proposal/model arithmetic from saved v5/v11 InitialEvaluation, original coordinates, policy configuration and accepted/rejected steps. No optical evaluations or rerun of optimization. Local has three rejected attempts followed by one acceptance, so its complete path uses the saved initial Jacobian. Full has12 active coordinates throughout and cannot enter the two-dimensional fallback. Replay its first model only; do not fabricate missing intermediate Jacobians. An optional internal observer records primary/fallback outcomes without altering selection. Deadline60seconds, trace quota1 with final required count0.

## Verified branch behavior

The initial audit completed in1.7938293seconds with zero traced rays. At each of the four Local attempts, v11 invokes its fallback for decrease.01, but the fallback returns no candidate. The unchanged primary solver then succeeds at.001, so all four v5/v11 coordinates match exactly. The first three attempts are rejected and the fourth accepted, and the replayed fourth coordinate matches the persisted Local endpoint exactly. This is not candidate-ranking displacement, a disabled policy, or resumed stale state.

All22 recorded Full attempts have12 active coordinates; the two-dimensional fallback cannot run. Its first model likewise exhausts the primary solver at.01 and succeeds at.001. Original Full ends at its400,000-ray cap, not a new convergence declaration. The actual saved pair is fresh, not a checkpoint resume. The next narrow question is whether Full's.01 linear target was truly infeasible; no optical optimization is rerun to answer it.

## Full-phase omission confirmed

An independent five-second-capped LP check of the saved Full initial model returned a witness with normalized merit descent−0.0626304633 and maximum original-row violation2.619e−15. The requested.01 decrease is therefore feasible. A separate zero-trace C# regression checks every original row and confirms that the4096-sweep primary projection nevertheless returns null. This is a genuine12-dimensional linear-solver limitation on the actual paired trajectory, distinct from the earlier two-dimensional state reached only by v10. No nonlinear improvement is inferred from the linear witness.

The next bounded candidate may add a managed linear-feasibility fallback after exhaustion, with the same original rows, same12 coordinates and unit step box, no extra function/gradient/trace calls, explicit identity and unchanged nonlinear acceptance. It must pass the saved12D witness regression and bounded infeasible/cancellation tests before any fresh same-budget pair. No primary successful solution should be replaced.

## Explicit v12 repair stage

The managed fallback uses phase-I linear feasibility plus an L1 preference to the existing preferred point, limited to2048 pivots with cancellation. It retains the same original halfspaces, unit step box and original-row1e−10 verification. It is called only after primary exhaustion in3..12 dimensions; two-dimensional cases retain v11 polygon clipping. No Core or optical computation is added. The preferred metric differs from Euclidean projection, so a nonlinear benefit must be measured rather than assumed. Default policies remain unchanged.

Four zero-ray solver regressions passed, followed by ten focused policy/solver/gate/resume regressions. The independently capped serialized-resume optical test consumed101,748 rays of300,000; all other tests in these groups are algebraic. A fresh original-budget v5/v12 comparison is specified in[protocol.json](protocol.json) but not yet run at this stage.
