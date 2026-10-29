"""Restore original immutable bytes and freeze the verified final binary cohort."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = (Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / "artifacts/validation/final-core-recheck-20261029")
RUN.mkdir(parents=True, exist_ok=True)
assert not (RUN / "items.json").exists(), "Use a fresh run, never overwrite completed evidence"
EXPECTED_CORE = "41929bff2ea31eafbdc5ca3835d624d3d88193bfd241fc816e3117eccb7cfa5d"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        assert path.read_bytes() == data
    else:
        path.write_bytes(data)


def relative(old):
    path = old.replace("\\", "/")
    return "artifacts/" + path.split("/artifacts/", 1)[1]


cohort = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else ROOT / "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0"
assert sha((cohort / "OpticalSystemDesign.Core.dll").read_bytes()) == EXPECTED_CORE
binary_records = []
for path in sorted(cohort.iterdir()):
    if path.is_file() and path.suffix in [".dll", ".pdb", ".json"]:
        if path.name == "comparison-settings.json":
            continue
        for folder in ["cohort", "runner"]:
            write(RUN / folder / path.name, path.read_bytes())
        binary_records.append({"file": path.name, "sha256": sha(path.read_bytes())})
if (cohort / "runtimes").exists():
    for path in (cohort / "runtimes").rglob("*"):
        if path.is_file():
            for folder in ["cohort", "runner"]:
                write(RUN / folder / path.relative_to(cohort), path.read_bytes())
    # The isolated reference-only runner has no NuGet RID graph; give the
    # existing off-screen plotter its exact Windows native library at app base.
    native = cohort / "runtimes/win-x64/native/libSkiaSharp.dll"
    for folder in ["cohort", "runner"]:
        write(RUN / folder / native.name, native.read_bytes())
    binary_records.append({"file": native.name, "sha256": sha(native.read_bytes())})

prior_path = ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json"
prior = json.loads(prior_path.read_text(encoding="utf-8-sig"))
archives = {}
archive_records = []
for path, digest in [
    (ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip", "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1"),
    (prior_path.parent / "afocal-current-evidence.zip", prior["archive"]["sha256"])]:
    assert sha(path.read_bytes()) == digest
    archives[path.name] = zipfile.ZipFile(path)
    archive_records.append({"path": str(path.relative_to(ROOT)).replace("\\", "/"), "sha256": digest})
zip = archives["evidence-current.zip"]
identities, rows = {}, []


def materialize(member):
    if "/native/" in member:
        parent, name = member.rsplit("/native/", 1)
        path = RUN / "inputs/native" / parent.rsplit("/", 1)[-1] / name
    elif "/models/" in member:
        path = RUN / "inputs/models" / member.rsplit("/", 1)[-1]
    else:
        path = RUN / "inputs/misc" / sha(member.encode())[:12] / member.rsplit("/", 1)[-1]
    data = zip.read(member)
    write(path, data)
    identities[member] = {"archiveMember": member, "path": str(path.relative_to(RUN)).replace("\\", "/"), "sha256": sha(data), "bytes": len(data)}
    return str(path)


policy_member = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy_path = materialize(policy_member)
write(RUN / "runner/comparison-settings.json", Path(policy_path).read_bytes())
angular = next(row["effectiveTolerances"]["RmsSpotRadius"] for row in prior["cases"] if row["analysis"] == "Spot Diagram")
assert all(row["effectiveTolerances"]["RmsSpotRadius"] == angular
    and row["effectiveTolerances"]["GeoSpotRadius"] == angular for row in prior["cases"] if row["analysis"] == "Spot Diagram")
angular_path = str(RUN / "inputs/angular-policy.json")
write(Path(angular_path), json.dumps(angular, indent=2).encode())
for set_name, branch, count in [("original-150", "final-150-v2", 150), ("fixed-20", "final-fixed-v2", 20)]:
    member = f"artifacts/validation/afocal-fft-energy-20261027/{branch}/items.json"
    old_rows = json.loads(zip.read(member))
    assert len(old_rows) == count
    materialize(member)
    for row in old_rows:
        key = row["id"]
        original = next(case for case in prior["cases"] if case["set"] == set_name and case["id"] == key)
        snapshot = materialize(relative(row["snapshot"]))
        native_member = relative(row["native"]).rstrip("/") + "/"
        native_files = [n for n in zip.namelist() if n.startswith(native_member) and not n.endswith("/")]
        assert native_files
        for name in native_files:
            materialize(name)
        source_path = ROOT / "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources" / row["lens"]
        source = RUN / "inputs/sources" / row["lens"]
        write(source, source_path.read_bytes())
        old_data = next((x for x in prior["resultStorage"] if x["set"] == set_name
            and x["path"] == key + "/workbench/data.json" and x["dataKind"] == "core-data"), None)
        baseline = None
        if old_data:
            storage = old_data["storage"]
            data = archives[Path(storage["archive"]).name].read(storage["member"])
            assert sha(data) == old_data["sha256"]
            baseline = RUN / "baseline-data" / set_name / key / "data.json"
            write(baseline, data)
        rows.append({"set": set_name, "id": key, "lens": row["lens"], "configuration": row["configuration"],
            "key": row["key"], "snapshot": snapshot, "native": str(RUN / "inputs/native" / key),
            "source": str(source), "sourceSha256": sha(source.read_bytes()),
            "baselineWorkbench": str(baseline) if baseline else None,
            "previousConclusion": original["conclusion"], "expectedFingerprint": original["requestFingerprint"],
            "expectedEffectiveTolerances": original["effectiveTolerances"],
            "request": row["request"]})
assert len(rows) == 170 and len({r["id"] for r in rows}) == 170
for archive in archives.values():
    archive.close()
for name, value in [("items.json", rows), ("input-identity.json", {"archives": archive_records,
    "files": list(identities.values()), "policyPath": policy_path, "angularPolicyPath": angular_path,
    "angularPolicySource": {"path": str(prior_path), "sha256": sha(prior_path.read_bytes()), "quantity": "Spot Diagram/RmsSpotRadius; all original spot rows verified identical"},
    "sourceCommit": "4ec6ababa1961c5e370242156f2233422dc285f5", "expectedCoreSha256": EXPECTED_CORE,
    "binaries": binary_records})]:
    (RUN / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"requests": len(rows), "inputFiles": len(identities), "core": EXPECTED_CORE}))
