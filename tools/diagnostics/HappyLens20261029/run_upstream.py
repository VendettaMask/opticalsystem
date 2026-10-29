"""Bounded, headless HappyLens API exercise; never imported by the product.

Use the original library's Delano, System, Generation_Prime and SHADE methods.
Only population sizes and iteration budgets differ from the research examples.
"""
import argparse
import json
import os
from pathlib import Path
import sys
import time

os.environ.setdefault("MPLBACKEND", "Agg")
os.environ.setdefault("OMP_NUM_THREADS", "2")
os.environ.setdefault("MKL_NUM_THREADS", "2")
parser = argparse.ArgumentParser()
parser.add_argument("--source", required=True)
parser.add_argument("--output", required=True)
args = parser.parse_args()
sys.path.insert(0, args.source)
import torch
torch.set_num_threads(2)
torch.set_default_dtype(torch.float64)
torch.set_default_device("cpu")
import lens

class BoundedSystem(lens.System):
    # Original initialization algorithm, with an explicit four-proposal smoke budget.
    def simulated_annealing(self, *a, **kw):
        return super().simulated_annealing(*a, **kw, T=2., T_min=1., alpha=.5, iter=2)

output = Path(args.output)
output.mkdir(parents=True, exist_ok=True)
started = time.monotonic()
lens.set_random_seed(42)
merit = {
    "EFL": {"target": 50., "weight": 1.},
    "FNO": {"target": 8., "weight": .01},
    "SPOT": {"ref": "chief", "k": 1., "weight": 1.},
    "BFL": {"target": 15., "weight": 1.},
    "TOTR": {"target": 65., "weight": 1.},
    "GLA_MIN_THICK": {"min_thick": .5, "weight": 10.},
    "GLA_MAX_THICK": {"max_thick": 10., "weight": 1.},
    "AIR_THICK": {"target": .1, "weight": 10.},
}
delano = lens.Delano("S|S|S", 32, 5., 50., 8., 65., 15., 0, "K|F|K")
initial = float(delano.fitness().min().detach())
adam = torch.optim.Adam([delano._y, delano._ybar], lr=1e-3)
for _ in range(20):
    adam.zero_grad()
    loss = delano.fitness()
    loss.sum().backward()
    adam.step()
    delano.update_y_ybar()
    delano.generate()
final = delano.fitness().detach()
delano.idx = int(torch.nan_to_num(final, nan=float("inf")).argmin())
delano.ins_tol_iters = 1
summary = {"device": "cpu", "torch": torch.__version__, "seed": 42,
           "delano": {"population": 32, "updates": 20, "initial_min": initial,
                      "final_min": float(final.min()), "finite": int(torch.isfinite(final).sum())},
           "paths": {}}
common = dict(wavelengths=[486.1327e-6, 587.5618e-6, 656.2725e-6], p_wvl=1,
              max_view=5., sys_num=4, cfg_num=1, norm_views=[0., 1.], azimuths=[0.],
              pre_samp=None, samp_method="ring")
for path in ["delano", "random"]:
    print("PATH", path, flush=True)
    system = BoundedSystem(**common, **({"delano": [delano, "S|S|S", "K|F|K", "G|G|G", merit]}
                           if path == "delano" else {"random": ["S|S|S", "S|S|S", "K|F|K", "G|G|G", 0, merit]}))
    generation = lens.Generation_Prime(system=system, samp_rays=2)
    for i in range(1, len(system.system)-1):
        system.freeze_sys_param(i, "all")
        if i != system.stop_id:
            system.unfreeze_sys_param(i, "roc")
            system.unfreeze_sys_param(i, "thick")
    generation.update_stop_radius([8.])
    generation.update_system()
    local = torch.optim.Adam(generation.params_lr(1e-4), fused=False)
    losses = []
    for stage in range(2):
        for _ in range(2):
            local.zero_grad()
            loss = generation.forward_loss(merit)
            finite = torch.isfinite(loss)
            if not finite.any():
                raise RuntimeError(path + ": all losses non-finite")
            loss[finite].sum().backward()
            local.step()
        losses.append(float(loss[finite].min().detach()))
        generation.differential_evolution_system_shade(merit, iters=1, c=2, p=.5)
    with torch.no_grad():
        loss = generation.forward_loss(merit)
    summary["paths"][path] = {"local_updates": 4, "shade_generations": 2,
        "population": 4, "stage_loss": losses, "final_loss": loss.tolist(),
        "valid": system.valid.tolist(), "finite": int(torch.isfinite(loss).sum())}
    # Original library serialization; analysis plots run with the Agg backend.
    data = system.extract_all_sys_data()
    (output / (path + "-prescriptions.json")).write_text(json.dumps(data,
        default=lambda x: x.detach().cpu().tolist() if torch.is_tensor(x) else str(x), indent=2))
summary["elapsed_seconds"] = time.monotonic() - started
(output / "summary.json").write_text(json.dumps(summary, indent=2))
print(json.dumps(summary), flush=True)
