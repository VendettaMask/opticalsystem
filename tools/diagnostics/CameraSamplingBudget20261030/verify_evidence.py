"""Offline identity/output/accounting checks; all optical calculations run in the shared C# Core."""
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path.cwd()
raw = root / "artifacts/validation/camera-sampling-budget-20261030"
out = root / "validation/rechecks/2026-10-30/camera-sampling-budget"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def experiment(path):
    with zipfile.ZipFile(path) as z:
        payload = z.read("experiment.json")
        assert hashlib.sha256(payload).hexdigest() == z.read("sha256.txt").decode("ascii")
        return json.loads(payload)

def levels(c):
    return [c] + (c.get("FurtherRefinements") or [])

def equal_frame(a, b):
    for key in ("Settings", "Bands", "PhotoElectrons", "NoiselessRawDn", "LocalBayerPattern", "Diagnostics", "Execution"):
        assert a[key] == b[key], key

if "--capture" in sys.argv:
    out.mkdir(parents=True, exist_ok=True)
    latest, batches = {}, []
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    for name in ("first.trx", "integration.trx", "delivery.trx", "final-core.trx"):
        cases = [dict(e.attrib) for e in ET.parse(raw / name).findall(".//t:UnitTestResult", ns)]
        batches.append({"file": name, "counts": {s: sum(e["outcome"] == s for e in cases) for s in sorted({e["outcome"] for e in cases})}})
        for case in cases: latest[case["testName"]] = {**case, "batch": name}
    assert all(c["outcome"] == "Passed" for c in latest.values())
    old = experiment(root / "artifacts/validation/camera-scene-diffraction-20261030/numeric/scene-diffraction-32.camera-experiment.zip")
    saved = experiment(raw / "numeric-ladder/convergence.camera-experiment.zip")
    assert saved["B"] == old["B"] and saved.get("A") == old.get("A"), "Original full-frame A/B changed"
    report = json.loads((raw / "numeric-ladder/convergence.json").read_text(encoding="utf-8"))
    assert report == saved["SamplingReport"]
    assert len(report["Comparisons"]) == 2
    assert report["SavedElectronDifference"]["MaximumAbsolute"] == 0
    assert all(d["MaximumAbsolute"] == 0 for d in report["SavedBandDifferences"])
    assert "DELIVERY: all pupil and scene levels completed" in (raw / "ladder.log").read_text(encoding="utf-8-sig")
    fresh = json.loads((raw / "numeric-ladder/fresh-replay.json").read_text(encoding="utf-8"))
    assert "REPLAY: every band, electron, noiseless RAW" in (raw / "replay.log").read_text(encoding="utf-8-sig")
    assert fresh["SourceRunSha256"] == report["SourceRunSha256"] and fresh["Region"] == report["Region"]
    equal_frame(fresh["Baseline"], report["Baseline"])
    summary, frames = [], [report["Baseline"]]
    for c in report["Comparisons"]:
        chain = levels(c); other = levels(next(v for v in fresh["Comparisons"] if v["Axis"] == c["Axis"]))
        expected = [24, 48, 64] if c["Axis"] == "Pupil" else [2, 4]
        key = "PupilSamplesAcross" if c["Axis"] == "Pupil" else "SceneSubdivisions"
        assert [v["RequestedSettings"]["Diffraction"][key] for v in chain] == expected
        assert len(chain) == len(other)
        for a, b in zip(chain, other):
            assert a["Status"] == b["Status"] == "Compared"
            for field in ("RequestedSettings", "BandDifferences", "ElectronDifference", "RawDifference"): assert a[field] == b[field], field
            equal_frame(a["Refined"], b["Refined"])
            unchanged = dict(a["RequestedSettings"]["Diffraction"]); unchanged[key] = report["Baseline"]["Settings"]["Diffraction"][key]
            assert unchanged == report["Baseline"]["Settings"]["Diffraction"]
            assert a["RequestedSettings"]["PixelSampling"] == report["Baseline"]["Settings"]["PixelSampling"]
            summary.append({"axis": c["Axis"], "value": a["RequestedSettings"]["Diffraction"][key], "electrons": a["ElectronDifference"], "execution": a["Refined"]["Execution"]})
            frames.append(a["Refined"])
    for frame in frames:
        e = frame["Execution"]
        assert e["Policy"] == "RegionExactSupport/2"
        assert e["MaximumRays"] == 67108864 and e["MaximumSurfaceInteractions"] == 400000000
        assert 0 <= e["LaunchedRays"] <= e["MaximumRays"] and 0 <= e["SurfaceInteractions"] <= e["MaximumSurfaceInteractions"]
        assert frame["Diagnostics"]["SurfaceSamples"] <= 65536 and frame["Diagnostics"]["ScalarCellEvaluations"] <= 200000000
    with zipfile.ZipFile(root / "validation/rechecks/2026-10-30/camera-sampling/evidence.zip") as z:
        prior = z.read("artifacts/validation/camera-sampling-20261030/numeric/scene-32-sampling.camera-experiment.zip")
        assert prior == (root / "tests/OpticalSystemDesign.Tests/Fixtures/camera-sampling-v1-frozen.zip").read_bytes()
    unchanged = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs", "src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs", "src/OpticalSystemDesign.Core/Optic.cs", "validation/zemax"]
    subprocess.run(["git", "diff", "--exit-code", "--", *unchanged], check=True, capture_output=True)
    audit = json.loads((raw / "documentation.json").read_text(encoding="utf-8-sig")); assert not audit["BrokenLinks"] and not audit["MarkdownFenceIssues"]
    core_paths = {"default": root / "src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
                  "app": root / "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
                  "tested": raw / "test-bin/OpticalSystemDesign.Core.dll",
                  "freshReplay": root / "tools/diagnostics/CameraSamplingBudget20261030/bin/Release/net10.0/OpticalSystemDesign.Core.dll"}
    assert len({sha(core_paths[k]) for k in ("default", "app", "tested")}) == 1, "Final App and tested Core differ"
    provenance = json.loads((raw / "core-provenance.json").read_text(encoding="utf-8-sig"))
    assert provenance["ReplayPdbSha256"] == sha(core_paths["freshReplay"].with_suffix(".pdb"))
    assert provenance["FinalPdbSha256"] == sha(core_paths["default"].with_suffix(".pdb"))
    assert all(p.endswith(("/Services/CenteredPrescriptionScaling.cs", "/Coatings/AdaptiveThinFilmSpectrum.cs")) for p in provenance["ChangedDocuments"])
    build = (raw / "build-final.log").read_text(encoding="utf-8-sig"); assert "0 个警告" in build and "0 个错误" in build
    sources = ["src/OpticalSystemDesign.Core/Analysis/Imaging/DiffractionCameraRenderer.cs",
               "src/OpticalSystemDesign.Application/Contracts/CameraSamplingContracts.cs", "src/OpticalSystemDesign.Application/Contracts/CameraImagingContracts.cs",
               "src/OpticalSystemDesign.Application/Services/CameraImagingService.Sampling.cs", "src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Sampling.cs",
               "tests/OpticalSystemDesign.Tests/CameraSamplingTests.cs", "tests/OpticalSystemDesign.Tests/CameraSamplingBudgetTests.cs", "tests/OpticalSystemDesign.Tests/CameraSamplingWindowTests.cs",
               "tests/OpticalSystemDesign.Tests/Fixtures/camera-sampling-v1-frozen.zip", "tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj",
               "tools/diagnostics/CameraSamplingBudget20261030/Program.cs", "tools/diagnostics/CameraSamplingBudget20261030/CameraSamplingBudget20261030.csproj",
               "tools/diagnostics/CameraSamplingBudget20261030/Provenance/Program.cs", "tools/diagnostics/CameraSamplingBudget20261030/Provenance/Provenance.csproj",
               "tools/diagnostics/CameraSamplingBudget20261030/verify_evidence.py"]
    with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(raw.rglob("*")):
            if p.is_file() and "test-bin" not in p.relative_to(raw).parts and p.name != "evidence-check.log": z.write(p, p.relative_to(root).as_posix())
    # The two independent axes each computed a baseline. Count both real executions in the diagnostic ledger.
    costs = [f["Execution"] for f in frames] + [report["Baseline"]["Execution"]]
    data = {"date": "2026-10-30", "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(), "includesUncommittedChanges": True,
            "passedDistinctLatest": len(latest), "failedLatest": 0, "skippedLatest": 0, "batches": batches, "results": list(latest.values()),
            "sourceSha256": {p: sha(root / p) for p in sources}, "coreSha256": {k: sha(v) for k, v in core_paths.items()},
            "initialDeliveryCore": json.loads((raw / "delivery-core.json").read_text(encoding="utf-8-sig")), "compilationSourceDifferences": provenance["ChangedDocuments"], "evidenceSha256": sha(out / "evidence.zip"),
            "sourceRunSha256": report["SourceRunSha256"], "region": report["Region"], "levels": summary,
            "pupilConvergenceCertified": False,
            "perDeliveryOrReplayLedger": {"renders": 7, "baselineRenders": 2,
                **{k: sum(e[k] for e in costs) for k in ("LaunchedRays", "SurfaceInteractions", "AvoidedAuxiliaryRays", "SourceCandidateVisits")},
                **{k: sum(f["Diagnostics"][k] for f in frames + [report["Baseline"]]) for k in ("SurfaceSamples", "ScalarCellEvaluations")}},
            "scope": "Exact empty-support auxiliary scheduling; bounded local pupil/scene ladders; every-level frozen save/replay; original full renderer unchanged",
            "limitations": ["Pupil 48->64 still changes 1.542003% in electron relative L1; do not claim pupil convergence", "Local independent-axis comparisons do not certify whole-frame or joint high-sampling convergence", "No physical tolerance changed or automatic precision pass", "No native desktop, full repository/170-case run, release or push"]}
    (out / "verification.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

data = json.loads((out / "verification.json").read_text(encoding="utf-8"))
for p, digest in data["sourceSha256"].items(): assert sha(root / p) == digest, p
assert sha(out / "evidence.zip") == data["evidenceSha256"]
with zipfile.ZipFile(out / "evidence.zip") as z: assert z.testzip() is None
print(json.dumps({k: data[k] for k in ("passedDistinctLatest", "perDeliveryOrReplayLedger", "scope")}))
