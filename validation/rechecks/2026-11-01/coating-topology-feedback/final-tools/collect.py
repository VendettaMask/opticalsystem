from pathlib import Path
import json
import shutil
import sys

root = Path(sys.argv[1])
read = lambda p: json.loads(Path(p).read_text(encoding="utf-8-sig"))
arms = {"feedback": ["primary-feedback-final", "hr-feedback-final", "cross-feedback-final"], "control": ["primary-control-final", "hr-control-final", "cross-control-final"], "prototype": ["primary-prototype-final", "cross-prototype-final"]}
if len(sys.argv)>2 and sys.argv[2]=="--native-only":
    del arms["prototype"]
rows = {}
stages = []
all_stages = []
for arm, folders in arms.items():
    rows[arm] = [r for folder in folders for r in read(root / folder / "summary.json")]
    assert len(rows[arm]) == (6 if arm == "prototype" else 9), (arm, len(rows[arm]))
    dest = root / (arm + "-all")
    dest.mkdir(exist_ok=True)
    for folder in folders:
        for p in (root / folder).glob("*.coating.json"):
            shutil.copyfile(p, dest / p.name)
    for row in rows[arm]:
        original = read(root / "inputs" / (row["Name"] + ".coating.json"))
        for index, run in enumerate(row["Runs"]):
            all_stages.append({"Name": row["Name"] + "-" + arm + "-" + str(index), "Source": row["Name"], "Arm": arm, "Stage": run["Stage"], "Round": run["Round"], "Layers": run["FinalLayers"], "ReportedMetrics": run["Metrics"], "ReportedOd": run["Od"], "ReportedPassed": run["Passed"]})
            if run["Stage"] == "full" or run["Passed"] and not original["Result"]["Passed"]:
                stages.append({"Name": row["Name"] + "-" + arm + "-" + str(index), "Source": row["Name"], "Arm": arm, "Stage": run["Stage"], "Round": run["Round"], "Layers": run["FinalLayers"], "ReportedMetrics": run["Metrics"], "ReportedOd": run["Od"], "ReportedPassed": run["Passed"]})
(root / "native-stage-prescriptions.json").write_text(json.dumps(stages, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(root / "all-stage-prescriptions.json").write_text(json.dumps(all_stages, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(root / "paired-raw.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"arms": {a: len(v) for a, v in rows.items()}, "fullAndNewPassStagePrescriptions": len(stages), "topologyProposals": sum(r["Proposals"] for r in rows["feedback"])}))
