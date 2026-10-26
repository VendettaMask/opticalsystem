"""Run the unchanged upstream entry point and retain completion/provenance."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
import traceback

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
source = args.source.resolve()
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
status_file = output / "original-status.json"
if status_file.exists():
    raise RuntimeError("Use a new output directory for a new original run.")
os.chdir(source)
sys.path.insert(0, str(source))
before = set((source / "results").glob("*")) if (source / "results").exists() else set()
status = {
    "state": "running", "pid": os.getpid(), "startedUtc": datetime.now(timezone.utc).isoformat(),
    "sourceDirectory": str(source), "python": sys.version,
    "upstreamCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "entryPointSha256": hashlib.sha256((source / "1_autolens.py").read_bytes()).hexdigest(),
    "configuration": "Unchanged MOBILE_CONFIG, seed 42, 2000+3000 inclusive iterations, default sampling and diagnostics",
}
status_file.write_text(json.dumps(status, indent=2) + "\n")
started = time.perf_counter()
code = 0
try:
    runpy.run_path(str(source / "1_autolens.py"), run_name="__main__")
except BaseException as error:
    code = error.code if isinstance(error, SystemExit) and isinstance(error.code, int) else 1
    status["exception"] = traceback.format_exc()
    traceback.print_exc()
finally:
    status.update(state="completed" if code == 0 else "failed", exitCode=code,
                  finishedUtc=datetime.now(timezone.utc).isoformat(), durationSeconds=time.perf_counter() - started,
                  resultDirectories=[str(path) for path in (source / "results").glob("*") if path not in before])
    status_file.write_text(json.dumps(status, indent=2) + "\n")
    print(json.dumps(status), flush=True)
raise SystemExit(code)
