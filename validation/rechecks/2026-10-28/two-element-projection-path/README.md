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

Four zero-ray solver regressions passed, followed by ten focused policy/solver/gate/resume regressions. The independently capped serialized-resume optical test consumed101,748 rays of300,000; all other tests in these groups are algebraic. The fresh original-budget v5/v12 comparison specified in[protocol.json](protocol.json) is now complete; its failed result is recorded below.

## Completed v5/v12 comparison: numerical repair does not improve this search

|Arm|RMS µm|Maximum µm|Distortion %|Functions/gradients|Traced rays|Seconds|Accepted updates|
|---|---:|---:|---:|---:|---:|---:|---:|
|v5|55.925892725|161.461275055|2.999882642|31/29|1,333,862|13.064|13|
|v12|58.751976558|167.185273187|2.999101238|31/29|1,334,998|10.607|1|

Both arms independently start at the exact58.752088/167.340368µm original endpoint. Original12 absolute variable bounds, aperture,21°field, wavelengths, sampling,50/150µm image gates and3% distortion remain fixed. Each arm has600 functions/100 gradients/2M rays/60seconds,400k training-stage caps and800k final reserve. Every pivot runs inside the optimizer and enclosing comparison stopwatch/deadline. No fallback optical calls are hidden or added. Both Full stages exhaust their original400k ray quota. Final assessments complete; all35 fields have960/960 integration and99/99 check rays. Both remain unqualified. The two independent search budgets total2,668,860 rays, not one cumulative run.

### Actual fallback and acceptance

Local repeats the earlier path: four fallback calls at.01 produce no feasible candidate, and the primary.001 path yields one accepted update. Full v12 calls the new12D fallback23 times and produces23 feasible linear candidates at.01. Twenty-two complete optical evaluations are all rejected by the unchanged nonlinear filter; the23rd is interrupted by the original ray quota and remains charged. It is not counted as accepted or as a completed optical evaluation. The Full endpoint evaluation is exactly its initial evaluation.

The first trial raises other-gate merit from0.05116634195 to0.10342564239. The second reduces aggregate merit to0.04992534649 but still fails the per-row no-worsening filter. The final completed trial also slightly reduces aggregate merit and is rejected. The persisted summary does not identify individual rejected rows, so no unsupported attribution to a specific optical row is made.

The successful.01 linear fallback now preempts the primary.001 candidate that v5 would select. Since every Full candidate is rejected, the state stays fixed; repeated halving regenerates the same unscaled fallback direction rather than trying the lower-decrease primary candidate. Final rate is2.384185791015625e−9. This is a selection/acceptance integration limitation supported by this trajectory, not proof that feasible linear directions are physically useful or that the prescription is impossible.

v12 is explicit experimental behavior and is **not recommended or enabled by default**. Its deterministic linear feasibility repair passes tests, but its optical outcome is worse than control. No extra search, changed gates, extra active variables or cumulative continuation was used to hide this failure. A future candidate must first show that it can preserve useful primary-candidate progress after a fallback's nonlinear rejection; another stronger linear-feasibility claim alone is insufficient.

### Independent verification and accounting

Both exported unqualified STAROPT files were imported and independently validated with the original settings; every checked metric and validity count matches its original final assessment. This readback costs848,200 additional rays across both files, outside optimization budgets. See[readback evidence](native-audit/native-audits.json) and[verification](verification.json).

The newly added fallback-trigger/acceptance logging passed six focused gate/resume regressions. Its independent capped resume test used101,748 of300,000 rays. The prior implementation-stage resume test separately used101,748 rays; it is not reused or folded into a search budget. Pure algebra regressions, saved-Jacobian path replay and the independent LP reference use zero rays. The native readback has its own800k cap per file.

Raw configurations, all Local/Full steps and diagnostics, failed endpoints, summary and frozen protocol remain in the incremental Library archive under this directory's `paired/` tree. The checked-in summary retains the honest failure and budget boundaries. Core/defaults remain unchanged; no native Zemax/GPU, user desktop, main merge, PR, release or forbidden Actions query.
