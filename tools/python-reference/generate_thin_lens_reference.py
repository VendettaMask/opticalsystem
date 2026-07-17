#!/usr/bin/env python3
import argparse
import json

import numpy as np
import optiland.backend as be
from optiland.materials import IdealMaterial
from optiland.optic import Optic
from optiland.rays import ParaxialRays, RealRays


X = np.asarray([0.0, 0.25, -0.4, 0.7])
Y = np.asarray([0.0, -0.18, 0.32, 0.55])
L = np.asarray([0.04, -0.1, 0.16, 0.02])
M = np.asarray([0.03, 0.07, -0.08, 0.13])
N = np.sqrt(1 - L**2 - M**2)
WAVELENGTH = np.asarray([0.55, 0.6, 0.48, 0.65])
INTENSITY = np.asarray([0.9, 0.8, 0.7, 0.6])
PROPAGATION_DISTANCE = 12.0


def json_value(value):
    if isinstance(value, dict):
        return {key: json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_value(item) for item in value]
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, np.generic):
        return json_value(value.item())
    if isinstance(value, float) and not np.isfinite(value):
        if np.isnan(value):
            return "NaN"
        return "Infinity" if value > 0 else "-Infinity"
    return value


def make_model(focal_length, index_after, reflective):
    optic = Optic()
    optic.add_surface(index=0, radius=be.inf, thickness=be.inf)
    optic.add_surface(
        index=1,
        radius=be.inf,
        material=IdealMaterial(index_after),
        interaction_type="thin_lens",
        f=focal_length,
    )
    model = optic.surface_group.surfaces[1].interaction_model
    model.is_reflective = reflective
    return model


def real_samples(model):
    rays = RealRays(X, Y, np.zeros_like(X), L, M, N, INTENSITY, WAVELENGTH)
    index_before = np.asarray(model.material_pre.n(WAVELENGTH))
    index_after = np.asarray(model.material_post.n(WAVELENGTH))
    model.interact_real_rays(rays)
    thin_direction = np.column_stack((rays.L.copy(), rays.M.copy(), rays.N.copy()))
    thin_opd = rays.opd.copy()
    thin_is_normalized = rays.is_normalized

    model.material_post.propagation_model.propagate(rays, PROPAGATION_DISTANCE)
    rays.opd = rays.opd + np.abs(PROPAGATION_DISTANCE * index_after)

    output = []
    for index in range(len(X)):
        output.append(
            {
                "x": float(X[index]),
                "y": float(Y[index]),
                "direction_x": float(L[index]),
                "direction_y": float(M[index]),
                "direction_z": float(N[index]),
                "wavelength_micrometers": float(WAVELENGTH[index]),
                "input_intensity": float(INTENSITY[index]),
                "refractive_index_before": float(np.atleast_1d(index_before)[index]),
                "refractive_index_after": float(np.atleast_1d(index_after)[index]),
                "thin_direction_x": float(thin_direction[index, 0]),
                "thin_direction_y": float(thin_direction[index, 1]),
                "thin_direction_z": float(thin_direction[index, 2]),
                "thin_opd": float(thin_opd[index]),
                "thin_is_normalized": bool(thin_is_normalized),
                "propagated_x": float(rays.x[index]),
                "propagated_y": float(rays.y[index]),
                "propagated_z": float(rays.z[index]),
                "propagated_direction_x": float(rays.L[index]),
                "propagated_direction_y": float(rays.M[index]),
                "propagated_direction_z": float(rays.N[index]),
                "propagated_opd": float(rays.opd[index]),
            }
        )
    return output


def paraxial_samples(model):
    height = np.asarray([0.0, 0.25, -0.4, 0.7])
    slope = np.asarray([0.02, -0.04, 0.08, -0.12])
    rays = ParaxialRays(height, slope, np.zeros_like(height), WAVELENGTH)
    index_before = np.asarray(model.material_pre.n(WAVELENGTH))
    index_after = np.asarray(model.material_post.n(WAVELENGTH))
    model.interact_paraxial_rays(rays)
    return [
        {
            "height": float(height[index]),
            "slope": float(slope[index]),
            "wavelength_micrometers": float(WAVELENGTH[index]),
            "refractive_index_before": float(np.atleast_1d(index_before)[index]),
            "refractive_index_after": float(np.atleast_1d(index_after)[index]),
            "output_slope": float(rays.u[index]),
        }
        for index in range(len(height))
    ]


def case(name, focal_length, index_after, reflective):
    model = make_model(focal_length, index_after, reflective)
    return {
        "name": name,
        "focal_length_millimeters": focal_length,
        "material_index_after": index_after,
        "is_reflective": reflective,
        "dictionary": json_value(model.to_dict()),
        "propagation_distance_millimeters": PROPAGATION_DISTANCE,
        "real_samples": real_samples(model),
        "paraxial_samples": paraxial_samples(model),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    cases = [
        case("transmitted_positive", 40.0, 1.5, False),
        case("transmitted_negative", -35.0, 1.62, False),
        case("reflected_positive", 50.0, 1.7, True),
        case("reflected_negative", -45.0, 1.48, True),
    ]
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(
            json_value({"optiland_version": "0.5.8", "cases": cases}),
            stream,
            indent=2,
        )
        stream.write("\n")


if __name__ == "__main__":
    main()
