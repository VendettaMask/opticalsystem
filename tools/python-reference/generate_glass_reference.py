#!/usr/bin/env python3
"""Generate multi-vendor glass n/k reference values with Optiland 0.5.8."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from optiland.materials import Material


GLASSES = [
    ("SCHOTT", "N-BK7"),
    ("OHARA", "S-FPL53"),
    ("HOYA", "E-FD10"),
    ("HIKARI", "J-BK7A"),
    ("CDGM", "H-ZK9B"),
    ("SUMITA", "K-PSFn2"),
    ("AMI", "AMTIR-2"),
    ("MISC", "Nyakuchena"),
    ("SCHOTT", "BOROFLOAT33"),
    ("AMI", "AMTIR-1"),
]


def scalar(value) -> float:
    return float(np.asarray(value).reshape(-1)[0])


def generate() -> dict:
    entries = []
    for manufacturer, name in GLASSES:
        material = Material(name, reference=manufacturer.lower())
        minimum = float(material.material_data["min_wavelength"])
        maximum = float(material.material_data["max_wavelength"])
        margin = min(0.01, (maximum - minimum) * 0.05)
        wavelengths = [minimum + margin, (minimum + maximum) / 2, maximum - margin]
        entries.append(
            {
                "manufacturer": manufacturer,
                "name": name,
                "samples": [
                    {
                        "wavelength_um": wavelength,
                        "n": scalar(material.n(wavelength)),
                        "k": scalar(material.k(wavelength)),
                    }
                    for wavelength in wavelengths
                ],
            }
        )
    return {"optiland_version": "0.5.8", "entries": entries}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.write_text(json.dumps(generate(), indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
