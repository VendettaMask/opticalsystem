"""Freeze/check scoped evidence only. Product physics remains managed Core code."""
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
out = root / "validation/rechecks/2026-10-30/camera-scene3d"
raw = root / "artifacts/validation/camera-scene3d-20261030"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
record = out / "verification.json"
if "--capture" in sys.argv:
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    latest = {}
    batches = []
    for name in ("scope.trx", "final.trx", "ui-final.trx", "delivery.trx"):
        tree = ET.parse(raw / name)
        results = [dict(r.attrib) for r in tree.findall(".//t:UnitTestResult", ns)]
        batches.append({"file": name, "outcomes": {state: sum(r["outcome"] == state for r in results)
                        for state in sorted({r["outcome"] for r in results})}})
        for result in results:
            latest[result["testName"]] = {**result, "batch": name}
    assert latest and all(r["outcome"] == "Passed" for r in latest.values())
    new = sum("CameraScene3D" in name for name in latest)
    assert new == 19, new
    assert sum("OriginalCameraSizeAndFourByFour" in name for name in latest) == 2
    source_paths = [
        "src/OpticalSystemDesign.Core/Analysis/Imaging/CameraPixelAperture.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraScene.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraRenderer.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/SpectralDetectorPower.cs",
        "src/OpticalSystemDesign.Core/NonSequential/NonSequentialIntersectionScene.cs",
        "src/OpticalSystemDesign.Core/NonSequential/NonSequentialTracingEngine.cs",
        "src/OpticalSystemDesign.Application/Contracts/CameraImagingContracts.cs",
        "src/OpticalSystemDesign.Application/Services/CameraImagingService.cs",
        "src/OpticalSystemDesign.Application/Services/CameraImagingService.Scene3D.cs",
        "src/OpticalSystemDesign.Application/Services/CameraImagingService.Experiments.cs",
        "src/OpticalSystemDesign.App/Services/UiLocalization.CameraPixelSampling.cs",
        "tests/OpticalSystemDesign.Tests/CameraScene3DTests.cs",
        "tests/OpticalSystemDesign.Tests/CameraScene3DWindowTests.cs",
        "tests/OpticalSystemDesign.Tests/CameraPixelAreaTests.cs",
        "tests/OpticalSystemDesign.Tests/CameraPixelAreaWindowTests.cs",
        "samples/camera-imaging/direct-light.camera-scene3d.json",
    ]
    source_paths += [p.relative_to(root).as_posix() for p in
        (root / "src/OpticalSystemDesign.App/Laboratories").glob("CameraImagingWindow*.cs")]
    core = root / "src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    test_core = root / "tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    app_core = root / "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    assert sha(core) == sha(test_core) == sha(app_core)
    audit = json.loads((raw / "documentation.json").read_text(encoding="utf-8-sig"))
    own_docs = {"docs/CAMERA_IMAGING_LAB.md", "docs/USER_GUIDE.md", "docs/README.md", "docs/CURRENT_STATUS.md",
                "docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md", "samples/camera-imaging/README.md",
                "validation/rechecks/2026-10-30/camera-scene3d/README.md"}
    assert not any(r["Path"] in own_docs for r in audit["BrokenLinks"])
    assert not audit["MarkdownFenceIssues"]
    entries = [p for p in raw.rglob("*") if p.is_file()]
    with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(entries):
            z.write(p, p.relative_to(root).as_posix())
    data = {
        "date": "2026-10-30", "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "includesUncommittedChanges": True, "passedDistinctLatest": len(latest), "newScene3DCases": new,
        "failedLatest": 0, "skippedLatest": 0, "batches": batches, "results": list(latest.values()),
        "defaultCoreSha256": sha(core), "testedCoreSha256": sha(test_core), "appCoreSha256": sha(app_core),
        "testAssemblySha256": sha(root / "tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll"),
        "sourceSha256": {name: sha(root / name) for name in sorted(set(source_paths))},
        "evidenceSha256": sha(out / "evidence.zip"),
        "scope": "Bounded 3D direct geometric camera in existing desktop lab; offscreen interaction only",
        "documentationUnrelatedBrokenLinks": audit["BrokenLinks"],
        "limitations": ["No full ImSym/Speos equivalence, measured RAW validation or EMVA certification",
                        "No general scattering, interreflection, 3D diffraction or microlens model",
                        "Original optical reference prescriptions, sampling and tolerances were not rewritten",
                        "Finite quadrature does not certify arbitrary-scene convergence"]
    }
    record.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

data = json.loads(record.read_text(encoding="utf-8"))
for name, digest in data["sourceSha256"].items():
    assert sha(root / name) == digest, name
assert sha(out / "evidence.zip") == data["evidenceSha256"]
with zipfile.ZipFile(out / "evidence.zip") as z:
    assert z.testzip() is None
print(json.dumps({key: data[key] for key in ("passedDistinctLatest", "newScene3DCases", "scope")}, ensure_ascii=False))
