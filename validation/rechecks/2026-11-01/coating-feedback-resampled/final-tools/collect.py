from pathlib import Path
import json
import shutil
import sys

r = Path(sys.argv[1])
read = lambda p: json.loads(Path(p).read_text(encoding="utf-8-sig"))
groups = ["primary", "material", "cross"]
for arm, folders, count in [
    ("formal-baseline-all", ["formal-search-baseline"] + ["baseline-" + g + "-search" for g in groups], 9),
    ("formal-final-all", ["two-window-" + g for g in groups], 9),
    ("formal-append-all", ["append-" + g + "-old" for g in groups], 9),
]:
    dest = r / arm
    dest.mkdir(exist_ok=True)
    rows = {}
    for folder in folders:
        for row in read(r / folder / "summary.json"):
            assert row["Name"] not in rows, (arm, row["Name"])
            rows[row["Name"]] = row
        for p in (r / folder).glob("*.coating.json"):
            shutil.copyfile(p, dest / p.name)
    assert len(rows) == count, (arm, len(rows))
    (dest / "summary.json").write_text(json.dumps(list(rows.values()), indent=2) + "\n")

stages = []
for p in (r / "formal-final-all").glob("*.coating.json"):
    d = read(p)
    for window in d["Result"]["Run"].get("StructureFeedbackWindows", []):
        if window["BaselineLayers"]:
            stages.append({"Name": d["Name"] + f"-feedback-{window['Round']}-baseline", "Source": d["Name"], "Arm": "formal-feedback", "Stage": "baseline", "Round": window["Round"], "Layers": window["BaselineLayers"]})
        for i, short in enumerate(window["Screening"]):
            stages.append({"Name": d["Name"] + f"-feedback-{window['Round']}-short-{i}", "Source": d["Name"], "Arm": "formal-feedback", "Stage": "short", "Round": window["Round"], "Layers": short["Layers"]})
        if window["FullResultLayers"]:
            stages.append({"Name": d["Name"] + f"-feedback-{window['Round']}-full", "Source": d["Name"], "Arm": "formal-feedback", "Stage": "full", "Round": window["Round"], "Layers": window["FullResultLayers"]})
(r / "feedback-stage-prescriptions.json").write_text(json.dumps(stages, indent=2) + "\n")

arms = ["all-inputs", "formal-baseline-all", "formal-final-all", "formal-append-all", "primary-old-schedule", "primary-role-baseline", "primary-rematch", "hr-old-schedule", "hr-topology", "hr-chirp", "material-rematch", "material-old-schedule", "continued-rematch", "continued-old-schedule", "larger-old", "reference-native"]
cold = r / "cold-inputs"
cold.mkdir(exist_ok=True)
index = []
for arm in arms:
    for p in sorted((r / arm).glob("*.coating.json")):
        name = arm + "__" + p.name
        shutil.copyfile(p, cold / name)
        index.append({"File": name, "Arm": arm, "Original": str(p), "Name": read(p)["Name"]})
(r / "cold-index.json").write_text(json.dumps(index, indent=2) + "\n")
print(json.dumps({"formalCases": 9, "feedbackStagePrescriptions": len(stages), "retainedColdFiles": len(index)}))
