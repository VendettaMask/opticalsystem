#!/usr/bin/env python3
import argparse
import json

import numpy as np
import optiland.backend as be
from optiland.materials import IdealMaterial
from optiland.optic import Optic
from optiland.rays import ParaxialRays, RealRays


RAY_X = np.asarray([0.0, 0.2, -0.45, 0.73])
RAY_Y = np.asarray([0.0, -0.1, 0.35, 0.58])
RAY_L = np.asarray([0.05, -0.12, 0.2, 0.0])
RAY_M = np.asarray([0.02, 0.08, -0.1, 0.15])
RAY_N = np.sqrt(1 - RAY_L**2 - RAY_M**2)
RAY_WAVELENGTH = np.asarray([0.55, 0.6, 0.48, 0.65])


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


def make_model(radius, conic, order, period, angle, index_after, reflective):
    optic = Optic()
    optic.add_surface(index=0, radius=be.inf, thickness=be.inf)
    optic.add_surface(
        index=1,
        surface_type="grating",
        radius=radius,
        conic=conic,
        material=IdealMaterial(index_after),
        grating_order=order,
        grating_period=period,
        groove_orientation_angle=angle,
    )
    model = optic.surface_group.surfaces[1].interaction_model
    model.is_reflective = reflective
    return model


def real_samples(model):
    geometry = model.geometry
    z = np.asarray(geometry.sag(RAY_X, RAY_Y))
    rays = RealRays(
        RAY_X,
        RAY_Y,
        z,
        RAY_L,
        RAY_M,
        RAY_N,
        np.asarray([0.9, 0.8, 0.7, 0.6]),
        RAY_WAVELENGTH,
    )
    normals = geometry.surface_normal(rays)
    grating_vectors = geometry.grating_vector(rays)
    with np.errstate(invalid="ignore"):
        model.interact_real_rays(rays)
    output = []
    for index in range(len(RAY_X)):
        output.append(
            {
                "x": float(RAY_X[index]),
                "y": float(RAY_Y[index]),
                "z": float(z[index]) if z.ndim else float(z),
                "direction_x": float(RAY_L[index]),
                "direction_y": float(RAY_M[index]),
                "direction_z": float(RAY_N[index]),
                "wavelength_micrometers": float(RAY_WAVELENGTH[index]),
                "normal_x": float(np.asarray(normals[0])[index]),
                "normal_y": float(np.asarray(normals[1])[index]),
                "normal_z": float(np.asarray(normals[2])[index]),
                "grating_vector_x": float(np.asarray(grating_vectors[0])[index]),
                "grating_vector_y": float(np.asarray(grating_vectors[1])[index]),
                "grating_vector_z": float(np.asarray(grating_vectors[2])[index]),
                "output_direction_x": float(rays.L[index]),
                "output_direction_y": float(rays.M[index]),
                "output_direction_z": float(rays.N[index]),
            }
        )
    return output


def paraxial_samples(model):
    height = np.asarray([0.0, 0.25, -0.4, 0.7])
    slope = np.asarray([0.02, -0.04, 0.08, -0.12])
    wavelength = np.asarray([0.55, 0.6, 0.48, 0.65])
    rays = ParaxialRays(height, slope, np.zeros_like(height), wavelength)
    model.interact_paraxial_rays(rays)
    return [
        {
            "height": float(height[index]),
            "slope": float(slope[index]),
            "wavelength_micrometers": float(wavelength[index]),
            "output_slope": float(rays.u[index]),
        }
        for index in range(len(height))
    ]


def case(name, radius, conic, order, period, angle, index_after, reflective):
    model = make_model(radius, conic, order, period, angle, index_after, reflective)
    geometry = model.geometry
    return {
        "name": name,
        "radius": json_value(float(radius)),
        "conic": conic,
        "order": order,
        "period_micrometers": period,
        "groove_orientation_angle_radians": angle,
        "refractive_index_before": 1.0,
        "refractive_index_after": index_after,
        "is_reflective": reflective,
        "geometry_dictionary": json_value(geometry.to_dict()),
        "interaction_dictionary": json_value(model.to_dict()),
        "real_samples": real_samples(model),
        "paraxial_samples": paraxial_samples(model),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    cases = [
        case("plane_transmitted", be.inf, 0.0, 1, 1.2, 0.3, 1.5, False),
        case("plane_reflected", be.inf, 0.0, -1, 0.82, -0.45, 1.62, True),
        case("standard_transmitted", 45.0, -0.2, 2, 1.4, -0.35, 1.7, False),
        case("standard_reflected", -60.0, 0.35, 1, 0.95, 0.6, 1.48, True),
        case("plane_evanescent", be.inf, 0.0, 10, 0.2, 0.15, 1.3, False),
        case("standard_default_period", 35.0, 0.0, 0, be.inf, 0.0, 1.5, False),
    ]
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(
            json_value({"optiland_version": "0.5.8", "cases": cases}),
            stream,
            indent=2,
            allow_nan=True,
        )
        stream.write("\n")


if __name__ == "__main__":
    main()
