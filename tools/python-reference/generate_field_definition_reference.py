#!/usr/bin/env python3
import argparse
import json

import numpy as np
import optiland.backend as be
from optiland.materials import IdealMaterial
from optiland.optic import Optic
from optiland.samples import CookeTriplet


HX = 0.6
HY = 0.8
PX = 0.25
PY = -0.4
WAVELENGTH = 0.55


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


def finite_system():
    optic = Optic()
    optic.add_surface(index=0, radius=be.inf, thickness=100.0)
    optic.add_surface(
        index=1,
        radius=40.0,
        thickness=5.0,
        material=IdealMaterial(1.5),
        is_stop=True,
    )
    optic.add_surface(
        index=2,
        radius=-40.0,
        thickness=30.0,
        material=IdealMaterial(1.0),
    )
    optic.add_surface(index=3, radius=be.inf)
    optic.set_aperture("EPD", 8.0)
    optic.add_field(y=0.0)
    optic.add_field(x=3.0, y=4.0, vx=0.2, vy=0.35)
    optic.add_wavelength(WAVELENGTH, is_primary=True)
    return optic


def configure(optic, field_type, telecentric=False):
    optic.set_field_type(field_type)
    optic.obj_space_telecentric = telecentric
    if telecentric:
        optic.set_aperture("objectNA", 0.2)


def coordinate_offset(optic):
    object_z = float(np.ravel(np.asarray(optic.surface_group.positions))[0])
    return 0.0 if not np.isfinite(object_z) else -object_z


def ray_data(rays, offset):
    return {
        "x": float(np.ravel(np.asarray(rays.x))[0]),
        "y": float(np.ravel(np.asarray(rays.y))[0]),
        "z": float(np.ravel(np.asarray(rays.z))[0]) + offset,
        "l": float(np.ravel(np.asarray(rays.L))[0]),
        "m": float(np.ravel(np.asarray(rays.M))[0]),
        "n": float(np.ravel(np.asarray(rays.N))[0]),
        "intensity": float(np.ravel(np.asarray(rays.i))[0]),
    }


def initial_ray(optic, generic):
    pupil_x = PX
    pupil_y = PY
    if generic:
        vx, vy = optic.fields.get_vig_factor(HX, HY)
        pupil_x *= 1 - float(np.asarray(vx))
        pupil_y *= 1 - float(np.asarray(vy))
    rays = optic.ray_tracer.ray_generator.generate_rays(
        HX,
        HY,
        np.asarray([pupil_x]),
        np.asarray([pupil_y]),
        WAVELENGTH,
    )
    return ray_data(rays, coordinate_offset(optic))


def final_generic_ray(optic):
    rays = optic.trace_generic(HX, HY, PX, PY, WAVELENGTH)
    return ray_data(rays, coordinate_offset(optic))


def unit_chief_ray(optic):
    stop_index = optic.surface_group.stop_index
    positions = np.ravel(np.asarray(optic.surface_group.positions))
    image_height, _ = optic.paraxial._trace_generic(
        0,
        1,
        positions[stop_index],
        optic.primary_wavelength,
        skip=stop_index,
    )
    object_height, object_slope = optic.paraxial._trace_generic(
        0,
        1,
        positions[-1] - positions[stop_index],
        optic.primary_wavelength,
        reverse=True,
        skip=optic.surface_group.num_surfaces - stop_index,
    )
    return {
        "image_height": float(np.ravel(np.asarray(image_height))[-1]),
        "object_height": float(np.ravel(np.asarray(object_height))[-1]),
        "object_slope": float(np.ravel(np.asarray(object_slope))[-1]),
    }


def case(name, optic, field_type, telecentric=False):
    configure(optic, field_type, telecentric)
    result = {
        "name": name,
        "field_type": field_type,
        "telecentric": telecentric,
        "aperture_type": optic.aperture.ap_type,
        "aperture_value": float(optic.aperture.value),
        "initial_distribution_ray": initial_ray(optic, generic=False),
        "initial_generic_ray": initial_ray(optic, generic=True),
        "final_generic_ray": final_generic_ray(optic),
    }
    if field_type == "paraxial_image_height":
        result["unit_chief_ray"] = unit_chief_ray(optic)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    finite = finite_system()
    finite_dictionary = json_value(finite.to_dict())
    cases = [
        case("finite_angle", finite_system(), "angle"),
        case("finite_object_height", finite_system(), "object_height"),
        case("finite_paraxial_image_height", finite_system(), "paraxial_image_height"),
        case("finite_object_height_telecentric", finite_system(), "object_height", True),
        case("infinite_angle", CookeTriplet(), "angle"),
        case("infinite_paraxial_image_height", CookeTriplet(), "paraxial_image_height"),
    ]
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(
            json_value(
                {
                    "optiland_version": "0.5.8",
                    "normalized_field": {"x": HX, "y": HY},
                    "normalized_pupil": {"x": PX, "y": PY},
                    "wavelength_micrometers": WAVELENGTH,
                    "finite_system": finite_dictionary,
                    "cases": cases,
                }
            ),
            stream,
            indent=2,
        )
        stream.write("\n")


if __name__ == "__main__":
    main()
