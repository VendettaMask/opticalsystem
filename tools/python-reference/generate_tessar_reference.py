import argparse
import json

import numpy as np
from optiland.samples.objectives import TessarLens


def values(array):
    return [
        float(value) if np.isfinite(value) else None
        for value in np.asarray(array).reshape(-1)
    ]


def scalar(value):
    return float(np.asarray(value).reshape(-1)[0])


def trace_case(optic, case):
    optic.trace_generic(
        Hx=case["field_x"],
        Hy=case["field_y"],
        Px=case["pupil_x"],
        Py=case["pupil_y"],
        wavelength=case["wavelength_micrometers"],
    )
    surfaces = []
    for index in range(len(optic.surface_group.surfaces)):
        surfaces.append(
            {
                "surface": index,
                "x": values(optic.surface_group.x[index])[0],
                "y": values(optic.surface_group.y[index])[0],
                "z": values(optic.surface_group.z[index])[0],
                "l": values(optic.surface_group.L[index])[0],
                "m": values(optic.surface_group.M[index])[0],
                "n": values(optic.surface_group.N[index])[0],
                "opd": values(optic.surface_group.opd[index])[0],
                "intensity": values(optic.surface_group.intensity[index])[0],
            }
        )
    return {**case, "surfaces": surfaces}


def bundle_case(optic, field_y, wavelength, ray_count=9):
    rays = optic.trace(
        Hx=0,
        Hy=field_y,
        wavelength=wavelength,
        num_rays=ray_count,
        distribution="line_y",
    )
    x = np.asarray(rays.x, dtype=float)
    y = np.asarray(rays.y, dtype=float)
    intensity = np.asarray(rays.i, dtype=float)
    valid = intensity > 0
    weight = intensity[valid]
    centroid_x = float(np.sum(x[valid] * weight) / np.sum(weight))
    centroid_y = float(np.sum(y[valid] * weight) / np.sum(weight))
    radius2 = (x[valid] - centroid_x) ** 2 + (y[valid] - centroid_y) ** 2
    rms = float(np.sqrt(np.sum(radius2 * weight) / np.sum(weight)))
    return {
        "field_y": field_y,
        "wavelength_micrometers": wavelength,
        "ray_count": ray_count,
        "x": values(rays.x),
        "y": values(rays.y),
        "intensity": values(rays.i),
        "centroid_x": centroid_x,
        "centroid_y": centroid_y,
        "rms_spot_radius": rms,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    optic = TessarLens()
    cases = [
        {"name": "axis-chief-d", "field_x": 0, "field_y": 0, "pupil_x": 0, "pupil_y": 0, "wavelength_micrometers": 0.5875618},
        {"name": "axis-upper-d", "field_x": 0, "field_y": 0, "pupil_x": 0, "pupil_y": 1, "wavelength_micrometers": 0.5875618},
        {"name": "mid-chief-d", "field_x": 0, "field_y": 0.5, "pupil_x": 0, "pupil_y": 0, "wavelength_micrometers": 0.5875618},
        {"name": "full-chief-d", "field_x": 0, "field_y": 1, "pupil_x": 0, "pupil_y": 0, "wavelength_micrometers": 0.5875618},
        {"name": "full-upper-d", "field_x": 0, "field_y": 1, "pupil_x": 0, "pupil_y": 1, "wavelength_micrometers": 0.5875618},
        {"name": "mid-oblique-f", "field_x": 0, "field_y": 0.5, "pupil_x": 0.5, "pupil_y": 0.5, "wavelength_micrometers": 0.4861327},
        {"name": "mid-oblique-c", "field_x": 0, "field_y": 0.5, "pupil_x": 0.5, "pupil_y": 0.5, "wavelength_micrometers": 0.6562725},
    ]
    output = {
        "source": "Optiland 0.5.8 optiland.samples.objectives.TessarLens",
        "prescription": {
            "effective_focal_length": scalar(optic.paraxial.f2()),
            "f_number": scalar(optic.paraxial.FNO()),
            "entrance_pupil_diameter": scalar(optic.paraxial.EPD()),
            "entrance_pupil_location": scalar(optic.paraxial.EPL()),
            "surface_positions": values(optic.surface_group.positions),
        },
        "traces": [trace_case(optic, case) for case in cases],
        "line_y_bundles": [
            bundle_case(optic, 0, 0.5875618),
            bundle_case(optic, 0.5, 0.5875618),
            bundle_case(optic, 1, 0.5875618),
        ],
    }
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(output, stream, indent=2, allow_nan=False)


if __name__ == "__main__":
    main()
