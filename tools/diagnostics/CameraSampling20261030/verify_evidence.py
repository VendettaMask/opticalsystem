"""Offline evidence checks only. Product optics and sensor calculations are shared C# Core."""
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
raw = root / "artifacts/validation/camera-sampling-20261030"
out = root / "validation/rechecks/2026-10-30/camera-sampling"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def experiment(path):
    with zipfile.ZipFile(path) as z:
        payload = z.read("experiment.json")
        assert hashlib.sha256(payload).hexdigest() == z.read("sha256.txt").decode("ascii")
        return json.loads(payload)

if "--capture" in sys.argv:
    out.mkdir(parents=True, exist_ok=True)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    latest, batches = {}, []
    for name in ("first.trx", "integration.trx", "final.trx", "scalar.trx", "ui-final.trx"):
        cases = [dict(r.attrib) for r in ET.parse(raw / name).findall(".//t:UnitTestResult", ns)]
        batches.append({"file": name, "counts": {s: sum(r["outcome"] == s for r in cases) for s in sorted({r["outcome"] for r in cases})}})
        for r in cases:
            latest[r["testName"]] = {**r, "batch": name}
    assert all(r["outcome"] == "Passed" for r in latest.values())
    old = experiment(root / "artifacts/validation/camera-scene-diffraction-20261030/numeric/scene-diffraction-32.camera-experiment.zip")
    saved = experiment(raw / "numeric/scene-32-sampling.camera-experiment.zip")
    assert old["B"] == saved["B"] and old.get("A") == saved.get("A"), "Original full-frame A/B changed"
    report = json.loads((raw / "numeric/scene-32-sampling.json").read_text(encoding="utf-8"))
    assert report == saved["SamplingReport"]
    fresh = json.loads((raw / "numeric/scene-32-fresh-pixel-replay.json").read_text(encoding="utf-8"))
    assert fresh["SourceRunSha256"] == report["SourceRunSha256"] and fresh["Region"] == report["Region"]
    pixel = next(c for c in report["Comparisons"] if c["Axis"] == "Pixel")
    for a, b in ((fresh["Baseline"], report["Baseline"]), (fresh["Comparisons"][0]["Refined"], pixel["Refined"])):
        for key in ("Settings", "Bands", "PhotoElectrons", "NoiselessRawDn", "LocalBayerPattern", "Diagnostics"): assert a[key] == b[key], key
    assert len(report["Comparisons"]) == 5
    assert report["SavedElectronDifference"]["MaximumAbsolute"] == 0
    assert all(d["MaximumAbsolute"] == 0 for d in report["SavedBandDifferences"])
    assert "All five axes attempted; frozen regional baseline and reopened pixel comparison match exactly." in (raw / "scene-32.log").read_text(encoding="utf-8-sig")
    assert "All five axes attempted; frozen regional baseline and reopened pixel comparison match exactly." in (raw / "replay.log").read_text(encoding="utf-8-sig")
    with zipfile.ZipFile(root / "validation/rechecks/2026-10-30/camera-scene-diffraction/evidence.zip") as z:
        original = z.read("artifacts/validation/camera-scene-diffraction-20261030/numeric/diffraction-mixed-AB.camera-experiment.zip")
        assert original == (root / "tests/OpticalSystemDesign.Tests/Fixtures/camera-scene-v8-frozen.zip").read_bytes()
    unchanged = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs", "src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs", "src/OpticalSystemDesign.Core/Optic.cs", "validation/zemax"]
    subprocess.run(["git", "diff", "--exit-code", "--", *unchanged], check=True, capture_output=True)
    audit = json.loads((raw / "documentation.json").read_text(encoding="utf-8-sig"))
    assert not audit["BrokenLinks"] and not audit["MarkdownFenceIssues"]
    core_paths = [root / p / "OpticalSystemDesign.Core.dll" for p in
                  ("src/OpticalSystemDesign.Core/bin/Release/net10.0", "src/OpticalSystemDesign.App/bin/Release/net10.0", "artifacts/validation/camera-sampling-20261030/test-bin")]
    assert len({sha(p) for p in core_paths}) == 1, "Default and tested Core differ"
    assert "0 个警告" in (raw / "build-final.log").read_text(encoding="utf-8-sig")
    assert "0 个错误" in (raw / "build-final.log").read_text(encoding="utf-8-sig")
    sources = ["src/OpticalSystemDesign.Core/Analysis/ScalarHuygensIntegral.cs", "src/OpticalSystemDesign.Core/Analysis/Imaging/DiffractionCameraRenderer.cs",
               "src/OpticalSystemDesign.Application/Contracts/CameraImagingContracts.cs", "src/OpticalSystemDesign.Application/Contracts/CameraSamplingContracts.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.Sampling.cs", "src/OpticalSystemDesign.Application/Services/CameraImagingService.Scene3D.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.Experiments.cs", "src/OpticalSystemDesign.App/Services/UiLocalization.CameraPixelSampling.cs",
               "tests/OpticalSystemDesign.Tests/CameraSamplingTests.cs", "tests/OpticalSystemDesign.Tests/CameraSamplingWindowTests.cs",
               "tests/OpticalSystemDesign.Tests/CameraSceneDiffractionTests.cs", "tests/OpticalSystemDesign.Tests/CameraExperimentTests.cs",
               "tests/OpticalSystemDesign.Tests/Fixtures/camera-scene-v7-frozen.zip",
               "tests/OpticalSystemDesign.Tests/Fixtures/camera-scene-v8-frozen.zip", "tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj",
               "tools/diagnostics/CameraSampling20261030/Program.cs", "tools/diagnostics/CameraSampling20261030/CameraSampling20261030.csproj",
               "tools/diagnostics/CameraSampling20261030/verify_evidence.py"]
    sources += [p.relative_to(root).as_posix() for p in (root / "src/OpticalSystemDesign.App/Laboratories").glob("CameraImagingWindow*.cs")]
    files = [p for p in raw.rglob("*") if p.is_file() and "test-bin" not in p.relative_to(raw).parts and p.name != "evidence-check.log"]
    with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(files): z.write(p, p.relative_to(root).as_posix())
    summary = [{"axis": c["Axis"], "status": c["Status"], "message": c["Message"], "electrons": c["ElectronDifference"]} for c in report["Comparisons"]]
    data = {"date": "2026-10-30", "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(), "includesUncommittedChanges": True,
            "passedDistinctLatest": len(latest), "failedLatest": 0, "skippedLatest": 0, "batches": batches, "results": list(latest.values()),
            "sourceSha256": {p: sha(root / p) for p in sorted(set(sources))}, "coreSha256": dict(zip(("default", "app", "tested"), map(sha, core_paths))),
            "evidenceSha256": sha(out / "evidence.zip"), "originalV8FixtureSha256": sha(root / "tests/OpticalSystemDesign.Tests/Fixtures/camera-scene-v8-frozen.zip"),
            "region": report["Region"], "sourceRunSha256": report["SourceRunSha256"], "sampling": summary, "unchangedTrackedPaths": unchanged,
            "scope": "Local original-coordinate sampling diagnostics, frozen full-frame B, original sensor/ISP, saved report/recheck and offscreen UI",
            "limitations": ["Local diagnostics do not certify whole-frame convergence or change physical tolerances", "Original full-render budgets unchanged; no high-resolution tiling", "No native desktop, commercial certification, full repository run, release or push"]}
    (out / "verification.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

data = json.loads((out / "verification.json").read_text(encoding="utf-8"))
for p, digest in data["sourceSha256"].items(): assert sha(root / p) == digest, p
assert sha(out / "evidence.zip") == data["evidenceSha256"]
with zipfile.ZipFile(out / "evidence.zip") as z: assert z.testzip() is None
print(json.dumps({k: data[k] for k in ("passedDistinctLatest", "scope")}))
