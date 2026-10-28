"""Plot already recorded independent C# physical assessments, with no optical evaluation."""
import json
from pathlib import Path
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
summary = json.loads((HERE / "paired-runs/summary.json").read_text(encoding="utf-8"))
fig, axes = plt.subplots(1, 2, figsize=(10, 4.1), constrained_layout=True)
for row in summary["Runs"]:
    fields = sorted(row["Final"]["Fields"], key=lambda f: f["Field"]["Y"])
    xs = [f["Field"]["Y"] for f in fields]
    for ax, key in zip(axes, ["RmsRadiusMillimeters", "MaximumRadiusMillimeters"]):
        ax.plot(xs, [f[key] * 1000 for f in fields], label=f"v{row['Version']}", linewidth=2)
for ax, gate, title in zip(axes, [50, 150], ["Polychromatic RMS radius (um)", "Maximum spot radius (um)"]):
    ax.axhline(gate, color="#c24d44", linestyle="--", label=f"Original gate: {gate}")
    ax.set_title(title, fontsize=11)
    ax.set_xlabel("Normalized field")
    ax.grid(alpha=.2)
    ax.spines[["top", "right"]].set_visible(False)
    ax.legend()
fig.suptitle("Fixed two-element source | unchanged aperture, field, twelve bounds and physical gates", fontsize=11)
fig.savefig(HERE / "field-comparison.png", dpi=160)
plt.close(fig)
