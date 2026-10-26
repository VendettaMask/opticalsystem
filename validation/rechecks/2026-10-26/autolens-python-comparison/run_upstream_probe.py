"""Development-only comparison; calls the downloaded, unchanged AutoLens code."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import random
import string
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--mode", choices=["fixed-rays", "short-training"], required=True)
args = parser.parse_args()
sys.path.insert(0, str(args.source))
args.output.mkdir(parents=True, exist_ok=True)

import torch
from deeplens import GeoLens
from deeplens.geometric_surface import Aperture, Aspheric
from deeplens.ray import Ray


def fixed_rays():
    """Identical geometry, input rays, material n/V and double precision."""
    policy = json.loads((args.output / "regenerated-policy.json").read_text())
    initial = policy["initialCases"][0]
    torch.set_default_dtype(torch.float64)
    lens = GeoLens(device="cpu", dtype=torch.float64)
    lens.surfaces = []
    for i, row in enumerate(initial["surfaces"]):
        if i == 0:
            surface = Aperture(r=row["radius"], d=row["d"])
        else:
            material = "air" if row["material"] == "air" else f'{row["nd"]}/{row["abbe"]}'
            surface = Aspheric(r=row["radius"], d=row["d"], c=row["curvature"],
                               k=0.0, ai=[0.0] * 8, mat2=material)
            surface.get_optimizer_params(optim_mat=True)
        lens.surfaces.append(surface)
    lens.aper_idx = 0
    lens.d_sensor = torch.tensor(10.0, requires_grad=True)
    lens.distortion_max = 0.03
    pupils = [(0, 0), (.3, 0), (-.3, 0), (0, .3), (0, -.3), (.8, 0), (1.1, 0)]
    angles = [0, 10, 30, 45]
    fields, backups, ideals = [], [], []
    for wave_index, wavelength in enumerate([.656, .587, .486]):
        origins, directions = [], []
        for ring, angle in enumerate(angles):
            for arm in range(2):
                h = math.tan(math.radians(angle))
                hx, hy = (h, 0) if arm == 0 else (0, h)
                origin = [-20000 * hx, -20000 * hy, -20000]
                ideal = [5 * hx, 5 * hy]
                if wave_index == 0:
                    ideals.append(ideal)
                inputs = []
                for px, py in pupils:
                    target = [px * initial["surfaces"][0]["radius"], py * initial["surfaces"][0]["radius"], 0]
                    direction = [target[k] - origin[k] for k in range(3)]
                    norm = math.sqrt(sum(v * v for v in direction))
                    direction = [v / norm for v in direction]
                    origins.append(origin)
                    directions.append(direction)
                    inputs.append({"origin": origin, "direction": direction})
                fields.append({"ring": ring, "arm": arm, "wave": wave_index,
                               "ideal": ideal, "inputs": inputs})
        ray = Ray(torch.tensor(origins).reshape(4, 2, 7, 3),
                  torch.tensor(directions).reshape(4, 2, 7, 3), wavelength, device="cpu")
        backups.append(ray)
        with torch.no_grad():
            result = lens.trace2sensor(ray.clone())
        for j, field in enumerate(fields[wave_index * 8:(wave_index + 1) * 8]):
            positions = result.o.reshape(8, 7, 3)[j].tolist()
            valid = result.is_valid.reshape(8, 7)[j].tolist()
            field["outputs"] = [{"position": p, "valid": bool(v)} for p, v in zip(positions, valid)]
    pinhole = torch.tensor(ideals).reshape(4, 2, 2)
    selected = [lens.surfaces[1].c, lens.surfaces[1].d, lens.surfaces[1].mat2.n, lens.d_sensor]
    cases = []
    for centroid in [False, True]:
        rms, distortion = lens.loss_rms_from_ray(backups, pinhole, centroid=centroid)
        loss = rms + .1 * distortion
        gradient = torch.autograd.grad(loss, selected)
        cases.append({"centroid": centroid, "loss": loss.item(),
                      "gradient": [g.item() for g in gradient]})
    result = {"scope": "Fixed rays through six-element seed-42 initial lens; not optimized quality",
              "dtype": "float64", "elements": 6, "rays": 168,
              "parameters": [{"surface": 2, "kind": "Curvature"},
                             {"surface": 2, "kind": "AxialPosition"},
                             {"surface": 2, "kind": "MaterialIndex"},
                             {"surface": 14, "kind": "AxialPosition"}],
              "fields": fields, "imageCases": cases}
    path = args.output / "upstream-fixed-rays.json"
    path.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n")
    print(json.dumps({"output": str(path), "imageCases": cases}), flush=True)


def short_training():
    """Original six-element mobile protocol with 8+8 iterations, default ray counts."""
    spec = importlib.util.spec_from_file_location("original_autolens", args.source / "1_autolens.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    cfg = module.MOBILE_CONFIG.copy()
    cfg.update(curriculum_iters=8, refine_iters=8)
    module.set_seed(cfg["seed"])
    tag = "".join(random.choice(string.ascii_letters + string.digits) for _ in range(4))
    directory = args.output / "upstream-short-training"
    directory.mkdir(exist_ok=True)
    module.set_logger(str(directory))
    (directory / "config.json").write_text(json.dumps(cfg, indent=2))
    started = time.perf_counter()
    lens = module.create_lens(foclen=cfg["foclen"], fov=cfg["fov"], fnum=cfg["fnum"],
                              bfl=cfg["bfl"], thickness=cfg["thickness"], surf_list=cfg["surf_list"],
                              save_dir=str(directory))
    lens.set_target_fov_fnum(rfov_eff=cfg["fov"] / 2 / 57.3, fnum=cfg["fnum"])
    lens.curriculum_design(lrs=cfg["lrs"], iterations=8, test_per_iter=4,
                           optim_mat=True, shape_control=True, centroid=True, result_dir=str(directory))
    lens.match_materials()
    lens.set_fnum(cfg["fnum"])
    handoff = directory / "curriculum_final.json"
    lens.write_lens_json(str(handoff))
    use_centroid = lens.rfov_eff > .52
    lens = GeoLens(filename=str(handoff))
    lens.optimize(lrs=[.5 * rate for rate in cfg["lrs"]], iterations=8, test_per_iter=4,
                  centroid=use_centroid, optim_mat=False, shape_control=True,
                  sample_more_off_axis=True, result_dir=str(directory / "refine"))
    lens.prune_surf(expand_factor=.05)
    lens.post_computation()
    lens.write_lens_json(str(directory / "final_lens.json"))
    lens.analysis(save_name=str(directory / "final_lens"))
    summary = {"scope": "Short smoke run; 9+9 updates, default ray counts; not default complete optimization or quality comparison",
               "device": str(lens.device), "gpu": torch.cuda.get_device_name(0), "torch": torch.__version__,
               "durationSeconds": time.perf_counter() - started, "tag": tag,
               "curriculumUpdates": 9, "refinementUpdates": 9,
               "focalLengthMm": float(lens.foclen), "halfFovRadians": float(lens.rfov_eff),
               "fNumber": float(lens.fnum), "sensorRadiusMm": float(lens.r_sensor)}
    (directory / "run-summary.json").write_text(json.dumps(summary, indent=2, allow_nan=False) + "\n")
    print(json.dumps(summary), flush=True)


fixed_rays() if args.mode == "fixed-rays" else short_training()
