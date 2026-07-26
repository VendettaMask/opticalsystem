"""Export Zemax OpticStudio FFT MTF reference data through ZOS-API.

Run this with the Python distribution bundled with Ansys 2026 R1. That
distribution already contains pythonnet, so no additional packages are
required.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any

import clr


DEFAULT_ZEMAX_DIRECTORY = Path(
    r"D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio"
)
DEFAULT_ZMX_PATH = Path(r"C:\Users\19851\Desktop\123456.ZMX")
DEFAULT_OUTPUT_PATH = Path(
    r"D:\Projects\opticalsystem\artifacts\zemax\123456-fft-mtf.json"
)


def load_zosapi(zemax_directory: Path):
    net_helper = zemax_directory / "ZOSAPI_NetHelper.dll"
    if not net_helper.is_file():
        raise FileNotFoundError(f"ZOSAPI_NetHelper.dll not found: {net_helper}")

    clr.AddReference(str(net_helper))
    import ZOSAPI_NetHelper

    if not ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(
        str(zemax_directory)
    ):
        raise RuntimeError(
            f"Could not initialize OpticStudio under {zemax_directory}"
        )

    clr.AddReference(str(zemax_directory / "ZOSAPI.dll"))
    clr.AddReference(str(zemax_directory / "ZOSAPI_Interfaces.dll"))
    import ZOSAPI

    return ZOSAPI


def ensure_no_existing_instance() -> None:
    import System

    existing = list(
        System.Diagnostics.Process.GetProcessesByName("OpticStudio")
    )
    if existing:
        process_ids = ", ".join(str(process.Id) for process in existing)
        raise RuntimeError(
            "Refusing to start a second OpticStudio instance. "
            f"Close PID(s): {process_ids}"
        )


def net_vector(values: Any) -> list[float]:
    return [float(value) for value in values]


def net_matrix_column(values: Any, column: int) -> list[float]:
    return [
        float(values.GetValue(row, column))
        for row in range(values.GetLength(0))
    ]


def read_fft_mtf_series(results: Any) -> list[dict[str, Any]]:
    series = []
    for series_number in range(int(results.NumberOfDataSeries)):
        data = results.GetDataSeries(series_number)
        series.append(
            {
                "fieldNumber": series_number + 1,
                "frequencyCyclesPerMillimeter": net_vector(
                    data.XData.Data
                ),
                "tangential": net_matrix_column(data.YData.Data, 0),
                "sagittal": net_matrix_column(data.YData.Data, 1),
            }
        )
    return series


def export_reference_rays(system: Any, ZOSAPI: Any) -> list[dict[str, Any]]:
    from System import Double, Int32

    samples = [
        ("chief", 0.0, 0.0),
        ("tangential-positive", 0.0, 0.984375),
        ("tangential-negative", 0.0, -0.984375),
        ("sagittal-positive", 0.984375, 0.0),
        ("sagittal-negative", -0.984375, 0.0),
    ]
    field = system.SystemData.Fields.GetField(2)
    maximum_field = max(
        abs(float(system.SystemData.Fields.GetField(number).Y))
        for number in range(1, int(system.SystemData.Fields.NumberOfFields) + 1)
    )
    hy = 0.0 if maximum_field <= 1e-30 else float(field.Y) / maximum_field
    raytrace = system.Tools.OpenBatchRayTrace()
    try:
        data = raytrace.CreateNormUnpol(
            len(samples),
            ZOSAPI.Tools.RayTrace.RaysType.Real,
            int(system.LDE.NumberOfSurfaces),
        )
        results = []
        for wavelength_number in range(
            1, int(system.SystemData.Wavelengths.NumberOfWavelengths) + 1
        ):
            data.ClearData()
            for _, px, py in samples:
                data.AddRay(
                    Int32(wavelength_number),
                    Double(0.0),
                    Double(hy),
                    Double(px),
                    Double(py),
                    ZOSAPI.Tools.RayTrace.OPDMode.CurrentAndChief,
                )
            raytrace.RunAndWaitForCompletion()
            data.StartReadingResults()
            placeholder_int = Int32(0)
            placeholder_double = Double(0.0)
            for name, px, py in samples:
                output = data.ReadNextResult(
                    placeholder_int,
                    placeholder_int,
                    placeholder_int,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                    placeholder_double,
                )
                results.append(
                    {
                        "wavelengthNumber": wavelength_number,
                        "sample": name,
                        "px": px,
                        "py": py,
                        "errorCode": int(output[2]),
                        "vignetteCode": int(output[3]),
                        "x": float(output[4]),
                        "y": float(output[5]),
                        "z": float(output[6]),
                        "l": float(output[7]),
                        "m": float(output[8]),
                        "n": float(output[9]),
                        "opdWaves": float(output[13]),
                        "intensity": float(output[14]),
                        "stopX": float(
                            system.MFE.GetOperandValue(
                                ZOSAPI.Editors.MFE.MeritOperandType.REAX,
                                9,
                                wavelength_number,
                                0.0,
                                hy,
                                px,
                                py,
                                0.0,
                                0.0,
                            )
                        ),
                        "stopY": float(
                            system.MFE.GetOperandValue(
                                ZOSAPI.Editors.MFE.MeritOperandType.REAY,
                                9,
                                wavelength_number,
                                0.0,
                                hy,
                                px,
                                py,
                                0.0,
                                0.0,
                            )
                        ),
                    }
                )
        return results
    finally:
        raytrace.Close()


def export_fft_mtf(
    zmx_path: Path,
    output_path: Path,
    zemax_directory: Path,
) -> dict[str, Any]:
    if not zmx_path.is_file():
        raise FileNotFoundError(f"ZMX file not found: {zmx_path}")

    ZOSAPI = load_zosapi(zemax_directory)
    ensure_no_existing_instance()

    connection = ZOSAPI.ZOSAPI_Connection()
    application = None
    analysis = None
    try:
        application = connection.CreateNewApplication()
        if application is None:
            raise RuntimeError("CreateNewApplication returned null")
        if not application.IsValidLicenseForAPI:
            raise RuntimeError(
                "ZOS-API license check failed: "
                f"{application.LicenseStatus}"
            )

        system = application.PrimarySystem
        if system is None:
            raise RuntimeError("ZOS-API returned no primary optical system")
        system.LoadFile(str(zmx_path), False)

        analysis = system.Analyses.New_FftMtf()
        settings = analysis.GetSettings()
        zemax_defaults = {
            "fieldNumber": int(settings.Field.GetFieldNumber()),
            "surfaceNumber": int(settings.Surface.GetSurfaceNumber()),
            "wavelengthNumber": int(
                settings.Wavelength.GetWavelengthNumber()
            ),
            "type": str(settings.Type),
            "sampleSize": str(settings.SampleSize),
            "showDiffractionLimit": bool(settings.ShowDiffractionLimit),
            "useDashes": bool(settings.UseDashes),
            "usePolarization": bool(settings.UsePolarization),
            "maximumFrequencyCyclesPerMillimeter": float(
                settings.MaximumFrequency
            ),
        }
        settings.MaximumFrequency = 50.0
        settings.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
        settings.Field.SetFieldNumber(0)
        settings.Wavelength.SetWavelengthNumber(0)
        analysis.ApplyAndWaitForCompletion()
        results = analysis.GetResults()

        fields = []
        for field_number in range(
            1, int(system.SystemData.Fields.NumberOfFields) + 1
        ):
            field = system.SystemData.Fields.GetField(field_number)
            fields.append(
                {
                    "number": field_number,
                    "x": float(field.X),
                    "y": float(field.Y),
                    "weight": float(field.Weight),
                }
            )

        wavelengths = []
        for wavelength_number in range(
            1, int(system.SystemData.Wavelengths.NumberOfWavelengths) + 1
        ):
            wavelength = system.SystemData.Wavelengths.GetWavelength(
                wavelength_number
            )
            wavelengths.append(
                {
                    "number": wavelength_number,
                    "micrometers": float(wavelength.Wavelength),
                    "weight": float(wavelength.Weight),
                }
            )

        surface_refractive_indices = []
        for surface_number in range(int(system.LDE.NumberOfSurfaces)):
            surface = system.LDE.GetSurfaceAt(surface_number)
            if not str(surface.Material):
                continue
            surface_refractive_indices.append(
                {
                    "surfaceNumber": surface_number,
                    "material": str(surface.Material),
                    "indices": [
                        float(
                            system.MFE.GetOperandValue(
                                ZOSAPI.Editors.MFE.MeritOperandType.INDX,
                                surface_number,
                                wavelength["number"],
                                0.0,
                                0.0,
                                0.0,
                                0.0,
                                0.0,
                                0.0,
                            )
                        )
                        for wavelength in wavelengths
                    ],
                }
            )

        series = read_fft_mtf_series(results)
        monochromatic_series = []
        for wavelength in wavelengths:
            settings.Wavelength.SetWavelengthNumber(wavelength["number"])
            analysis.ApplyAndWaitForCompletion()
            monochromatic_series.append(
                {
                    "wavelengthNumber": wavelength["number"],
                    "micrometers": wavelength["micrometers"],
                    "series": read_fft_mtf_series(analysis.GetResults()),
                }
            )
        settings.Wavelength.SetWavelengthNumber(0)

        payload = {
            "source": "Ansys Zemax OpticStudio 2026 R1 ZOS-API",
            "systemFile": str(system.SystemFile),
            "licenseStatus": str(application.LicenseStatus),
            "mode": str(application.Mode),
            "surfaceCount": int(system.LDE.NumberOfSurfaces),
            "fields": fields,
            "wavelengths": wavelengths,
            "surfaceRefractiveIndices": surface_refractive_indices,
            "referenceRays": export_reference_rays(system, ZOSAPI),
            "zemaxDefaults": zemax_defaults,
            "settings": {
                "analysis": "FFT MTF",
                "sampleSize": "64x64",
                "maximumFrequencyCyclesPerMillimeter": 50.0,
                "fieldNumber": 0,
                "wavelengthNumber": 0,
            },
            "series": series,
            "monochromaticSeries": monochromatic_series,
        }
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        return payload
    finally:
        if analysis is not None:
            try:
                analysis.Close()
            except Exception:
                pass
        if application is not None:
            try:
                application.CloseApplication()
            except Exception:
                pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zmx", type=Path, default=DEFAULT_ZMX_PATH)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_PATH)
    parser.add_argument(
        "--zemax-directory",
        type=Path,
        default=DEFAULT_ZEMAX_DIRECTORY,
    )
    return parser.parse_args()


def main() -> int:
    arguments = parse_args()
    payload = export_fft_mtf(
        arguments.zmx,
        arguments.output,
        arguments.zemax_directory,
    )
    print(
        "connected=true; "
        f"license={payload['licenseStatus']}; "
        f"surfaces={payload['surfaceCount']}; "
        f"fields={len(payload['fields'])}; "
        f"wavelengths={len(payload['wavelengths'])}; "
        f"series={len(payload['series'])}; "
        f"output={arguments.output}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
