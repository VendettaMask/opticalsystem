"""Seal completed runs and immutable reference identities; never calculate product optics."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/huygens-focus-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/huygens-through-focus-mtf"
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
        if "HuygensThroughFocusPointwiseTests" not in result.attrib["testName"]:
            continue
        row = json.loads(result.findtext("t:Output/t:StdOut", "", NS))
        found[row["id"]] = row
    assert len(found) == 10
    return found


policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
prior = read(ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json")
before, after = cases("huygens-focus-baseline.trx"), cases("huygens-focus-final.trx")
archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
records = []
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
        fixed = key.startswith("fixed-")
        branch = "final-fixed-v2" if fixed else "final-150-v2"
        request_member = f"artifacts/validation/afocal-fft-energy-20261027/{branch}/{key}/workbench/request.json"
        request_bytes = zip.read(request_member)
        request = json.loads(request_bytes)["request"]
        assert request["pupilSampling"] == (64 if fixed else 32)
        assert request["imageSampling"] == (128 if fixed else 32)
        assert request["workbenchSettings"]["Steps"] == "5"
        branch = "fixed-grid-controls" if fixed else "expanded"
        native_prefix = f"artifacts/validation/afocal-recompare-20261026/{branch}/{key}/native/"
        records.append({"id": key, "lens": original["lens"], "configuration": original["configuration"],
            "request": request, "requestFingerprint": actual["fingerprint"], "sourceSha256": sha(ROOT / source),
            "requestArchiveMember": request_member, "requestMemberSha256": hashlib.sha256(request_bytes).hexdigest(),
            "nativeMembers": [{"member": native_prefix + n, "sha256": hashlib.sha256(zip.read(native_prefix + n)).hexdigest()}
                              for n in ["data.json", "model.json", "captured-settings.json"]],
            "before": before[key], "after": actual,
            "beforeMaximumAbsolute": max(c["metric"]["maxAbsolute"] for c in before[key]["curves"]),
            "afterMaximumAbsolute": max(c["metric"]["maxAbsolute"] for c in actual["curves"])})
test_names, final_tests = set(), []
for name in ["huygens-focus-final.trx", "huygens-focus-protection.trx"]:
    counters, rows = results(name)
    assert all(row.attrib["outcome"] == "Passed" for row in rows)
    test_names.update(row.attrib["testName"] for row in rows)
    final_tests.append({"file": name, "counters": counters, "sha256": sha(RUN / name)})
cores = [{"path": p, "sha256": sha(ROOT / p)} for p in [
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/HuygensFocus20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]]
assert len({row["sha256"] for row in cores}) == 1
frozen = read(RUN / "frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668
sources = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs",
    "src/OpticalSystemDesign.Core/Analysis/MtfScanAnalysis.cs",
    "src/OpticalSystemDesign.Core/Analysis/Diffraction/HuygensPsfSynthesis.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/HuygensThroughFocusPointwiseTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/HuygensAfocalDefocusTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/HuygensFocusDiagnostics.cs",
    "tools/diagnostics/HuygensFocus20261029/FocusedHuygensRegression.csproj",
    "tools/diagnostics/HuygensFocus20261029/packages.lock.json",
    "tools/diagnostics/HuygensFocus20261029/Diagnose.py",
    "tools/diagnostics/HuygensFocus20261029/Collect-Evidence.py"]
members = sorted(RUN.glob("*.trx")) + [RUN / n for n in ["decomposition.json", "frozen-integrity.json",
    "DiffractionEngine.before.cs", "MtfScanAnalysis.before.cs", "HuygensPsfSynthesis.before.cs", "core-before.json", "app-build.log"]]
OUT.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for path in members:
        zip.write(path, path.name)
    for path in sources:
        zip.write(ROOT / path, "source/" + path)
    for path in sorted((RUN / "pupil-diagnostics").glob("*beam*.json")):
        # Traced pupil/PSF replay inputs are new diagnostics. Native arrays remain
        # referenced by archive member above, without a new native-data copy.
        data = read(path)
        zip.writestr("pupil-diagnostics/" + path.name,
            json.dumps({k: v for k, v in data.items() if k not in ["actual", "expected"]}, indent=2) + "\n")
record = {"date": "2026-10-29", "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "scope": "Five original afocal Huygens through-focus MTF requests and five existing fixed-angle controls; 20 curves, five calculated focus planes and 101 display points per curve.",
    "rootCause": "Native-compatible added diopter phase is defined on normalized pupil coordinates scaled by the paraxial exit-pupil radius. Physical distorted pupil coordinates remain necessary for propagation, but must not also replace the phase datum.",
    "samplingPrescriptionAndToleranceUnchanged": True, "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
    "nativeArchive": str(archive.relative_to(ROOT)).replace('\\', '/'), "nativeArchiveSha256": sha(archive),
    "cases": records, "decomposition": read(RUN / "decomposition.json"),
    "finalTests": final_tests, "distinctPassingTests": len(test_names), "coreBinaries": cores,
    "sourceIdentities": [{"path": p, "sha256": sha(ROOT / p)} for p in sources], "frozenIntegrity": frozen,
    "appBuild": {"defaultOutput": True, "exitCode": 0, "warnings": 0, "errors": 0, "appLaunched": False},
    "earlierFailures": ["Initial diagnostic compile used a nonexistent PsfResult pixel-pitch property; corrected before running optical tests.",
        "huygens-focus-before.trx: all ten blocked by the generic focal normalizer attempting Diopter/Millimeter conversion. The test now checks typed Diopter axes and compares original native arrays directly, as in the earlier afocal fixture controls.",
        "huygens-focus-baseline.trx: two genuine pointwise failures and eight passes; historical aggregate NRMSE passed all ten.",
        "huygens-focus-pupil.trx repeats the two original failures for diagnostic exports, not new coverage."],
    "limitations": ["Definition inferred and verified for the listed native captures; not a universal description of undocumented Zemax internals.",
        "General physical defocus default, actual propagation geometry and zero-defocus behavior retain their contracts.",
        "Not a rerun of the full 150-request matrix, 95-file corpus, GUI/CUDA, native recapture or release."],
    "evidenceArchiveSha256": sha(OUT / "evidence.zip"),
    "evidenceMembers": [{"file": p.name, "sha256": sha(p), "bytes": p.stat().st_size} for p in members]}
if (RUN / "documentation.json").exists():
    doc = read(RUN / "documentation.json")
    record["documentation"] = {k: doc[k] for k in ["Documents", "BrokenLinks", "MarkdownFenceIssues", "LocalOnlyLinks"]}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(test_names), "cases": len(records),
    "violationsBefore": sum(row["before"]["violations"] for row in records),
    "violationsAfter": sum(row["after"]["violations"] for row in records),
    "maximumAfter": max(row["afterMaximumAbsolute"] for row in records)}))
