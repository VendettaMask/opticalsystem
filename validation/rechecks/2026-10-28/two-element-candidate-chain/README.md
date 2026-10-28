# Nonlinear rejection must advance the candidate chain

Predeclared reproduction: use the saved actual v12 Full starting topology after Local, original variables/targets/constraints, and at most two updates. v12 repeatedly selects the stronger linear fallback after its nonlinear rejection; the .001 primary path is never reached. Red regression expects the second trial to use that existing primary path, then green explicit v13 must preserve this suppression state through serialization. This is a candidate-selection policy repair, not a changed optical acceptance condition.

Scope: after a fallback is rejected, suppress it only at the current accepted point. Ordinary rejection still halves the existing rate. Successful acceptance clears suppression for the new point. Every candidate still uses the existing counted Evaluate path; no precomputed optical candidates, no new variables or changed bounds/gates. A dedicated regression has150k rays and60seconds; a later pair starts fresh from the original58.752088/167.340368 endpoint under the unchanged600/100/2M/60s protocol.

## Red/green verification

v12 red regression reproduces two rejected fallback trials with50,858 rays. Explicit v13 green regression accepts the existing primary candidate on attempt2 after rejecting fallback attempt1. It then cancels after attempt1, serializes/resumes and obtains the exact same final topology and function/gradient/ray counts; dropping the suppression state is rejected before tracing. Seven focused chain/gate tests pass. Exact independent counts are in[red-summary.json](red-summary.json) and[green-summary.json](green-summary.json); each optical regression has150k rays/60seconds.

The fix changes candidate sequencing in explicit v13: a linear success is not treated as nonlinear acceptance. A rejected fallback is suppressed until a new point is accepted. It does not loosen acceptance or replace the original smaller-decrease primary solve. Legacy versions/defaults are unchanged. This corrects v12's integration failure; improved final image quality remains a separate claim requiring the same-budget pair in[protocol.json](protocol.json).
