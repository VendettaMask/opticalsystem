"""Export Merit Function Editor rows through the official ZOS-API.

The output is a deterministic golden-data artifact. It records the loaded
source hash, OpticStudio version, license, row order, raw parameter slots,
targets, weights, current values, contributions, and active state.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any

from zosapi_export import (
    DEFAULT_ZEMAX_DIRECTORY,
    ensure_no_existing_instance,
    load_zosapi,
)


DEFAULT_ZMX_PATH = Path(
    r"D:\Projects\opticalsystem\local-data\lens-library\originals\user-zmx\project\root\[MS-L7](10X大NA大视场).ZMX"
)
DEFAULT_OUTPUT_PATH = Path(
    r"D:\Projects\opticalsystem\artifacts\zemax\ms-l7-zemax-2026-r1-baseline\merit-function.json"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def enum_name(value: Any) -> str:
    return str(value).split(".")[-1]


def required_property(instance: Any, name: str) -> Any:
    try:
        return getattr(instance, name)
    except Exception as error:
        raise RuntimeError(
            f"ZOS-API row does not expose required property {name!r}"
        ) from error


def integer_cell(row: Any, column: int) -> int:
    cell = row.GetCellAt(column)
    try:
        return int(cell.IntegerValue)
    except Exception:
        try:
            return int(float(cell.DoubleValue))
        except Exception:
            return 0


def double_cell(row: Any, column: int) -> float | None:
    cell = row.GetCellAt(column)
    try:
        number = float(cell.DoubleValue)
    except Exception:
        try:
            number = float(cell.IntegerValue)
        except Exception:
            return 0.0
    return number if math.isfinite(number) else None


def finite_number(value: Any) -> float | None:
    number = float(value)
    return number if math.isfinite(number) else None


def read_row(row: Any, row_number: int) -> dict[str, Any]:
    return {
        "row": row_number,
        "type": enum_name(required_property(row, "Type")),
        "int1": integer_cell(row, 2),
        "int2": integer_cell(row, 3),
        "data1": double_cell(row, 4),
        "data2": double_cell(row, 5),
        "data3": double_cell(row, 6),
        "data4": double_cell(row, 7),
        "target": finite_number(required_property(row, "Target")),
        "weight": finite_number(required_property(row, "Weight")),
        "value": finite_number(required_property(row, "Value")),
        "contribution": finite_number(required_property(row, "Contribution")),
        "isActive": bool(required_property(row, "IsActive")),
    }


def export_merit_function(
    zmx_path: Path,
    output_path: Path,
    zemax_directory: Path,
    allow_existing: bool,
) -> None:
    if not zmx_path.is_file():
        raise FileNotFoundError(f"ZMX file not found: {zmx_path}")
    if not allow_existing:
        ensure_no_existing_instance()

    ZOSAPI = load_zosapi(zemax_directory)
    connection = ZOSAPI.ZOSAPI_Connection()
    application = None
    try:
        application = connection.CreateNewApplication()
        if application is None:
            raise RuntimeError("CreateNewApplication returned null")
        if not application.IsValidLicenseForAPI:
            raise RuntimeError(
                f"ZOS-API license check failed: {application.LicenseStatus}"
            )

        system = application.PrimarySystem
        if system is None:
            raise RuntimeError("ZOS-API returned no primary optical system")
        system.LoadFile(str(zmx_path.resolve()), False)

        mfe = system.MFE
        row_count = int(mfe.NumberOfOperands)
        merit_function = float(mfe.CalculateMeritFunction())
        rows = [read_row(mfe.GetOperandAt(index), index) for index in range(1, row_count + 1)]
        payload = {
            "schemaVersion": 1,
            "source": "Ansys Zemax OpticStudio 2026 R1 ZOS-API",
            "apiAssemblyVersion": str(
                application.GetType().Assembly.GetName().Version
            ),
            "licenseStatus": enum_name(application.LicenseStatus),
            "sourceFile": zmx_path.name,
            "sourceSha256": sha256(zmx_path),
            "rowCount": row_count,
            "meritFunction": merit_function,
            "rows": rows,
        }
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
            encoding="utf-8",
        )
    finally:
        if application is not None:
            application.CloseApplication()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zmx", type=Path, default=DEFAULT_ZMX_PATH)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_PATH)
    parser.add_argument(
        "--zemax-directory",
        type=Path,
        default=DEFAULT_ZEMAX_DIRECTORY,
    )
    parser.add_argument("--allow-existing", action="store_true")
    args = parser.parse_args()
    export_merit_function(
        args.zmx,
        args.output,
        args.zemax_directory,
        args.allow_existing,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
