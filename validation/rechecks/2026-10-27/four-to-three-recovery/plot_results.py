"""Plot recorded physical endpoints; does not run optical calculations."""
import json
from pathlib import Path
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
data = json.loads((HERE / "verification.json").read_text(encoding="utf-8"))
cells = {c["Id"]: c for c in data["Cells"]}
stages = ["axial", "pupil", "conic", "allocated"]
labels = ["A/C + axes", "+ fixed-stop pupil", "+ six conics", "Use remaining quota"]
values = [cells[f"four-to-three-42-C-{s}"]["ThreeElement"] for s in stages]
fig, axes = plt.subplots(1, 3, figsize=(12, 4.6), constrained_layout=True)
for ax, key, title, threshold in zip(axes, ["RmsMicrometers", "MaximumMicrometers", "DistortionPercent"],
                                  ["Worst-field RMS radius (um)", "Worst-field maximum radius (um)", "Maximum absolute distortion (%)"], [50, 150, 3]):
    bars = ax.bar(range(4), [v[key] for v in values], color=["#8696a8", "#5095c9", "#d9a33e", "#2c9c79"])
    ax.axhline(threshold, color="#c24d44", linestyle="--", label=f"Unchanged gate: {threshold}")
    ax.set_xticks(range(4), labels, rotation=30, ha="right", fontsize=9)
    ax.set_title(title, fontsize=11)
    ax.set_ylim(0, max(max(v[key] for v in values), threshold) * 1.2)
    ax.bar_label(bars, fmt="%.3f", fontsize=9, padding=4)
    ax.spines[["top", "right"]].set_visible(False)
    ax.legend(loc="upper right", fontsize=8)
fig.suptitle("Same qualified four-element source -> three elements | direct deletion, search seed 42", fontsize=12)
fig.supxlabel("All endpoints: zero lost rays. Conics add freedom; final allocation changes Full ceiling 8 -> 99 within the original total quotas.", fontsize=9)
fig.savefig(HERE / "endpoint-comparison.png", dpi=170)
plt.close(fig)
