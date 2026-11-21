"""Apply scoped recaptures without replacing the historical image evidence."""
import json
import os
from pathlib import Path


MANIFEST = Path(__file__).resolve().parent.parent / "corrections.json"


def local_path(base, value, evidence_root):
    path = (base / value).resolve()
    if not path.is_relative_to(evidence_root) or not path.is_file() or not path.stat().st_size:
        raise ValueError(f"Invalid correction evidence: {path}")
    return path


def apply_corrections(data, output):
    if not MANIFEST.exists():
        return []
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    base = MANIFEST.parent
    evidence_root = base.parent.resolve()
    applied = []
    reviewed = []
    for entry in manifest.get("reviews", []):
        for lens in data["lenses"]:
            source = lens.get("relativePath", lens.get("source", "")).replace("\\", "/")
            if not source.casefold().endswith(entry["sourceRelativePath"].casefold()):
                continue
            if lens["sha256"] != entry["sourceSha256"]:
                raise ValueError(f"Review lens identity changed: {entry['sourceRelativePath']}")
            result = lens["results"].get(entry["analysis"])
            if result is None:
                raise ValueError(f"Review analysis is missing: {entry['analysis']}")
            evidence = local_path(base, entry["evidence"], evidence_root)
            result["review"] = {
                "id": entry["id"],
                "date": manifest["date"],
                "disposition": entry["disposition"],
                "evidence": Path(os.path.relpath(evidence, output)).as_posix(),
            }
            result["note"] = entry["note"]
            result.pop("nativeStatusLabel", None)
            if entry["disposition"] == "missing_native" and result.get("native"):
                result["nativeStatusLabel"] = "通用参考图 · 无本项对应捕获"
            if not result.get("correction"):
                result["currentStatusLabel"] = entry["statusLabel"]
            reviewed.append({"lens": lens["name"], "analysis": entry["analysis"], "id": entry["id"]})
    for entry in manifest["entries"]:
        for lens in data["lenses"]:
            source = lens.get("relativePath", lens.get("source", "")).replace("\\", "/")
            expected = entry["sourceRelativePath"]
            if not source.casefold().endswith(expected.casefold()):
                continue
            if lens["sha256"] != entry["sourceSha256"]:
                raise ValueError(f"Correction lens identity changed: {expected}")
            result = lens["results"].get(entry["analysis"])
            if result is None:
                raise ValueError(f"Correction analysis is missing: {entry['analysis']}")
            image = local_path(base, entry["image"], evidence_root)
            evidence = local_path(base, entry["evidence"], evidence_root)
            previous = result.get("correction", {})
            result["originalCurrent"] = result.get("originalCurrent", result["current"])
            result["correction"] = {
                "id": entry["id"],
                "date": manifest["date"],
                "originalStatus": previous.get("originalStatus", result["status"]),
                "originalReason": previous.get("originalReason", result.get("reason")),
                "evidence": Path(os.path.relpath(evidence, output)).as_posix(),
                "relatedImages": [
                    {"label": item["label"], "path": Path(os.path.relpath(local_path(base, item["path"], evidence_root), output)).as_posix()}
                    for item in entry.get("relatedImages", [])
                ],
            }
            result["current"] = Path(os.path.relpath(image, output)).as_posix()
            result["status"] = "corrected"
            if "currentOutcome" in entry:
                result["outcome"] = entry["currentOutcome"]
            result["reason"] = entry.get("currentReason")
            result["currentStatusLabel"] = entry["statusLabel"]
            result["note"] = entry["note"]
            applied.append({"lens": lens["name"], "analysis": entry["analysis"], "id": entry["id"]})
    data["appliedCorrections"] = applied
    data["appliedReviews"] = reviewed
    return applied
