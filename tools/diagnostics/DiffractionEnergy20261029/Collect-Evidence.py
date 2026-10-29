"""Bind completed tests to unchanged requests, references and policy; seal new evidence."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from Diagnose import compare

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/diffraction-energy-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/diffraction-energy"
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
        if "AfocalDiffractionEnergyPointwiseTests" in result.attrib["testName"]:
            row = json.loads(result.findtext("t:Output/t:StdOut", "", NS))
            found[row["id"]] = row
    assert len(found) == 5
    return found


policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:"+policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
ledger_path = ROOT / "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json"
prior = read(ledger_path)
before, after = cases("diffraction-energy-baseline.trx"), cases("diffraction-energy-final.trx")
archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
assert sha(archive) == "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1"
records = []
with zipfile.ZipFile(archive) as z:
    for key, current in sorted(after.items()):
        original = next(c for c in prior["cases"] if c["id"] == key)
        assert current["fingerprint"] == before[key]["fingerprint"] == original["requestFingerprint"]
        gate = original["effectiveTolerances"]["EnergyFraction"]
        assert gate["absolute"] == 1e-5 and gate["relative"] == .002 and gate["nrmse"] == .003
        assert current["violations"] == 0
        assert all(c["metric"]["coverage"] >= gate["minimumCoverage"] and c["metric"]["conclusion"] == "Pass" for c in current["curves"])
        assert all(c["metric"]["coverage"] == b["metric"]["coverage"] for c,b in zip(current["curves"], before[key]["curves"]))
        source = "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/"+original["lens"]
        assert subprocess.run(["git", "diff", "--quiet", "HEAD", "--", source], cwd=ROOT).returncode == 0
        request_member = f"artifacts/validation/afocal-fft-energy-20261027/final-150-v2/{key}/workbench/request.json"
        request_bytes = z.read(request_member)
        request = json.loads(request_bytes)["request"]
        assert request["pupilSampling"] == 32 and request["imageSampling"] == 64
        assert request["workbenchSettings"]["NumPoints"] == "401"
        model = key[5:key.index("-primary-")]
        snapshot = f"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/{model}.json"
        native_prefix = f"artifacts/validation/afocal-recompare-20261026/expanded/{key}/native/"
        audits = {}
        for stage, metrics in [("before", before), ("after", after)]:
            data = read(RUN / stage / (key+".json"))
            items = []
            for i, (a, b) in enumerate(zip(data["actual"]["series"], data["expected"]["series"])):
                audit = compare(a["x"], a["y"], b["x"], b["y"])
                measured = metrics[key]["curves"][i]
                assert audit["count"] == measured["metric"]["count"]
                assert audit["violations"] == measured["violations"]
                assert abs(audit["maximumAbsolute"]-measured["metric"]["maxAbsolute"]) < 3e-14
                items.append(audit)
            assert len(items) == 2
            audits[stage] = items
        records.append({"id": key, "lens": original["lens"], "configuration": original["configuration"],
            "requestFingerprint": current["fingerprint"], "request": request, "effectiveTolerances": gate,
            "sourceSha256": sha(ROOT/source), "requestArchiveMember": request_member,
            "requestMemberSha256": hashlib.sha256(request_bytes).hexdigest(),
            "snapshotArchiveMember": snapshot, "snapshotSha256": hashlib.sha256(z.read(snapshot)).hexdigest(),
            "nativeMembers": [{"member": native_prefix+n, "sha256": hashlib.sha256(z.read(native_prefix+n)).hexdigest()}
                              for n in ["data.json", "data.txt", "model.json", "captured-settings.json"]],
            "before": before[key], "after": current, "independentValueAudit": audits})

test_names, final_tests, complex_control = set(), [], None
for name in ["diffraction-energy-final.trx", "diffraction-energy-protection.trx"]:
    counters, rows = results(name)
    assert all(r.attrib["outcome"] == "Passed" for r in rows)
    test_names.update(r.attrib["testName"] for r in rows)
    for r in rows:
        if "FrozenComplexControlConfirmsAmplitudeStencilAndUnchangedMask" in r.attrib["testName"]:
            complex_control = r.findtext("t:Output/t:StdOut", "", NS).strip()
    final_tests.append({"file": name, "counters": counters, "sha256": sha(RUN/name)})
assert complex_control is not None
cores = [{"path": p, "sha256": sha(ROOT/p)} for p in [
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/DiffractionEnergy20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]]
assert len({p["sha256"] for p in cores}) == 1
frozen = read(RUN/"frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668
decomposition = read(RUN/"decomposition.json")
assert all(c["metric"]["violations"] == 0 for c in decomposition["controls"] if c["interpolateAmplitude"])
sources = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.CumulativeEnergy.cs",
    "src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs",
    "src/OpticalSystemDesign.Core/Analysis/Radiometry/EncircledEnergyVariants.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalDiffractionEnergyPointwiseTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/DiffractionEnergyDiagnostics.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPupilAmplitudeInterpolationTests.cs",
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj",
    "tools/diagnostics/DiffractionEnergy20261029/FocusedEnergyRegression.csproj",
    "tools/diagnostics/DiffractionEnergy20261029/packages.lock.json",
    "tools/diagnostics/DiffractionEnergy20261029/Diagnose.py",
    "tools/diagnostics/DiffractionEnergy20261029/Collect-Evidence.py"]
members = sorted(RUN.glob("*.trx")) + sorted(RUN.glob("*.log")) + [RUN/n for n in
    ["frozen-integrity.json", "decomposition.json", "DiffractionEngine.CumulativeEnergy.before.cs", "core-before.json"]]
if (RUN/"documentation.json").exists():
    members.append(RUN/"documentation.json")
OUT.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(OUT/"evidence.zip", "w", zipfile.ZIP_DEFLATED) as z:
    for p in members:
        z.write(p, p.name)
    for p in sources:
        z.write(ROOT/p, "source/"+p)
    z.write(ROOT/cores[0]["path"], "binary/OpticalSystemDesign.Core.dll")
    for stage in ["before", "after"]:
        for p in sorted((RUN/stage).glob("*.json")):
            data = read(p)
            z.writestr(stage+"/"+p.name, json.dumps({k:v for k,v in data.items() if k != "expected"}, indent=2)+"\n")
record = {"date": "2026-10-29", "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "scope": "Five original afocal diffraction enclosed-energy requests; ten actual/ideal curves, original 32/64 grids and 401 display points.",
    "rootCause": "Afocal cumulative-energy FFT bypassed the existing scalar pupil amplitude interpolation used by PSF displays. Both ideal and aberrated Gaussian pupil responses were integrated from the unsmoothed amplitudes.",
    "prescriptionSamplingReferenceToleranceUnchanged": True, "pupilAcceptanceAndCoordinatesUnchanged": True,
    "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
    "effectiveToleranceLedger": str(ledger_path.relative_to(ROOT)).replace('\\','/'), "effectiveToleranceLedgerSha256": sha(ledger_path),
    "nativeArchive": str(archive.relative_to(ROOT)).replace('\\','/'), "nativeArchiveSha256": sha(archive),
    "cases": records, "decomposition": decomposition, "frozenComplexAmplitudeControl": complex_control,
    "finalTests": final_tests, "distinctPassingTests": len(test_names), "coreBinaries": cores,
    "sourceIdentities": [{"path":p,"sha256":sha(ROOT/p)} for p in sources], "frozenIntegrity": frozen,
    "appBuild": {"defaultOutput":True,"exitCode":0,"warnings":0,"errors":0,"appLaunched":False},
    "earlierFailures": ["Initial diagnostic compile attempted internal grid properties directly; reflection access corrected before optical runs.",
        "diffraction-energy-diagnostic-error.trx: four passes, beam diagnostic failed because the complex pupil is 32x32, not the 64x64 image. Corrected export only.",
        "diffraction-energy-baseline.trx: four passes, one genuine pointwise failure; 752 original out-of-gate comparisons reproduced exactly.",
        "diffraction-energy-protection-fixture-error.trx: 35 passes and two missing-fixture errors in the dedicated project; added links to existing immutable original files.",
        "diffraction-energy-targeted.trx repeats the final five passing requests, not additional coverage."],
    "limitations": ["The detailed amplitude stencil is supported by frozen native complex fields, not published as a universal Zemax internal formula.",
        "Product change is scoped to the existing afocal scalar cumulative-energy branch. General PSF, focal cumulative-energy, physical pixel-area integrator and polarization contracts retain their paths.",
        "No full 170-request rerun after this correction, 95-file corpus, independent Cooke/Tessar investigation, native recapture, GUI/GPU or release."],
    "evidenceArchiveSha256": sha(OUT/"evidence.zip"),
    "evidenceMembers": [{"file":p.name,"sha256":sha(p),"bytes":p.stat().st_size} for p in members]}
if (RUN/"documentation.json").exists():
    doc=read(RUN/"documentation.json")
    record["documentation"]={k:doc[k] for k in ["Documents","BrokenLinks","MarkdownFenceIssues","LocalOnlyLinks"]}
(OUT/"verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests":len(test_names),"cases":len(records),
    "violationsBefore":sum(r["before"]["violations"] for r in records),"violationsAfter":sum(r["after"]["violations"] for r in records),
    "beamMaximumAfter":max(c["metric"]["maxAbsolute"] for r in records if "beam-expander" in r["id"] for c in r["after"]["curves"])}))
