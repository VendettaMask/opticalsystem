"""Summarize frozen C# results; this script performs no optical computation."""
from pathlib import Path
import json
import sys

root = Path(sys.argv[1]).resolve()
protocol = json.loads((root / "experiment-protocol.json").read_text(encoding="utf-8-sig"))
rows = []
for arm in protocol["arms"]:
    for seed in protocol["seeds"]:
        for case in protocol["cases"]:
            folder = root / arm / f"seed{seed}"
            saved = json.loads((folder / f"{case}.seed-search.json").read_text(encoding="utf-8-sig"))["result"]
            inputs = json.loads((folder / f"{case}.input.json").read_text(encoding="utf-8-sig"))
            evolution = saved["evolution"]
            archive = evolution["qualifiedArchive"]
            finalists = []
            for trial in evolution["trials"]:
                assessment = trial.get("originalAssessment")
                if not assessment or not assessment["completed"]:
                    continue
                fields = assessment["fields"]
                rms = [f["rmsRadiusMillimeters"] for f in fields]
                maximum = [f["maximumRadiusMillimeters"] for f in fields]
                finalists.append(dict(id=trial["id"], operation=trial["operation"],
                    elements=len(trial["endpoint"]["elements"]), qualified=assessment["meetsTargets"],
                    worstRmsUm=max(rms) * 1000 if all(v is not None for v in rms) else None,
                    worstMaximumUm=max(maximum) * 1000 if all(v is not None for v in maximum) else None,
                    minimumValid=min(f["validRays"] / f["attemptedRays"] for f in fields),
                    boundaryLost=sum(f["checkAttemptedRays"] - f["checkValidRays"] for f in fields),
                    violations=assessment["violations"]))
            work, acquired = saved["work"], inputs["acquisition"]
            row = dict(arm=arm, seed=seed, case=case, stopReason=evolution["stopReason"],
                inherited=sum(q["parentId"] is None for q in archive),
                new=sum(q["parentId"] is not None for q in archive), finalists=finalists,
                functions=work["functionEvaluations"] + acquired["functionEvaluations"],
                gradients=work["gradientEvaluations"] + acquired["gradientEvaluations"],
                rays=work["tracedRays"] + acquired["tracedRays"],
                searchElapsed=work["elapsed"], acquisitionElapsed=acquired["elapsed"])
            assert row["functions"] <= 600 and row["gradients"] <= 200 and row["rays"] <= 2_000_000
            rows.append(row)
(root / "comparison.json").write_text(json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8")
lines = ["| 组别 | 原任务 | 运行 | 继承合格 | 新合格 | 终验候选 | 总实际光线（含起点） |",
         "| --- | --- | ---: | ---: | ---: | ---: | ---: |"]
for arm in protocol["arms"]:
    for case in protocol["cases"]:
        selected = [row for row in rows if row["arm"] == arm and row["case"] == case]
        lines.append(f"| {arm} | {case} | {len(selected)} | {sum(r['inherited'] for r in selected)} | "
                     f"{sum(r['new'] for r in selected)} | {sum(len(r['finalists']) for r in selected)} | {sum(r['rays'] for r in selected):,} |")
(root / "comparison-table.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
