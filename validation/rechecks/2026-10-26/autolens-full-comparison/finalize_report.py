"""Build an honest comparison after both requested full runs terminate."""
import csv
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def link(path, title):
    return f"[{title}](<{path.resolve().as_posix()}>)"


def build(original_directory, analysis_directory):
    original_directory, analysis_directory = Path(original_directory), Path(analysis_directory)
    original = load(ROOT / "original-status.json")
    native_directory = ROOT / "csharp-optimized" if (ROOT / "csharp-optimized/status.json").exists() else ROOT / "csharp"
    native = load(native_directory / "status.json")
    recovery_path = ROOT / "recovery-record.json"
    recovery = load(recovery_path) if recovery_path.exists() else None
    recovery_note = ("约 13:26 两次训练及本地汇总进程曾同时无正常退出记录地中断；系统同刻安装 Codex 更新并替换沙箱服务，时间相关，直接因果未证明。"
                     "C# 从完整优化器检查点精确续跑；原版没有优化器与随机状态检查点，故从种子 42 完整重跑未修改的入口。"
                     "早先未完成运行及中断记录单独保留，最终表只采用重跑完成的原版与完整续跑的 C#。"
                     if recovery else "原版运行包含中间隔离测速的暂停，耗时只作记录。")
    complete_counts = {"originalCurriculum": len(list(csv.DictReader((original_directory / "loss_history.tsv").open(), delimiter="\t"))),
                       "originalRefinement": len(list(csv.DictReader((original_directory / "refine/loss_history.tsv").open(), delimiter="\t"))),
                       "csharpCurriculum": native["CurriculumUpdates"], "csharpRefinement": native["RefinementUpdates"]}
    complete = list(complete_counts.values()) == [2001, 3001, 2001, 3001]
    common = load(analysis_directory / "common-analysis.json")
    a, b = common["Results"]
    metrics = [("焦距（mm）", "FocalLengthMm"), ("F 数", "FNumber"), ("入瞳直径（mm）", "EntrancePupilDiameterMm"),
               ("像面轴向位置（mm）", "SensorPositionMm"), ("最差有数据视场 RMS（mm）", "WorstValidFieldRmsMm"),
               ("有数据视场数 / 21", "ValidFieldCount"), ("积分及检查光线有效比例", "ValidRayFraction")]
    def number(value):
        return "无有效数据" if value is None else f"{value:.9g}" if isinstance(value, (int, float)) else str(value)
    table = "\n".join(f"| {name} | {number(a[key])} | {number(b[key])} |" for name, key in metrics)
    rows = []
    for sa, sb in zip(a["Prescription"]["Surfaces"], b["Prescription"]["Surfaces"]):
        ca, cb = sa["Coefficients"], sb["Coefficients"]
        rows.append({"surface": sa["Number"], "originalMaterial": sa["Material"], "csharpMaterial": sb["Material"],
                     "originalPositionMm": sa["PositionMm"], "csharpPositionMm": sb["PositionMm"],
                     "positionDifferenceMm": sb["PositionMm"] - sa["PositionMm"],
                     "originalCurvaturePerMm": sa["Curvature"], "csharpCurvaturePerMm": sb["Curvature"],
                     "curvatureDifferencePerMm": sb["Curvature"] - sa["Curvature"],
                     "originalRadiusMm": sa["RadiusMm"], "csharpRadiusMm": sb["RadiusMm"],
                     "originalConic": sa["Conic"], "csharpConic": sb["Conic"],
                     "maximumCoefficientAbsoluteDifference": max([abs(x-y) for x, y in zip(ca, cb)] or [0])})
    with (ROOT / "prescription-differences.csv").open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    summary = {"scope": "Same MOBILE_CONFIG and seed 42, existing Python float32 and C# double implementations; final evaluation in shared Core CPU/double",
               "fullCountsVerified": complete, "counts": complete_counts, "originalRun": original, "csharpRun": native,
               "commonAnalysis": common, "sameGpuConcurrentRuns": True,
               "performanceCaution": "Overlapping runs share CPU/GPU; elapsed times are observed work durations, not an isolated speed benchmark",
               "csharpImplementation": "Performance-repaired implementation with cuda-fixed-input-forward-v1; pre-repair run stopped at update 34 and kept separately",
               "productNumericalCodeChangesDuringThisComparison": ["Implementation changed before optimized full run; see ../autolens-performance/README.md"],
               "prescriptionDifferences": rows,
               "interruptionRecovery": recovery,
               "sha256": {str(p.relative_to(ROOT)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in ROOT.rglob("*") if p.is_file() and not {"bin", "obj", "__pycache__"}.intersection(p.parts)
                          and p.name not in {"final-comparison.json", "job-status.json", "monitor-console.log"}}}
    (ROOT / "final-comparison.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    body = f"""# AutoLens 完整默认流程对比

用户要求：相同配置和种子，把原版及现有 C# 都完整运行后比较终点。

完整更新次数核对：**{'全部完成' if complete else '未全部完成，不能作为完整结果'}**。
原版 {complete_counts['originalCurriculum']} + {complete_counts['originalRefinement']}，C# {complete_counts['csharpCurriculum']} + {complete_counts['csharpRefinement']}。

两边均为六片、目标 5 mm、F/1.8、全视场 90°、初始总长 10 mm、后焦 2 mm、物距 20000 mm、RGB 656/587/486 nm、种子 42，训练采样数量及阶段长度没有降低。原版运行未修改的 e58 入口；C# 调用现有 UpstreamE58 优化器，按原有精确恢复机制保存每次更新。仅增加运行资源上限，没有更改训练算法。双方共用这台电脑的 CPU/GPU 并行运行，耗时作为运行记录，不作为独占硬件速度排名。

## 最终光学结果

两份保存的终点处方统一在现有共享 Core CPU/double 中评价：21 个 0–45° 半视场、10×32 高斯面积积分瞳，另检查中心及 64 个边界点，三波长等权，使用共同多色质心；保持各自原像面和口径，不重新调焦或放大镜片。原版 JSON 按其读取规则累加 d_next，并使用对应原目录真实色散。

| 指标 | 原版 Python 终点 | C# 耗时修复后的终点 |
| --- | ---: | ---: |
{table}

最差 RMS 只汇总有有效数据的视场；视场缺失或通光不足不能因其他光斑较小而算达标。产品原有独立硬验收另见 C# 最终结果文件，不把训练完成等同于设计成功。

{link(ROOT / 'comparison-plots.png', '训练轨迹及统一终点曲线')}

{link(ROOT / 'prescription-differences.csv', '逐面处方差异')} · {link(ROOT / 'final-comparison.json', '完整机器账目')} · {link(analysis_directory / 'common-analysis.json', '统一评价原始数据')}

## 解释范围

这是两种现有实现相同用户配置及相同启动种子的实际端到端对照。原版使用 float32，C# 使用共享 Core double；训练随机流和诊断随机数消耗未逐位统一。结果差异同时包含这些既有实现差异，不能仅用本轮一个种子推断成功率或归因于单一因素。没有中途改算法后覆盖失败结果。

原版进程退出码：{original.get('exitCode')}。{recovery_note} 耗时只作运行记录。改动前 C# 第 34 步的状态另行保留，本表不混用旧运行终点。两边原始日志、状态、损失轨迹、处方和运行文件指纹均保留。原版完整结果目录：`{original_directory}`。
"""
    (ROOT / "FINAL_REPORT.md").write_text(body, encoding="utf-8")
    plot(original_directory, common)
    return summary


def plot(original_directory, common):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    series = []
    for label, directory in [("Original", original_directory)]:
        values = []
        for relative in ["loss_history.tsv", "refine/loss_history.tsv"]:
            with (directory / relative).open() as handle:
                rows = list(csv.DictReader(handle, delimiter="\t"))
            values.extend(float(r["loss_rms"]) + .1 * (float(r["loss_reg"]) + float(r["loss_distortion"])) for r in rows)
        series.append((label, values))
    native_directory = ROOT / "csharp-optimized" if (ROOT / "csharp-optimized/status.json").exists() else ROOT / "csharp"
    with (native_directory / "loss-history.tsv").open() as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))
    series.append(("C#", [float(r["preUpdateLoss"]) for r in rows]))
    fig, axes = plt.subplots(2, 2, figsize=(12, 8), constrained_layout=True)
    colors = ["#2563eb", "#ea580c"]
    for (label, values), color in zip(series, colors):
        axes[0, 0].semilogy(range(1, len(values)+1), values, label=label, color=color, linewidth=.8)
    axes[0, 0].axvline(2001, color="gray", linestyle="--", linewidth=.7)
    axes[0, 0].set(xlabel="Adam update", ylabel="Pre-update training objective", title="Full training trajectories")
    for data, color in zip(common["Results"], colors):
        fields = data["Fields"]
        x = [row["HalfAngleDegrees"] for row in fields]
        axes[0, 1].plot(x, [1000 * row["RmsMm"] if row.get("RmsMm") is not None else float("nan") for row in fields], color=color, marker=".", label=data["Name"])
        axes[1, 0].plot(x, [(row.get("IntegrationValid", 0) + row.get("CheckValid", 0)) /
                           max(1, row.get("IntegrationAttempted", 0) + row.get("CheckAttempted", 0)) for row in fields], color=color, marker=".", label=data["Name"])
    axes[0, 1].axhline(10, color="gray", linestyle="--", linewidth=.7)
    axes[0, 1].set(xlabel="Half field (degrees)", ylabel="Polychromatic RMS radius (um)", title="Shared Core evaluation at saved image planes")
    axes[1, 0].axhline(.98, color="gray", linestyle="--", linewidth=.7)
    axes[1, 0].set(xlabel="Half field (degrees)", ylabel="Valid-ray fraction", ylim=(-.02, 1.02), title="Integration and boundary-check throughput")
    for offset, (data, color) in enumerate(zip(common["Results"], colors)):
        axes[1, 1].bar([offset*.3, 1+offset*.3], [data["FocalLengthMm"]/5, data["FNumber"]/1.8], width=.28, color=color, label=data["Name"])
    axes[1, 1].axhline(1, color="gray", linestyle="--", linewidth=.7)
    axes[1, 1].set(xticks=[.15, 1.15], xticklabels=["EFL / target", "F-number / target"], ylabel="Ratio", title="Final first-order targets")
    for axis in axes.flat:
        axis.grid(alpha=.18)
        axis.legend(fontsize=8)
    fig.savefig(ROOT / "comparison-plots.png", dpi=160)
    plt.close(fig)


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
