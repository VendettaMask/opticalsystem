import argparse
import json

import numpy as np
import optiland
from optiland.physical_apertures import (
    DifferenceAperture,
    EllipticalAperture,
    FileAperture,
    IntersectionAperture,
    OffsetRadialAperture,
    PolygonAperture,
    RadialAperture,
    RectangularAperture,
    UnionAperture,
)


SAMPLE_POINTS = [
    (-4.0, 0.0),
    (-2.0, -1.0),
    (0.0, 0.0),
    (0.75, -0.5),
    (1.25, -0.75),
    (2.5, 0.0),
    (4.0, 2.0),
]


def aperture_case(name, aperture):
    x = np.asarray([point[0] for point in SAMPLE_POINTS], dtype=float)
    y = np.asarray([point[1] for point in SAMPLE_POINTS], dtype=float)
    inside = np.asarray(aperture.contains(x, y), dtype=bool)
    return {
        "name": name,
        "dictionary": json_value(aperture.to_dict()),
        "samples": [
            {"x": point[0], "y": point[1], "inside": bool(result)}
            for point, result in zip(SAMPLE_POINTS, inside, strict=True)
        ],
    }


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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    apertures = [
        aperture_case("annular_radial", RadialAperture(r_max=3.0, r_min=1.0)),
        aperture_case(
            "offset_radial",
            OffsetRadialAperture(
                r_max=2.5,
                r_min=0.5,
                offset_x=1.25,
                offset_y=-0.75,
            ),
        ),
        aperture_case(
            "asymmetric_rectangular",
            RectangularAperture(x_min=-2.0, x_max=4.0, y_min=-3.0, y_max=1.0),
        ),
        aperture_case(
            "offset_elliptical",
            EllipticalAperture(a=4.0, b=2.0, offset_x=0.5, offset_y=-0.25),
        ),
        aperture_case(
            "polygon",
            PolygonAperture(x=[-3.0, 2.0, 3.0, 0.0], y=[-1.0, -2.0, 1.0, 3.0]),
        ),
        aperture_case(
            "file_polygon",
            FileAperture(
                filepath="tools/python-reference/aperture_vertices.txt",
                delimiter=" ",
                skip_header=0,
            ),
        ),
        aperture_case(
            "union",
            UnionAperture(
                RadialAperture(r_max=2.0),
                OffsetRadialAperture(r_max=1.5, offset_x=2.0),
            ),
        ),
        aperture_case(
            "intersection",
            IntersectionAperture(
                RectangularAperture(x_min=-2.5, x_max=2.5, y_min=-1.0, y_max=1.0),
                EllipticalAperture(a=3.0, b=2.0),
            ),
        ),
        aperture_case(
            "difference",
            DifferenceAperture(
                RadialAperture(r_max=3.0),
                RectangularAperture(x_min=-0.5, x_max=0.5, y_min=-4.0, y_max=4.0),
            ),
        ),
    ]
    output = {
        "optiland_version": optiland.__version__,
        "apertures": apertures,
    }

    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(output, stream, indent=2)
        stream.write("\n")


if __name__ == "__main__":
    main()
