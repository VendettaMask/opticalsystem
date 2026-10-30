"""Capture/check this bounded diffraction scope; no product optical calculation in Python."""
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
raw = root / "artifacts/validation/camera-scene-diffraction-20261030"
out = root / "validation/rechecks/2026-10-30/camera-scene-diffraction"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = out / "verification.json"
if "--capture" in sys.argv:
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    latest = {}
    batches = []
    for name in ("integration-first.trx", "final.trx", "delivery.trx", "grazing-final.trx"):
        results = [dict(e.attrib) for e in ET.parse(raw / name).findall(".//t:UnitTestResult", ns)]
        batches.append({"file": name, "counts": {s: sum(r["outcome"] == s for r in results)
                                               for s in sorted({r["outcome"] for r in results})}})
        for r in results:
            latest[r["testName"]] = {**r, "batch": name}
    assert all(r["outcome"] == "Passed" for r in latest.values())
    assert sum("CameraSceneDiffraction" in name for name in latest) == 13
    unchanged = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs",
                 "src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs", "src/OpticalSystemDesign.Core/Optic.cs",
                 "validation/zemax"]
    subprocess.run(["git", "diff", "--exit-code", "--", *unchanged], check=True, capture_output=True)
    sources = ["src/OpticalSystemDesign.Core/Analysis/ScalarHuygensIntegral.cs",
               "src/OpticalSystemDesign.Core/Analysis/Imaging/DiffractionCameraRenderer.cs",
               "src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraRenderer.cs",
               "src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraScene.cs",
               "src/OpticalSystemDesign.Core/NonSequential/NonSequentialIntersectionScene.cs",
               "src/OpticalSystemDesign.Application/Contracts/CameraImagingContracts.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.Scene3D.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.Experiments.cs",
               "src/OpticalSystemDesign.App/Services/UiLocalization.CameraPixelSampling.cs",
               "tests/OpticalSystemDesign.Tests/CameraSceneDiffractionTests.cs",
               "tests/OpticalSystemDesign.Tests/CameraSceneDiffractionWindowTests.cs",
               "tests/OpticalSystemDesign.Tests/Fixtures/camera-scene-v7-frozen.zip"]
    sources += [p.relative_to(root).as_posix() for p in (root / "src/OpticalSystemDesign.App/Laboratories").glob("CameraImagingWindow*.cs")]
    core_paths = [root / p / "OpticalSystemDesign.Core.dll" for p in
                  ("src/OpticalSystemDesign.Core/bin/Release/net10.0", "src/OpticalSystemDesign.App/bin/Release/net10.0",
                   "tests/OpticalSystemDesign.Tests/bin/Release/net10.0")]
    assert len({sha(p) for p in core_paths}) == 1, "Default and tested Core differ"
    audit = json.loads((raw / "documentation.json").read_text(encoding="utf-8-sig"))
    assert not audit["BrokenLinks"] and not audit["MarkdownFenceIssues"]
    with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(raw.rglob("*")):
            if p.is_file():
                z.write(p, p.relative_to(root).as_posix())
    data = {
        "date": "2026-10-30", "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "includesUncommittedChanges": True, "passedDistinctLatest": len(latest), "newDiffractionCases": 13,
        "failedLatest": 0, "skippedLatest": 0, "batches": batches, "results": list(latest.values()),
        "sourceSha256": {p: sha(root / p) for p in sorted(set(sources))},
        "defaultCoreSha256": sha(core_paths[0]), "appCoreSha256": sha(core_paths[1]), "testedCoreSha256": sha(core_paths[2]),
        "evidenceSha256": sha(out / "evidence.zip"), "unchangedTrackedPaths": unchanged,
        "scope": "Bounded small-angle scalar 3D diffraction, existing sensor/ISP, mixed A/B and frozen V7/V8 replay; offscreen UI",
        "limitations": ["No full vector/high-NA/coherent-scene diffraction or commercial numerical certification",
                        "Finite source, pupil, pixel and window quadrature require separate convergence",
                        "No full-repository or original 170-case native matrix rerun", "No foreground desktop, release or push"]
    }
    manifest.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

data = json.loads(manifest.read_text(encoding="utf-8"))
for p, digest in data["sourceSha256"].items():
    assert sha(root / p) == digest, p
assert sha(out / "evidence.zip") == data["evidenceSha256"]
with zipfile.ZipFile(out / "evidence.zip") as z:
    assert z.testzip() is None
print(json.dumps({k: data[k] for k in ("passedDistinctLatest", "newDiffractionCases", "scope")}, ensure_ascii=False))
