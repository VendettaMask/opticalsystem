"""Validate generated STEP fixtures with FreeCAD/OpenCascade.

Run inside a FreeCAD console with OPTILAND_STEP_FIXTURE_DIR or a directory argument.
"""

from __future__ import annotations

import os
import pathlib
import re
import sys
import traceback

import FreeCAD as App
import Part


def solid_key(solid: object) -> tuple[float, ...]:
    box = solid.BoundBox
    return tuple(
        round(value, 7)
        for value in (
            solid.Volume,
            box.XMin,
            box.YMin,
            box.ZMin,
            box.XMax,
            box.YMax,
            box.ZMax,
        )
    )


def validate(path: pathlib.Path, expected_count: int) -> None:
    imported = Part.read(str(path))
    if imported.isNull():
        raise RuntimeError(f"{path.name}: OpenCascade returned a null shape")

    solids: dict[tuple[float, ...], object] = {}
    for solid in imported.Solids:
        if not solid.isValid():
            raise RuntimeError(f"{path.name}: imported solid is invalid")
        if solid.Volume <= 0:
            raise RuntimeError(f"{path.name}: imported solid has non-positive volume")
        solids[solid_key(solid)] = solid

    if len(solids) != expected_count:
        raise RuntimeError(
            f"{path.name}: expected {expected_count} unique solids, found {len(solids)}"
        )

    document = App.newDocument(f"step_validation_{path.stem.replace('-', '_')}")
    try:
        for index, solid in enumerate(solids.values(), start=1):
            item = document.addObject("Part::Feature", f"Lens{index}")
            item.Label = f"Lens {index}"
            item.Shape = solid
        document.recompute()
        for item in document.Objects:
            if item.Shape.isNull() or not item.Shape.isValid():
                raise RuntimeError(f"{path.name}: object {item.Name} has an invalid shape")
    finally:
        App.closeDocument(document.Name)


def main() -> int:
    fixture_argument = os.environ.get("OPTILAND_STEP_FIXTURE_DIR")
    if not fixture_argument and len(sys.argv) < 2:
        raise RuntimeError("fixture directory argument is required")
    fixture_argument = fixture_argument or sys.argv[-1]
    directory = pathlib.Path(fixture_argument).resolve()
    fixtures = sorted(directory.glob("*.step"))
    if not fixtures:
        raise RuntimeError(f"no STEP fixtures found in {directory}")

    for fixture in fixtures:
        match = re.search(r"-(\d+)\.step$", fixture.name)
        if match is None:
            raise RuntimeError(f"fixture name does not contain expected solid count: {fixture.name}")
        validate(fixture, int(match.group(1)))
        print(f"validated {fixture.name}", flush=True)
    return 0


if __name__ == "__main__":
    try:
        exit_code = main()
    except Exception:
        traceback.print_exc()
        os._exit(1)
    os._exit(exit_code)
