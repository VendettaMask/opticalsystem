"""Verify both browser indexes and retain identities for scoped image overrides."""
import hashlib
import json
import subprocess
from pathlib import Path
from PIL import Image

base = Path(__file__).resolve().parent.parent
sha256 = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
record = {"date": "2026-11-21", "scope": "gallery references and image identities only",
          "correctionsManifestSha256": sha256(base / "corrections.json"), "galleries": []}
for output in (base, base / "representative-six"):
    check = subprocess.run(["node", str(base / "capture/verify-gallery.cjs"), str(output)],
                           check=True, capture_output=True, text=True, encoding="utf-8")
    result = {"directory": output.relative_to(base).as_posix(), "checks": json.loads(check.stdout), "corrections": []}
    data = json.loads((output / "gallery-data.json").read_text(encoding="utf-8"))
    for lens in data["lenses"]:
        if sha256(Path(lens["source"])) != lens["sha256"]:
            raise ValueError(f"Source identity changed: {lens['source']}")
        for analysis, item in lens["results"].items():
            if not item.get("correction"):
                continue
            identities = {}
            for key in ("current", "originalCurrent", "native"):
                if not item.get(key):
                    continue
                path = (output / item[key]).resolve()
                with Image.open(path) as image:
                    image.load()
                    size = list(image.size)
                identities[key] = {"path": item[key], "sha256": sha256(path), "pixelSize": size}
            result["corrections"].append({"lens": lens["name"], "analysis": analysis,
                                          "id": item["correction"]["id"], "images": identities,
                                          "evidence": item["correction"]["evidence"]})
    record["galleries"].append(result)
(base / "corrections-verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"verifiedGalleries": len(record["galleries"]),
                  "scopedCorrections": [len(item["corrections"]) for item in record["galleries"]]}, ensure_ascii=False))
