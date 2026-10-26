"""Report the exact first-stage objective and honestly label available optics."""
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


capture = load(ROOT / "capture.json")
original_directory = Path(capture["originalSource"]).parent
with (original_directory / "loss_history.tsv").open() as handle:
    original_history = list(csv.DictReader(handle, delimiter="\t"))
state = load(ROOT / "csharp-frozen-state.json")
native_history = [row for row in state["TrainingSteps"] if row["Phase"] == 0]
if len(original_history) != 2001 or len(native_history) != 2001:
    raise RuntimeError("Both complete first-stage histories are required.")
original_objective = [float(row["loss_rms"]) + .1 * (float(row["loss_reg"]) + float(row["loss_distortion"])) for row in original_history]
native_objective = [row["BeforeLoss"] for row in native_history]
analysis_directory = ROOT / "common-analysis-corrected-apertures" if (ROOT / "common-analysis-corrected-apertures/common-analysis.json").exists() else ROOT / "common-analysis"
comparison = load(analysis_directory / "common-analysis.json")
a, b = comparison["Results"]
if not all(result["PrescriptionUnchangedByAnalysis"] for result in [a, b]):
    raise RuntimeError("Endpoint analysis mutated a prescription.")
keys = ["FocalLengthMm", "FNumber", "EntrancePupilDiameterMm", "SensorPositionMm", "WorstValidFieldRmsMm", "ValidFieldCount", "ValidRayFraction"]
differences = []
for key in keys:
    x, y = a.get(key), b.get(key)
    differences.append({"metric": key, "original": x, "csharp": y,
                        "difference": y - x if x is not None and y is not None else None,
                        "relativeDifferencePercent": 100 * (y - x) / abs(x) if x and y is not None else None})
objective = {"original": original_objective[-1], "csharp": native_objective[-1],
             "difference": native_objective[-1] - original_objective[-1],
             "relativeDifferencePercent": 100 * (native_objective[-1] - original_objective[-1]) / original_objective[-1],
             "scope": "Last pre-update objective of each complete 2001-update curriculum, before catalog handoff. Original logged components have limited decimal precision. Different dtype and random streams remain.",
             "formula": "loss_rms + 0.1*(loss_reg + loss_distortion)",
             "opticalQualityCertificate": False}
surfaces = []
for sa, sb in zip(a["Prescription"]["Surfaces"], b["Prescription"]["Surfaces"]):
    surfaces.append({"surface": sa["Number"], "originalMaterial": sa["Material"], "csharpMaterial": sb["Material"],
                     "positionDifferenceMm": sb["PositionMm"] - sa["PositionMm"],
                     "curvatureDifferencePerMm": sb["Curvature"] - sa["Curvature"],
                     "semiDiameterDifferenceMm": sb["RadiusMm"] - sa["RadiusMm"]})
summary = {"scope": capture["scope"], "counts": capture["counts"], "firstStageObjective": objective,
           "availableOpticalResults": differences, "commonAnalysis": comparison, "prescriptionDifferences": surfaces,
           "equalStageOpticalComparisonAvailable": False,
           "limitation": "The C# runner overwrote its rolling state without retaining a dedicated curriculum/catalog boundary snapshot. Do not label the partially refined C# optic as an unrefined endpoint.",
           "capture": capture, "analysisDirectory": str(analysis_directory),
           "apertureCorrection": load(ROOT / "aperture-correction-check.json") if (ROOT / "aperture-correction-check.json").exists() else None,
           "sha256": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
                      for path in ROOT.rglob("*") if path.is_file() and path.name not in {"summary.json", "REPORT.md", "comparison.png"} and "__pycache__" not in path.parts}}
(ROOT / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

labels = ["焦距 / mm", "F 数", "入瞳直径 / mm", "像面位置 / mm", "最差有数据视场 RMS / mm", "有数据视场数 / 21", "积分及边界检查采样有效比例"]
def number(value):
    return "无数据" if value is None else f"{value:.9g}"
table = "\n".join(f"| {label} | {number(row['original'])} | {number(row['csharp'])} | {number(row['difference'])} |"
                  for label, row in zip(labels, differences))
report = f"""# AutoLens 现有结果即时对比

用户因完整精修耗时较长，要求先看不等待精修完成时的差异。

后续更正：先前原版导入只保留显示/机械半口径，未建立物理口径；现已逐面显式设置物理口径并对原两份冻结处方补算。本次采样下原版及 C# 的表内数值经核对没有改变，但旧导入快照不作为正确口径模型的证明。以下以补算数据为准，旧报告、机器数据及快照另存保留。见 [口径更正核对](aperture-correction-check.json)。

## 可以严格按阶段比较的训练记录

两边均已完整执行第一阶段 2001 次更新。最后一次更新前的训练总目标，原版 **{objective['original']:.9g}**，C# **{objective['csharp']:.9g}**；C# 减少 **{-objective['difference']:.9g}（{-objective['relativeDifferencePercent']:.4f}%）**。原版总目标由原始 TSV 的 `loss_rms + 0.1*(loss_reg+loss_distortion)` 重建，因此精度受日志小数位限制。该差值描述训练目标，不等于焦距、光斑或通光率差值，也不证明达到设计目标。

## 可用处方的统一物理评价

原版采用旧运行已完成的第一阶段及材料目录交接处方，**2001 次课程、0 次精修**。C# 采用本次冻结的最新滚动状态，**{capture['counts']['csharpCurriculum']} 次课程、{capture['counts']['csharpRefinement']} 次精修**。C# 编排只滚动保存，没有单独保留第一阶段边界处方，这是本轮记录的缺口；不能把该处方称为未精修终点，也不能用此表判断精修前两套实现的优劣。

统一使用相同共享 Core CPU/double、21 个 0–45° 半视场、RGB 656/587/486 nm 等权、10×32 高斯面积积分瞳及中心/64 个边界检查点。保持各自保存的像面与口径；没有重新调焦或放大口径。两份处方分析前后指纹一致。

| 指标 | 原版：第一阶段后 | C#：部分精修后 | C# − 原版 |
| --- | ---: | ---: | ---: |
{table}

采样有效比例是几何通光情况，不包含表面反射吸收等能量透过率。最差 RMS 只包含有数据视场，不能忽略视场缺失或通光不足。设计目标为 5 mm、F/1.8、全视场 90°，训练目标接近不代表光学硬验收通过。

见 [完整数据](summary.json)、[统一评价补算输出](common-analysis-corrected-apertures/common-analysis.json)、[曲线](comparison.png)及[冻结来源与计数](capture.json)。保留被中断的原版运行并明确其阶段归属，不将其冒充完整 5002 次更新结果。
"""
(ROOT / "REPORT.md").write_text(report, encoding="utf-8")

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
fig, axes = plt.subplots(1, 3, figsize=(15, 4), constrained_layout=True)
for values, label, color in [(original_objective, "Original curriculum", "#2563eb"), (native_objective, "C# curriculum", "#ea580c")]:
    axes[0].semilogy(range(1, len(values) + 1), values, color=color, label=label, linewidth=.8)
axes[0].set(title="Same stage: training objective", xlabel="Curriculum update", ylabel="Pre-update objective")
for data, label, color in [(a, "Original: 2001 + 0", "#2563eb"), (b, f"C#: 2001 + {capture['counts']['csharpRefinement']}", "#ea580c")]:
    fields = data["Fields"]
    x = [field["HalfAngleDegrees"] for field in fields]
    axes[1].plot(x, [1000*field["RmsMm"] if field.get("RmsMm") is not None else float("nan") for field in fields], color=color, marker=".", label=label)
    axes[2].plot(x, [(field.get("IntegrationValid", 0) + field.get("CheckValid", 0)) / max(1, field.get("IntegrationAttempted", 0) + field.get("CheckAttempted", 0)) for field in fields], color=color, marker=".", label=label)
axes[1].set(title="Available optics: different stages", xlabel="Half field (degrees)", ylabel="Polychromatic RMS radius (um)")
axes[2].set(title="Available optics: geometric throughput", xlabel="Half field (degrees)", ylabel="Valid-ray fraction", ylim=(-.02, 1.02))
for axis in axes:
    axis.grid(alpha=.2)
    axis.legend(fontsize=8)
fig.savefig(ROOT / "comparison.png", dpi=150)
plt.close(fig)
print(json.dumps({"firstStageObjective": objective, "availableOpticalResults": differences}, ensure_ascii=False, indent=2))
