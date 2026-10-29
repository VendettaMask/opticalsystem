"""Seal existing OPD runs. Python is offline evidence tooling, never product computation."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/opd-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/opd-reference-plane"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def results(name):
    tree = ET.parse(RUN / name)
    return tree.find("t:ResultSummary/t:Counters", NS).attrib, tree.findall("t:Results/t:UnitTestResult", NS)


def cases(name):
    found = {}
    for result in results(name)[1]:
        text = result.findtext("t:Output/t:StdOut", "", NS).strip()
        if text.startswith("{"):
            row = json.loads(text)
            if "fingerprint" in row:
                found[row["id"]] = row
    assert len(found) == 20
    return found


policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
prior = read(ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json")
before, after = cases("opd-baseline.trx"), cases("opd-final.trx")
archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
records, decomposition, replay = [], [], []
with zipfile.ZipFile(archive) as zip:
    for key, actual in sorted(after.items()):
        original = next(row for row in prior["cases"] if row["id"] == key)
        assert actual["fingerprint"] == before[key]["fingerprint"] == original["requestFingerprint"]
        assert actual["violations"] == 0
        assert all(c["metric"]["coverage"] == 1 and c["metric"]["conclusion"] == "Pass" for c in actual["curves"])
        source = "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/" + original["lens"]
        assert subprocess.run(["git", "diff", "--quiet", "HEAD", "--", source], cwd=ROOT).returncode == 0
        fixture = ROOT / "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/Fixtures/afocal-repair/sources" / original["lens"]
        assert sha(fixture) == sha(ROOT / source)
        request_member = "artifacts/validation/afocal-fft-energy-20261027/final-150-v2/" + key + "/workbench/request.json"
        request_bytes = zip.read(request_member)
        request = json.loads(request_bytes)["request"]
        assert request["rayCount"] == 20 and request["workbenchSettings"]["NumberOfRays"] == "20"
        native_prefix = "artifacts/validation/afocal-recompare-20261026/expanded/" + key + "/native/"
        records.append({"id": key, "lens": original["lens"], "configuration": original["configuration"],
            "request": request, "requestFingerprint": actual["fingerprint"], "sourceSha256": sha(ROOT / source),
            "requestArchiveMember": request_member, "requestMemberSha256": hashlib.sha256(request_bytes).hexdigest(),
            "nativeMembers": [{"member": native_prefix + n, "sha256": hashlib.sha256(zip.read(native_prefix + n)).hexdigest()}
                              for n in ["data.json", "model.json", "captured-settings.json"]],
            "before": before[key], "after": actual,
            "beforeMaximumAbsoluteWaves": max(c["metric"]["maxAbsolute"] for c in before[key]["curves"]),
            "afterMaximumAbsoluteWaves": max(c["metric"]["maxAbsolute"] for c in actual["curves"])})
        diagnostic = RUN / "before" / (key + ".json")
        if "riflescope" not in key:
            continue
        data = read(diagnostic)
        reference = data["expected"]["series"]
        def error(values):
            return [max(abs(values[41 * k + i] - y) for i, y in enumerate(reference[k]["y"])) for k in range(2)]
        decomposition.append({"id": key, "selectedWavelengthPupilPositionMm": data["selectedDistance"],
            "primaryWavelengthPupilPositionMm": data["primaryDistance"],
            "originalMaximumByFanWaves": error([s["opdWaves"] for s in data["selected"]["samples"]]),
            "wrongPrimaryChiefMaximumByFanWaves": error([s["opdWaves"] for s in data["primaryReference"]["samples"]]),
            "onlyAxialDatumCorrectedMaximumByFanWaves": error(data["distanceOnly"])})
        # Keep traced samples and diagnostic predictions, referencing existing native bytes above.
        replay.append({k: v for k, v in data.items() if k not in ["actual", "expected"]})

(RUN / "decomposition.json").write_text(json.dumps(decomposition, indent=2) + "\n", encoding="utf-8")
(RUN / "diagnostic-replay.json").write_text(json.dumps(replay, indent=2) + "\n", encoding="utf-8")
test_names, final_tests = set(), []
for name in ["opd-final.trx", "opd-protection.trx"]:
    counters, rows = results(name)
    assert all(row.attrib["outcome"] == "Passed" for row in rows)
    test_names.update(row.attrib["testName"] for row in rows)
    final_tests.append({"file": name, "counters": counters, "sha256": sha(RUN / name)})
cores = [{"path": p, "sha256": sha(ROOT / p)} for p in [
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/Opd20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]]
assert len({row["sha256"] for row in cores}) == 1
frozen = read(RUN / "frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668
sources = ["src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalOpdPointwiseTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalOpdReferencePlaneTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/OpdReferenceDiagnostics.cs",
    "tools/diagnostics/Opd20261029/FocusedOpdRegression.csproj",
    "tools/diagnostics/Opd20261029/Collect-Evidence.py"]
members = sorted(RUN.glob("*.trx")) + [RUN / n for n in ["decomposition.json", "diagnostic-replay.json",
    "frozen-integrity.json", "WavefrontEngine.before.cs", "core-before.json", "app-build.log"]]
OUT.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for path in members:
        zip.write(path, path.name)
    for path in sources:
        zip.write(ROOT / path, "source/" + path)
record = {"date": "2026-10-29", "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "scope": "Twenty exact original afocal OPD requests, 40 fans, 41 nodes per fan, against existing OpticStudio 2026 R1 captures.",
    "rootCause": "A selected reference chief also changed the paraxial exit-pupil axial datum to that wavelength. Keep the system primary-wavelength pupil position while using the selected reference chief direction.",
    "samplingPrescriptionAndToleranceUnchanged": True, "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
    "nativeArchive": str(archive.relative_to(ROOT)).replace('\\', '/'), "nativeArchiveSha256": sha(archive),
    "cases": records, "decomposition": decomposition, "finalTests": final_tests, "distinctPassingTests": len(test_names),
    "coreBinaries": cores, "sourceIdentities": [{"path": p, "sha256": sha(ROOT / p)} for p in sources],
    "frozenIntegrity": frozen, "appBuild": {"defaultOutput": True, "exitCode": 0, "warnings": 0, "errors": 0, "appLaunched": False},
    "earlierFailures": ["Initial diagnostic compile used the wrong Vector3D namespace; no optical tests ran.",
        "opd-before.trx includes diagnostic failures from unnormalized fields and non-finite JSON metrics; not an optical baseline.",
        "opd-baseline.trx cleanly reproduces two pointwise failures and eighteen passes; historical aggregate NRMSE passed all twenty.",
        "Independent-control compile used the wrong RayTraceSample namespace; corrected before running.",
        "opd-targeted.trx has nine snapshot-serialization failures after optical assertions passed; corrected JSON number handling in the test."],
    "limitations": ["Residuals remain nonzero within unchanged absolute/relative and NRMSE gates.",
        "Not a rerun of the full 150-request matrix, 95-file corpus, GUI/CUDA, native recapture or release."],
    "evidenceArchiveSha256": sha(OUT / "evidence.zip"),
    "evidenceMembers": [{"file": p.name, "sha256": sha(p), "bytes": p.stat().st_size} for p in members]}
if (RUN / "documentation.json").exists():
    doc = read(RUN / "documentation.json")
    record["documentation"] = {k: doc[k] for k in ["Documents", "BrokenLinks", "MarkdownFenceIssues", "LocalOnlyLinks"]}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(test_names),
    "violationsBefore": sum(row["before"]["violations"] for row in records),
    "violationsAfter": sum(row["after"]["violations"] for row in records),
    "maximumAfterWaves": max(row["afterMaximumAbsoluteWaves"] for row in records)}))
