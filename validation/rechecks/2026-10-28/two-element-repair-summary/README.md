# Exact two-element repair and bounded failed comparisons

The prescription remains unqualified. All fresh comparisons start at RMS58.75208791787859µm / maximum167.34036843237768µm, endpoint SHA256 `091e12a43c57e531e1c80d6beca77206c9f377b6fc820a46d3c4edbeb63a0984`. The producer is the autonomous fixed-topology compensation path, not legacy AutoLens reference training.

## Retained changes

Absolute axial variables no longer recover authorized coordinates by subtracting and accumulating Core thicknesses. That round trip moved the protected stop by approximately1.78e−15mm and falsely rejected5/20 legal perturbations. The binder now preserves fixed planes and maps declared coordinates directly; Core still supplies optical calculations. Deliberate1e−6mm stop mutation remains rejected.

The linear feasibility solver optionally verifies original unnormalized rows, in addition to its historical normalized-row tolerance. A deterministic scale-separated regression establishes the former mismatch. Explicit policy v9 requests1e−10 original-row accuracy; existing v3/v4/v5 and defaults remain unchanged. This is not an image-quality improvement claim.

## Fixed protocol and outcomes

All comparisons retain12 absolute variable bounds/scales, aperture,21°field, three wavelengths,4×16 training and35-field independent checks,50/150µm image-quality limits and3% distortion. Each arm has600 functions/100 gradients/2,000,000 rays/60seconds; each training stage400,000 rays and final reserve800,000. Every arm starts fresh; independent test/diagnostic costs are separate.

|Arm|RMS µm|Maximum µm|Outcome|
|---|---:|---:|---|
|Mapping control|55.911122|161.414889|Fail|
|Mapping repaired v5|55.925893|161.461275|Fail; retained functional repair|
|v6 remainder heuristic|58.583085|166.356273|Fail; removed|
|v7 rejected-point correction|56.164298|165.541348|Fail; removed|
|v8 gate Gauss-Newton|58.620935|167.340364|Fail; removed|
|v9 original-row tolerance|58.738397|167.185273|Fail; explicit accuracy option only|
|v10 quadratic contraction|56.339609|164.616008|Fail; removed|

Edge transverse variance is mainly aligned anisotropic (61.19%), with cubic16.15%, isotropic11.58% and color-centroid10.04% terms; these are diagnostic polynomial components, not unique Seidel coefficients. The21°blue/red rays contribute39.89%/33.33% of RMS variance. Tiny distortion margin interacts with the focal/F-number guards and front-Z lower bound.

The nonsmooth-maximum hypothesis was falsified on both saved directions:36 probes,317,028 rays,5.44seconds; rank changes only between symmetry-equivalent rays (gradient difference<=3.664e−15). All12 parameter finite differences agree within8.959e−8 relative error. Positive error scales quadratically. v10 used already-paid tangents/residuals for contraction but eventually reduced its rate to5.775e−14 and stalled; it does not establish a useful fix. No physical-impossibility proof is claimed.

## Verification and evidence retention

Earlier retained-code focused regressions:45 passed. v10 experiment:9 focused regressions passed, separately308,260 rays; its two search arms consumed2,507,620 rays across two budgets. Product files restored after withdrawal; Release build and strict documentation checks passed. Two historical Linux checkpoint fingerprint tests remain blocked; fresh SHA-pinned imports do not bypass checkpoint resume checks.

Compact results: [mapping](mapping-verification.json), [tangent/accuracy](tangent-verification.json), [maximum envelope](envelope-verification.json), [curvature contraction](curvature-verification.json). Failed experimental patches are retained in the Library archive for review; they are not applied or enabled. The v10 patch carries temporary baseline prefixes; exact experimental source is in the Library archive.

Large raw traces, all failed endpoints, logs, complete reports and original inputs remain in ChatGPT Library, not duplicated as large Git blobs. Exact Library identity: `libfile_b3b554cb381881918ebd21ba0e590728`, version3, `autolens-two-element-20261028-v4.zip`,43,519,330bytes, SHA256 `af008de175e8217c81617aa2bebd32383325b59f8e6f5f0287a5df6ffe05f07e`. The [archive member manifest](library-v4-manifest.json) records all240 file hashes. Original failed evidence is retained, not replaced by successes.

All tests were cloud CPU background/headless. No Core modifications, native Zemax/GPU execution or user-desktop access. Git synchronization is on the isolated work branch only; no main merge or release.
