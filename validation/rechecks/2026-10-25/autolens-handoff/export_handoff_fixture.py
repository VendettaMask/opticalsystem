"""Development only: invoke the unchanged pinned upstream JSON writer/reader.
No optimization, tracing, image-quality comparison or product Python dependency.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--upstream-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
sys.path.insert(0, str(args.upstream_root))

import torch
from deeplens.geolens import GeoLens
from deeplens.geometric_surface import Aperture, Aspheric

torch.set_default_dtype(torch.float64)
cases = []
for case_index, shift in enumerate([0.0, 0.00337]):
    lens = GeoLens(device="cpu", dtype=torch.float64)
    lens.aper_idx = 0
    lens.float_enpd = True
    lens.foclen, lens.fnum, lens.r_sensor = 5.0, 1.8, 5.0
    lens.d_sensor = torch.tensor(6.20018 + shift)
    lens.surfaces = [
        Aperture(r=1.234567, d=shift),
        Aspheric(r=1.456789, d=0.10006 + shift, c=0.123456789,
                 k=-0.234567, ai=[1.234567891e-5, -2.345678912e-7], mat2="h-k9l"),
        Aspheric(r=1.567891, d=0.50012 + shift, c=-0.213456789 if case_index == 0 else 0,
                 k=0.123456, ai=[-3.456789123e-5, 4.567891234e-7], mat2="air"),
    ]

    def describe(value):
        surfaces = []
        for surface in value.surfaces:
            row = {"d": surface.d.item(), "radius": surface.r,
                   "curvature": 0.0, "conic": 0.0, "coefficients": []}
            if isinstance(surface, Aspheric):
                row.update(curvature=surface.c.item(), conic=surface.k.item(),
                           coefficients=[getattr(surface, f"_ai{2 * (i + 2)}").item()
                                         for i in range(surface.ai_degree)])
            surfaces.append(row)
        return {"surfaces": surfaces, "sensor": value.d_sensor.item()}

    before = describe(lens)
    output_path = args.output.parent / f"original-handoff-{case_index}.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    lens.write_lens_json(str(output_path))
    restored = GeoLens(device="cpu", dtype=torch.float64)
    # Direct original IO call avoids post-computation; this fixture concerns only serialization.
    restored.read_lens_json(str(output_path))
    cases.append({"input": before, "reloaded": describe(restored),
                  "originalJsonSha256": hashlib.sha256(output_path.read_bytes()).hexdigest()})

fixture = {
    "upstreamCommit": "e58db32f3a0b07f25861b5839a45970f77b5c947",
    "dtype": "float64 isolated serialization rule; not a final optimized output comparison",
    "sourceFiles": {str(relative): hashlib.sha256((args.upstream_root / relative).read_bytes()).hexdigest()
                    for relative in [Path("deeplens/geolens_pkg/io.py"),
                                     Path("deeplens/geometric_surface/aspheric.py"),
                                     Path("deeplens/geometric_surface/aperture.py")]},
    "cases": cases,
}
args.output.write_text(json.dumps(fixture, indent=2, allow_nan=False) + "\n", encoding="utf-8")
print(f"Wrote {len(cases)} isolated upstream write/read cases: {args.output}")
