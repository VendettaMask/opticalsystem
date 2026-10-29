"""Verify every audited quantity independently and seal the completed 150+20 run."""
from pathlib import Path
from collections import Counter
import csv
import hashlib
import json
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/final-core-recheck-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/final-core-170"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def slug(value):
    return "-".join(filter(None, re.split(r'[ /\\.:<>"|?*]+', value.lower())))


rows = read(RUN / "comparison.json")
items = {row["id"]: row for row in read(RUN / "items.json")}
identities = read(RUN / "input-identity.json")
runtime = read(RUN / "runtime-identity.json")
workers = read(RUN / "worker-results.json")
old_path = ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json"
old = read(old_path)
old_cases = {row["id"]: row for row in old["cases"]}
assert len(rows) == len(items) == len(workers) == 170
assert runtime["coreSha256"] == identities["expectedCoreSha256"] == "41929bff2ea31eafbdc5ca3835d624d3d88193bfd241fc816e3117eccb7cfa5d"
assert all(not worker["timedOut"] and not worker["cancelled"] for worker in workers)
assert Counter(worker["exitCode"] for worker in workers) == {0: 168, 3: 2}
for entry in identities["files"]:
    assert sha(RUN / entry["path"]) == entry["sha256"]
for entry in identities["archives"]:
    assert sha(ROOT / entry["path"]) == entry["sha256"]
for entry in identities["binaries"]:
    assert sha(RUN / "runner" / entry["file"]) == entry["sha256"]
assert sha(RUN / "runner/OpticalSystemDesign.Core.dll") == runtime["coreSha256"]
assert sha(ROOT / "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll") == runtime["coreSha256"]
policy = Path(identities["policyPath"]).read_bytes().replace(b"\r\n", b"\n")
policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
assert policy == (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert digest(policy) == old["comparisonPolicySha256"] == "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5"
frozen = read(RUN / "frozen-integrity.json")
assert frozen["unchanged"] and frozen["workingTree"]["unchanged"] and frozen["matched"] == 2668

pointwise, increases, closed, new_violations, new_components, increased_violation_components, cases = [], [], [], [], [], [], []
error_statistic_increases = []
new_point_violations = resolved_point_violations = 0
storage, payload = [], {}
reuse_by_sha = {entry["sha256"]: entry["storage"] for entry in old["resultStorage"]}


def store(path, name, kind):
    hashed = sha(path)
    if hashed in reuse_by_sha:
        location = reuse_by_sha[hashed]
        if location["kind"] == "current-archive":
            location = {**location, "kind": "existing-archive", "archive": "validation/rechecks/2026-10-28/precision-check/afocal-current-evidence.zip",
                        "archiveSha256": old["archive"]["sha256"]}
    else:
        member = payload.setdefault(hashed, (name, path))[0]
        location = {"kind": "this-archive", "member": member}
    storage.append({"path": name, "dataKind": kind, "sha256": hashed, "bytes": path.stat().st_size, "storage": location})


def has_violations(row, phase):
    return any(point["violations"] for point in row[phase]["points"])


for row in rows:
    key, token = row["id"], row["outputToken"]
    item, previous = items[key], old_cases[key]
    before, after = row["before"], row["after"]
    assert row["requestFingerprint"] == previous["requestFingerprint"] == item["expectedFingerprint"]
    assert before["run"]["conclusion"] == previous["conclusion"]
    assert len(before["run"]["metrics"]) == len(previous["metrics"])
    for metric in before["run"]["metrics"]:
        old_metric = next(m for m in previous["metrics"] if m["id"] == metric["id"])
        assert metric == old_metric, (key, metric["id"], "original metric drift")
    baseline_samples = {}
    for phase in ["before", "after"]:
        evaluation = row[phase]
        if evaluation["run"]["metrics"]:
            assert evaluation["run"]["tolerances"] == item["expectedEffectiveTolerances"]
        for point in evaluation["points"]:
            path = RUN / phase / token / "comparisons" / token / (slug(point["id"]) + "-values.csv")
            samples = list(csv.DictReader(path.open(encoding="utf-8-sig", newline="")))
            gate = point["gate"]
            violations, maximum = 0, 0.0
            for sample in samples:
                a, b = float(sample["workbench"]), float(sample["zemax"])
                error = abs(a - b)
                assert error == float(sample["absoluteError"])
                maximum = max(maximum, error)
                violations += error > gate["absolute"] + gate["relative"] * abs(b)
            assert len(samples) == point["count"] and violations == point["violations"] and maximum == point["maxAbsolute"]
            if phase == "before":
                baseline_samples[point["id"]] = samples
            if phase == "after":
                prior_point = next(p for p in before["points"] if p["id"] == point["id"])
                assert point["count"] == prior_point["count"] and point["coverage"] == prior_point["coverage"]
                for a, b in zip(samples, baseline_samples[point["id"]]):
                    assert a["x"] == b["x"] and a["y"] == b["y"] and a["zemax"] == b["zemax"]
                    limit = gate["absolute"] + gate["relative"] * abs(float(a["zemax"]))
                    bad_after, bad_before = float(a["absoluteError"]) > limit, float(b["absoluteError"]) > limit
                    new_point_violations += bad_after and not bad_before
                    resolved_point_violations += bad_before and not bad_after
                pointwise.append({"set": row["set"], "id": key, "analysis": row["key"], "requestConclusion": after["run"]["conclusion"],
                    "before": prior_point, "after": point})
                if point["violations"] and not prior_point["violations"]:
                    new_components.append({"id": key, "metric": point["id"]})
                if point["violations"] > prior_point["violations"]:
                    increased_violation_components.append({"id": key, "metric": point["id"]})
                if point["maxAbsolute"] > prior_point["maxAbsolute"]:
                    increases.append({"id": key, "analysis": row["key"], "metric": point["id"], "unit": point["unit"],
                        "beforeMaxAbsolute": prior_point["maxAbsolute"], "afterMaxAbsolute": point["maxAbsolute"],
                        "increase": point["maxAbsolute"] - prior_point["maxAbsolute"], "pointwiseViolations": violations})
                store(path, f"results/{row['set']}/{key}/{path.name}", "matched-values")
    if has_violations(row, "before") and not has_violations(row, "after"):
        closed.append(key)
    if not has_violations(row, "before") and has_violations(row, "after"):
        new_violations.append(key)
    if previous["conclusion"] == "Pass":
        assert after["run"]["conclusion"] == "Pass", (key, "formal regression")
    assert before["run"]["conclusion"] == after["run"]["conclusion"]
    for metric in after["run"]["metrics"]:
        prior_metric = next(m for m in before["run"]["metrics"] if m["id"] == metric["id"])
        changed = {name: {"before": prior_metric[name], "after": metric[name]}
            for name in ["maxAbsolute", "meanAbsolute", "rmse", "nrmse"] if metric[name] > prior_metric[name]}
        if changed:
            error_statistic_increases.append({"id": key, "metric": metric["id"], "unit": metric["unit"], "increases": changed})
    output = RUN / "workbench" / token
    if (output / "data.json").exists():
        provenance = read(output / "provenance.json")
        assert provenance["requestFingerprint"] == row["requestFingerprint"]
        assert provenance["snapshotSha256"] == sha(Path(item["snapshot"]))
        assert provenance["codeFingerprint"] == runtime["applicationSha256"] + ":" + runtime["coreSha256"]
        store(output / "data.json", f"results/{row['set']}/{key}/data.json", "core-data")
    assert sha(Path(item["source"])) == item["sourceSha256"]
    cases.append({"set": row["set"], "id": key, "lens": row["lens"], "configuration": row["configuration"], "analysis": row["key"],
        "requestFingerprint": row["requestFingerprint"], "request": item["request"], "effectiveTolerances": item["expectedEffectiveTolerances"],
        "sourceSha256": item["sourceSha256"], "snapshotSha256": sha(Path(item["snapshot"])),
        "previousConclusion": previous["conclusion"], "conclusion": after["run"]["conclusion"], "reason": after["run"]["reason"],
        "beforeMetrics": before["run"]["metrics"], "metrics": after["run"]["metrics"],
        "pointwiseBefore": before["points"], "pointwise": after["points"], "physicalGaps": after["gaps"],
        "normalizations": after["run"]["normalizations"], "availabilityError": after["error"], "outputToken": token})
assert not new_violations and not new_components and not increased_violation_components and not new_point_violations


def scope_summary(subset):
    points_before = [point for row in subset for point in row["before"]["points"]]
    points_after = [point for row in subset for point in row["after"]["points"]]
    return {"requests": len(subset), "conclusions": dict(Counter(row["after"]["run"]["conclusion"] for row in subset)),
        "auditedQuantities": len(points_after), "alignedPointComparisons": sum(p["count"] for p in points_after),
        "pointwiseViolationsBefore": sum(p["violations"] for p in points_before), "pointwiseViolationsAfter": sum(p["violations"] for p in points_after),
        "formallyPassingRequestsWithPointwiseViolationsBefore": sum(row["before"]["run"]["conclusion"] == "Pass" and has_violations(row, "before") for row in subset),
        "formallyPassingRequestsWithPointwiseViolationsAfter": sum(row["after"]["run"]["conclusion"] == "Pass" and has_violations(row, "after") for row in subset),
        "formalRegressions": 0, "newPointwiseViolatingRequests": 0}


OUT.mkdir(parents=True, exist_ok=True)
sources = ["tools/diagnostics/FinalAfocalRecheck20261029/Program.cs",
    "tools/diagnostics/FinalAfocalRecheck20261029/FinalAfocalRecheck20261029.csproj",
    "tools/diagnostics/FinalAfocalRecheck20261029/Stage-Inputs.py",
    "tools/diagnostics/FinalAfocalRecheck20261029/Collect-Evidence.py",
    "tools/diagnostics/FinalAfocalRecheck20261029/packages.lock.json",
    "tools/diagnostics/AfocalReferenceFocus20261027/ScopedAfocalNormalizer.cs"]
binary_names = ["FinalAfocalRecheck20261029.dll", "FinalAfocalRecheck20261029.deps.json", "FinalAfocalRecheck20261029.runtimeconfig.json",
    "OpticalSystemDesign.Core.dll", "OpticalSystemDesign.Application.dll", "OpticalSystemDesign.ZemaxComparison.dll",
    "OpticalSystemDesign.ZemaxComparison.deps.json", "OpticalSystemDesign.ZemaxComparison.runtimeconfig.json",
    "ILGPU.dll", "ILGPU.Algorithms.dll", "SkiaSharp.dll", "libSkiaSharp.dll", "comparison-settings.json"]
binary_records = [{"file": name, "sha256": sha(RUN / "runner" / name)} for name in binary_names]
with zipfile.ZipFile(OUT / "binary-cohort.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for name in binary_names:
        zip.write(RUN / "runner" / name, name)
    zip.writestr("binary-identity.json", json.dumps(binary_records, indent=2) + "\n")
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for _, (member, path) in sorted(payload.items()):
        zip.write(path, member)
    for path in sources:
        zip.write(ROOT / path, "source/" + path)
    for path in sorted(RUN.glob("*.log")):
        zip.write(path, "audit/" + path.name)
    for name in ["runtime-identity.json", "input-identity.json", "worker-results.json", "frozen-integrity.json"]:
        zip.write(RUN / name, "audit/" + name)
    for name in ["audit-initial-incomplete.json", "audit-native-loader-incomplete.json", "audit-scoped-policy-incomplete.json"]:
        zip.write(RUN / name, "audit/" + name)
    for row in rows:
        token = row["outputToken"]
        for name in ["execution.json", "worker-result.json", "process.log", "provenance.json", "request.json"]:
            path = RUN / "workbench" / token / name
            if path.exists():
                zip.write(path, f"executions/{token}/{name}")
    zip.writestr("audit/pointwise-audit.json", json.dumps(pointwise, ensure_ascii=False, indent=2) + "\n")
record = {"date": "2026-10-29", "scope": "Actual recomputation of the original 150 and fixed-angle 20 requests; every alignable scalar, series and grid quantity audited pointwise.",
    "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "coreSha256": runtime["coreSha256"], "previousCoreSha256": old["currentCoreSha256"], "runtimeIdentity": runtime,
    "prescriptionSamplingReferencesAndToleranceUnchanged": True, "comparisonPolicyLfSha256": digest(policy),
    "inputIdentity": identities, "frozenIntegrity": frozen, "workerCounts": dict(Counter(str(w["exitCode"]) for w in workers)),
    "runs": [{"set": name, **scope_summary([row for row in rows if row["set"] == name])} for name in ["original-150", "fixed-20"]],
    "byAnalysis": [{"analysis": name, **scope_summary([row for row in rows if row["key"] == name])} for name in sorted({row["key"] for row in rows})],
    "baselineLedgerExactlyReconstructed": True, "independentCsvPointwiseRecalculationVerified": True,
    "pointwiseSummary": {"auditedQuantities": len(pointwise), "alignedPointComparisons": sum(p["after"]["count"] for p in pointwise),
        "minimumCoverage": min(p["after"]["coverage"] for p in pointwise),
        "violationsBefore": sum(p["before"]["violations"] for p in pointwise), "violationsAfter": sum(p["after"]["violations"] for p in pointwise),
        "closedViolatingRequests": closed, "newViolatingRequests": new_violations,
        "newViolatingComponents": new_components, "componentsWithIncreasedViolationCounts": increased_violation_components,
        "newViolatingPoints": new_point_violations, "resolvedViolatingPoints": resolved_point_violations,
        "matchedCoordinatesAndNativeValuesIdentical": True,
        "remainingFormallyPassingRequests": [row["id"] for row in rows if row["after"]["run"]["conclusion"] == "Pass" and has_violations(row, "after")]},
    "maximumErrorIncreases": increases, "errorStatisticIncreases": error_statistic_increases, "cases": cases, "resultStorage": storage,
    "storageSummary": dict(Counter(entry["storage"]["kind"] for entry in storage)),
    "binaryCohort": {"file": "binary-cohort.zip", "sha256": sha(OUT / "binary-cohort.zip"), "files": binary_records},
    "evidenceArchiveSha256": sha(OUT / "evidence.zip"), "sourceIdentities": [{"path": path, "sha256": sha(ROOT / path)} for path in sources],
    "earlierHarnessIssues": ["Archive reconstruction initially lacked an old declared-policy file and exceeded Windows path length; original bytes were remapped to shorter task-local paths. No optical computation had run.",
        "Initial audit lacked the required registry policy at app base; retained as audit-initial-incomplete.",
        "Second audit lacked native plotting-library resolution in the isolated runner; the exact existing off-screen library was supplied. Completed optical outputs were unaffected.",
        "Generic global policy has no afocal gate extensions. The audit initially rejected those cases, then restored every request's exact effective gates from the prior authoritative ledger. Both before and after gates are verified identical; the global policy remains unchanged."],
    "limitations": ["No full 95-file corpus, new native capture, GUI/CUDA, installer or release validation.",
        "Grid coordinate mismatches remain incomparable; no interpolation, shift or scale fit was applied to them.",
        "No new independent dense-pupil audit; the earlier binocular boundary evidence retains its original scope.",
        "Formal Pass permits NRMSE acceptance; three Pass requests still have pointwise violations, disclosed separately."]}
doc = RUN / "documentation.json"
if doc.exists():
    record["documentation"] = read(doc)
(OUT / "afocal-comparison.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"runs": record["runs"], "pointwiseSummary": record["pointwiseSummary"],
    "storageSummary": record["storageSummary"], "maximumErrorIncreases": increases}, ensure_ascii=False))
