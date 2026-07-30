"""Capture a complete OpticStudio analysis baseline for one ZMX file.

The data pass uses the official ZOS-API Standalone connection. Every
AnalysisIDM value is attempted with its Zemax defaults and receives a status
record even when it is unavailable for the loaded sequential system.

For successful analyses that have an official ZPL string code, a second pass
launches OpticStudio with a command-line ZPL macro and exports the actual
analysis window to JPEG. Structured ZOS-API plots are rendered as a fallback
for successful analyses without a ZPL string code.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
import traceback
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import clr

from zosapi_export import (
    DEFAULT_ZEMAX_DIRECTORY,
    DEFAULT_ZMX_PATH,
    ensure_no_existing_instance,
    load_zosapi,
)


DEFAULT_OUTPUT_PATH = Path(
    r"D:\Projects\opticalsystem\artifacts\zemax"
    r"\123456-zemax-2026-r1-baseline"
)
DEFAULT_ZPL_PATH = Path(__file__).with_name("capture_analysis_window.zpl")

# Official, case-sensitive OpticStudio ZPL analysis string codes.
ZPL_CODES = {
    "RayFan": "Ray",
    "OpticalPathFan": "Opd",
    "PupilAberrationFan": "Pab",
    "FieldCurvatureAndDistortion": "Fcd",
    "FocalShiftDiagram": "Cfs",
    "GridDistortion": "Grd",
    "LateralColor": "Lat",
    "LongitudinalAberration": "Lon",
    "RayTrace": "Rtr",
    "SeidelCoefficients": "Sei",
    "SeidelDiagram": "Sdi",
    "ZernikeAnnularCoefficients": "Zat",
    "ZernikeCoefficientsVsField": "Zvf",
    "ZernikeFringeCoefficients": "Zfr",
    "ZernikeStandardCoefficients": "Zst",
    "FftMtf": "Mtf",
    "FftThroughFocusMtf": "Tfm",
    "GeometricThroughFocusMtf": "Tfg",
    "GeometricMtf": "Gtf",
    "FftMtfMap": "Fmm",
    "GeometricMtfMap": "Gmm",
    "FftSurfaceMtf": "Smf",
    "FftMtfvsField": "Mth",
    "GeometricMtfvsField": "Gvf",
    "HuygensMtfvsField": "Hmh",
    "HuygensMtf": "Hmf",
    "HuygensSurfaceMtf": "Hsm",
    "HuygensThroughFocusMtf": "Htf",
    "FftPsf": "Fps",
    "FftPsfCrossSection": "Pcs",
    "FftPsfLineEdgeSpread": "Lsf",
    "HuygensPsfCrossSection": "Hcs",
    "HuygensPsf": "Hps",
    "DiffractionEncircledEnergy": "Enc",
    "GeometricEncircledEnergy": "Gee",
    "GeometricLineEdgeSpread": "Lin",
    "ExtendedSourceEncircledEnergy": "Xse",
    "SurfaceCurvatureCross": "Scc",
    "SurfacePhaseCross": "Spc",
    "SurfaceSagCross": "Ssc",
    "SurfaceCurvature": "Scv",
    "SurfacePhase": "Srp",
    "SurfaceSag": "Srs",
    "StandardSpot": "Spt",
    "ThroughFocusSpot": "Stf",
    "FullFieldSpot": "Sff",
    "MatrixSpot": "Sma",
    "ConfigurationMatrixSpot": "Smc",
    "RMSField": "Rms",
    "RMSFieldMap": "Rfm",
    "RMSLambdaDiagram": "Rmw",
    "RMSFocus": "Rmf",
    "Foucault": "Foa",
    "Interferogram": "Int",
    "WavefrontMap": "Wfm",
    "Draw2D": "Lay",
    "Draw3D": "L3d",
    "ImageSimulation": "Sim",
    "GeometricImageAnalysis": "Ima",
    "IMABIMFileViewer": "Imv",
    "GeometricBitmapImageAnalysis": "Ibm",
    "BitmapFileViewer": "Jbv",
    "LightSourceAnalysis": "Lsa",
    "PartiallyCoherentImageAnalysis": "Pci",
    "ExtendedDiffractionImageAnalysis": "Xdi",
    "BiocularFieldOfViewAnalysis": "Fov",
    "BiocularDipvergenceConvergence": "Dip",
    "RelativeIllumination": "Rel",
    "VignettingDiagramSettings": "Vig",
    "FootprintSettings": "Foo",
    "YYbarDiagram": "Yyb",
    "PowerFieldMapSettings": "Pal",
    "PowerPupilMapSettings": "Ppm",
    "IncidentAnglevsImageHeight": "Iht",
    "FiberCouplingSettings": "Fcl",
    "YNIContributions": "Yni",
    "SagTable": "Sag",
    "CardinalPoints": "Car",
    "DispersionDiagram": "Dis",
    "GlassMap": "Gmp",
    "AthermalGlassMap": "Agm",
    "DispersionvsWavelength": "Dvl",
    "GrinProfile": "Gip",
    "GradiumProfile": "Gpr",
    "UniversalPlot1D": "Uni",
    "UniversalPlot2D": "Un2",
    "PolarizationRayTrace": "Pol",
    "PolarizationPupilMap": "Pmp",
    "Transmission": "Tra",
    "PhaseAberration": "Pha",
    "TransmissionFan": "Ptf",
    "ParaxialGaussianBeam": "Gbp",
    "SkewGaussianBeam": "Gbs",
    "PhysicalOpticsPropagation": "Pop",
    "BeamFileViewer": "Bfv",
    "ReflectionvsAngle": "Cra",
    "TransmissionvsAngle": "Cta",
    "AbsorptionvsAngle": "Caa",
    "DiattenuationvsAngle": "Cda",
    "PhasevsAngle": "Cpa",
    "RetardancevsAngle": "Cna",
    "ReflectionvsWavelength": "Crw",
    "TransmissionvsWavelength": "Ctw",
    "AbsorptionvsWavelength": "Caw",
    "DiattenuationvsWavelength": "Cdw",
    "PhasevsWavelength": "Cpw",
    "RetardancevsWavelength": "Cnw",
    "DirectivityPlot": "Sdv",
    "SourcePolarViewer": "Spo",
    "SourceSpectrumViewer": "Ssp",
    "SurfaceDataSettings": "Sur",
    "PrescriptionDataSettings": "Pre",
    "PartViewer": "Pvr",
    "ReverseRadianceAnalysis": "Rda",
    "PathAnalysis": "Pat",
    "FluxvsWavelength": "Fvw",
    "ScatterFunctionViewer": "Sfv",
    "ScatterPolarPlotSettings": "Spv",
    "ZemaxElementDrawing": "Ele",
    "ShadedModel": "Lsh",
    "NSCShadedModel": "LSn",
    "NSC3DLayout": "L3n",
    "NSCObjectViewer": "Obv",
    "RayDatabaseViewer": "Rdb",
    "ISOElementDrawing": "ISO",
    "SystemData": "Sys",
    "TestPlateList": "Tpl",
    "SourceColorChart1931": "C31",
    "SourceColorChart1976": "C76",
    "CoatingListing": "Cls",
    "FullFieldAberration": "Ffa",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def slug(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")


def finite(value: Any) -> Any:
    number = float(value)
    return number if math.isfinite(number) else str(number)


def vector(values: Any) -> list[Any]:
    return [finite(value) for value in values]


def matrix(values: Any) -> list[list[Any]]:
    return [
        [finite(values.GetValue(row, column)) for column in range(values.GetLength(1))]
        for row in range(values.GetLength(0))
    ]


def simple_object(value: Any) -> Any:
    if value is None:
        return None
    if isinstance(value, (str, bool, int)):
        return value
    if isinstance(value, float):
        return finite(value)
    try:
        object_type = value.GetType()
    except Exception:
        return str(value)
    result: dict[str, Any] = {}
    for property_info in object_type.GetProperties():
        if property_info.GetIndexParameters().Length != 0:
            continue
        try:
            item = property_info.GetValue(value, None)
        except Exception:
            continue
        if item is None or isinstance(item, (str, bool, int)):
            result[property_info.Name] = item
        elif isinstance(item, float):
            result[property_info.Name] = finite(item)
        elif item.GetType().IsEnum:
            result[property_info.Name] = str(item)
    return result or str(value)


def serialize_results(results: Any) -> dict[str, Any]:
    header_lines = results.HeaderData.Lines
    payload: dict[str, Any] = {
        "metadata": {
            "featureDescription": str(results.MetaData.FeatureDescription),
            "lensFile": str(results.MetaData.LensFile),
            "lensTitle": str(results.MetaData.LensTitle),
            "date": str(results.MetaData.Date),
        },
        "header": [str(line) for line in header_lines]
        if header_lines is not None
        else [],
        "messages": [
            simple_object(results.GetMessageAt(index))
            for index in range(int(results.NumberOfMessages))
        ],
        "dataSeries": [],
        "dataSeriesRgb": [],
        "dataGrids": [],
        "dataGridsRgb": [],
        "scatterPoints": [],
        "scatterPointsRgb": [],
        "rayData": [],
    }
    for index in range(int(results.NumberOfDataSeries)):
        item = results.GetDataSeries(index)
        labels = item.SeriesLabels
        x_data = item.XData
        y_data = item.YData
        payload["dataSeries"].append(
            {
                "index": index,
                "description": str(item.Description),
                "xLabel": str(item.XLabel),
                "seriesLabels": [str(label) for label in labels]
                if labels is not None
                else [],
                "x": vector(x_data.Data) if x_data is not None else [],
                "y": matrix(y_data.Data) if y_data is not None else [],
            }
        )
    for index in range(int(results.NumberOfDataSeriesRgb)):
        item = results.GetDataSeriesRgb(index)
        rows = int(item.NumberOfRows)
        columns = int(item.NumSeries)
        labels = item.SeriesLabels
        x_data = item.XData
        payload["dataSeriesRgb"].append(
            {
                "index": index,
                "description": str(item.Description),
                "xLabel": str(item.XLabel),
                "seriesLabels": [str(label) for label in labels]
                if labels is not None
                else [],
                "x": vector(x_data.Data) if x_data is not None else [],
                "rgb": [
                    [
                        simple_object(item.GetYPoint(row, column))
                        for column in range(columns)
                    ]
                    for row in range(rows)
                ],
            }
        )
    for index in range(int(results.NumberOfDataGrids)):
        item = results.GetDataGrid(index)
        payload["dataGrids"].append(
            {
                "index": index,
                "description": str(item.Description),
                "xLabel": str(item.XLabel),
                "yLabel": str(item.YLabel),
                "valueLabel": str(item.ValueLabel),
                "nx": int(item.Nx),
                "ny": int(item.Ny),
                "dx": finite(item.Dx),
                "dy": finite(item.Dy),
                "minX": finite(item.MinX),
                "minY": finite(item.MinY),
                "values": matrix(item.Values),
            }
        )
    for index in range(int(results.NumberOfDataGridsRgb)):
        item = results.GetDataGridRgb(index)
        payload["dataGridsRgb"].append(
            {
                "index": index,
                "description": str(item.Description),
                "xLabel": str(item.XLabel),
                "yLabel": str(item.YLabel),
                "valueLabel": str(item.ValueLabel),
                "nx": int(item.Nx),
                "ny": int(item.Ny),
                "rgb": [
                    [
                        simple_object(item.GetValue(row, column))
                        for column in range(int(item.Nx))
                    ]
                    for row in range(int(item.Ny))
                ],
            }
        )
    for key, count_name, getter_name in (
        ("scatterPoints", "NumberOfDataScatterPoints", "GetDataScatterPoint"),
        (
            "scatterPointsRgb",
            "NumberOfDataScatterPointsRgb",
            "GetDataScatterPointRgb",
        ),
    ):
        for index in range(int(getattr(results, count_name))):
            item = getattr(results, getter_name)(index)
            payload[key].append(
                {
                    "index": index,
                    "description": str(item.Description),
                    "xLabel": str(item.XLabel),
                    "yLabel": str(item.YLabel),
                    "valueLabel": str(item.ValueLabel),
                    "points": [
                        simple_object(item.GetPoint(point))
                        for point in range(int(item.NumPoints))
                    ],
                }
            )
    for index in range(int(results.NumberOfRayData)):
        item = results.GetRayData(index)
        payload["rayData"].append(
            {
                "index": index,
                "description": str(item.Description),
                "rays": [
                    simple_object(item.GetRay(ray))
                    for ray in range(int(item.NumRays))
                ],
            }
        )
    try:
        spot = results.SpotData
        if spot is not None:
            fields = int(spot.NumberOfFields)
            wavelengths = int(spot.NumberOfWavelengths)
            payload["spotData"] = {
                "numberOfFields": fields,
                "numberOfWavelengths": wavelengths,
                "halfWidthX": finite(spot.HalfWidth_X),
                "halfWidthY": finite(spot.HalfWidth_Y),
                "maxRadius": finite(spot.MaxRadius),
                "meanRadius": finite(spot.MeanRadius),
                "samples": [
                    {
                        "fieldNumber": field + 1,
                        "wavelengthNumber": wavelength + 1,
                        "x": finite(spot.Get_X_For(field, wavelength)),
                        "y": finite(spot.Get_Y_For(field, wavelength)),
                        "z": finite(spot.Get_Z_For(field, wavelength)),
                        "rmsSpotSize": finite(
                            spot.GetRMSSpotSizeFor(field, wavelength)
                        ),
                        "geoSpotSize": finite(
                            spot.GetGeoSpotSizeFor(field, wavelength)
                        ),
                    }
                    for field in range(fields)
                    for wavelength in range(wavelengths)
                ],
            }
    except Exception:
        pass
    for key, property_name in (
        ("criticalRayData", "CriticalRayData"),
        ("pathAnalysisData", "PathAnalysisData"),
        ("nscSpotData", "NSCSpotData"),
        ("nscSingleRayTraceData", "NSCSingleRayTraceData"),
    ):
        try:
            item = getattr(results, property_name)
            if item is not None:
                payload[key] = simple_object(item)
        except Exception:
            pass
    return payload


def write_json(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def render_fallback(data_path: Path, text_path: Path, output_path: Path, title: str) -> bool:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import numpy as np

    payload = json.loads(data_path.read_text(encoding="utf-8"))
    grids = payload.get("dataGrids", [])
    rgb_grids = payload.get("dataGridsRgb", [])
    series = payload.get("dataSeries", [])
    scatter = payload.get("scatterPoints", [])
    if rgb_grids:
        grid = rgb_grids[0]
        image = np.array(
            [
                [
                    [pixel.get("R", 0), pixel.get("G", 0), pixel.get("B", 0)]
                    for pixel in row
                ]
                for row in grid["rgb"]
            ],
            dtype=float,
        )
        plt.figure(figsize=(10, 7))
        plt.imshow(image)
        plt.title(title)
        plt.axis("off")
    elif grids:
        grid = grids[0]
        plt.figure(figsize=(10, 7))
        plt.imshow(np.array(grid["values"], dtype=float), origin="lower", cmap="viridis")
        plt.colorbar(label=grid.get("valueLabel") or "")
        plt.title(title)
        plt.xlabel(grid.get("xLabel") or "")
        plt.ylabel(grid.get("yLabel") or "")
    elif series:
        plt.figure(figsize=(10, 7))
        for item in series:
            x_values = item["x"]
            y_values = item["y"]
            labels = item.get("seriesLabels", [])
            if y_values and len(y_values) == len(x_values):
                columns = len(y_values[0]) if y_values[0] else 0
                for column in range(columns):
                    label = labels[column] if column < len(labels) else None
                    plt.plot(x_values, [row[column] for row in y_values], label=label)
        plt.title(title)
        if any(item.get("seriesLabels") for item in series):
            plt.legend(fontsize=8)
        plt.grid(alpha=0.25)
    elif scatter:
        plt.figure(figsize=(10, 7))
        for item in scatter:
            points = item["points"]
            plt.scatter(
                [point.get("X", 0) for point in points],
                [point.get("Y", 0) for point in points],
                s=6,
            )
        plt.title(title)
        plt.axis("equal")
        plt.grid(alpha=0.25)
    elif text_path.is_file() and text_path.stat().st_size:
        text = text_path.read_text(encoding="utf-16", errors="replace")
        plt.figure(figsize=(12, 8))
        plt.axis("off")
        plt.title(title)
        plt.text(
            0.01,
            0.98,
            "\n".join(text.splitlines()[:60]),
            va="top",
            family="monospace",
            fontsize=7,
        )
    else:
        metadata = payload.get("metadata", {})
        header = payload.get("header", [])
        lines = [
            f"Analysis: {title}",
            f"Feature: {metadata.get('featureDescription', '')}",
            f"Lens: {metadata.get('lensFile', '')}",
            "",
            *header[:50],
            "",
            "ZOS-API returned no plottable numeric series or grid.",
        ]
        plt.figure(figsize=(12, 8))
        plt.axis("off")
        plt.title(title)
        plt.text(
            0.01,
            0.98,
            "\n".join(lines),
            va="top",
            family="monospace",
            fontsize=8,
        )
    plt.tight_layout()
    plt.savefig(output_path, dpi=160)
    plt.close()
    return True


def wait_for_analysis(analysis: Any, timeout_seconds: float) -> None:
    analysis.Apply()
    started = time.monotonic()
    while bool(analysis.IsRunning()):
        if time.monotonic() - started > timeout_seconds:
            analysis.Terminate()
            raise TimeoutError(
                f"analysis exceeded {timeout_seconds:g} seconds"
            )
        time.sleep(0.05)


def capture_data(
    zmx_path: Path,
    output_path: Path,
    zemax_directory: Path,
    timeout_seconds: float,
    include: set[str] | None,
    allow_existing: bool,
    retry_failed: bool,
) -> dict[str, Any]:
    import System

    ZOSAPI = load_zosapi(zemax_directory)
    if not allow_existing:
        ensure_no_existing_instance()
    connection = ZOSAPI.ZOSAPI_Connection()
    application = None
    manifest_path = output_path / "manifest.json"
    if retry_failed:
        if not manifest_path.is_file():
            raise FileNotFoundError(
                f"Cannot retry without an existing manifest: {manifest_path}"
            )
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    else:
        manifest = {
            "schemaVersion": 1,
            "createdUtc": utc_now(),
            "source": "Ansys Zemax OpticStudio 2026 R1",
            "systemFile": str(zmx_path),
            "sourceSha256": sha256(zmx_path),
            "analysisTimeoutSeconds": timeout_seconds,
            "analyses": [],
        }
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
        system.LoadFile(str(zmx_path), False)
        manifest["licenseStatus"] = str(application.LicenseStatus)
        manifest["applicationMode"] = str(application.Mode)
        manifest["surfaceCount"] = int(system.LDE.NumberOfSurfaces)
        manifest["fieldCount"] = int(system.SystemData.Fields.NumberOfFields)
        manifest["wavelengthCount"] = int(
            system.SystemData.Wavelengths.NumberOfWavelengths
        )
        names = list(System.Enum.GetNames(ZOSAPI.Analysis.AnalysisIDM))
        if include:
            names = [name for name in names if name in include]
        if retry_failed:
            work_items = [
                (entry["index"], entry["analysisId"], entry)
                for entry in manifest["analyses"]
                if entry["status"] != "captured"
                and (not include or entry["analysisId"] in include)
            ]
        else:
            work_items = [
                (index, name, None)
                for index, name in enumerate(names, start=1)
            ]
        analyses_path = output_path / "analyses"
        analyses_path.mkdir(parents=True, exist_ok=True)
        for position, (index, name, existing_entry) in enumerate(
            work_items,
            start=1,
        ):
            folder = analyses_path / f"{index:03d}-{slug(name)}"
            folder.mkdir(parents=True, exist_ok=True)
            entry: dict[str, Any] = existing_entry or {
                "index": index,
                "analysisId": name,
                "zplCode": ZPL_CODES.get(name),
                "directory": str(folder.relative_to(output_path)).replace("\\", "/"),
            }
            entry.update(
                {
                    "status": "started",
                    "startedUtc": utc_now(),
                    "error": None,
                    "exceptionType": None,
                }
            )
            if existing_entry is None:
                manifest["analyses"].append(entry)
            analysis = None
            try:
                analysis_id = getattr(ZOSAPI.Analysis.AnalysisIDM, name)
                analysis = system.Analyses.New_Analysis(analysis_id)
                if analysis is None:
                    raise RuntimeError("New_Analysis returned null")
                entry["title"] = str(analysis.Title)
                entry["analysisName"] = str(analysis.GetAnalysisName)
                settings = analysis.GetSettings()
                entry["hasAnalysisSpecificSettings"] = bool(
                    analysis.HasAnalysisSpecificSettings
                )
                try:
                    entry["settingsSaved"] = bool(
                        settings.SaveTo(str(folder / "settings.cfg"))
                    )
                except Exception as error:
                    entry["settingsSaved"] = False
                    entry["settingsError"] = str(error)
                wait_for_analysis(analysis, timeout_seconds)
                results = analysis.GetResults()
                if results is None:
                    raise RuntimeError("GetResults returned null")
                text_path = folder / "data.txt"
                try:
                    entry["textSaved"] = bool(results.GetTextFile(str(text_path)))
                except Exception as error:
                    entry["textSaved"] = False
                    entry["textError"] = str(error)
                data_path = folder / "data.json"
                write_json(data_path, serialize_results(results))
                entry["status"] = "captured"
                entry["dataJson"] = data_path.name
                if text_path.is_file():
                    entry["textFile"] = text_path.name
            except TimeoutError as error:
                entry["status"] = "timeout"
                entry["error"] = str(error)
            except Exception as error:
                entry["status"] = "not-applicable-or-failed"
                entry["error"] = str(error)
                entry["exceptionType"] = type(error).__name__
                traceback_text = traceback.format_exc()
                traceback_text = traceback_text.replace("\r\n", "\n").replace(
                    "\r",
                    "\n",
                )
                (folder / "error.txt").write_text(
                    traceback_text,
                    encoding="utf-8",
                )
            finally:
                entry["finishedUtc"] = utc_now()
                if analysis is not None:
                    try:
                        analysis.Close()
                    except Exception:
                        pass
                write_json(folder / "status.json", entry)
                write_json(output_path / "manifest.json", manifest)
                print(
                    f"[{position:03d}/{len(work_items):03d}] "
                    f"{name}: {entry['status']}"
                )
        return manifest
    finally:
        if application is not None:
            try:
                application.CloseApplication()
            except Exception:
                pass


def capture_zpl_screenshots(
    manifest: dict[str, Any],
    zmx_path: Path,
    output_path: Path,
    opticstudio_exe: Path,
    zpl_path: Path,
    timeout_seconds: float,
) -> None:
    for entry in manifest["analyses"]:
        if entry["status"] != "captured":
            continue
        folder = output_path / entry["directory"]
        existing_screenshot = (
            folder / entry["screenshot"]
            if entry.get("screenshot")
            else None
        )
        if existing_screenshot is not None and existing_screenshot.is_file():
            continue
        zpl_code = entry.get("zplCode")
        screenshot = folder / "screenshot.jpg"
        if zpl_code:
            command = [
                str(opticstudio_exe),
                f"-zpl={zpl_path}",
                f"-vLens={zmx_path}",
                f"-vOutput={folder}",
                f"-vCode={zpl_code}",
            ]
            try:
                completed = subprocess.run(
                    command,
                    cwd=str(output_path),
                    timeout=timeout_seconds,
                    check=False,
                    capture_output=True,
                    text=True,
                )
                entry["zplExitCode"] = completed.returncode
                entry["zplStdout"] = completed.stdout[-2000:]
                entry["zplStderr"] = completed.stderr[-2000:]
            except subprocess.TimeoutExpired:
                entry["screenshotStatus"] = "zpl-timeout"
            # OpticStudio normalizes the JPG suffix to uppercase on Windows.
            # Store a deterministic lowercase name so the baseline also works
            # on case-sensitive Git checkouts.
            opticstudio_screenshot = folder / "screenshot.JPG"
            if opticstudio_screenshot.is_file():
                if opticstudio_screenshot.name != screenshot.name:
                    temporary_screenshot = folder / "screenshot.rename"
                    opticstudio_screenshot.replace(temporary_screenshot)
                    temporary_screenshot.replace(screenshot)
            if screenshot.is_file() and screenshot.stat().st_size:
                entry["screenshotStatus"] = "captured-by-opticstudio-zpl"
                entry["screenshot"] = screenshot.name
        if entry.get("screenshotStatus") != "captured-by-opticstudio-zpl":
            try:
                fallback = folder / "screenshot.png"
                if render_fallback(
                    folder / "data.json",
                    folder / "data.txt",
                    fallback,
                    entry.get("title") or entry["analysisId"],
                ):
                    entry["screenshotStatus"] = "rendered-from-zosapi-data"
                    entry["screenshot"] = fallback.name
                else:
                    entry["screenshotStatus"] = "no-renderable-result"
            except Exception as error:
                entry["screenshotStatus"] = "render-failed"
                entry["screenshotError"] = str(error)
        write_json(folder / "status.json", entry)
        write_json(output_path / "manifest.json", manifest)
        print(
            f"[screenshot] {entry['analysisId']}: "
            f"{entry.get('screenshotStatus')}"
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zmx", type=Path, default=DEFAULT_ZMX_PATH)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_PATH)
    parser.add_argument(
        "--zemax-directory",
        type=Path,
        default=DEFAULT_ZEMAX_DIRECTORY,
    )
    parser.add_argument("--zpl", type=Path, default=DEFAULT_ZPL_PATH)
    parser.add_argument("--analysis-timeout", type=float, default=30.0)
    parser.add_argument("--screenshot-timeout", type=float, default=45.0)
    parser.add_argument("--data-only", action="store_true")
    parser.add_argument("--screenshots-only", action="store_true")
    parser.add_argument(
        "--retry-failed",
        action="store_true",
        help="Retry only non-captured entries in an existing manifest.",
    )
    parser.add_argument(
        "--allow-existing",
        action="store_true",
        help=(
            "Allow capture while an intentional interactive OpticStudio "
            "session remains open."
        ),
    )
    parser.add_argument(
        "--include",
        help="Comma-separated AnalysisIDM names for a targeted capture.",
    )
    return parser.parse_args()


def main() -> int:
    arguments = parse_args()
    zmx_path = arguments.zmx.resolve()
    output_path = arguments.output.resolve()
    include = (
        {item.strip() for item in arguments.include.split(",") if item.strip()}
        if arguments.include
        else None
    )
    if not zmx_path.is_file():
        raise FileNotFoundError(f"ZMX file not found: {zmx_path}")
    if not arguments.zpl.is_file():
        raise FileNotFoundError(f"ZPL capture macro not found: {arguments.zpl}")
    opticstudio_exe = arguments.zemax_directory / "OpticStudio.exe"
    if not opticstudio_exe.is_file():
        raise FileNotFoundError(f"OpticStudio.exe not found: {opticstudio_exe}")
    output_path.mkdir(parents=True, exist_ok=True)
    source_path = output_path / "source"
    source_path.mkdir(parents=True, exist_ok=True)
    shutil.copy2(zmx_path, source_path / zmx_path.name)
    if arguments.screenshots_only:
        manifest = json.loads(
            (output_path / "manifest.json").read_text(encoding="utf-8")
        )
    else:
        manifest = capture_data(
            zmx_path,
            output_path,
            arguments.zemax_directory,
            arguments.analysis_timeout,
            include,
            arguments.allow_existing,
            arguments.retry_failed,
        )
    if not arguments.data_only:
        if not arguments.allow_existing:
            ensure_no_existing_instance()
        capture_zpl_screenshots(
            manifest,
            zmx_path,
            output_path,
            opticstudio_exe,
            arguments.zpl.resolve(),
            arguments.screenshot_timeout,
        )
    manifest["completedUtc"] = utc_now()
    manifest["summary"] = {
        "total": len(manifest["analyses"]),
        "captured": sum(
            entry["status"] == "captured"
            for entry in manifest["analyses"]
        ),
        "notApplicableOrFailed": sum(
            entry["status"] == "not-applicable-or-failed"
            for entry in manifest["analyses"]
        ),
        "timeout": sum(
            entry["status"] == "timeout"
            for entry in manifest["analyses"]
        ),
        "screenshots": sum(
            bool(entry.get("screenshot"))
            for entry in manifest["analyses"]
        ),
        "opticStudioScreenshots": sum(
            entry.get("screenshotStatus") == "captured-by-opticstudio-zpl"
            for entry in manifest["analyses"]
        ),
    }
    write_json(output_path / "manifest.json", manifest)
    print(json.dumps(manifest["summary"], ensure_ascii=False))
    print(f"baseline={output_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
