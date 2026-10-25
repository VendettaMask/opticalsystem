"""Development-only: execute pinned upstream functions on small deterministic inputs.
No optimized prescriptions or final image-quality comparisons are produced.
"""
import hashlib
import json
import math
import pathlib
import sys
import random
import string
from unittest.mock import patch
import numpy as np

ROOT = pathlib.Path(__file__).parent
sys.path.insert(0, str(ROOT / "source"))
import torch
from deeplens.geolens_pkg.optim import GeoLensOptim, get_cosine_schedule_with_warmup
from deeplens.geometric_surface import Aperture, Aspheric
from deeplens.ray import Ray
from deeplens.material import Material

torch.set_default_dtype(torch.float64)

class LossLens:
    device = 'cpu'
    loss_distortion_from_ray = GeoLensOptim.loss_distortion_from_ray
    distortion_max = .03
    def trace2sensor(self, ray):
        dx = (ray._fixture_gx * self.p).sum(-1)
        dy = (ray._fixture_gy * self.p).sum(-1)
        ray.o = ray.o + torch.stack((dx,dy,torch.zeros_like(dx)),dim=-1)
        return ray

inputs = []
for ring in range(3):
    for arm in range(2):
        ideal = [ring * (.8 if arm == 0 else -.7), ring * .3]
        for wave in range(3):
            rays = []
            for sample in range(4):
                rays.append(dict(x=ideal[0] + .07*(sample-1.4) + .03*wave,
                                 y=ideal[1] + .05*(sample-1) - .02*wave,
                                 gx=[.2+.03*sample, -.1+.02*wave],
                                 gy=[-.3+.02*ring, .4+.05*sample],
                                 valid=not (sample == 3 and ring == 1) and not (ring == 2 and arm == 1 and wave == 1)))
            inputs.append(dict(ring=ring, arm=arm, wave=wave, ideal=ideal, rays=rays))

image_cases = []
for centroid in (False, True):
    p = torch.zeros(2, requires_grad=True)
    rays = []
    for wave in range(3):
        o = []
        mask = []
        for field in [x for x in inputs if x['wave'] == wave]:
            o.append([torch.tensor([r['x'],r['y'],0.]) for r in field['rays']])
            mask.append([float(r['valid']) for r in field['rays']])
        o = torch.stack([torch.stack(field) for field in o]).reshape(3, 2, 4, 3)
        d = torch.zeros_like(o); d[..., 2] = 1
        ray = Ray(o, d, [0.656, .587, .486][wave], device='cpu')
        ray.is_valid = torch.tensor(mask).reshape(3, 2, 4)
        selected = [field for field in inputs if field['wave'] == wave]
        ray._fixture_gx = torch.tensor([[r['gx'] for r in field['rays']] for field in selected]).reshape(3,2,4,2)
        ray._fixture_gy = torch.tensor([[r['gy'] for r in field['rays']] for field in selected]).reshape(3,2,4,2)
        rays.append(ray)
    pinhole = torch.tensor([f['ideal'] for f in inputs if f['wave'] == 1]).reshape(3, 2, 2)
    fixture_lens=LossLens(); fixture_lens.p=p
    rms, distortion = GeoLensOptim.loss_rms_from_ray(fixture_lens, rays, pinhole, centroid=centroid)
    total = rms + .1*distortion
    total.backward()
    image_cases.append(dict(centroid=centroid, rms=rms.item(), distortion=distortion.item(), total=total.item(), gradient=p.grad.tolist()))

class GeometryLens:
    loss_profile = GeoLensOptim.loss_profile
    loss_bound = GeoLensOptim.loss_bound
    loss_mat = GeoLensOptim.loss_mat
    def find_diff_surf(self):
        return [1, 2]

geometry_cases = []
for curvature in (.32, .7):
    lens = GeometryLens()
    lens.surfaces = [Aperture(r=1.4, d=0), Aspheric(r=2.2, d=.08, c=curvature, k=-.2, ai=[1e-5]+[0.]*7, mat2='1.95/25'),
                     Aspheric(r=2., d=.38, c=-.29, k=.1, ai=[-2e-5]+[0.]*7, mat2='air')]
    lens.d_sensor = torch.tensor(6.08, requires_grad=True)
    lens.device = 'cpu'
    for s in lens.surfaces:
        s.to('cpu')
    lens.surfaces[1].get_optimizer_params(optim_mat=True)
    lens.surfaces[2].get_optimizer_params(optim_mat=False)
    lens.air_edge_min = lens.air_center_min = .05
    lens.air_edge_max = lens.air_center_max = 5.
    lens.thick_edge_min = lens.thick_center_min = .4
    lens.thick_edge_max = lens.thick_center_max = 2.
    lens.bfl_min=1.; lens.bfl_max=5.; lens.ttl_min=5.; lens.ttl_max=15.
    lens.sag2diam_max=.2; lens.surf_angle_max=30.; lens.diam2thick_max=15.; lens.tmax2tmin_max=5.
    profile = lens.loss_profile()
    clearance, envelope = lens.loss_bound()
    material = lens.loss_mat()
    total = profile + clearance + envelope + material
    total.backward()
    a,b = lens.surfaces[1:]
    parameters=[a.d,a.c,a.k,a._ai4,a.mat2.n,a.mat2.V,b.d,b.c,b.k,b._ai4,lens.d_sensor]
    geometry_cases.append(dict(curvature=curvature, profile=profile.item(), clearance=clearance.item(), envelope=envelope.item(),
                               material=material.item(), total=total.item(), gradient=[0. if p.grad is None else p.grad.item() for p in parameters]))

adam_cases = []
gradients = [[2.,-1.],[-3.,.5],[float('nan'),float('inf')],[.1,-.2],[0.,-4.]]
for curriculum in (True, False):
    p = torch.tensor([.3,2.], requires_grad=True)
    optimizer = torch.optim.Adam([{'params':[p], 'lr':1e-3}])
    scheduler = torch.optim.lr_scheduler.CosineAnnealingWarmRestarts(optimizer, T_0=8//4) if curriculum else get_cosine_schedule_with_warmup(optimizer, 100, 3000)
    records = []
    for g in gradients:
        p.grad = torch.nan_to_num(torch.tensor(g),nan=0.,posinf=0.,neginf=0.)
        rate=optimizer.param_groups[0]['lr']
        optimizer.step(); scheduler.step()
        state=optimizer.state[p]
        records.append(dict(rate=rate, coordinates=p.tolist(), first=state['exp_avg'].tolist(), second=state['exp_avg_sq'].tolist()))
    adam_cases.append(dict(curriculum=curriculum, records=records))

materials=[]
for name in ['n-bk7','h-k9l','pmma','h-zf88']:
    m=Material(name,device='cpu')
    materials.append(dict(name=name, nd=float(m.n), abbe=float(m.V), indices=[float(m.refractive_index(w)) for w in [.486,.587,.656]]))

from deeplens.geolens import GeoLens
from deeplens.geolens_pkg.optim_init import create_lens
initial_cases=[]
torch.set_default_dtype(torch.float32)
for seed in [42,101]:
    random.seed(seed); np.random.seed(seed); torch.manual_seed(seed)
    tag=''.join(random.choice(string.ascii_letters+string.digits) for _ in range(4))
    # Omit reporting and derived-property tracing only; invoke the original constructor.
    with patch.object(GeoLens,'analysis',lambda *a,**k:None), patch.object(GeoLens,'write_lens_json',lambda *a,**k:None), \
         patch.object(GeoLens,'post_computation',lambda *a,**k:None), patch.object(GeoLens,'calc_pupil',lambda *a,**k:None):
        lens=create_lens(foclen=5.,fov=90.,fnum=1.8,bfl=2.,thickness=10.,surf_list=[['Aperture']]+[['Aspheric','Aspheric']]*6,
                         save_dir=str(ROOT/'evidence'/'fixture-initialization'))
        lens.find_aperture()
        lens.set_target_fov_fnum(45./57.3,1.8)
        surfaces=[]
        for surface in lens.surfaces:
            name=surface.mat2.name
            if isinstance(surface,Aspheric): surface.get_optimizer_params(optim_mat=True)
            surfaces.append(dict(d=float(surface.d), curvature=float(surface.c) if hasattr(surface,'c') else 0.,
                                 radius=float(surface.r), material=name, nd=float(surface.mat2.n), abbe=float(surface.mat2.V)))
        initial_cases.append(dict(seed=seed,tag=tag,focal=lens.foclen,surfaces=surfaces))

files=['deeplens/geolens_pkg/optim.py','deeplens/geometric_surface/aspheric.py','deeplens/geometric_surface/base.py','deeplens/material/materials.py']
data=dict(commit='e58db32f3a0b07f25861b5839a45970f77b5c947', dtype='float64', scope='isolated optimization rules, not final optical output comparison',
          sourceHashes={f:hashlib.sha256((ROOT/'source'/f).read_bytes()).hexdigest() for f in files},
          imageInputs=inputs,imageCases=image_cases,geometryCases=geometry_cases,adamCases=adam_cases,materials=materials,initialCases=initial_cases)
destination=pathlib.Path(sys.argv[1])
destination.parent.mkdir(parents=True,exist_ok=True)
destination.write_text(json.dumps(data,indent=2,allow_nan=False),encoding='utf-8')
print(json.dumps(dict(path=str(destination),sha256=hashlib.sha256(destination.read_bytes()).hexdigest(), imageCases=len(image_cases),geometryCases=len(geometry_cases),adamCases=len(adam_cases))))
