"""Capture or read-only verify the library bridge cohort; never run product physics in Python."""
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
out = root / "validation/rechecks/2026-10-30/hybrid-camera-bridge"
raw = root / "artifacts/validation/hybrid-camera-bridge-20261030"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
verification_path = out / "verification.json"
if "--capture" in sys.argv:
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    tree = ET.parse(raw / "bridge-final.trx")
    results = [dict(r.attrib) for r in tree.findall(".//t:UnitTestResult", ns)]
    assert len(results) == 44 and all(r["outcome"] == "Passed" for r in results)
    assert sum("NonSequentialCameraSensorTests." in r["testName"] for r in results) == 13
    source_names = [
        "src/OpticalSystemDesign.Core/Analysis/Imaging/SpectralDetectorPower.cs",
        "src/OpticalSystemDesign.Core/NonSequential/NonSequentialCameraSensor.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/CameraSensorPipeline.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/CameraContinuousSpectrum.cs",
        "src/OpticalSystemDesign.Core/Analysis/Imaging/SensorSpectralResponse.cs",
        "src/OpticalSystemDesign.Core/NonSequential/NonSequentialDetectorAccumulator.cs",
        "tests/OpticalSystemDesign.Tests/NonSequentialCameraSensorTests.cs",
    ]
    core = root / "src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    tested_core = root / "tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    assert sha(core) == sha(tested_core), "Tested Core differs from default output"
    verification = {
        "date": "2026-10-30", "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "includesUncommittedChanges": True, "scope": "Core library bridge only; no desktop scene/camera integration",
        "passed": 44, "newBridgeCases": 13, "existingCameraCases": 31, "failed": 0, "skipped": 0,
        "results": results, "sourceSha256": {name: sha(root / name) for name in source_names},
        "defaultCoreSha256": sha(core), "testedCoreSha256": sha(tested_core),
        "testAssemblySha256": sha(root / "tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Tests.dll"),
        "limits": ["Matching immutable NSC snapshot is a caller requirement; application session binding is still planned",
                   "No desktop entry, full 3D scene renderer, formal scatter or full ImSym/Speos equivalence",
                   "Existing pixel-fill sampling degeneracy remains open", "No full repository or native certification"]
    }
    audit_path = root / "artifacts/validation/hybrid-camera-bridge-documentation.json"
    if audit_path.exists():
        audit = json.loads(audit_path.read_text(encoding="utf-8-sig"))
        verification["documentationBrokenLinks"] = audit["BrokenLinks"]
        verification["documentationFenceIssues"] = audit["MarkdownFenceIssues"]
    out.mkdir(parents=True, exist_ok=True)
    verification_path.write_text(json.dumps(verification, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    entries = [*raw.glob("*.trx"), *raw.glob("numeric/*.json"),
               *(root / "artifacts/validation").glob("hybrid-camera-bridge*.log")]
    if audit_path.exists():
        entries.append(audit_path)
    with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(set(entries)):
            z.write(p, p.relative_to(root).as_posix())
    verification["evidenceSha256"] = sha(out / "evidence.zip")
    verification_path.write_text(json.dumps(verification, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

verification = json.loads(verification_path.read_text(encoding="utf-8"))
for path, digest in verification["sourceSha256"].items():
    assert sha(root / path) == digest, path
assert sha(out / "evidence.zip") == verification["evidenceSha256"]
with zipfile.ZipFile(out / "evidence.zip") as z:
    assert z.testzip() is None
print(json.dumps({"passed": verification["passed"], "newBridgeCases": verification["newBridgeCases"],
                  "evidenceBytes": (out / "evidence.zip").stat().st_size, "scope": verification["scope"]}))
