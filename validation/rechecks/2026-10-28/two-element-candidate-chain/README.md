# Nonlinear rejection must advance the candidate chain

Predeclared reproduction: use the saved actual v12 Full starting topology after Local, original variables/targets/constraints, and at most two updates. v12 repeatedly selects the stronger linear fallback after its nonlinear rejection; the .001 primary path is never reached. Red regression expects the second trial to use that existing primary path, then green explicit v13 must preserve this suppression state through serialization. This is a candidate-selection policy repair, not a changed optical acceptance condition.

Scope: after a fallback is rejected, suppress it only at the current accepted point. Ordinary rejection still halves the existing rate. Successful acceptance clears suppression for the new point. Every candidate still uses the existing counted Evaluate path; no precomputed optical candidates, no new variables or changed bounds/gates. A dedicated regression has150k rays and60seconds; a later pair starts fresh from the original58.752088/167.340368 endpoint under the unchanged600/100/2M/60s protocol.

## Red/green verification

v12 red regression reproduces two rejected fallback trials with50,858 rays. Explicit v13 green regression accepts the existing primary candidate on attempt2 after rejecting fallback attempt1. It then cancels after attempt1, serializes/resumes and obtains the exact same final topology and function/gradient/ray counts; dropping the suppression state is rejected before tracing. Seven focused chain/gate tests pass. Exact independent counts are in[red-summary.json](red-summary.json) and[green-summary.json](green-summary.json); each optical regression has150k rays/60seconds.

The fix changes candidate sequencing in explicit v13: a linear success is not treated as nonlinear acceptance. A rejected fallback is suppressed until a new point is accepted. It does not loosen acceptance or replace the original smaller-decrease primary solve. Legacy versions/defaults are unchanged. This corrects v12's integration failure; improved final image quality remains a separate claim requiring the same-budget pair in[protocol.json](protocol.json).

## Completed original-budget comparison

|Arm|RMS µm|Maximum µm|Distortion %|Functions/gradients|Rays|Seconds|Accepted updates|
|---|---:|---:|---:|---:|---:|---:|---:|
|v5|55.925892725|161.461275055|2.999882642|31/29|1,333,862|12.531|13|
|v13|56.021857187|163.594757095|2.999884696|31/29|1,334,162|10.931|12|

Both arms independently start from the original58.752088/167.340368µm endpoint. Original12 absolute bounds, aperture/21°fields, wavelengths, sampling and50/150µm plus3% gates remain unchanged. Each arm retains600 functions/100 gradients/2M rays/60seconds,400k training-stage caps and800k final reserve. All linear-model arithmetic and candidate-selection work is inside the original wall-time/deadline; every optical candidate follows the original counted Evaluate path. Two arms total2,668,024 rays across two separate budgets, never a cumulative continuation. Full exhausts its original400k ray cap in both arms.

The sequence fix works: Full v13 completes11 fallback evaluations, all rejected, then completes11 primary evaluations, all accepted. The original nonlinear no-worsening filter is unchanged. The12th fallback solve belongs to the final quota-interrupted optical evaluation, which remains charged and is not counted as accepted or complete. No same fallback is regenerated after rejection at the same accepted point; suppression clears only after a different point is accepted. Saved per-step `UsedFallbackCandidate` and continuation `SuppressFallbackAtCurrentPoint` agree with the entire decision history.

Nevertheless v13 is worse than v5 by0.095964µm RMS and2.133482µm maximum. Compared with v12 it restores productive steps, but repeatedly testing an unsuccessful stronger candidate at each newly accepted point still costs11 completed attempts. The candidate-chain implementation is corrected; the preference for trying this fallback first remains an ineffective strategy on this case. v13 is explicit experimental behavior, not enabled by default and not recommended as an image-quality improvement. No additional search or budget increase was used. A future change must justify when a fallback is worth evaluating before an already available primary candidate; merely generating a stronger linear direction has repeatedly failed this test.

All35 fields have960/960 integration and99/99 boundary-check rays. Manufacturing/transmission feasibility is distinct from the failed image-quality gates. Both exported unqualified STAROPT files were independently imported and assessed; all checked metrics and counts match, costing847,364 separate readback rays. See[verification](verification.json),[offline verifier](verify.py), and[readback evidence](native-audit/native-audits.json).

The red regression consumed50,858 rays; the green full/cancel/serialized-resume regression consumed101,972 rays. Each had150k rays/60seconds and these costs are outside optimization budgets. The deliberately failed red test and its exact source are preserved, along with all raw test results and paired candidate diagnostics in the increment archive. Seven green tests passed. No Core/default/UI changes, user desktop, native Zemax/GPU, main merge, PR, release, or forbidden Actions retry. This remains an unqualified two-element result, not a proof of physical impossibility.
