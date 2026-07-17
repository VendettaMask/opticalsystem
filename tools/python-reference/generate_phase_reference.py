#!/usr/bin/env python3
import argparse
import json

import numpy as np
import optiland
import optiland.backend as be
from optiland.materials import IdealMaterial
from optiland.optic import Optic
from optiland.phase import (
    ConstantPhaseProfile,
    GridPhaseProfile,
    LinearGratingPhaseProfile,
    RadialPhaseProfile,
)
from optiland.rays import RealRays


PROFILE_POINTS = [
    (0.0, 0.0),
    (0.2, -0.1),
    (-0.45, 0.35),
    (0.73, 0.58),
]


def json_value(value):
    if isinstance(value, dict):
        return {key: json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_value(item) for item in value]
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, np.generic):
        return value.item()
    return value


def profile_case(name, profile):
    samples = []
    for x, y in PROFILE_POINTS:
        phase = np.asarray(profile.get_phase(np.asarray([x]), np.asarray([y])))
        dx, dy, _ = profile.get_gradient(np.asarray([x]), np.asarray([y]))
        paraxial = profile.get_paraxial_gradient(np.asarray([y]))
        samples.append(
            {
                "x": x,
                "y": y,
                "phase": float(phase[0]),
                "gradient_x": float(np.asarray(dx)[0]),
                "gradient_y": float(np.asarray(dy)[0]),
                "paraxial_gradient": float(np.asarray(paraxial)[0]),
            }
        )
    return {
        "name": name,
        "dictionary": json_value(profile.to_dict()),
        "samples": samples,
    }


def interaction_case(name, profile, is_reflective):
    optic = Optic()
    optic.add_surface(index=0, radius=be.inf, thickness=be.inf)
    optic.add_surface(
        index=1,
        radius=be.inf,
        material=IdealMaterial(1.5),
        phase_profile=profile,
    )
    model = optic.surface_group.surfaces[1].interaction_model
    model.is_reflective = is_reflective

    x = np.asarray([0.0, 0.2, -0.45, 0.73])
    y = np.asarray([0.0, -0.1, 0.35, 0.58])
    direction_x = np.asarray([0.05, -0.12, 0.2, 0.0])
    direction_y = np.asarray([0.02, 0.08, -0.1, 0.15])
    direction_z = np.sqrt(1 - direction_x**2 - direction_y**2)
    intensity = np.asarray([0.9, 0.8, 0.7, 0.6])
    wavelength = np.asarray([0.55, 0.6, 0.48, 0.65])
    rays = RealRays(
        x,
        y,
        np.zeros_like(x),
        direction_x,
        direction_y,
        direction_z,
        intensity,
        wavelength,
    )
    model.interact_real_rays(rays)

    samples = []
    for index in range(len(x)):
        samples.append(
            {
                "x": float(x[index]),
                "y": float(y[index]),
                "direction_x": float(direction_x[index]),
                "direction_y": float(direction_y[index]),
                "direction_z": float(direction_z[index]),
                "wavelength_micrometers": float(wavelength[index]),
                "input_intensity": float(intensity[index]),
                "output_direction_x": float(rays.L[index]),
                "output_direction_y": float(rays.M[index]),
                "output_direction_z": float(rays.N[index]),
                "output_intensity": float(rays.i[index]),
                "opd": float(rays.opd[index]),
            }
        )
    return {
        "name": name,
        "dictionary": json_value(model.to_dict()),
        "samples": samples,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    x_coords = np.asarray([-1.0, -0.3, 0.4, 1.2, 2.0])
    y_coords = np.asarray([-1.5, -0.4, 0.2, 0.9, 1.7])
    phase_grid = np.asarray(
        [
            [0.42, -0.18, 0.31, 0.75, 1.12],
            [0.08, 0.22, -0.14, 0.48, 0.91],
            [-0.35, 0.12, 0.44, -0.21, 0.53],
            [0.16, -0.27, 0.38, 0.82, -0.09],
            [0.67, 0.05, -0.33, 0.29, 1.04],
        ]
    )
    profiles = {
        "constant": ConstantPhaseProfile(phase=0.35),
        "linear_grating": LinearGratingPhaseProfile(
            period=0.8,
            angle=0.3,
            order=1,
            efficiency=0.75,
        ),
        "radial": RadialPhaseProfile(coefficients=[0.4, -0.08, 0.015]),
        "grid": GridPhaseProfile(x_coords, y_coords, phase_grid),
    }
    output = {
        "optiland_version": optiland.__version__,
        "profiles": [profile_case(name, profile) for name, profile in profiles.items()],
        "interactions": [
            interaction_case("constant_transmissive", profiles["constant"], False),
            interaction_case("linear_transmissive", profiles["linear_grating"], False),
            interaction_case("radial_reflective", profiles["radial"], True),
            interaction_case("grid_transmissive", profiles["grid"], False),
        ],
    }
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(output, stream, indent=2, allow_nan=False)
        stream.write("\n")


if __name__ == "__main__":
    main()
