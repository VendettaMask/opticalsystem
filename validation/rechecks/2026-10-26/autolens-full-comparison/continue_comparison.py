"""Local worker for the already requested full comparison; never sends messages."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import time
import traceback
import xml.etree.ElementTree as ET

from progress import snapshot
from finalize_report import build

ROOT = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--runtime", type=Path, required=True)
args = parser.parse_args()
runtime = args.runtime.resolve() / "OpticalSystemDesign.InitialStructure.Benchmarks.dll"
native_directory = ROOT / "csharp-optimized"
if native_directory.exists():
    raise RuntimeError("Optimized full run already exists; do not create a duplicate.")

for name, minimum_passed in [("core-final.trx",31), ("reference-final.trx",18)]:
    path = ROOT.parent / "autolens-performance" / name
    counters = ET.parse(path).find(".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters").attrib
    if counters["failed"] != "0" or counters["notExecuted"] != "0" or int(counters["passed"]) < minimum_passed or counters["executed"] != counters["passed"]:
        raise RuntimeError(f"Regression barrier did not pass: {name}")


def save(state, **extra):
    target = ROOT / "job-status.json"
    data = {"state":state, "updatedUtc":datetime.now(timezone.utc).isoformat(), **extra}
    temporary = target.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    temporary.replace(target)


def incomplete(reason, progress):
    save("incomplete", reason=reason, progress=progress)
    (ROOT / "FINAL_REPORT.md").write_text("# 完整对比未完成\n\n"+reason+
        "\n\n未把中间检查点当作完整结果。原始日志、状态和错误记录仍在本目录。\n", encoding="utf-8")


try:
    with (ROOT / "csharp-optimized-console.log").open("w", encoding="utf-8") as console:
        process = subprocess.Popen(["dotnet",str(runtime),"train",str(native_directory)], stdout=console, stderr=subprocess.STDOUT)
        save("running", csharpPid=process.pid, runtime=str(runtime))
        while True:
            current = snapshot()
            original = current["original"]["status"]
            native = current["csharp"]
            exit_code = process.poll()
            save("running", csharpPid=process.pid, runtime=str(runtime),
                 originalProgress=current["original"]["progress"], csharpProgress=native)
            if exit_code is not None and (exit_code != 0 or native.get("State") != "completed"):
                incomplete(f"优化版 C# 退出码 {exit_code}，阶段 {native.get('Phase')}，课程 {native.get('CurriculumUpdates')}/2001，精修 {native.get('RefinementUpdates')}/3001。", current)
                break
            if exit_code == 0 and original.get("state") in {"completed", "failed"}:
                result = Path(current["original"]["resultDirectory"])
                final_original = result / "final_lens.json"
                final_native = native_directory / "final.optic.json"
                if not final_original.exists() or not final_native.exists():
                    incomplete("进程已结束，但缺少一份最终处方。", current)
                    break
                save("analyzing", originalResultDirectory=str(result), csharpResultDirectory=str(native_directory))
                analysis = ROOT / "final-common-analysis"
                with (ROOT / "final-analysis.log").open("w", encoding="utf-8") as analysis_log:
                    code = subprocess.call(["dotnet",str(runtime),"compare",str(final_original),str(final_native),str(analysis)],
                                           stdout=analysis_log, stderr=subprocess.STDOUT)
                if code:
                    incomplete(f"统一终点评价退出码 {code}；训练文件保持。", current)
                    break
                summary = build(result, analysis)
                save("completed" if summary["fullCountsVerified"] else "incomplete",
                     fullCountsVerified=summary["fullCountsVerified"], counts=summary["counts"],
                     report=str(ROOT / "FINAL_REPORT.md"), summary=str(ROOT / "final-comparison.json"))
                break
            time.sleep(30)
except BaseException:
    save("failed", error=traceback.format_exc())
    raise
