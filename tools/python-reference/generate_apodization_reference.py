#!/usr/bin/env python3
import argparse
import json

import numpy as np
import optiland
from optiland.apodization import (
    CosineSquaredApodization,
    GaussianApodization,
    HannApodization,
    PolynomialApodization,
    SuperGaussianApodization,
    TukeyApodization,
    UniformApodization,
)


SAMPLE_POINTS = [
    (0.0, 0.0),
    (0.25, 0.0),
    (0.5, 0.25),
    (0.72, 0.0),
    (0.8, 0.0),
    (0.9, 0.0),
    (1.0, 0.0),
    (-0.4, 0.4),
]


def apodization_case(name, model):
    x = np.asarray([point[0] for point in SAMPLE_POINTS], dtype=float)
    y = np.asarray([point[1] for point in SAMPLE_POINTS], dtype=float)
    with np.errstate(divide="ignore", invalid="ignore"):
        intensity = np.asarray(model.get_intensity(x, y), dtype=float)
    return {
        "name": name,
        "dictionary": model.to_dict(),
        "samples": [
            {"x": point[0], "y": point[1], "intensity": float(value)}
            for point, value in zip(SAMPLE_POINTS, intensity, strict=True)
        ],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    args = parser.parse_args()

    cases = [
        apodization_case("uniform", UniformApodization()),
        apodization_case("gaussian", GaussianApodization(sigma=0.6)),
        apodization_case("cosine_squared", CosineSquaredApodization(R=0.8)),
        apodization_case("hann", HannApodization(D=1.6)),
        apodization_case("polynomial", PolynomialApodization(R=0.9, p=2.5)),
        apodization_case("super_gaussian", SuperGaussianApodization(w=0.7, n=4.0)),
        apodization_case("tukey", TukeyApodization(R=0.9, alpha=0.4)),
        apodization_case("tukey_uniform_limit", TukeyApodization(R=0.9, alpha=0.0)),
    ]
    output = {
        "optiland_version": optiland.__version__,
        "apodizations": cases,
    }
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(output, stream, indent=2, allow_nan=False)
        stream.write("\n")


if __name__ == "__main__":
    main()
