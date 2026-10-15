"""Offline evidence audit only; optical values come from the formal Core/ZOS API."""
import csv
import hashlib
import json
import math
import pathlib
import sys
from collections import Counter

root = pathlib.Path(sys.argv[1]).resolve()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, value):
    (root / name).write_text(json.dumps(value, ensure_ascii=True, indent=2), encoding="utf-8")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


sources = read(root / "sources.json")
models = read(root / "models.json")
by_slug = {pathlib.Path(m["snapshot"]).stem: m for m in models if "snapshot" in m}
integrity = []
for row in sources:
    source = row["source"]
    observed = sha(pathlib.Path(source["path"]))
    integrity.append({"lens": source["id"], "excluded": row["excluded"], "originalUnchanged": observed == source["sha256"], "sha256": observed})
    if observed != source["sha256"]:
        raise ValueError("Original changed: " + source["id"])

rays = []
for directory in sorted((root / "rays").iterdir()):
    if (directory / "error.json").exists():
        rays.append({"id": directory.name, "conclusion": "Error", "error": read(directory / "error.json")})
        continue
    job = read(directory / "job.json")
    model_key = directory.name.rsplit("-w", 1)[0]
    model = by_slug[model_key]
    summary = {"id": directory.name, "lens": model["id"], "configuration": job["configuration"], "wavelength": job["wavelength"], "unit": model["unit"],
               "semantics": "Physical GQ6 normalized rays, original vignetting retained; pointwise tolerance, acceptance and first clip checked separately. No RMS convergence certification."}
    result = directory / "native/batch-result.json"
    if not result.exists() or read(result)["exitCode"] != 0:
        summary.update(conclusion="Error", reason="Native capture missing or failed")
        error = directory / "native/error.json"
        if error.exists():
            summary["error"] = read(error)
        rays.append(summary)
        continue
    native = read(directory / "native/ray-audit.json")
    core = read(directory / "core.json")
    inputs = read(directory / "inputs.json")
    if native["inputs"] != inputs:
        raise ValueError("Native/Core normalized inputs differ: " + directory.name)
    nfields = len(job["fields"])
    nper = len(core[0]["rays"])
    if nfields * nper != len(inputs):
        raise ValueError("Incomplete ray field/surface coverage: " + directory.name)
    if len(native["surfaces"]) != model["surfaceCount"] - 1:
        summary.update(conclusion="Incomparable", reason="Core folds coordinate-break rows; native and Core raw surface indices need an explicit correspondence before a per-surface numerical comparison",
                       coreSurfaceCount=model["surfaceCount"], nativeSurfaceCount=len(native["surfaces"]) + 1, inputs=len(inputs))
        rays.append(summary)
        continue
    scale = {"MM": 1., "CM": 10., "IN": 25.4}[model["unit"]]
    physical = []
    for field in core:
        if len(field["rays"]) != nper:
            raise ValueError("Incomplete Core pupil")
        physical.extend(field["rays"])
    image = native["surfaces"][-1]["rays"]
    acceptance = first_clip = coord_bad = dir_bad = compared = valid_values = 0
    max_coord = max_dir = 0.
    witnesses = []
    for i, nr in enumerate(image):
        last = physical[i]["samples"][-1]
        accepted = nr["error"] == 0 and nr["vignette"] == 0
        core_accepted = last["surfaceNumber"] == model["surfaceCount"] - 1 and not last["vignetted"] and last["intensity"] > 0
        if accepted != core_accepted:
            acceptance += 1
            if len(witnesses) < 12:
                witnesses.append({"input": inputs[i], "kind": "acceptance", "nativeError": nr["error"], "nativeVignette": nr["vignette"], "coreLast": last})
        if nr["error"] == 0 and nr["vignette"] > 0 and last["vignetted"] and last["hit"] and nr["vignette"] != last["surfaceNumber"]:
            first_clip += 1
    for surface in native["surfaces"]:
        si = surface["surface"]
        if len(surface["rays"]) != len(inputs):
            raise ValueError("Incomplete native pupil")
        for i, nr in enumerate(surface["rays"]):
            if nr["number"] != i + 1:
                raise ValueError("Native ray ordering differs")
            ps = next((s for s in physical[i]["samples"] if s["surfaceNumber"] == si), None)
            if nr["error"] != 0 or nr["vignette"] != 0 or ps is None or ps["vignetted"] or not ps["hit"]:
                continue
            compared += 1
            for actual_key, native_key in (("x", "x"), ("y", "y"), ("z", "z")):
                actual, expected = ps["position"][actual_key], nr[native_key] * scale
                if not math.isfinite(actual) or not math.isfinite(expected):
                    raise ValueError("Nonfinite accepted position")
                delta = abs(actual - expected)
                max_coord = max(max_coord, delta)
                coord_bad += delta > 1e-6 + 1e-6 * abs(expected)
                valid_values += 1
            for actual_key, native_key in (("x", "l"), ("y", "m"), ("z", "n")):
                actual, expected = ps["direction"][actual_key], nr[native_key]
                if not math.isfinite(actual) or not math.isfinite(expected):
                    raise ValueError("Nonfinite accepted direction")
                delta = abs(actual - expected)
                max_dir = max(max_dir, delta)
                dir_bad += delta > 1e-8 + 1e-6 * abs(expected)
                valid_values += 1
    summary.update(inputs=len(inputs), surfaceResults=len(inputs) * len(native["surfaces"]), physicalSurfaceComparisons=compared,
                   comparedValues=valid_values, acceptanceDifferences=acceptance, firstClipDifferences=first_clip, coordinateFailures=coord_bad,
                   directionFailures=dir_bad, maximumCoordinateMillimeters=max_coord, maximumDirection=max_dir, witnesses=witnesses,
                   conclusion="Difference" if acceptance or first_clip or coord_bad or dir_bad else "Pass" if compared else "Incomparable")
    if model.get("preflight"):
        summary["calculatedConclusion"] = summary["conclusion"]
        summary["conclusion"] = "Incomparable"
        summary["preflight"] = model["preflight"]
        summary["reason"] = "Unsupported imported surface; diagnostic traces do not certify the optical model"
    rays.append(summary)
write("ray-results.json", rays)
write("ray-summary.json", {"jobs": len(rays), "counts": dict(Counter(r["conclusion"] for r in rays)),
    "inputs": sum(r.get("inputs", 0) for r in rays), "surfaceResults": sum(r.get("surfaceResults", 0) for r in rays),
    "physicalSurfaceComparisons": sum(r.get("physicalSurfaceComparisons", 0) for r in rays),
    "comparedValues": sum(r.get("comparedValues", 0) for r in rays), "lenses": sorted(set(m["id"] for m in models))})
write("source-integrity.json", integrity)

analysis = read(root / "analysis-results.json")
with (root / "analysis-matrix.csv").open("w", encoding="utf-8-sig", newline="") as stream:
    writer = csv.writer(stream)
    writer.writerow(["Lens", "Configuration", "Scope", "Analysis", "Field", "Wavelength", "PupilSampling", "Conclusion", "WorstNRMSE", "MaximumAbsolute", "Reason", "Evidence"])
    for item in analysis:
        run = item["run"]
        request = run.get("request") or {}
        metrics = run.get("metrics", [])
        writer.writerow([item["lens"], item["configuration"], item["scope"], run["key"], request.get("field"), request.get("wavelength"), request.get("pupilSampling"),
            run["conclusion"], max((m["nrmse"] for m in metrics), default=None), max((m["maxAbsolute"] for m in metrics), default=None), run["reason"], item["path"]])
print(json.dumps(read(root / "analysis-summary.json"), ensure_ascii=True))
print(json.dumps(read(root / "ray-summary.json"), ensure_ascii=True))
