import json
from pathlib import Path
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

root = Path(__file__).resolve().parent
font = font_manager.FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
plt.rcParams.update({"font.family": font.get_name(), "axes.unicode_minus": False, "font.size": 10})
rows = json.loads((root / "fixed-repair/summary.json").read_text(encoding="utf-8"))["Runs"]
loss = [sum(f["CheckAttemptedRays"] - f["CheckValidRays"] for f in row["Final"]["Fields"]) for row in rows]
maximum = [1000 * max(f["MaximumRadiusMillimeters"] for f in row["Final"]["Fields"]) for row in rows]
distortion = [100 * max(f["DistortionFraction"] for f in row["Final"]["Fields"]) for row in rows]
labels = ["旧v3", "仅最大统计修正", "联合修正"]
colors = ["#94a3b8", "#d97706", "#059669"]
fig, axes = plt.subplots(1, 3, figsize=(12.8, 4.6))
fig.patch.set_facecolor("#f8fafc")
for ax in axes:
    ax.spines[["top", "right"]].set_visible(False)
    ax.grid(axis="y", alpha=.2)
    ax.set_axisbelow(True)
    ax.set_xticks(range(3), labels, fontsize=9)
axes[0].bar(range(3), loss, color=colors, width=.55)
axes[0].set_ylim(0, 12)
axes[0].set_title("边界检查失光（条）")
for i, value in enumerate(loss):
    axes[0].text(i, value + .35, str(value), ha="center", fontweight="bold")
axes[1].bar(range(3), maximum, color=colors, width=.55)
axes[1].axhline(150, color="#dc2626", linestyle="--", linewidth=1)
axes[1].set_ylim(0, 185)
axes[1].set_title("正式最大光斑半径（µm）")
for i, value in enumerate(maximum):
    axes[1].text(i, value + 4, f"{value:.3f}", ha="center")
axes[2].axhline(0, color="#dc2626", linestyle="--", linewidth=1)
delta = [value - 3 for value in distortion]
axes[2].scatter(range(3), delta, c=colors, s=65)
axes[2].set_ylim(-.003, .022)
axes[2].set_xlim(-.5, 2.5)
axes[2].set_title("畸变相对3%门槛的偏差（百分点）")
for i, value in enumerate(distortion):
    axes[2].annotate(f"{value:.6f}%", (i, delta[i]), xytext=(0, -20 if i == 2 else 12), textcoords="offset points", ha="center", fontsize=10)
fig.suptitle("F/3.34、半视场21°：从10条失光终点做同预算短程接续", fontsize=15, fontweight="bold")
fig.text(.5, .035, "仍为四片；原16参数边界、最多8次更新。联合修正2次更新后通过全部原门槛；仅统计修正仍有失光与畸变超限。", ha="center", fontsize=9)
fig.tight_layout(rect=(0, .09, 1, .89))
fig.savefig(root / "comparison.png", dpi=170)
fig.savefig(root / "comparison.svg")
