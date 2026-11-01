"""Summarize immutable evidence; no optical model, optimization or acceptance calculation."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

root = Path(sys.argv[1]).resolve()
with zipfile.ZipFile(root / "source.zip") as archive:
    for item in json.loads((root / "source-sha256.json").read_text(encoding="utf-8")):
        if hashlib.sha256(archive.read(item["path"])).hexdigest() != item["sha256"]:
            raise SystemExit(f"Frozen source mismatch: {item['path']}")
for manifest_name in ("runtime-sha256.json",):
    for item in json.loads((root / manifest_name).read_text(encoding="utf-8")):
        if hashlib.sha256((root / item["path"]).read_bytes()).hexdigest() != item["sha256"]:
            raise SystemExit(f"Frozen runtime mismatch: {item['path']}")
rows = []
for seed in range(42, 47):
    row = {"seed": seed}
    for arm in ("control", "candidate"):
        path = root / arm / f"seed{seed}" / "summary.json"
        row[arm] = json.loads(path.read_text(encoding="utf-8")) if path.exists() else None
    rows.append(row)
print(json.dumps(rows, indent=2, ensure_ascii=False))
