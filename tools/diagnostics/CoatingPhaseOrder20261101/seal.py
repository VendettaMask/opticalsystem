from pathlib import Path
import hashlib
import json
import sys

root = Path(sys.argv[1])
entries = []
for p in sorted(root.rglob("*")):
    relative = p.relative_to(root)
    if not p.is_file() or p.name == "final-manifest.json" or any(x.endswith("-runtime") or x in {"bin", "obj", "__pycache__"} for x in relative.parts[:-1]):
        continue
    entries.append({"path": relative.as_posix(), "bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()})
manifest = {"scope": "2026-11-01 offline diagnostic evidence; runtimes identified separately, bin/obj excluded", "files": len(entries), "bytes": sum(x["bytes"] for x in entries), "entries": entries}
path = root / "final-manifest.json"
path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
reopened = json.loads(path.read_text(encoding="utf-8"))
assert all(hashlib.sha256((root / x["path"]).read_bytes()).hexdigest() == x["sha256"] for x in reopened["entries"])
print(json.dumps({"files": manifest["files"], "bytes": manifest["bytes"], "readbackMismatches": 0}))
