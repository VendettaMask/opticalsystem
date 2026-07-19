#!/usr/bin/env python3
"""Generate a compact Zemax import reference with Optiland 0.5.8."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from optiland.fileio import load_zemax_file


def scalar(value):
    parsed = float(np.asarray(value).reshape(-1)[0])
    if np.isposinf(parsed):
        return "Infinity"
    if np.isneginf(parsed):
        return "-Infinity"
    return parsed


def material_name(material):
    for attribute in ("name", "material_name"):
        value = getattr(material, attribute, None)
        if value:
            return str(value)
    return material.__class__.__name__


def geometry_data(geometry):
    sag_x = 1.25
    sag_y = 0.75
    data = {
        "type": geometry.__class__.__name__,
        "position": [scalar(value) for value in geometry.cs.position_in_gcs],
        "sag_sample": {
            "x": sag_x,
            "y": sag_y,
            "z": scalar(geometry.sag(sag_x, sag_y)),
        },
    }
    for attribute in ("radius", "radius_x", "radius_y", "k"):
        if hasattr(geometry, attribute):
            data[attribute] = scalar(getattr(geometry, attribute))
    if hasattr(geometry, "coefficients"):
        data["coefficients"] = [scalar(value) for value in geometry.coefficients]
    return data


def generate(source: Path):
    optic = load_zemax_file(str(source))
    return {
        "optiland_version": "0.5.8",
        "aperture": optic.aperture.to_dict(),
        "field_type": optic.field_definition.__class__.__name__,
        "fields": [
            {
                "x": scalar(field.x),
                "y": scalar(field.y),
                "vx": scalar(field.vx),
                "vy": scalar(field.vy),
            }
            for field in optic.fields.fields
        ],
        "wavelengths": [
            {
                "value_um": scalar(wavelength.value),
                "weight": scalar(wavelength.weight),
                "is_primary": bool(wavelength.is_primary),
            }
            for wavelength in optic.wavelengths.wavelengths
        ],
        "surfaces": [
            {
                "is_stop": bool(surface.is_stop),
                "material": material_name(surface.material_post),
                "geometry": geometry_data(surface.geometry),
            }
            for surface in optic.surface_group.surfaces
        ],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.write_text(json.dumps(generate(args.source), indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
