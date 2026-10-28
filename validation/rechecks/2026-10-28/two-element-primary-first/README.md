# Primary-first lazy fallback: bounded v14 experiment

The single predeclared v5/v14 pair is complete. **v14 is worse; stop this candidate-chain series.** Default policy and shared Core are unchanged. No success or physical-impossibility claim is made.

## Hypothesis and implementation

v12 selected a strong feasible linear candidate before a smaller primary candidate, then repeatedly failed nonlinear acceptance. v13 made that smaller candidate reachable after rejection but paid for a new failed strong fallback at every accepted point. v14 tests scheduling capability, not a claim that every strong candidate is incorrect: try the complete existing primary decrease ladder first; consider the existing stronger fallback lazily only after primary nonlinear rejection or no primary candidate. Once considered and rejected, suppress fallback at that accepted point. Reset only after acceptance. No eager optical evaluation or new search dimensions.

The actual saved Full-phase fixture reproduces v13 preemption (expected red). v14 gives primary rejected, fallback rejected, primary accepted. No fallback solver runs on the first available-primary attempt. A considered fallback cannot repeat at the same rejected point; serialization retains pending/suppressed state, tampered pending state fails before tracing, and resumed topology and work equal continuous execution. Every candidate uses the original charged Evaluate path; all solver CPU and restoration time remain inside the phase/global deadlines. Existing rate halving and nonlinear filter are unchanged.

## Paired result

Both start independently at RMS58.75208791787859 / maximum167.34036843237768 µm, frozen source SHA091e12a43c57e531e1c80d6beca77206c9f377b6fc820a46d3c4edbeb63a0984.

| Measure | v5 control | v14 explicit experiment |
| --- | ---: | ---: |
| RMS µm | 55.9258927246604 | 56.91803828200751 |
| Maximum µm | 161.4612750551711 | 164.79991864610514 |
| Absolute distortion % | 2.999882641658419 | 2.9996056548276773 |
| Functions / gradients | 31 / 29 | 31 / 29 |
| Traced rays | 1,333,862 | 1,334,422 |
| Complete wall seconds | 12.5840554 | 10.5636947 |
| Accepted Local + Full updates | 1 + 12 | 1 + 10 |
| Strict target status | failed | failed |

The CPU timing difference is a single execution measurement, not a speed claim. Final validation is complete, all original field/ray checks valid. Manufacturing/first-order feasibility and sub3% distortion do not imply the required50/150µm image-quality gates. v14 fails RMS and maximum at fields33 and34; v5 fails RMS33/34 and maximum34.

v14 Full selected six lazy fallback candidates; all six were nonlinearly rejected. No accepted fallback. Its first trial is primary, so it does not silently reproduce v5 or misreport control improvement. Scheduler behavior is confirmed, but introducing failed fallback trials displaces productive primary updates before the original400k stage-ray cap. The remaining evidence points to linear feasible proposals failing the nonlinear gate and to the cost of trying them, not a proven unsatisfied scheduling contract. Reordering this chain again has no supported benefit; no further tuning or pair is run. A different model/curvature hypothesis would need separate evidence before any further search. These observations do not establish physical infeasibility under fixed freedom.

## Frozen conditions and accounting

Original12 parameters, absolute bounds, aperture/stop/planes,21° field, three wavelengths, training/final sampling,50/150µm and3% gates unchanged. Each arm600 functions/100 gradients/2M rays/60s; training stages400k and final reserve800k; Local4/Full99. [Protocol](protocol.json), [offline verification](verification.json), [verifier](verify.py), [independent native readback](native-audit/native-audits.json). Two-arm total2,668,284 rays is two independent budgets, never one continued optimization.

Independent red scheduling regression67,720 rays; green scheduling regression136,776 rays, each capped250k/60s. Native readbacks add847,624 rays and match both exported failed prescriptions. The broader test invocation passed13/15: scheduling regression and all seven nonlinear gate policies passed; two historical checkpoint tests failed before optimization with `Candidate physical assessment does not match the original specification`. Pre-existence of those failures was not proven in this run; do not claim all tests green. Their logs/TRX are retained. Other existing test helpers were not separately ray-instrumented; they are diagnostic cost, never attributed to the optimization arms. Strict documentation validation is recorded separately.

## Evidence and boundaries

Raw `paired/`, `red-evidence/`, `green-evidence/`, logs and failed STAROPT outputs are retained in the incremental Library ZIP, together with reviewed source patch and per-member SHA256 manifest. Compact summaries, tests, protocol and audit are committed. Old Library artifacts remain unchanged. All computation was cloud background/headless. No Core/default change, GPU/native Zemax/user desktop, main merge, PR or release. GitHub Actions remains unqueried after its earlier Forbidden denial; remote checks are not claimed green.
