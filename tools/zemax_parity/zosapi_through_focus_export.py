"""Export Zemax FFT through-focus MTF reference data through ZOS-API."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from zosapi_export import (
    DEFAULT_ZEMAX_DIRECTORY,
    DEFAULT_ZMX_PATH,
    ensure_no_existing_instance,
    load_zosapi,
    net_matrix_column,
    net_vector,
)


DEFAULT_OUTPUT_PATH = Path(
    r"D:\Projects\opticalsystem\artifacts\zemax"
    r"\123456-fft-through-focus-mtf.json"
)


def enum_name(owner: Any, property_name: str) -> str:
    import System

    property_info = owner.GetType().GetProperty(property_name)
    value = property_info.GetValue(owner, None)
    return str(System.Enum.GetName(property_info.PropertyType, value))


def read_series(results: Any) -> list[dict[str, Any]]:
    series = []
    for series_index in range(int(results.NumberOfDataSeries)):
        data = results.GetDataSeries(series_index)
        column_count = int(data.YData.Data.GetLength(1))
        series.append(
            {
                "seriesIndex": series_index,
                "focusMillimeters": net_vector(data.XData.Data),
                "columns": [
                    net_matrix_column(data.YData.Data, column)
                    for column in range(column_count)
                ],
            }
        )
    return series


def export_wavefront_samples(system: Any, ZOSAPI: Any) -> list[dict[str, Any]]:
    from System import Double, Int32

    pupil_coordinates = [
        (0.0, 0.0),
        (0.0, 0.25),
        (0.0, 0.5),
        (0.0, 0.75),
        (0.0, 0.984375),
        (0.0, 1.0),
        (0.0, -1.0),
        (0.25, 0.0),
        (0.5, 0.0),
        (0.75, 0.0),
        (0.984375, 0.0),
        (1.0, 0.0),
        (-1.0, 0.0),
        (0.5, 0.5),
        (-0.5, -0.5),
    ]
    raytrace = system.Tools.OpenBatchRayTrace()
    try:
        data = raytrace.CreateNormUnpol(
            len(pupil_coordinates),
            ZOSAPI.Tools.RayTrace.RaysType.Real,
            int(system.LDE.NumberOfSurfaces),
        )
        samples = []
        maximum_field = max(
            abs(float(system.SystemData.Fields.GetField(number).Y))
            for number in range(
                1, int(system.SystemData.Fields.NumberOfFields) + 1
            )
        )
        for field_number in (1, 2):
            field = system.SystemData.Fields.GetField(field_number)
            hy = (
                0.0
                if maximum_field <= 1e-30
                else float(field.Y) / maximum_field
            )
            for wavelength_number in range(
                1,
                int(system.SystemData.Wavelengths.NumberOfWavelengths) + 1,
            ):
                data.ClearData()
                for px, py in pupil_coordinates:
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
                for px, py in pupil_coordinates:
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
                    samples.append(
                        {
                            "fieldNumber": field_number,
                            "wavelengthNumber": wavelength_number,
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
                            "l2": float(output[10]),
                            "m2": float(output[11]),
                            "n2": float(output[12]),
                            "opdWaves": float(output[13]),
                        }
                    )
        return samples
    finally:
        raytrace.Close()


def export_single_ray_history(
    system: Any,
    ZOSAPI: Any,
    ray_type: Any,
) -> list[dict[str, Any]]:
    from System import Double, Int32

    history = []
    for surface_number in range(1, int(system.LDE.NumberOfSurfaces)):
        raytrace = system.Tools.OpenBatchRayTrace()
        try:
            data = raytrace.CreateNormUnpol(
                1,
                ray_type,
                surface_number,
            )
            data.AddRay(
                Int32(2),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.5),
                ZOSAPI.Tools.RayTrace.OPDMode.CurrentAndChief,
            )
            raytrace.RunAndWaitForCompletion()
            data.StartReadingResults()
            output = data.ReadNextResult(
                Int32(0),
                Int32(0),
                Int32(0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
                Double(0.0),
            )
            history.append(
                {
                    "surfaceNumber": surface_number,
                    "errorCode": int(output[2]),
                    "vignetteCode": int(output[3]),
                    "x": float(output[4]),
                    "y": float(output[5]),
                    "z": float(output[6]),
                    "l": float(output[7]),
                    "m": float(output[8]),
                    "n": float(output[9]),
                }
            )
        finally:
            raytrace.Close()
    return history


def export_defocused_wavefront_samples(
    system: Any,
    ZOSAPI: Any,
    defocus_values: list[float],
) -> list[dict[str, Any]]:
    from System import Double, Int32

    pupil_coordinates = [
        (0.0, 0.0),
        (0.0, 0.25),
        (0.0, 0.5),
        (0.0, 0.75),
        (0.0, 0.984375),
    ]
    thickness_cell = system.LDE.GetSurfaceAt(21).ThicknessCell
    thickness_cell.MakeSolveFixed()
    nominal_thickness = float(thickness_cell.DoubleValue)
    samples = []
    try:
        for defocus in defocus_values:
            thickness_cell.DoubleValue = nominal_thickness + defocus
            raytrace = system.Tools.OpenBatchRayTrace()
            try:
                data = raytrace.CreateNormUnpol(
                    len(pupil_coordinates),
                    ZOSAPI.Tools.RayTrace.RaysType.Real,
                    int(system.LDE.NumberOfSurfaces),
                )
                for px, py in pupil_coordinates:
                    data.AddRay(
                        Int32(2),
                        Double(0.0),
                        Double(0.0),
                        Double(px),
                        Double(py),
                        ZOSAPI.Tools.RayTrace.OPDMode.CurrentAndChief,
                    )
                raytrace.RunAndWaitForCompletion()
                data.StartReadingResults()
                for px, py in pupil_coordinates:
                    output = data.ReadNextResult(
                        Int32(0),
                        Int32(0),
                        Int32(0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                        Double(0.0),
                    )
                    samples.append(
                        {
                            "defocusMillimeters": defocus,
                            "px": px,
                            "py": py,
                            "opdWaves": float(output[13]),
                        }
                    )
            finally:
                raytrace.Close()
    finally:
        thickness_cell.DoubleValue = nominal_thickness
    return samples


def export_mtf_operand_samples(
    system: Any,
    ZOSAPI: Any,
    defocus_values: list[float],
    frequency: float,
) -> list[dict[str, Any]]:
    thickness_cell = system.LDE.GetSurfaceAt(21).ThicknessCell
    thickness_cell.MakeSolveFixed()
    nominal_thickness = float(thickness_cell.DoubleValue)
    samples = []
    try:
        for defocus in defocus_values:
            thickness_cell.DoubleValue = nominal_thickness + defocus
            for field_number in range(
                1, int(system.SystemData.Fields.NumberOfFields) + 1
            ):
                for sampling_code in (1, 2, 3):
                    item = {
                        "defocusMillimeters": defocus,
                        "fieldNumber": field_number,
                        "wavelengthNumber": 2,
                        "samplingCode": sampling_code,
                    }
                    for operand_name in ("MTFT", "MTFS"):
                        try:
                            item[operand_name] = float(
                                system.MFE.GetOperandValue(
                                    getattr(
                                        ZOSAPI.Editors.MFE.MeritOperandType,
                                        operand_name,
                                    ),
                                    sampling_code,
                                    2,
                                    float(field_number),
                                    frequency,
                                    0.0,
                                    0.0,
                                    0,
                                    0,
                                )
                            )
                        except Exception as error:
                            item[operand_name] = (
                                f"{type(error).__name__}: {error}"
                            )
                    samples.append(item)
    finally:
        thickness_cell.DoubleValue = nominal_thickness
    return samples


def export_reference(
    zmx_path: Path,
    output_path: Path,
    zemax_directory: Path,
    delta_focus: float,
    frequency: float,
    number_of_steps: int,
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
        system = application.PrimarySystem
        system.LoadFile(str(zmx_path), False)
        resaved_path = output_path.with_name("123456-zemax-resaved.ZMX")
        system.SaveAs(str(resaved_path))
        loaded_thicknesses = [
            (
                lambda value: value if math.isfinite(value) else None
            )(float(system.LDE.GetSurfaceAt(number).Thickness))
            for number in range(int(system.LDE.NumberOfSurfaces))
        ]
        thickness_solve_properties = {}
        for surface_number in (20, 21):
            thickness_cell = system.LDE.GetSurfaceAt(surface_number).ThicknessCell
            thickness_solve = thickness_cell.GetSolveData()
            properties = {
                "cellMethods": [
                    str(method.Name)
                    for method in thickness_cell.GetType().GetMethods()
                ]
            }
            for property_info in thickness_solve.GetType().GetProperties():
                if property_info.GetMethod is None:
                    continue
                try:
                    value = property_info.GetValue(thickness_solve, None)
                    properties[str(property_info.Name)] = (
                        value
                        if isinstance(value, (bool, int, str))
                        or (isinstance(value, float) and math.isfinite(value))
                        else str(value)
                    )
                except Exception:
                    pass
            thickness_solve_properties[str(surface_number)] = properties

        analysis = system.Analyses.New_FftThroughFocusMtf()
        settings = analysis.GetSettings()
        defaults = {
            "sampleSize": enum_name(settings, "SampleSize"),
            "deltaFocusMillimeters": float(settings.DeltaFocus),
            "frequencyCyclesPerMillimeter": float(settings.Frequency),
            "numberOfSteps": int(settings.NumberOfSteps),
            "wavelengthNumber": int(
                settings.Wavelength.GetWavelengthNumber()
            ),
            "fieldNumber": int(settings.Field.GetFieldNumber()),
            "type": enum_name(settings, "Type"),
            "usePolarization": bool(settings.UsePolarization),
            "useDashes": bool(settings.UseDashes),
        }

        settings.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
        settings.DeltaFocus = delta_focus
        settings.Frequency = frequency
        settings.NumberOfSteps = number_of_steps
        settings.Wavelength.SetWavelengthNumber(0)
        settings.Field.SetFieldNumber(0)
        settings.UsePolarization = False
        settings.UseDashes = False
        analysis.ApplyAndWaitForCompletion()

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

        surface_indices = []
        for surface_number in range(int(system.LDE.NumberOfSurfaces)):
            values = []
            for wavelength in wavelengths:
                values.append(
                    float(
                        system.MFE.GetOperandValue(
                            ZOSAPI.Editors.MFE.MeritOperandType.INDX,
                            surface_number,
                            wavelength["number"],
                            0.0,
                            0.0,
                            0.0,
                            0.0,
                            0,
                            0,
                        )
                    )
                )
            surface_indices.append(
                {
                    "surfaceNumber": surface_number,
                    "thickness": (
                        lambda value: value if math.isfinite(value) else None
                    )(
                        float(
                            system.LDE.GetSurfaceAt(surface_number).Thickness
                        )
                    ),
                    "indicesAfter": values,
                }
            )

        working_f_numbers = []
        for field in fields:
            for wavelength in wavelengths:
                values = {}
                for operand_name in ("WFNO", "TFNO", "SFNO"):
                    try:
                        operand_type = getattr(
                            ZOSAPI.Editors.MFE.MeritOperandType,
                            operand_name,
                        )
                        values[operand_name] = float(
                            system.MFE.GetOperandValue(
                                operand_type,
                                field["number"],
                                wavelength["number"],
                                0.0,
                                0.0,
                                0.0,
                                0.0,
                                0,
                                0,
                            )
                        )
                    except Exception as error:
                        values[operand_name] = f"{type(error).__name__}: {error}"
                working_f_numbers.append(
                    {
                        "fieldNumber": field["number"],
                        "wavelengthNumber": wavelength["number"],
                        **values,
                    }
                )

        pupil_data = system.LDE.GetPupil()
        if isinstance(pupil_data, tuple):
            pupil_properties = {
                "tupleLength": len(pupil_data),
                "tupleValues": [
                    value
                    if isinstance(value, (bool, int, float, str))
                    else str(value)
                    for value in pupil_data
                ],
            }
        else:
            pupil_properties = {}
            for property_info in pupil_data.GetType().GetProperties():
                if property_info.GetMethod is None:
                    continue
                try:
                    value = property_info.GetValue(pupil_data, None)
                    if isinstance(value, (bool, int, float, str)):
                        pupil_properties[str(property_info.Name)] = value
                    else:
                        pupil_properties[str(property_info.Name)] = str(value)
                except Exception:
                    pass

        ray_aiming_properties = {}
        ray_aiming = system.SystemData.RayAiming
        for property_info in ray_aiming.GetType().GetProperties():
            if property_info.GetMethod is None:
                continue
            try:
                value = property_info.GetValue(ray_aiming, None)
                ray_aiming_properties[str(property_info.Name)] = (
                    value
                    if isinstance(value, (bool, int, float, str))
                    else str(value)
                )
            except Exception:
                pass

        series = read_series(analysis.GetResults())
        monochromatic_series = []
        for wavelength in wavelengths:
            settings.Wavelength.SetWavelengthNumber(wavelength["number"])
            analysis.ApplyAndWaitForCompletion()
            monochromatic_series.append(
                {
                    "wavelengthNumber": wavelength["number"],
                    "micrometers": wavelength["micrometers"],
                    "series": read_series(analysis.GetResults()),
                }
            )
        settings.Wavelength.SetWavelengthNumber(0)

        payload = {
            "source": "Ansys Zemax OpticStudio 2026 R1 ZOS-API",
            "systemFile": str(system.SystemFile),
            "licenseStatus": str(application.LicenseStatus),
            "fields": fields,
            "wavelengths": wavelengths,
            "surfaceIndices": surface_indices,
            "workingFNumbers": working_f_numbers,
            "loadedThicknesses": loaded_thicknesses,
            "surface21ThicknessSolve": thickness_solve_properties,
            "pupilData": pupil_properties,
            "rayAiming": ray_aiming_properties,
            "zemaxDefaults": defaults,
            "settings": {
                "analysis": "FFT Through Focus MTF",
                "sampleSize": "S_64x64",
                "deltaFocusMillimeters": delta_focus,
                "frequencyCyclesPerMillimeter": frequency,
                "numberOfSteps": number_of_steps,
                "wavelengthNumber": 0,
                "fieldNumber": 0,
                "type": defaults["type"],
                "usePolarization": False,
                "useDashes": False,
            },
            "wavefrontSamples": export_wavefront_samples(
                system,
                ZOSAPI,
            ),
            "singleRayHistory": export_single_ray_history(
                system,
                ZOSAPI,
                ZOSAPI.Tools.RayTrace.RaysType.Real,
            ),
            "singleParaxialRayHistory": export_single_ray_history(
                system,
                ZOSAPI,
                ZOSAPI.Tools.RayTrace.RaysType.Paraxial,
            ),
            "series": series,
            "monochromaticSeries": monochromatic_series,
        }
        payload["defocusedWavefrontSamples"] = export_defocused_wavefront_samples(
            system,
            ZOSAPI,
            [-delta_focus, delta_focus],
        )
        payload["mtfOperandSamples"] = export_mtf_operand_samples(
            system,
            ZOSAPI,
            [
                -delta_focus,
                -delta_focus / 2,
                0.0,
                delta_focus / 2,
                delta_focus,
            ],
            frequency,
        )
        system.SaveAs(
            str(output_path.with_name("123456-zemax-fixed-thickness.ZMX"))
        )
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        print(
            f"fields={len(fields)}; series={len(payload['series'])}; "
            f"output={output_path}"
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zmx", type=Path, default=DEFAULT_ZMX_PATH)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_PATH)
    parser.add_argument(
        "--zemax-directory",
        type=Path,
        default=DEFAULT_ZEMAX_DIRECTORY,
    )
    parser.add_argument("--delta-focus", type=float, default=0.1)
    parser.add_argument("--frequency", type=float, default=50.0)
    parser.add_argument("--steps", type=int, default=5)
    arguments = parser.parse_args()
    export_reference(
        arguments.zmx,
        arguments.output,
        arguments.zemax_directory,
        arguments.delta_focus,
        arguments.frequency,
        arguments.steps,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
