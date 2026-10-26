"""Short external validation only: unchanged upstream functions, fixed inputs, no training."""
import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import sys
import time
import traceback

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--epsilon-diagnostic", action="store_true")
args = parser.parse_args()
sys.path.insert(0, str(args.source))
sys.path.insert(0, str(args.source.parent / "python-deps"))
import torch
from deeplens import GeoLens
from deeplens.geometric_surface import Aperture, Aspheric
from deeplens.ray import Ray

torch.set_default_dtype(torch.float64)
torch.set_num_threads(2)
ROOT = Path(__file__).resolve().parent
WAVES = [.656, .587, .486]
policy = json.loads((ROOT.parent / "autolens-python-comparison/regenerated-policy.json").read_text())


def from_initial(index):
    rows = copy.deepcopy(policy["initialCases"][index]["surfaces"])
    for row in rows:
        row["conic"] = 0.0
        row["ai"] = [0.0] * 8
    return rows


def from_saved(path):
    data = json.loads(path.read_text())
    position = 0.0
    rows = []
    for row in data["surfaces"]:
        rows.append({"d": position, "radius": row["r"], "curvature": 1/row["roc"] if row.get("roc") else 0,
                     "conic": row.get("k", 0), "ai": row.get("ai", [0.0]*8), "material": row["mat2"]})
        position += row["d_next"]
    return rows, position


def create_lens(rows, sensor, materials):
    lens = GeoLens(device="cpu", dtype=torch.float64)
    lens.surfaces = []
    leaves, parameters = [], []
    for i, row in enumerate(rows):
        if i == 0:
            surface = Aperture(r=row["radius"], d=row["d"])
        else:
            material = row["material"]
            if materials and material != "air":
                material = f'{row["nd"]:.17g}/{row["abbe"]:.17g}'
            surface = Aspheric(r=row["radius"], d=row["d"], c=row["curvature"],
                               k=row["conic"], ai=row["ai"], mat2=material)
            surface.get_optimizer_params(optim_mat=materials)
            for kind, leaf, coefficient in [("AxialPosition", surface.d, 0), ("Curvature", surface.c, 0), ("Conic", surface.k, 0)]:
                leaves.append(leaf)
                parameters.append({"surface": i+1, "kind": kind, "coefficient": coefficient})
            for index in range(surface.ai_degree):
                leaves.append(getattr(surface, f"_ai{2*(index+2)}"))
                parameters.append({"surface": i+1, "kind": "EvenAsphereCoefficient", "coefficient": index+1})
            if materials and row["material"] != "air":
                for kind, leaf in [("MaterialIndex", surface.mat2.n), ("MaterialAbbe", surface.mat2.V)]:
                    leaves.append(leaf)
                    parameters.append({"surface": i+1, "kind": kind, "coefficient": 0})
        lens.surfaces.append(surface)
    lens.aper_idx = 0
    lens.d_sensor = torch.tensor(float(sensor), requires_grad=True)
    leaves.append(lens.d_sensor)
    parameters.append({"surface": len(rows)+1, "kind": "AxialPosition", "coefficient": 0})
    lens.r_sensor = 5.0
    lens.rfov_eff = math.pi/4
    lens.rfov = math.pi/4
    lens.foclen = 5.0
    lens.obj_depth = -20000.0
    lens.init_constraints({})
    return lens, leaves, parameters


def inputs(lens, rows, kind, scale=1.0):
    pupils = [(0,0),(.23,.17),(-.23,-.17),(.7,0),(-.7,0),(0,.7),(0,-.7),(.9,.2),(-.9,-.2),(1.01,0),(-1.01,0)]
    angles = [0, 10, 30, 45]
    if kind != "ordinary":
        angles = [0, 0]
    fields, backups, ideals = [], [], []
    for wi, wave in enumerate(WAVES):
        origins, directions = [], []
        for ring, angle in enumerate(angles):
            for arm in range(2):
                h = math.tan(math.radians(angle))
                hx, hy = (h, 0) if arm == 0 else (0, h)
                ideal = [5*hx, 5*hy]
                if wi == 0:
                    ideals.append(ideal)
                local = []
                if kind.startswith("critical"):
                    n = float(lens.surfaces[1].mat2.ior(torch.tensor(wave)).detach())
                    radial = 1/(abs(rows[2]["curvature"])*n)
                    fractions = [0,.7,1-1e-4,1-1e-7,1-1e-10,1,1+1e-10,1+1e-7,1+1e-4,1.05,-1+1e-7]
                    if kind == "critical-below":
                        fractions = [0,.1,.3,.5,.7,.8,.9,.95,.99,.999,.9999]
                    elif kind == "critical-near-below":
                        fractions = [1-10**(-power) for power in [4,5,6,7,8,9,10,9,8,7,6]]
                    elif kind == "critical-above":
                        fractions = [1.0001+i*.002 for i in range(11)]
                elif kind == "grazing":
                    radial = 1/abs(rows[1]["curvature"])
                    fractions = [0,.5,.9,1-1e-4,1-1e-7,1-1e-10,1,1+1e-10,1+1e-7,1+1e-4,-.9]
                elif kind in {"aperture", "all-invalid"}:
                    radial = rows[0]["radius"]
                    fractions = [0,.5,.9,1-1e-7,1-1e-10,1,1+1e-10,1+1e-7,1.01,1.1,-1] if kind == "aperture" else [1.1+i*.1 for i in range(11)]
                else:
                    radial = None
                for pi, (px,py) in enumerate(pupils):
                    if radial is not None:
                        coordinate = radial*fractions[pi]*scale
                        origin = [coordinate if arm==0 else 0, coordinate if arm==1 else 0, -20000]
                        direction = [0,0,1]
                    else:
                        origin = [-20000*hx,-20000*hy,-20000]
                        target = [px*rows[0]["radius"]*scale,py*rows[0]["radius"]*scale,0]
                        direction = [target[k]-origin[k] for k in range(3)]
                        norm = math.sqrt(sum(v*v for v in direction))
                        direction = [v/norm for v in direction]
                    origins.append(origin)
                    directions.append(direction)
                    local.append({"origin":origin,"direction":direction})
                fields.append({"ring":ring,"arm":arm,"wave":wi,"ideal":ideal,"inputs":local,
                               "weights":[(.25+((pi+3*ring+5*arm+7*wi)%13)/13)*(-1 if (pi+ring)%3==0 else 1) for pi in range(len(pupils))]})
        backups.append(Ray(torch.tensor(origins,dtype=torch.float64).reshape(len(angles),2,len(pupils),3),
                           torch.tensor(directions,dtype=torch.float64).reshape(len(angles),2,len(pupils),3), wave,device="cpu"))
    return fields, backups, torch.tensor(ideals).reshape(len(angles),2,2)


def run(name, rows, sensor=10.0, materials=True, kind="ordinary", boundary=False):
    started = time.perf_counter()
    lens, leaves, parameters = create_lens(rows,sensor,materials)
    fields, backups, ideals = inputs(lens,rows,kind)
    cra_fields, cra_backups, _ = inputs(lens,rows,kind,.1)
    results = []
    for wi, ray in enumerate(backups):
        result = lens.trace2sensor(ray.clone())
        count = len(fields)//3
        for j, field in enumerate(fields[wi*count:(wi+1)*count]):
            positions = result.o.reshape(count,-1,3)[j].detach().tolist()
            directions = result.d.reshape(count,-1,3)[j].detach().tolist()
            bends = result.bend_penalty.reshape(count,-1)[j].detach().tolist()
            valid = result.is_valid.reshape(count,-1)[j].detach().tolist()
            field["outputs"] = [{"valid":bool(v),"x":p[0] if v else None,"y":p[1] if v else None,
                                 "directionZ":d[2] if v else None,"bend":b if v else None}
                                for p,d,b,v in zip(positions,directions,bends,valid)]
        results.append(result)

    def evaluate(label, factory):
        loss = factory()
        gradients = torch.autograd.grad(loss,leaves,allow_unused=True,retain_graph=True) if loss.requires_grad else [None]*len(leaves)
        raw = [float(g.detach()) if g is not None else 0.0 for g in gradients]
        return {"name":label,"value":float(loss.detach()),"gradient":[x if math.isfinite(x) else 0.0 for x in raw],
                "nonfiniteRawGradientSlots":[i for i,x in enumerate(raw) if not math.isfinite(x)]}

    def weighted(component):
        value = torch.tensor(0.0)
        for wi,result in enumerate(results):
            field_count = len(fields)//3
            weights = torch.tensor([f["weights"] for f in fields[wi*field_count:(wi+1)*field_count]]).reshape(result.is_valid.shape)
            raw = result.o[...,component] if component<2 else result.d[...,2] if component==2 else result.bend_penalty.squeeze(-1)
            value = value + (torch.where(result.is_valid.bool(),raw,torch.zeros_like(raw))*weights).sum()
        return value

    losses = [evaluate(name,lambda component=i:weighted(component)) for i,name in enumerate(["traceX","traceY","traceDirectionZ","traceBend"])]
    def image(centroid):
        rms, distortion = lens.loss_rms_from_ray(backups,ideals,centroid=centroid)
        return rms+.1*distortion
    def geometry():
        return lens.loss_reg(w_cra=0,w_ray_bend=0,w_mat=1 if materials else 0)[0]
    # Inject only immutable input rays into the original sampling boundary; the original
    # trace, CRA, bend, geometry and loss functions themselves are called unchanged.
    lens.sample_ring_arm_rays = lambda **kw: (cra_backups[1] if kw.get("scale_pupil")==.1 else backups[1]).clone()
    def regularization():
        return lens.loss_cra()+lens.loss_ray_bend()
    for centroid in [False,True]:
        losses.append(evaluate("image-centroid" if centroid else "image-pinhole",lambda centroid=centroid:image(centroid)))
    losses.append(evaluate("geometry",geometry))
    losses.append(evaluate("ray-regularization",regularization))
    for centroid in [False,True]:
        losses.append(evaluate("total-centroid" if centroid else "total-pinhole",lambda centroid=centroid:image(centroid)+.1*(geometry()+regularization())))
    cra = [field for field in cra_fields if field["wave"]==1]
    bend = [{k:v for k,v in field.items() if k!="outputs"} for field in fields if field["wave"]==1]
    data = {"name":name,"boundary":boundary,"kind":kind,"optimizeMaterials":materials,"sensor":sensor,"surfaces":rows,
            "parameters":parameters,"fields":fields,"craFields":cra,"bendFields":bend,"losses":losses,
            "bendCosine":math.cos(math.radians(20)),"craCosine":math.cos(math.radians(40)),
            "seconds":time.perf_counter()-started}
    print(json.dumps({"name":name,"parameters":len(parameters),"valid":sum(o['valid'] for f in fields for o in f['outputs']),
                      "rays":sum(len(f['inputs']) for f in fields),"seconds":data['seconds']}),flush=True)
    return data


cases = []
for index in [0,1]:
    cases.append((f"initial-seed-{42 if index==0 else 101}",from_initial(index),10,True,"ordinary",False))
for strength in [1,2]:
    rows = from_initial(0)
    for i,row in enumerate(rows[1:],1):
        row["curvature"] *= 2 if strength==1 else 4
        row["conic"] = [-.7,.4,-1.5,.8][i%4]
        row["ai"] = [(-1)**(i+j)*(.001 if strength==1 else .008)/(8*max(row['radius'],1)**(2*(j+2))) for j in range(8)]
    cases.append((f"nonzero-asphere-{strength}",rows,10,True,"ordinary",False))
rows = from_initial(1)
rows[1]["nd"], rows[1]["abbe"] = 1.499,70.01
rows[2]["d"] = rows[1]["d"]+.39
cases.append(("active-material-and-clearance",rows,10,True,"ordinary",False))
saved = args.source / "results/1026-104613-AutoLens-OhbV"
for name,path in [("trained-curriculum-catalog",saved/"curriculum_final.json"),("trained-refine-720-catalog",saved/"refine/iter720.json")]:
    rows,sensor = from_saved(path)
    cases.append((name,rows,sensor,False,"ordinary",False))
for kind in ["aperture","critical","critical-below","critical-near-below","critical-above","grazing","all-invalid"]:
    rows = [{"d":0.,"radius":1.0 if kind in {"aperture","all-invalid"} else 2.0,"curvature":0.,"conic":0.,"ai":[0.]*8,"material":"air"},
            {"d":.2,"radius":2.,"curvature":.8 if kind=="grazing" else 0.,"conic":0.,"ai":[0.]*8,"material":"relaxed","nd":1.5,"abbe":50.},
            {"d":2.,"radius":1.99,"curvature":.5 if kind.startswith("critical") else 0.,"conic":0.,"ai":[0.]*8,"material":"air"}]
    cases.append((kind,rows,6,True,kind,True))

started=time.perf_counter()
output=[]
import deeplens.geometric_surface.base as surface_base
original_epsilon = surface_base.EPSILON
if args.epsilon_diagnostic:
    # Controlled attribution run only, never the baseline reference. No source is edited.
    surface_base.EPSILON = 0.0
for case in cases:
    if args.epsilon_diagnostic and case[0] not in {"critical-below","critical-near-below"}:
        continue
    try:
        output.append(run(*case))
    except Exception:
        output.append({"name":case[0],"error":traceback.format_exc()})
        print(output[-1]["error"],flush=True)
surface_base.EPSILON = original_epsilon
result={"scope":"Fixed input direct upstream function checks, CPU float64, no optimizer updates or refinement",
        "controlledDiagnostic": "base module EPSILON=0 temporarily, to isolate its influence; not the unmodified reference result" if args.epsilon_diagnostic else None,
        "createdUtc":datetime.now(timezone.utc).isoformat(),"torch":torch.__version__,"seconds":time.perf_counter()-started,
        "upstreamFileSha256":{str(path.relative_to(args.source)):hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [args.source/"1_autolens.py",args.source/"deeplens/geometric_surface/base.py",args.source/"deeplens/geometric_surface/aspheric.py",args.source/"deeplens/geolens_pkg/optim.py"]},"cases":output}
args.output.write_text(json.dumps(result,indent=2,allow_nan=False)+'\n',encoding='utf-8')
