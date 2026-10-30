"""Read scoped TRX identities and archive camera evidence without replacing historical failures."""
import hashlib
import json
import pathlib
import subprocess
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
raw = root / "artifacts/validation/camera-pixel-area-20261030"
out = root / "validation/rechecks/2026-10-30/camera-pixel-area"
out.mkdir(parents=True, exist_ok=True)
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
runs = []
latest = {}
for name in ["pixel-area-first.trx", "camera-regression.trx", "pixel-area-final-v1.trx", "pixel-area-final.trx", "pixel-area-ui-final.trx"]:
    tree = ET.parse(raw / name)
    rows = [dict(item.attrib) for item in tree.findall(".//t:UnitTestResult", ns)]
    scoped = [item for item in rows if item["testName"].startswith("OpticalSystemDesign.Tests.Camera")]
    for item in scoped:
        latest[item["testName"]] = {"outcome": item["outcome"], "run": name, "duration": item.get("duration")}
    runs.append({"file": name, "allCounters": tree.find(".//t:Counters", ns).attrib,
                 "cameraCount": len(scoped), "incidental": [r for r in rows if r not in scoped]})
assert all(item["outcome"] == "Passed" for item in latest.values()), "Unresolved scoped outcome"
assert int(runs[-2]["allCounters"]["passed"]) == 32, "Final optical/service cohort incomplete"
assert int(runs[-1]["allCounters"]["passed"]) == 3, "Final UI cohort incomplete"

paths = [
    *root.glob("src/OpticalSystemDesign.Core/Analysis/Imaging/*.cs"),
    root / "src/OpticalSystemDesign.Core/Analysis/ImageSimulationEngine.cs",
    root / "src/OpticalSystemDesign.Core/Analysis/AnalysisResourceLimits.cs",
    root / "src/OpticalSystemDesign.Application/Contracts/CameraImagingContracts.cs",
    *root.glob("src/OpticalSystemDesign.Application/Services/CameraImagingService*.cs"),
    *root.glob("src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow*.cs"),
    root / "src/OpticalSystemDesign.App/Services/UiLocalization.cs",
    root / "src/OpticalSystemDesign.App/Services/UiLocalization.CameraPixelSampling.cs",
    *root.glob("tests/OpticalSystemDesign.Tests/Camera*.cs"),
    root / "tests/OpticalSystemDesign.Tests/Fixtures/camera-flir-bfs-u3-23s6-points.json",
    root / "tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj",
]
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
source = {p.relative_to(root).as_posix(): digest(p) for p in sorted(set(paths))}
binaries = {f"{name}.dll": digest(root / f"src/{name}/bin/Release/net10.0/{name}.dll")
            for name in ["OpticalSystemDesign.Core", "OpticalSystemDesign.Application", "OpticalSystemDesign.App"]}
tested_binaries = {name: digest(root / "tests/OpticalSystemDesign.Tests/bin/Release/net10.0" / name) for name in binaries}
assert tested_binaries == binaries, "Default outputs differ from the tested assemblies; review the binary cohort"
audit = json.loads((root / "artifacts/validation/camera-pixel-area-documentation.json").read_text(encoding="utf-8-sig"))
camera_documents = {"docs/CAMERA_IMAGING_LAB.md", "docs/USER_GUIDE.md", "docs/CURRENT_STATUS.md", "samples/camera-imaging/README.md",
                    "validation/rechecks/2026-10-30/camera-pixel-area/README.md"}
camera_links = [item for item in audit["BrokenLinks"] if item["Path"] in camera_documents]
assert not camera_links, "Camera documentation has broken links"
verification = {
    "date": "2026-10-30", "sourceHead": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "sourceIncludesUncommittedChanges": True, "scope": "Camera models and workflow; no full repository or native desktop acceptance",
    "latestDistinctCameraPassed": len(latest), "latestCameraOutcomes": latest,
    "runs": runs, "sourcesSha256": source, "binariesSha256": binaries, "testedBinariesSha256": tested_binaries,
    "documentation": {"cameraBrokenLinks": camera_links, "repositoryBrokenLinks": audit["BrokenLinks"],
                      "scope": "Other concurrent initial-structure work is preserved and reported separately"},
    "publishedData": json.loads((raw / "public-source/source.json").read_text(encoding="utf-8")),
    "limits": ["Published parameter-point controls, not measured RAW replay or a full sensor profile",
               "Current-lens area sampling is 2x2 within original work limits; 4x4 is ideal-optics only",
               "Finite centered-square quadrature; no universal convergence claim", "No per-pixel correction or EMVA certification"]
}
(out / "verification.json").write_text(json.dumps(verification, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
entries = [*raw.glob("*.trx"), *raw.glob("numeric/*"), *raw.glob("screenshots*/*.png"), raw / "public-source/source.json"]
entries += list((root / "artifacts/validation").glob("camera-pixel-area*.log"))
entries += list((root / "artifacts/validation").glob("camera-pixel-area-documentation.json"))
entries += [root / "tests/OpticalSystemDesign.Tests/Fixtures/camera-flir-bfs-u3-23s6-points.json"]
with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(set(entries)):
        archive.write(path, path.relative_to(root).as_posix())
manifest = {p.relative_to(root).as_posix(): {"bytes": p.stat().st_size, "sha256": digest(p)} for p in sorted(set(entries))}
(out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"latestDistinctCameraPassed": len(latest), "archiveBytes": (out / "evidence.zip").stat().st_size}))
