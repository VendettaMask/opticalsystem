"""Summarize saved Core results; no optical model or acceptance calculation."""
import json
from pathlib import Path
import statistics
import sys

root = Path(sys.argv[1]).resolve()
target = root / "comparison-summary.json"
if target.exists():
    raise SystemExit("Summary exists.")
runs = json.loads((root / "formal-runs.json").read_text(encoding="utf-8-sig"))
finals = json.loads((root / "formal-finalists.json").read_text(encoding="utf-8-sig"))
archives = json.loads((root / "formal-qualified-archive.json").read_text(encoding="utf-8-sig"))
replays = json.loads((root / "independent-original-replays.json").read_text(encoding="utf-8-sig"))
checks = json.loads((root / "formal-verification.json").read_text(encoding="utf-8-sig"))
protocol = json.loads((root / "experiment-protocol.json").read_text(encoding="utf-8-sig"))
expected = len(protocol["seeds"]) * len(protocol["arms"]) * len(protocol["cases"])
assert len(runs) == len(checks) == expected
assert all(value for check in checks for value in check.values() if isinstance(value, bool))
summary = []
for case in dict.fromkeys(r["case"] for r in runs):
    for arm in dict.fromkeys(r["arm"] for r in runs):
        group = [r for r in runs if r["case"] == case and r["arm"] == arm]
        added = [q for q in archives if q["case"] == case and q["arm"] == arm and q["parentId"] is not None]
        added_keys = {(q["seed"], q["id"]) for q in added}
        unique_new = {f["opticFingerprint"] for f in finals if f["case"] == case and f["arm"] == arm
                      and (f["seed"], f["id"]) in added_keys and not f["inheritedParent"] and f["meetsTargets"]}
        best = min(added, key=lambda q: (q["complexity"]["elements"], q["worstRmsMillimeters"]), default=None)
        summary.append({"case": case, "arm": arm, "runs": len(group),
                        "runsWithNewQualified": sum(r["newQualified"] > 0 for r in group),
                        "newQualified": sum(r["newQualified"] for r in group),
                        "uniqueNewOptics": len(unique_new),
                        "inheritedQualified": sum(r["inheritedQualified"] for r in group),
                        "medianEndToEndRays": statistics.median(r["endToEnd"]["rays"] for r in group),
                        "bestNewQualified": best})
target.write_text(json.dumps({"groups": summary, "runs": len(runs), "originalAssessments": len(finals),
                              "completedColdReplays": len(replays), "allSavedChecksPassed": True}, indent=2), encoding="utf-8")
lines = ["# 同任务、同上限、同 Core 对照", "", "仅本批显式策略与冻结任务；不是通用成功率或默认桌面效果。", "",
         "| 原任务 | 策略 | 新合格运行 | 新合格记录 / 去重处方 | 继承父方案 | 中位总光线 |", "| --- | --- | ---: | ---: | ---: | ---: |"]
for g in summary:
    lines.append(f'| {g["case"]} | {g["arm"]} | {g["runsWithNewQualified"]}/{g["runs"]} | {g["newQualified"]} / {g["uniqueNewOptics"]} | {g["inheritedQualified"]} | {g["medianEndToEndRays"]:,.0f} |')
lines += ["", f'{len(runs)} 次运行；{len(finals)} 份保存验收；{len(replays)} 份完成验收逐项冷复算。',
          "", "预算含原起点获取：600 函数、600 梯度、1000 万实际光线、300 秒；独立复算和诊断另计，不回灌搜索。",
          "", "F/4 不在本批运行中，保留原规格冲突。F/8 未合格不能据此认定物理不可行。"]
(root / "SUMMARY.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False))
