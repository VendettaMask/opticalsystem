"""Seal completed MTF comparisons and controls; do not rerun optical calculations."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/fft-mtf-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/fft-mtf"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def results(name):
    tree = ET.parse(RUN / name)
    return tree.find("t:ResultSummary/t:Counters", NS).attrib, tree.findall("t:Results/t:UnitTestResult", NS)


def cases(name):
    found = {}
    for result in results(name)[1]:
        for line in result.findtext("t:Output/t:StdOut", "", NS).splitlines():
            if line.strip().startswith("{"):
                row = json.loads(line)
                if "Fingerprint" in row:
                    found[row["id"]] = row
    if len(found) != 5:
        raise ValueError("Expected all five original FFT MTF cases")
    return found


policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
prior = read(ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json")
before, after = cases("mtf-before.trx"), cases("mtf-targeted.trx")
archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
case_records = []
with zipfile.ZipFile(archive) as zip:
    for key, actual in after.items():
        original = next(row for row in prior["cases"] if row["id"] == key)
        assert actual["Fingerprint"] == before[key]["Fingerprint"] == original["requestFingerprint"]
        assert actual["violations"] == 0
        assert all(c["metric"]["Coverage"] == 1 for c in actual["curves"])
        source = "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/" + original["lens"]
        assert subprocess.run(["git", "diff", "--quiet", "HEAD", "--", source], cwd=ROOT).returncode == 0
        request_member = "artifacts/validation/afocal-fft-energy-20261027/final-150-v2/" + key + "/workbench/request.json"
        request_bytes = zip.read(request_member)
        request = json.loads(request_bytes)["request"]
        native_prefix = "artifacts/validation/afocal-recompare-20261026/baseline-v4/analyses/" + key + "/native/"
        case_records.append({"id": key, "lens": original["lens"], "configuration": original["configuration"],
            "request": request, "requestFingerprint": actual["Fingerprint"], "sourceSha256": sha(ROOT / source),
            "requestArchiveMember": request_member, "requestMemberSha256": hashlib.sha256(request_bytes).hexdigest(),
            "nativeMembers": [{"member": native_prefix + n, "sha256": hashlib.sha256(zip.read(native_prefix + n)).hexdigest()}
                              for n in ["data.json", "model.json", "captured-settings.json"]],
            "before": before[key], "after": actual,
            "beforeMaximumAbsolute": max(c["metric"]["MaxAbsolute"] for c in before[key]["curves"]),
            "afterMaximumAbsolute": max(c["metric"]["MaxAbsolute"] for c in actual["curves"])})

test_names = set()
final_tests = []
for name in ["mtf-targeted.trx", "mtf-protection.trx"]:
    counters, rows = results(name)
    assert all(row.attrib["outcome"] == "Passed" for row in rows)
    test_names.update(row.attrib["testName"] for row in rows)
    final_tests.append({"file": name, "counters": counters, "sha256": sha(RUN / name)})
core_paths = ["tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/BeamFftPsf20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]
cores = [{"path": p, "sha256": sha(ROOT / p)} for p in core_paths]
assert len({row["sha256"] for row in cores}) == 1
frozen = read(RUN / "frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668
OUT.mkdir(parents=True, exist_ok=True)
members = sorted(RUN.glob("*.trx")) + [RUN / "decomposition.json", RUN / "frozen-integrity.json"]
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for path in members:
        zip.write(path, path.name)
sources = ["src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftMtfPointwiseTests.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftMtfDiagnostics.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftMtfSpectrumInterpolationTests.cs"]
record = {"date": "2026-10-29", "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
          "scope": "Five exact original 64-sample afocal FFT MTF requests and 30 T/S curves against existing OpticStudio 2026 R1 captures.",
          "rootCause": "Linear spectral regridding onto one coarse wavelength axis followed by a second spline loses curvature. Interpolate each complex monochromatic OTF to the requested display frequency first, then weight and derive the response.",
          "samplingPrescriptionAndToleranceUnchanged": True, "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
          "nativeArchive": str(archive.relative_to(ROOT)).replace('\\', '/'), "nativeArchiveSha256": sha(archive),
          "cases": case_records, "curveCount": sum(len(row["after"]["curves"]) for row in case_records),
          "finalTests": final_tests, "distinctPassingTests": len(test_names),
          "coreBinaries": cores, "sourceIdentities": [{"path": p, "sha256": sha(ROOT / p)} for p in sources],
          "decomposition": read(RUN / "decomposition.json"), "frozenIntegrity": frozen,
          "appBuild": {"defaultOutput": True, "exitCode": 0, "warnings": 0, "errors": 0, "appLaunched": False},
          "earlierFailures": ["Diagnostic first compile referenced internal SpotAnalysisEngine directly; corrected diagnostic reflection before any optical test ran.",
                              "Initial offline script allocated integer weights then attempted in-place floating normalization; corrected dtype before collecting diagnosis.",
                              "mtf-before.trx: three real optical failures and two original pointwise passes; NRMSE passed all five historically."],
          "limitations": ["Unchanged pointwise gates met; residuals are not zero and one prism-control maximum increases slightly within the same gates.",
                          "Not a rerun of the full 150-request matrix, 95-file corpus, GUI/CUDA, native recapture or release."],
          "evidenceArchiveSha256": sha(OUT / "evidence.zip"),
          "evidenceMembers": [{"file": p.name, "sha256": sha(p), "bytes": p.stat().st_size} for p in members]}
doc = RUN / "documentation.json"
if doc.exists():
    data = read(doc)
    record["documentation"] = {k: data[k] for k in ["Documents", "BrokenLinks", "MarkdownFenceIssues", "LocalOnlyLinks"]}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(test_names), "curves": record["curveCount"],
    "violationsBefore": sum(row["before"]["violations"] for row in case_records),
    "violationsAfter": sum(row["after"]["violations"] for row in case_records)}))
