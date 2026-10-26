"""Temporarily suspend this task's original run while measuring; always resume."""
import json
from pathlib import Path
import subprocess
import sys
import psutil

status = Path(__file__).resolve().parent.parent / "autolens-full-comparison/original-status.json"
process = None
if status.exists():
    original = json.loads(status.read_text())
    if original.get("state") == "running":
        try:
            candidate = psutil.Process(original["pid"])
            if any("run_original.py" in item for item in candidate.cmdline()):
                process = candidate
        except psutil.NoSuchProcess:
            pass
try:
    if process:
        process.suspend()
        print(f"Temporarily suspended original process {process.pid} for isolated measurement.", flush=True)
    code = subprocess.call(sys.argv[1:])
finally:
    if process:
        try:
            process.resume()
            print(f"Resumed original process {process.pid}.", flush=True)
        except psutil.NoSuchProcess:
            pass
raise SystemExit(code)
