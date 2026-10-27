import json
from pathlib import Path
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
import numpy as np

root = Path(__file__).resolve().parent
font = font_manager.FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
plt.rcParams.update({"font.family": font.get_name(), "axes.unicode_minus": False, "font.size": 10})
q = json.loads((root / "qualified-control/summary.json").read_text(encoding="utf-8"))["Runs"]
s = json.loads((root / "paired-runs/summary.json").read_text(encoding="utf-8"))["Runs"]
control = next(r for r in q if r["Seed"] == 42 and r["Group"] == "C" and r["Mode"] == "curvature")["ReducedCandidates"][0]
qualified = next(r for r in q if r["Seed"] == 42 and r["Group"] == "C" and r["Mode"] == "axial")
qualified = next(c for c in qualified["Archive"] if c["Elements"] == 3)
fig, axes = plt.subplots(1, 2, figsize=(12, 5), gridspec_kw={"width_ratios": [1, 1.7]})
fig.patch.set_facecolor("#f8fafc")
for ax in axes:
    ax.set_facecolor("white")
    ax.spines[["top", "right"]].set_visible(False)
    ax.grid(axis="y", alpha=.2)
    ax.set_axisbelow(True)
    ax.set_ylabel("正式密集验收最差 RMS（µm）")
    ax.axhline(50, color="#dc2626", linestyle="--", linewidth=1.2)
bars = axes[0].bar([0, 1], [control["WorstRmsMillimeters"] * 1000, qualified["WorstRmsMillimeters"] * 1000], color=["#94a3b8", "#059669"], width=.56)
axes[0].set_xticks([0, 1], ["仅曲率\n失光9条 · 未达标", "加入轴向补偿\n失光0条 · 达标"])
axes[0].set_ylim(0, 64)
axes[0].set_title("已合格四片 → 三片\n相同预算，正式自主流程", pad=15)
for bar in bars:
    axes[0].text(bar.get_x() + bar.get_width() / 2, bar.get_height() + 1.2, f"{bar.get_height():.3f}", ha="center")
axes[0].text(.5, 58, "4/4轴向控制得到同一合格三片", ha="center", fontsize=9, color="#047857")
before, after = [], []
for task in ["new-wide4-1", "new-wide4-2", "new-wide4-3", "new-wide4-4"]:
    for mode, values in [("curvature", before), ("axial", after)]:
        row = next(r for r in s if r["Task"] == task and r["Seed"] == 42 and r["Group"] == "C" and r["Mode"] == mode)
        values.append(row["BestEndpoint"]["WorstRmsMillimeters"] * 1000 if row["BestEndpoint"] else np.nan)
x = np.arange(4)
axes[1].bar(x - .18, before, .34, color="#94a3b8", label="高精度，仅曲率")
axes[1].bar(x + .18, after, .34, color="#2563eb", label="高精度 + 轴向")
axes[1].set_xticks(x, ["F/3.169 · 30°\n4片 → 4片", "F/3.238 · 18°\n无完整终点", "F/3.342 · 21°\n4片 → 4片", "F/3.608 · 28°\n4片 → 6片"])
axes[1].set_ylim(0, 145)
axes[1].set_title("四份新处方：尚无合格减片\n种子42 / 直接路线，所有失败保留", pad=15)
axes[1].text(1, 20, "未评完 / 瞄准不可用", ha="center", rotation=90, fontsize=9, color="#64748b")
axes[1].text(2, 60, "失光14 → 10\n最大光斑/畸变仍失败", ha="center", fontsize=8, color="#b45309")
axes[1].legend(frameon=False, fontsize=9)
fig.suptitle("高精度采样与有界轴向补偿：同预算短程验证", fontsize=16, fontweight="bold")
fig.text(.5, .015, "虚线为RMS 50µm门槛；合格还需通光、最大光斑、畸变、一阶参数和制造约束全部通过。单一控制源不代表普遍成功率。", ha="center", fontsize=9)
fig.tight_layout(rect=(0, .07, 1, .92))
fig.savefig(root / "comparison.png", dpi=170)
fig.savefig(root / "comparison.svg")
