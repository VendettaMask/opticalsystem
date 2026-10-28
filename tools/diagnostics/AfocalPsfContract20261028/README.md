# Remaining afocal capture (not executed here)

Requires a checkout of the recorded base commit with this patch applied, Python 3.9+,
and, **only for `--run`**, a licensed Windows OpticStudio 2026 R1 installation and
.NET Framework C# compiler. No product Python dependency is introduced.

Offline preparation (no native application):

```sh
python tools/diagnostics/AfocalPsfContract20261028/Capture-Remaining.py --output NEW_DIRECTORY
```

On a separately authorized licensed Windows machine, one bounded capture command:

```powershell
python tools/diagnostics/AfocalPsfContract20261028/Capture-Remaining.py --output C:\AfocalCapture\new-run --api 'C:\Program Files\ANSYS Inc\v261\Zemax OpticStudio' --run
```

The script extracts exact frozen rifle/binocular prescriptions, verifies SHA-256,
compiles the repository host against the installed assemblies, and runs 11 fresh
host processes sequentially (120 seconds/job, compile 60 seconds). It never attaches
to an existing application or saves the source prescription. A failure stops the
batch and retains logs. Timeout kills the host only; native child cleanup is then
explicitly uncertain, and the batch stops without targeting other applications.
Use a fresh output directory; previous captures are never overwritten.

Jobs / interpretation:

- Three pupil probes (rifle c1, binocular c1/c2): raw EPDI/EXPD/ENPP/EXPP values,
  actual eight zero arguments and native operand schemas, model and version.
  Unsupported operands/errors are preserved. Interpret operands against their
  schema and primary wavelength before comparing to PSF scale; an operand is not
  automatically the internal FFT X/Y scale or an appropriate mixed-NSC paraxial
  result. This can distinguish rounded text export from a real scale residual.
- Six Geometric Encircled Energy captures: the three configurations, Field=2,
  Wavelength=2, independent Centroid/ChiefRay instances, all other frozen settings
  retained. Raw arrays, text, settings and selectors distinguish stale-analysis
  reuse from reference-center semantics. They do not replace the original Field=0
  evidence or its three Differences.
- Two 33-ray binocular boundary/seam probes: center and each signed axis at radii
  0.99999, 1, 1.00001, Field=1, Wavelength=2. Output error/vignette/intensity,
  per-surface position and direction identifies physical admission either side of the
  endpoint. `image-geometry.json` has OPDMode.None: do not interpret its OPD column.
  These probes cannot by themselves reveal the native FFT pixel-to-launch mapping.
  Each job also traces 20 central probes: signed X/Y at h=.01,.003,.001,.0003,.0001.
  Full sequential surface records are requested, including surfaces 5 and 6 before/after
  the polygon group; internal facet IDs are still not exposed by this batch API.

Existing exact endpoints are already frozen and valid in both configurations;
`existing-evidence.json` exports their original input/index correspondence and
member hashes, excluding meaningless OPD. Existing System Data pupil numbers are
also extracted correctly from UTF-16/localized text, but are rounded.

If pupil operands do not expose the internal FFT definition, the remaining external
requirement is the 2026 R1 native FFT X/Y pupil scale and pixel-to-normalized-launch
mapping for (column,row)=(32,17),(32,47),(17,32),(47,32), 64x64 output from 32x32
pupil sampling, both binocular configurations. The script cannot manufacture
undocumented API fields. Existing Real/Imaginary arrays need not be recaptured.
Five relative-illumination references remain missing, and two mixed-NSC first-order
comparisons remain inapplicable; these 11 jobs intentionally do not invent either.

Cloud validation covers offline preparation, exact input hashes, all 11 selectors,
10 existing endpoint identities and bounded job structure. Native compilation and
execution are **not verified** without proprietary assemblies/license.

2026-10-28 native-edge context update: pupil probes also write
`afocal-nsc-context.json`, listing discovered SystemData interface property names
and primitive NSC/non-sequential settings (including glue distance only if exposed).
Missing properties/sections remain explicitly unavailable, not zero defaults.
`completeNativeEdgeContract=false`: API metadata and sequential group exit rays do
not establish internal facet IDs, rectangle diagonal or edge-selection behavior.
The 11-job cap and 120-second per-job timeout are unchanged. No native run performed.

Evidence completion contract: `existing-evidence.json` preserves the original NSCD
record verbatim (UTF-16/UTF-8 aware) with source hash. Token meaning is unverified;
no token is silently assigned to Glue Distance. Successful native processes are
now separately assessed. Missing/invalid context, wrong configuration, absent NSC
sections, getter errors and invalid glue readbacks are explicitly listed.
A numeric glue candidate still does not prove its units, rectangle diagonal or
facet-edge semantics. All completed jobs are retained; the final batch writes
`evidence-assessment.json` and exits **3** for incomplete native edge evidence,
instead of returning success merely because host processes exited 0. Capture/runtime
failures still stop the batch immediately. Native execution remains untested.

Offline regression: `python tools/diagnostics/AfocalPsfContract20261028/Test-CaptureRemaining.py`
(8 tests, no native software).
