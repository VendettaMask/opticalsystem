"""Compare real ordinary PNG exports across two off-screen window sizes."""
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops

root = Path(sys.argv[1])
output = Path(sys.argv[2])
audit = json.loads((root / "audit.json").read_text(encoding="utf-8-sig"))
records = audit["Records"]
groups = {}
for record in records:
    groups.setdefault(record["Kind"], []).append(record)

checks = []
for kind, pair in groups.items():
    assert len(pair) == 2, kind
    paths = [root / record["File"] for record in pair]
    with Image.open(paths[0]) as left, Image.open(paths[1]) as right:
        assert left.size == right.size == (2404, 1804), kind
        delta = ImageChops.difference(left.convert("RGBA"), right.convert("RGBA"))
        different = sum(pixel != (0, 0, 0, 0) for pixel in delta.get_flattened_data())
    assert different == 0, (kind, different)
    assert all(record["FooterWidth"] == 1200 and record["FooterHeight"] == 160 for record in pair), kind
    checks.append({
        "kind": kind,
        "pixelSize": [2404, 1804],
        "differentPixelsAcrossWindows": different,
        "files": [{"name": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in paths],
    })
assert len(checks) == 7
assert audit["SourceUnchanged"]
output.write_text(json.dumps({"sourceSha256": audit["SourceSha256"], "sourceUnchanged": True, "comparisons": checks},
                             ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("7 drawing families / 14 PNGs: all 2404x1804; all pairs have identical decoded RGBA pixels; source unchanged.")
