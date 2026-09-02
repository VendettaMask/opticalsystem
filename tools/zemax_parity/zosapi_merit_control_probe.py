"""Probe OpticStudio merit-function control flow through the official ZOS-API."""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from zosapi_export import DEFAULT_ZEMAX_DIRECTORY, ensure_no_existing_instance, load_zosapi


DEFAULT_ZMX_PATH = Path(
    r"D:\Projects\opticalsystem\local-data\lens-library\originals\user-zmx\project\root\[MS-L7](10X大NA大视场).ZMX"
)


def configure_operand(
    row, operand_type, *, target: float = 0.0, weight: float = 0.0, operand_row: int = 0
):
    row.ChangeType(operand_type)
    row.Target = target
    row.Weight = weight
    if operand_row:
        row.GetCellAt(2).IntegerValue = operand_row
    return row


def add_operand(
    mfe, operand_type, *, target: float = 0.0, weight: float = 0.0, operand_row: int = 0
):
    return configure_operand(
        mfe.AddOperand(),
        operand_type,
        target=target,
        weight=weight,
        operand_row=operand_row,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zmx", type=Path, default=DEFAULT_ZMX_PATH)
    parser.add_argument("--zemax-directory", type=Path, default=DEFAULT_ZEMAX_DIRECTORY)
    parser.add_argument("--allow-existing", action="store_true")
    args = parser.parse_args()

    if not args.allow_existing:
        ensure_no_existing_instance()
    ZOSAPI = load_zosapi(args.zemax_directory)
    application = ZOSAPI.ZOSAPI_Connection().CreateNewApplication()
    if application is None or not application.IsValidLicenseForAPI:
        raise RuntimeError("ZOS-API application or license is unavailable")

    try:
        system = application.PrimarySystem
        system.LoadFile(str(args.zmx.resolve()), False)
        mfe = system.MFE
        mfe.DeleteAllRows()
        operand_type = ZOSAPI.Editors.MFE.MeritOperandType
        configure_operand(mfe.GetOperandAt(1), operand_type.SKIS)
        add_operand(mfe, operand_type.EFFL, target=0.0, weight=1.0)
        add_operand(mfe, operand_type.EFFL, target=1000.0, weight=1.0)
        add_operand(mfe, operand_type.SKIN)
        add_operand(mfe, operand_type.EFFL, target=0.0, weight=1.0)
        add_operand(mfe, operand_type.EFFL, target=1000.0, weight=1.0)
        mfe.GetOperandAt(1).GetCellAt(2).IntegerValue = 3
        mfe.GetOperandAt(4).GetCellAt(2).IntegerValue = 6
        merit = float(mfe.CalculateMeritFunction())

        print("merit", merit)

        rows = [mfe.GetOperandAt(index) for index in range(1, int(mfe.NumberOfOperands) + 1)]
        if int(rows[0].GetCellAt(2).IntegerValue) != 3:
            raise RuntimeError("SKIS Op# did not round-trip as row 3")
        if float(rows[1].Value) != 0.0:
            raise RuntimeError("SKIS did not skip row 2 for the symmetric reference lens")
        if not math.isfinite(float(rows[2].Value)) or float(rows[2].Value) == 0.0:
            raise RuntimeError("SKIS did not resume execution at row 3")
        if not math.isfinite(float(rows[4].Value)) or float(rows[4].Value) == 0.0:
            raise RuntimeError("SKIN incorrectly skipped row 5 for the symmetric reference lens")

        for index, row in enumerate(rows, start=1):
            print(
                index,
                str(row.Type).split(".")[-1],
                int(row.GetCellAt(2).IntegerValue),
                float(row.Value),
                float(row.Contribution),
                bool(row.IsActive),
            )
    finally:
        application.CloseApplication()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
