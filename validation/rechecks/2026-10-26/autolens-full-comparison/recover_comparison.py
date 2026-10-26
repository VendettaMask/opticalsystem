"""Recover interrupted external validation without changing either optimizer."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import traceback
import xml.etree.ElementTree as ET

import psutil

from progress import snapshot
from finalize_report import build

ROOT = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--runtime", type=Path, required=True)
args = parser.parse_args()
runtime = args.runtime.resolve() / "OpticalSystemDesign.InitialStructure.Benchmarks.dll"
native_directory = ROOT / "csharp-optimized"


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, data):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def save(state, **extra):
    write(ROOT / "job-status.json", {"state": state, "updatedUtc": datetime.now(timezone.utc).isoformat(),
                                   "workerPid": os.getpid(), "runtime": str(runtime), **extra})


def ensure_no_training_process():
    for process in psutil.process_iter(["pid", "cmdline"]):
        if process.pid == os.getpid():
            continue
        try:
            command = process.info["cmdline"] or []
            tokens = [str(value).replace("\\", "/").lower() for value in command]
            is_worker = any(value.endswith("/continue_comparison.py") or value.endswith("/recover_comparison.py") for value in tokens)
            is_original = any(value.endswith("/run_original.py") for value in tokens)
            is_native = str(runtime).replace("\\", "/").lower() in tokens and "train" in tokens
            if is_worker or is_original or is_native:
                raise RuntimeError(f"An existing comparison process must be inspected first: {process.pid}")
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue


for name, expected in [("core-final.trx", 31), ("reference-final.trx", 18)]:
    counters = ET.parse(ROOT.parent / "autolens-performance" / name).find(
        ".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters").attrib
    if counters["failed"] != "0" or counters["notExecuted"] != "0" or int(counters["passed"]) < expected or counters["executed"] != counters["passed"]:
        raise RuntimeError(f"Regression barrier did not pass: {name}")
if not runtime.is_file():
    raise RuntimeError("Published runtime is missing.")
ensure_no_training_process()
old = snapshot()
native_state = load(native_directory / "state.json")
if not native_state.get("TrainingSteps"):
    raise RuntimeError("No resumable C# training history exists.")
expected_binaries = load(native_directory / "binaries.json")["Binaries"]
for name, digest in expected_binaries.items():
    actual = hashlib.sha256((runtime.parent / name).read_bytes()).hexdigest()
    if actual != digest:
        raise RuntimeError(f"The frozen runtime changed: {name}")

archive = ROOT / ("interrupted-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%SZ"))
archive.mkdir()
(archive / "csharp-optimized").mkdir()
for name in ["state.json", "root-proof.json", "status.json", "input.json", "binaries.json", "loss-history.tsv", "latest-result.json", "latest.optic.json"]:
    shutil.copy2(native_directory / name, archive / "csharp-optimized" / name)
for name in ["original-status.json", "original-console.log", "job-status.json", "pipeline-console.log", "pipeline-errors.log"]:
    path = ROOT / name
    if path.exists():
        shutil.move(str(path), str(archive / name))
recovery = {
    "observedUtc": datetime.now(timezone.utc).isoformat(),
    "reason": "All three comparison processes disappeared without an exception or normal exit around 13:26 local time. System events show a Codex Windows Store update and sandbox service replacement at 13:26:18-20. That timing suggests app/service replacement interrupted the jobs; direct causation is not proven. OS did not reboot.",
    "interruptedSnapshot": old,
    "archiveDirectory": str(archive),
    "csharpRecovery": "Resume the saved full optimizer state, moments, random state and training history with exactly the same frozen binaries.",
    "originalRecovery": "The unchanged upstream saves prescriptions but no optimizer/random-state checkpoint. Restart its full unmodified entry point from seed 42; do not present a partial restart as an exact continuation.",
    "trainingConfigurationChanged": False,
}
write(ROOT / "recovery-record.json", recovery)
source = Path(old["original"]["status"]["sourceDirectory"])
environment = os.environ.copy()
dependencies = source.parent / "python-deps"
environment["PYTHONPATH"] = str(dependencies) + (os.pathsep + environment["PYTHONPATH"] if environment.get("PYTHONPATH") else "")
environment["PYTHONUNBUFFERED"] = "1"

try:
    with (ROOT / "original-console.log").open("w", encoding="utf-8") as original_log, (ROOT / "csharp-optimized-console.log").open("a", encoding="utf-8") as native_log:
        native_log.write("\nRECOVERY: exact saved-state resume at " + datetime.now(timezone.utc).isoformat() + "\n")
        native_log.flush()
        original_process = subprocess.Popen([sys.executable, "-u", str(ROOT / "run_original.py"), "--source", str(source), "--output", str(ROOT)],
                                            cwd=source, env=environment, stdout=original_log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        native_process = subprocess.Popen(["dotnet", str(runtime), "train", str(native_directory)],
                                          cwd=ROOT, stdout=native_log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        recovery.update(restartedOriginalPid=original_process.pid, resumedCsharpPid=native_process.pid, workerPid=os.getpid())
        write(ROOT / "recovery-record.json", recovery)
        while True:
            current = snapshot()
            original, native = current["original"]["status"], current["csharp"]
            original_code, native_code = original_process.poll(), native_process.poll()
            save("running", originalPid=original_process.pid, csharpPid=native_process.pid,
                 originalProgress=current["original"]["progress"], csharpProgress=native, recoveryRecord=str(ROOT / "recovery-record.json"))
            if native_code is not None and (native_code != 0 or native.get("State") != "completed"):
                raise RuntimeError(f"Resumed C# exit code {native_code}, state {native.get('State')}; original process is preserved.")
            if native_code == 0 and original_code is not None:
                result = Path(current["original"]["resultDirectory"])
                final_original, final_native = result / "final_lens.json", native_directory / "final.optic.json"
                if not final_original.exists() or not final_native.exists():
                    raise RuntimeError(f"Missing final prescription; original exit code {original_code}.")
                save("analyzing", originalResultDirectory=str(result), csharpResultDirectory=str(native_directory))
                analysis = ROOT / "final-common-analysis"
                with (ROOT / "final-analysis.log").open("w", encoding="utf-8") as analysis_log:
                    code = subprocess.call(["dotnet", str(runtime), "compare", str(final_original), str(final_native), str(analysis)],
                                           cwd=ROOT, stdout=analysis_log, stderr=subprocess.STDOUT)
                if code:
                    raise RuntimeError(f"Common endpoint evaluation exited with {code}; training files are preserved.")
                summary = build(result, analysis)
                save("completed" if summary["fullCountsVerified"] else "incomplete", fullCountsVerified=summary["fullCountsVerified"],
                     counts=summary["counts"], report=str(ROOT / "FINAL_REPORT.md"), summary=str(ROOT / "final-comparison.json"))
                break
            time.sleep(30)
except BaseException:
    save("failed", error=traceback.format_exc())
    raise
