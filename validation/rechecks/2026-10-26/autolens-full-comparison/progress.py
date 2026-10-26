"""Read compact progress without changing either training process."""
import json
from datetime import datetime
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent


def read(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None


def snapshot():
    original = read(ROOT / "original-status.json") or {}
    native = read(ROOT / "csharp-optimized/status.json") or read(ROOT / "csharp/status.json") or {}
    if not (ROOT / "csharp-optimized/status.json").exists() and (ROOT / "csharp/stop.request").exists():
        native = dict(native, State="paused-for-performance-work")
    source = Path(original.get("sourceDirectory", "D:/optical system design/tmp/autolens-upstream-20261026/source"))
    candidates = list((source / "results").glob("*/config.json"))
    if original.get("resultDirectories"):
        candidates = [Path(path) / "config.json" for path in original["resultDirectories"] if (Path(path) / "config.json").exists()]
    elif original.get("startedUtc"):
        started = datetime.fromisoformat(original["startedUtc"]).timestamp()
        candidates = [path for path in candidates if path.stat().st_mtime >= started - 2]
    result = max(candidates, key=lambda p: p.stat().st_mtime).parent if candidates else None
    counts = {}
    if result:
        for phase, relative in [("curriculum", "loss_history.tsv"), ("refinement", "refine/loss_history.tsv")]:
            path = result / relative
            if path.exists():
                counts[phase] = max(0, len(path.read_text().splitlines()) - 1)
    path = ROOT / "original-console.log"
    if path.exists():
        with path.open("rb") as handle:
            handle.seek(max(0, path.stat().st_size - 128_000))
            tail = handle.read().decode("utf-8", errors="replace")
        matches = list(re.finditer(r"(Curriculum|Progress):[^\r\n]*?(\d+)/(\d+)", tail))
        if matches:
            match = matches[-1]
            counts["livePhase"] = "curriculum" if match[1] == "Curriculum" else "refinement"
            counts["liveUpdates"] = int(match[2])
            counts["livePlanned"] = int(match[3])
    return {"original": {"status": original, "resultDirectory": str(result) if result else None, "progress": counts},
            "csharp": native, "pipeline": read(ROOT / "job-status.json")}


if __name__ == "__main__":
    print(json.dumps(snapshot(), ensure_ascii=True, indent=2))
