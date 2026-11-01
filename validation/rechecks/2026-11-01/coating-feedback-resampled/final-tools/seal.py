from pathlib import Path
import hashlib
import json
import sys

root = Path(sys.argv[1])
entries = []
for path in sorted(root.rglob("*")):
    relative = path.relative_to(root)
    if not path.is_file() or path.name == "final-manifest.json" or any(
        part.endswith("-runtime") or part in {"bin", "obj", "__pycache__"}
        for part in relative.parts[:-1]
    ):
        continue
    entries.append({"path": relative.as_posix(), "bytes": path.stat().st_size,
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
manifest = {"scope": "Resampled coating feedback and measured material pairs; runtimes identified separately; bin/obj excluded",
            "files": len(entries), "bytes": sum(x["bytes"] for x in entries), "entries": entries}
target = root / "final-manifest.json"
target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
assert all(hashlib.sha256((root / entry["path"]).read_bytes()).hexdigest() == entry["sha256"]
           for entry in json.loads(target.read_text(encoding="utf-8"))["entries"])
print(json.dumps({"files": len(entries), "bytes": manifest["bytes"], "readbackMismatches": 0}))
