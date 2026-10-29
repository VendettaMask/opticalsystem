"""Seal completed Core runs and independently audit exported values; no runtime optics."""
from pathlib import Path
import hashlib
import json
import math
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/rms-field-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/rms-vs-field"
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
        if "AfocalRmsFieldPointwiseTests" in result.attrib["testName"]:
            row = json.loads(result.findtext("t:Output/t:StdOut", "", NS))
            found[row["id"]] = row
    assert len(found) == 5
    return found


policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
ledger_path = ROOT / "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json"
prior = read(ledger_path)
before, after = cases("rms-field-baseline.trx"), cases("rms-field-final.trx")
archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
assert sha(archive) == "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1"
records = []
with zipfile.ZipFile(archive) as z:
    for key, actual in sorted(after.items()):
        original = next(row for row in prior["cases"] if row["id"] == key)
        assert actual["fingerprint"] == before[key]["fingerprint"] == original["requestFingerprint"]
        gate = original["effectiveTolerances"]["Radius"]
        assert gate["absolute"] == 1.7453292519943293e-5 and gate["relative"] == .001
        assert actual["violations"] == 0 and actual["metric"]["conclusion"] == "Pass"
        assert actual["metric"]["coverage"] == 1
        source = "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/" + original["lens"]
        assert subprocess.run(["git", "diff", "--quiet", "HEAD", "--", source], cwd=ROOT).returncode == 0
        request_member = f"artifacts/validation/afocal-fft-energy-20261027/final-150-v2/{key}/workbench/request.json"
        request_bytes = z.read(request_member)
        request = json.loads(request_bytes)["request"]
        assert all(request["workbenchSettings"][k] == v for k, v in
                   [("NumRings", "6"), ("GaussianAzimuthalSamples", "12"), ("FieldDensity", "15")])
        model = key[5:key.index("-primary-")]
        snapshot = f"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/{model}.json"
        native_prefix = f"artifacts/validation/afocal-recompare-20261026/expanded/{key}/native/"
        # Independently compare the exported native and newly calculated curve values.
        # Zero-radius field scans collapse to one comparison coordinate, as in Core's ledger.
        audits = {}
        for stage, metrics in [("before", before), ("after", after)]:
            d = read(RUN / stage / (key + ".json"))
            a, b = d["actual"]["series"][0], d["expected"]["series"][0]
            ax = dict(zip(a["x"], a["y"]))
            bx = dict(zip(b["x"], b["y"]))
            assert len(ax) == len(bx)
            assert all(abs(x - y) < 1e-14 for x, y in zip(sorted(ax), sorted(bx)))
            errors = [abs(ax[x] - bx[y]) for x, y in zip(sorted(ax), sorted(bx))]
            bad = sum(e > gate["absolute"] + gate["relative"] * abs(bx[y])
                      for e, y in zip(errors, sorted(bx)))
            assert len(errors) == metrics[key]["metric"]["count"]
            assert bad == metrics[key]["violations"]
            assert abs(max(errors) - metrics[key]["metric"]["maxAbsolute"]) < 1e-13
            audits[stage] = {"alignedValues": len(errors), "violations": bad, "maximumAbsolute": max(errors)}
        records.append({"id": key, "lens": original["lens"], "configuration": original["configuration"],
            "requestFingerprint": actual["fingerprint"], "request": request, "effectiveTolerances": gate,
            "sourceSha256": sha(ROOT / source), "requestArchiveMember": request_member,
            "requestMemberSha256": hashlib.sha256(request_bytes).hexdigest(),
            "snapshotArchiveMember": snapshot, "snapshotSha256": hashlib.sha256(z.read(snapshot)).hexdigest(),
            "nativeMembers": [{"member": native_prefix + n, "sha256": hashlib.sha256(z.read(native_prefix + n)).hexdigest()}
                              for n in ["data.json", "data.txt", "model.json", "captured-settings.json"]],
            "before": before[key], "after": actual, "independentValueAudit": audits})

diagnostic = read(next((RUN / "azimuth-diagnostics").glob("*riflescope*.json")))
fields = diagnostic["fields"]
assert all(f["aliveLaunches"] == 72 and len(f["rays"]) == 72 for f in fields)
assert all(len(r["history"]) == 19 for f in fields for r in f["rays"])
stop_residual = max(r["stopResidual"] for f in fields for r in f["rays"])
assert stop_residual < 3.3e-13
variants = []
for v in diagnostic["angularVariants"]:
    points = v["points"]
    w = sum(p["weight"] for p in points)
    cx = sum(p["weight"] * p["x"] for p in points) / w
    cy = sum(p["weight"] * p["y"] for p in points) / w
    rms = math.sqrt(sum(p["weight"] * ((p["x"]-cx)**2 + (p["y"]-cy)**2) for p in points) / w)
    assert abs(rms - v["rmsMilliradians"]) < 2e-15
    expected = diagnostic["expected"]["series"][0]["y"][v["index"]]
    variants.append({"fieldIndex": v["index"], "physicalDegrees": v["index"] / 15 * 2.5,
        "nodeCount": v["nodeCount"], "alive": v["alive"], "phaseRadians": v["phaseRadians"],
        "independentlyRecomputedRms": rms, "native": expected, "difference": rms - expected})

test_names, final_tests = set(), []
for name in ["rms-field-final.trx", "rms-field-protection.trx"]:
    counters, rows = results(name)
    assert all(row.attrib["outcome"] == "Passed" for row in rows)
    test_names.update(row.attrib["testName"] for row in rows)
    final_tests.append({"file": name, "counters": counters, "sha256": sha(RUN / name)})
cores = [{"path": p, "sha256": sha(ROOT / p)} for p in [
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/RmsField20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]]
assert len({row["sha256"] for row in cores}) == 1
frozen = read(RUN / "frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668
sources = ["src/OpticalSystemDesign.Core/Raytrace/ApertureSampler.cs",
    "src/OpticalSystemDesign.Core/Analysis/Rays/SpotAnalysisSupport.cs",
    "tests/OpticalSystemDesign.Tests/GaussianAzimuthQuadratureTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalRmsFieldPointwiseTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/RmsFieldDiagnostics.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj",
    "tools/diagnostics/RmsField20261029/FocusedRmsRegression.csproj",
    "tools/diagnostics/RmsField20261029/packages.lock.json",
    "tools/diagnostics/RmsField20261029/Collect-Evidence.py"]
members = sorted(RUN.glob("*.trx")) + [RUN / n for n in ["frozen-integrity.json", "ApertureSampler.before.cs",
    "SpotAnalysisSupport.before.cs", "core-before.json", "app-build.log"]]
if (RUN / "documentation.json").exists():
    members += [RUN / "documentation.json", RUN / "documentation.log"]
OUT.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as z:
    for p in members:
        z.write(p, p.name)
    for p in sources:
        z.write(ROOT / p, "source/" + p)
    z.write(ROOT / cores[0]["path"], "binary/OpticalSystemDesign.Core.dll")
    for stage in ["before", "after", "azimuth-diagnostics"]:
        for p in sorted((RUN / stage).glob("*.json")):
            d = read(p)
            # Reference arrays remain in their unique original archive, identified above.
            z.writestr(stage + "/" + p.name,
                json.dumps({k: v for k, v in d.items() if k != "expected"}, ensure_ascii=False, indent=2) + "\n")
record = {"date": "2026-10-29", "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "scope": "Five original afocal RMS-vs-field requests; 16 calculated fields each, 50 distinct alignable coordinates overall.",
    "rootCause": "Gaussian RMS integration used periodic angular endpoints instead of mirrored Gauss-Chebyshev half-pupil nodes. Twelve arms need a 15-degree midpoint phase; radial rings, arms, weights and captured controls are unchanged.",
    "samplingControlsCountsRadiiWeightsUnchanged": True, "angularNodePlacementCorrected": True,
    "prescriptionReferenceAndToleranceUnchanged": True,
    "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
    "effectiveToleranceLedger": str(ledger_path.relative_to(ROOT)).replace('\\', '/'), "effectiveToleranceLedgerSha256": sha(ledger_path),
    "nativeArchive": str(archive.relative_to(ROOT)).replace('\\', '/'), "nativeArchiveSha256": sha(archive),
    "cases": records, "diagnosis": {"legacyFieldsAudited": len(fields), "raysPerField": 72,
        "allLaunchesAlive": True, "historySurfacesPerRay": 19, "maximumStopResidualMm": stop_residual,
        "halfStepControl": variants},
    "finalTests": final_tests, "distinctPassingTests": len(test_names), "coreBinaries": cores,
    "sourceIdentities": [{"path": p, "sha256": sha(ROOT / p)} for p in sources], "frozenIntegrity": frozen,
    "appBuild": {"defaultOutput": True, "exitCode": 0, "warnings": 0, "errors": 0, "appLaunched": False},
    "earlierFailures": ["rms-field-before.trx: five fixture-path failures, corrected to existing expanded/native members before optical comparison.",
        "rms-field-baseline.trx: original 0.002218114630527568 mrad and one pointwise failure reproduced; four controls pass.",
        "rms-field-azimuth.trx: repeats the original failure while exporting independent midpoint-node diagnostics.",
        "rms-field-targeted.trx: repeats the final five passing cases, not additional coverage."],
    "limitations": ["Angular convention inferred from Gaussian quadrature and verified on the listed frozen native captures; official documentation does not publish an explicit angular formula.",
        "General periodic sampler default, explicit pupil nodes, polarized/RA spot paths and wavefront integration remain separate contracts.",
        "No full 170-request rerun after this correction, 95-file corpus, independent Cooke/Tessar investigation, native recapture, GUI/GPU or release."],
    "evidenceArchiveSha256": sha(OUT / "evidence.zip"),
    "evidenceMembers": [{"file": p.name, "sha256": sha(p), "bytes": p.stat().st_size} for p in members]}
if (RUN / "documentation.json").exists():
    doc = read(RUN / "documentation.json")
    record["documentation"] = {k: doc[k] for k in ["Documents", "BrokenLinks", "MarkdownFenceIssues", "LocalOnlyLinks"]}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(test_names), "cases": len(records),
    "violationsBefore": sum(row["before"]["violations"] for row in records),
    "violationsAfter": sum(row["after"]["violations"] for row in records),
    "maximumAfter": max(row["after"]["metric"]["maxAbsolute"] for row in records)}))
