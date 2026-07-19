#!/usr/bin/env python3
"""Generate the embedded glass catalog from Optiland 0.5.8 data."""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

import yaml


SUPPORTED_N_TYPES = {
    "formula 1",
    "formula 2",
    "formula 3",
    "formula 5",
    "tabulated n",
    "tabulated nk",
}


def numbers(value: str) -> list[float]:
    return [float(token) for token in value.split()]


def table(value: str) -> list[list[float]]:
    return [numbers(line) for line in value.splitlines() if line.strip()]


def generate(optiland_database: Path) -> dict:
    catalog_path = optiland_database / "catalog_nk.csv"
    data_root = optiland_database / "data-nk"
    with catalog_path.open(newline="", encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))

    metadata_by_file: dict[str, list[dict[str, str]]] = {}
    for row in rows:
        filename = row["filename"]
        if filename.startswith("glass/"):
            metadata_by_file.setdefault(filename, []).append(row)

    entries = []
    for filename in sorted(metadata_by_file):
        path = data_root / filename
        source = yaml.safe_load(path.read_text(encoding="utf-8"))
        blocks = source.get("DATA", [])
        n_block = next((block for block in blocks if block.get("type") in SUPPORTED_N_TYPES), None)
        if n_block is None:
            raise ValueError(f"No supported refractive-index data in {filename}")

        k_block = next(
            (block for block in blocks if block.get("type") in {"tabulated k", "tabulated nk"}),
            None,
        )
        n_type = n_block["type"]
        row = metadata_by_file[filename][0]
        parts = Path(filename).parts
        entry = {
            "manufacturer": parts[1].upper(),
            "name": path.stem,
            "formula": n_type,
            "min_um": float(row["min_wavelength"]),
            "max_um": float(row["max_wavelength"]),
        }

        if n_type.startswith("formula "):
            entry["coefficients"] = numbers(n_block["coefficients"])
        else:
            n_table = table(n_block["data"])
            entry["n_um"] = [values[0] for values in n_table]
            entry["n"] = [values[1] for values in n_table]

        if k_block is not None:
            k_table = table(k_block["data"])
            entry["k_um"] = [values[0] for values in k_table]
            entry["k"] = [values[2] if len(values) > 2 else values[1] for values in k_table]

        entries.append(entry)

    return {
        "source": "Optiland 0.5.8 / refractiveindex.info CC0 database",
        "entries": entries,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("optiland_database", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = generate(args.optiland_database)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(result, ensure_ascii=True, separators=(",", ":")),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
