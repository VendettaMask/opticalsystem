#!/usr/bin/env python3
"""Generate a reproducible Workbench-versus-Zemax accuracy report.

The script consumes a freshly captured Workbench manifest, a validated Zemax
baseline, and the preceding comparison only as the stable analysis/series
mapping. It never reuses preceding Workbench numeric results.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import statistics
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib import font_manager
from PIL import Image, ImageDraw, ImageFont


def configure_fonts() -> None:
    for candidate in (
        Path(r"C:\Windows\Fonts\msyh.ttc"),
        Path(r"C:\Windows\Fonts\simhei.ttf"),
    ):
        if candidate.exists():
            font_manager.fontManager.addfont(candidate)
            matplotlib.rcParams["font.family"] = font_manager.FontProperties(
                fname=candidate).get_name()
            matplotlib.rcParams["axes.unicode_minus"] = False
            return


configure_fonts()


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def slug(value: str) -> str:
    return "-".join(
        part for part in "".join(
            character.lower() if character.isascii() and character.isalnum() else "-"
            for character in value
        ).split("-") if part
    )


def finite_array(values: Iterable[Any]) -> np.ndarray:
    result = []
    for value in values:
        try:
            number = float(value)
        except (TypeError, ValueError):
            number = math.nan
        result.append(number if math.isfinite(number) else math.nan)
    return np.asarray(result, dtype=float)


def current_series(view: dict[str, Any]) -> list[dict[str, Any]]:
    panes = view.get("plotPanes") or []
    pane_series = [
        series for pane in panes for series in (pane.get("series") or [])
        if series.get("points")
    ]
    if pane_series:
        return pane_series
    return [
        series for series in (view.get("seriesList") or [])
        if series.get("points")
    ]


def zemax_series(data: dict[str, Any]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for group_index, group in enumerate(data.get("dataSeries") or []):
        x = finite_array(group.get("x") or [])
        rows = group.get("y") or []
        if not rows:
            continue
        first = rows[0]
        if isinstance(first, list):
            matrix = np.asarray(
                [[float(value) if not isinstance(value, str) else math.nan for value in row]
                 for row in rows],
                dtype=float,
            )
            if matrix.ndim != 2:
                continue
            labels = group.get("seriesLabels") or []
            for column in range(matrix.shape[1]):
                label = str(labels[column]) if column < len(labels) else "None"
                result.append({
                    "name": label,
                    "x": x,
                    "y": matrix[:, column],
                    "group": group_index,
                })
        else:
            result.append({
                "name": "None",
                "x": x,
                "y": finite_array(rows),
                "group": group_index,
            })
    return result


def select_named(
    candidates: list[dict[str, Any]],
    used: set[int],
    name: str | None,
) -> tuple[int, dict[str, Any]] | None:
    normalized = None if name in (None, "", "None") else name
    if normalized is not None:
        for index, candidate in enumerate(candidates):
            if index not in used and candidate.get("name", "") == normalized:
                used.add(index)
                return index, candidate
    for index, candidate in enumerate(candidates):
        if index not in used:
            used.add(index)
            return index, candidate
    return None


def interpolate_parameter(values: np.ndarray, count: int = 257) -> np.ndarray:
    values = np.asarray(values, dtype=float)
    valid = np.isfinite(values)
    if valid.sum() == 0:
        return np.full(count, math.nan)
    if valid.sum() == 1:
        return np.full(count, values[valid][0])
    source = np.linspace(0.0, 1.0, len(values))[valid]
    target = np.linspace(0.0, 1.0, count)
    return np.interp(target, source, values[valid])


def curve_value_scales(analysis: str, value_axis: str) -> tuple[float, float]:
    if analysis == "Ray Fan" and value_axis == "y":
        return 1000.0, 1.0
    if analysis == "Color Focus Shift" and value_axis == "x":
        return 1.0, 1000.0
    return 1.0, 1.0


def nrmse(current: np.ndarray, reference: np.ndarray) -> tuple[float, float, float]:
    mask = np.isfinite(current) & np.isfinite(reference)
    if not mask.any():
        return math.nan, math.nan, math.nan
    current = current[mask]
    reference = reference[mask]
    denominator = max(float(np.max(np.abs(reference))), 1e-15)
    error = current - reference
    value = float(np.sqrt(np.mean(error * error)) / denominator)
    maximum = float(np.max(np.abs(error)))
    if len(current) < 2 or np.std(current) == 0 or np.std(reference) == 0:
        correlation = math.nan
    else:
        correlation = float(np.corrcoef(current, reference)[0, 1])
    return value, maximum, correlation


def compare_curves(
    analysis: str,
    view: dict[str, Any],
    zemax: dict[str, Any],
    mapping: dict[str, Any],
) -> tuple[list[dict[str, Any]], list[tuple[str, np.ndarray, np.ndarray]]]:
    current_candidates = current_series(view)
    for candidate in current_candidates:
        candidate["name"] = candidate.get("name") or ""
    zemax_candidates = zemax_series(zemax)
    current_used: set[int] = set()
    zemax_used: set[int] = set()
    details: list[dict[str, Any]] = []
    plot_pairs: list[tuple[str, np.ndarray, np.ndarray]] = []
    for item in mapping.get("details") or []:
        selected_current = select_named(
            current_candidates, current_used, item.get("currentSeries"))
        selected_zemax = select_named(
            zemax_candidates, zemax_used, item.get("zemaxSeries"))
        if selected_current is None or selected_zemax is None:
            continue
        _, current = selected_current
        _, reference = selected_zemax
        points = current.get("points") or []
        axis = item.get("valueAxis", "y")
        current_value = finite_array(point.get(axis) for point in points)
        current_coordinate = finite_array(
            point.get("y" if axis == "x" else "x") for point in points)
        reference_value = np.asarray(reference["y"], dtype=float)
        reference_coordinate = np.asarray(reference["x"], dtype=float)
        if item.get("referenceReversed"):
            reference_value = reference_value[::-1]
            reference_coordinate = reference_coordinate[::-1]
        current_value = interpolate_parameter(current_value)
        reference_value = interpolate_parameter(reference_value)
        current_coordinate = interpolate_parameter(current_coordinate)
        reference_coordinate = interpolate_parameter(reference_coordinate)
        current_scale, reference_scale = curve_value_scales(analysis, axis)
        current_value = current_value * current_scale
        reference_value = reference_value * reference_scale
        value_error, maximum_error, correlation = nrmse(
            current_value, reference_value)
        coordinate_error, _, _ = nrmse(
            current_coordinate, reference_coordinate)
        details.append({
            "label": item.get("label", f"curve {len(details) + 1}"),
            "currentSeries": current.get("name", ""),
            "zemaxSeries": reference.get("name", "None"),
            "valueAxis": axis,
            "currentUnitScale": current_scale,
            "zemaxUnitScale": reference_scale,
            "valueNrmse": value_error,
            "coordinateNrmse": coordinate_error,
            "correlation": correlation,
            "maximumAbsoluteError": maximum_error,
            "referenceMaximumAbsolute": float(np.nanmax(np.abs(reference_value))),
            "referenceReversed": bool(item.get("referenceReversed")),
        })
        plot_pairs.append((details[-1]["label"], current_value, reference_value))
    return details, plot_pairs


def current_grids(view: dict[str, Any]) -> list[np.ndarray]:
    grids: list[np.ndarray] = []
    for series in current_series(view):
        points = series.get("points") or []
        if not points:
            continue
        values = []
        for point in points:
            value = point.get("value")
            if value is None and point.get("red") is not None:
                value = (
                    0.2126 * float(point.get("red") or 0)
                    + 0.7152 * float(point.get("green") or 0)
                    + 0.0722 * float(point.get("blue") or 0)
                )
            values.append(value)
        if not any(value is not None for value in values):
            continue
        xs = sorted({float(point["x"]) for point in points})
        ys = sorted({float(point["y"]) for point in points})
        xi = {value: index for index, value in enumerate(xs)}
        yi = {value: index for index, value in enumerate(ys)}
        grid = np.full((len(ys), len(xs)), math.nan)
        for point, value in zip(points, values):
            if value is not None:
                grid[yi[float(point["y"])], xi[float(point["x"])]] = float(value)
        grids.append(grid)
    return grids


def zemax_grids(data: dict[str, Any]) -> list[np.ndarray]:
    grids: list[np.ndarray] = []
    for item in data.get("dataGrids") or []:
        rows = []
        for row in item.get("values") or []:
            rows.append([
                float(value) if not isinstance(value, str) else math.nan
                for value in row
            ])
        if rows:
            grids.append(np.asarray(rows, dtype=float))
    return grids


def resize_grid(grid: np.ndarray, shape: tuple[int, int]) -> np.ndarray:
    if grid.shape == shape:
        return grid
    source_y = np.linspace(0.0, 1.0, grid.shape[0])
    source_x = np.linspace(0.0, 1.0, grid.shape[1])
    target_y = np.linspace(0.0, 1.0, shape[0])
    target_x = np.linspace(0.0, 1.0, shape[1])
    valid = np.isfinite(grid).astype(float)
    filled = np.nan_to_num(grid)
    horizontal = np.vstack([
        np.interp(target_x, source_x, row) for row in filled
    ])
    horizontal_mask = np.vstack([
        np.interp(target_x, source_x, row) for row in valid
    ])
    result = np.vstack([
        np.interp(target_y, source_y, horizontal[:, column])
        for column in range(horizontal.shape[1])
    ]).T
    mask = np.vstack([
        np.interp(target_y, source_y, horizontal_mask[:, column])
        for column in range(horizontal_mask.shape[1])
    ]).T
    result[mask < 0.5] = math.nan
    return result


def orientations(grid: np.ndarray) -> list[tuple[str, np.ndarray]]:
    return [
        ("identity", grid),
        ("flip-x", np.fliplr(grid)),
        ("flip-y", np.flipud(grid)),
        ("flip-xy", np.flipud(np.fliplr(grid))),
        ("transpose", grid.T),
        ("transpose-flip-x", np.fliplr(grid.T)),
        ("transpose-flip-y", np.flipud(grid.T)),
        ("transpose-flip-xy", np.flipud(np.fliplr(grid.T))),
    ]


def compare_grids(
    view: dict[str, Any],
    zemax: dict[str, Any],
    mapping: dict[str, Any],
) -> tuple[list[dict[str, Any]], list[tuple[str, np.ndarray, np.ndarray]]]:
    current_candidates = current_grids(view)
    reference_candidates = zemax_grids(zemax)
    details: list[dict[str, Any]] = []
    plots: list[tuple[str, np.ndarray, np.ndarray]] = []
    for index, item in enumerate(mapping.get("details") or []):
        if not current_candidates or not reference_candidates:
            break
        current = current_candidates[min(index, len(current_candidates) - 1)]
        reference = reference_candidates[min(index, len(reference_candidates) - 1)]
        best: tuple[float, float, str, np.ndarray, float] | None = None
        for orientation, oriented in orientations(current):
            aligned = resize_grid(oriented, reference.shape)
            current_values = aligned.copy()
            reference_values = reference.copy()
            if item.get("peakNormalized"):
                current_peak = np.nanmax(np.abs(current_values))
                reference_peak = np.nanmax(np.abs(reference_values))
                if current_peak > 0:
                    current_values /= current_peak
                if reference_peak > 0:
                    reference_values /= reference_peak
            value_error, _, correlation = nrmse(
                current_values.ravel(), reference_values.ravel())
            compared = int(np.sum(
                np.isfinite(current_values) & np.isfinite(reference_values)))
            candidate = (value_error, -compared, orientation, current_values, correlation)
            if best is None or candidate[:2] < best[:2]:
                best = candidate
        if best is None:
            continue
        value_error, negative_compared, orientation, aligned, correlation = best
        details.append({
            "label": item.get("label", f"grid {index + 1}"),
            "valueNrmse": value_error,
            "correlation": correlation,
            "orientation": orientation,
            "comparedPixels": -negative_compared,
            "peakNormalized": bool(item.get("peakNormalized")),
        })
        plots.append((details[-1]["label"], aligned, reference))
    return details, plots


def classification(details: list[dict[str, Any]]) -> tuple[str, float, float, float]:
    values = sorted(
        float(detail["valueNrmse"]) for detail in details
        if math.isfinite(float(detail.get("valueNrmse", math.nan)))
    )
    if not values:
        return "not-compared", math.nan, math.nan, math.nan
    median = statistics.median(values)
    percentile90 = float(np.percentile(values, 90))
    worst = max(values)
    if median <= 0.03 and percentile90 <= 0.10:
        label = "high-agreement"
    elif median <= 0.10 and percentile90 <= 0.25:
        label = "close"
    else:
        label = "different"
    return label, median, percentile90, worst


def render_numeric(
    path: Path,
    analysis: str,
    kind: str,
    plots: list[tuple[str, np.ndarray, np.ndarray]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if kind == "curves":
        figure, axes = plt.subplots(1, 2, figsize=(14, 5), dpi=130)
        for label, current, reference in plots[:12]:
            t = np.linspace(0.0, 1.0, len(current))
            axes[0].plot(t, current, linewidth=1, label=label)
            axes[1].plot(t, reference, linewidth=1, label=label)
        axes[0].set_title(f"Workbench current — {analysis}")
        axes[1].set_title("Zemax 2026 R1 captured baseline")
        for axis in axes:
            axis.grid(alpha=0.25)
        if len(plots) <= 8:
            axes[0].legend(fontsize=7)
    else:
        count = max(1, min(2, len(plots)))
        figure, axes = plt.subplots(count, 3, figsize=(14, 4.2 * count), dpi=130)
        axes = np.atleast_2d(axes)
        for row, (label, current, reference) in enumerate(plots[:count]):
            difference = current - reference
            for column, (title, image) in enumerate((
                (f"Workbench — {label}", current),
                ("Zemax baseline", reference),
                ("Difference", difference),
            )):
                axes[row, column].imshow(image, origin="lower", cmap="viridis")
                axes[row, column].set_title(title)
                axes[row, column].set_xticks([])
                axes[row, column].set_yticks([])
    figure.suptitle(f"{analysis}: numeric comparison", fontsize=14)
    figure.tight_layout()
    figure.savefig(path)
    plt.close(figure)


def render_current_page(path: Path, analysis: str, view: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    series = current_series(view)
    figure = plt.figure(figsize=(10, 6.5), dpi=120)
    axis = figure.add_subplot(111)
    axis.set_title(f"Workbench current — {analysis}")
    plotted = False
    heatmaps = current_grids(view)
    if heatmaps:
        axis.imshow(heatmaps[0], origin="lower", cmap="viridis", aspect="auto")
        axis.set_xticks([])
        axis.set_yticks([])
        plotted = True
    else:
        for item in series[:16]:
            points = item.get("points") or []
            x = finite_array(point.get("x") for point in points)
            y = finite_array(point.get("y") for point in points)
            if len(x) == 0:
                continue
            name = item.get("name") or ""
            kind = item.get("kind", "line")
            if kind in ("scatter", "bar"):
                axis.scatter(x, y, s=5, label=name)
            else:
                axis.plot(x, y, linewidth=0.9, label=name)
            plotted = True
        if plotted:
            axis.grid(alpha=0.25)
            if 0 < len(series) <= 8:
                axis.legend(fontsize=7)
    if not plotted:
        axis.axis("off")
        rows = view.get("rows") or []
        table = view.get("table")
        lines = [f"{row.get('metric', '')}: {row.get('value', '')}" for row in rows[:24]]
        if table:
            lines.extend(" | ".join(map(str, row)) for row in (table.get("rows") or [])[:16])
        if not lines:
            lines = [str(view.get("reportText") or "")[:4000]]
        axis.text(0.02, 0.98, "\n".join(lines), va="top", family="monospace", fontsize=8)
    figure.tight_layout()
    figure.savefig(path)
    plt.close(figure)


def fit_image(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.convert("RGB")
    copy.thumbnail(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, "white")
    canvas.paste(copy, ((size[0] - copy.width) // 2, (size[1] - copy.height) // 2))
    return canvas


def compose_screenshot(
    path: Path,
    current_path: Path,
    reference_path: Path | None,
    analysis: str,
    reference_label: str,
) -> None:
    current = fit_image(Image.open(current_path), (980, 650))
    if reference_path and reference_path.exists():
        reference_source = Image.open(reference_path)
        if reference_source.width >= 1700 and reference_source.height >= 650:
            reference_source = reference_source.crop(
                (reference_source.width // 2, 0, reference_source.width, reference_source.height))
        reference = fit_image(reference_source, (980, 650))
    else:
        reference = Image.new("RGB", (980, 650), "#f2f2f2")
        draw = ImageDraw.Draw(reference)
        draw.text((60, 290), "No directly mapped Zemax baseline page", fill="#555555")
    canvas = Image.new("RGB", (2000, 730), "white")
    canvas.paste(current, (10, 70))
    canvas.paste(reference, (1010, 70))
    draw = ImageDraw.Draw(canvas)
    draw.text((20, 15), f"{analysis} — Workbench current", fill="black")
    draw.text((1020, 15), reference_label, fill="black")
    draw.line((1000, 0, 1000, 730), fill="#888888", width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(path)


def format_percent(value: float) -> str:
    if not math.isfinite(value):
        return "—"
    percentage = value * 100.0
    if percentage >= 1000:
        return f"{percentage:,.0f}%"
    return f"{percentage:.2f}%"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("previous_comparison", type=Path)
    arguments = parser.parse_args()
    baseline = arguments.baseline.resolve()
    output = arguments.output.resolve()
    previous = arguments.previous_comparison.resolve()
    output.mkdir(parents=True, exist_ok=True)

    manifest = read_json(output / "current-manifest.json")
    baseline_manifest = read_json(baseline / "manifest.json")
    previous_data = read_json(previous / "comparison.json")
    baseline_entries = {
        item["analysisId"]: item for item in baseline_manifest["analyses"]
    }
    current_entries = {
        item["name"]: item for item in manifest["analyses"]
    }

    numeric_results: list[dict[str, Any]] = []
    numeric_by_name: dict[str, dict[str, Any]] = {}
    for mapping in previous_data.get("numericResults") or []:
        analysis = mapping["analysis"]
        entry = current_entries.get(analysis)
        baseline_entry = baseline_entries.get(mapping["zemaxAnalysis"])
        if not entry or not entry.get("output") or not baseline_entry:
            continue
        view = read_json(output / entry["output"])
        zemax = read_json(
            baseline / baseline_entry["directory"] / baseline_entry["dataJson"])
        if mapping["kind"] == "grids":
            details, plots = compare_grids(view, zemax, mapping)
        else:
            details, plots = compare_curves(analysis, view, zemax, mapping)
        label, median, percentile90, worst = classification(details)
        result = {
            "analysis": analysis,
            "zemaxAnalysis": mapping["zemaxAnalysis"],
            "kind": mapping["kind"],
            "classification": label,
            "medianValueNrmse": median,
            "percentile90ValueNrmse": percentile90,
            "worstValueNrmse": worst,
            "comparedSeries": len(details),
            "details": details,
        }
        numeric_results.append(result)
        numeric_by_name[analysis] = result
        render_numeric(
            output / "images" / "numeric" / f"{slug(analysis)}.png",
            analysis,
            mapping["kind"],
            plots,
        )

    screenshot_rows = []
    structural_names = set(previous_data.get("structuralOnly") or [])
    for analysis, entry in current_entries.items():
        if not entry.get("output"):
            continue
        view = read_json(output / entry["output"])
        current_image = output / "images" / "current" / f"{slug(analysis)}.png"
        render_current_page(current_image, analysis, view)
        reference_path: Path | None = None
        reference_label = "Zemax 2026 R1 captured baseline"
        if analysis in numeric_by_name:
            baseline_entry = baseline_entries[numeric_by_name[analysis]["zemaxAnalysis"]]
            screenshot = baseline_entry.get("screenshot")
            if screenshot:
                reference_path = baseline / baseline_entry["directory"] / screenshot
        elif analysis in structural_names:
            reference_path = (
                previous / "images" / "structural" / f"{slug(analysis)}.png")
        else:
            reference_label = "No directly mapped Zemax baseline page"
        comparison_image = (
            output / "images" / "screenshots" / f"{slug(analysis)}.png")
        compose_screenshot(
            comparison_image, current_image, reference_path, analysis, reference_label)
        screenshot_rows.append({
            "analysis": analysis,
            "image": str(comparison_image.relative_to(output)).replace("\\", "/"),
            "zemaxReference": str(reference_path.relative_to(baseline)).replace("\\", "/")
            if reference_path and reference_path.is_relative_to(baseline) else None,
            "numericCompared": analysis in numeric_by_name,
        })

    summary = {
        "analysesCompared": len(numeric_results),
        "highAgreement": sum(
            result["classification"] == "high-agreement" for result in numeric_results),
        "close": sum(result["classification"] == "close" for result in numeric_results),
        "different": sum(
            result["classification"] == "different" for result in numeric_results),
        "notCompared": sum(
            result["classification"] == "not-compared" for result in numeric_results),
    }
    source_hash = hashlib.sha256(
        (baseline / "source" / "123456.ZMX").read_bytes()).hexdigest()
    current_huygens_ms = current_entries["Huygens Through Focus MTF"].get(
        "elapsedMilliseconds", 0)
    previous_retry_path = previous / "huygens-retry-manifest.json"
    previous_huygens_ms = 0
    if previous_retry_path.exists():
        previous_retry = read_json(previous_retry_path)
        previous_huygens_ms = previous_retry["analyses"][0].get(
            "elapsedMilliseconds", 0)
    huygens_ratio = (
        current_huygens_ms / previous_huygens_ms
        if previous_huygens_ms > 0 else math.nan
    )
    comparison = {
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "sourceSha256": source_hash,
        "currentRun": {
            "total": len(manifest["analyses"]),
            "captured": sum(item["status"] == "captured" for item in manifest["analyses"]),
            "failed": sum(item["status"] != "captured" for item in manifest["analyses"]),
            "elapsedSeconds": sum(
                item.get("elapsedMilliseconds", 0) for item in manifest["analyses"]) / 1000.0,
        },
        "structure": {
            "current": {
                "surfaces": manifest["surfaceCount"],
                "fields": manifest["fieldCount"],
                "wavelengths": manifest["wavelengthCount"],
            },
            "zemax": {
                "surfaces": 23,
                "fields": 5,
                "wavelengths": 3,
            },
        },
        "numericSummary": summary,
        "performance": {
            "huygensThroughFocusMtfMilliseconds": current_huygens_ms,
            "previousHuygensThroughFocusMtfMilliseconds": previous_huygens_ms,
            "ratio": huygens_ratio,
        },
        "numericResults": numeric_results,
        "screenshots": screenshot_rows,
        "method": {
            "curves": (
                "Current Workbench values and the captured Zemax data are paired by the "
                "tracked physical-series mapping and resampled to 257 normalized scan points."),
            "grids": (
                "Current and captured grids are resampled to one shape; eight axis "
                "orientations are checked and the selected orientation is recorded."),
            "thresholds": {
                "highAgreement": "median <= 3% and P90 <= 10%",
                "close": "median <= 10% and P90 <= 25%",
            },
        },
    }
    (output / "comparison.json").write_text(
        json.dumps(comparison, ensure_ascii=False, indent=2, allow_nan=True),
        encoding="utf-8",
    )

    sorted_results = sorted(
        numeric_results,
        key=lambda result: (
            -result["medianValueNrmse"]
            if math.isfinite(result["medianValueNrmse"]) else math.inf,
            result["analysis"],
        ),
    )
    table_lines = [
        "| Workbench | Zemax | 结论 | 中位 NRMSE | P90 | 最差 | 数值图 |",
        "|---|---|---|---:|---:|---:|---|",
    ]
    labels = {
        "high-agreement": "高度一致",
        "close": "接近",
        "different": "明显差异",
        "not-compared": "未完成",
    }
    for result in sorted_results:
        image = f"images/numeric/{slug(result['analysis'])}.png"
        table_lines.append(
            f"| {result['analysis']} | {result['zemaxAnalysis']} | "
            f"{labels[result['classification']]} | "
            f"{format_percent(result['medianValueNrmse'])} | "
            f"{format_percent(result['percentile90ValueNrmse'])} | "
            f"{format_percent(result['worstValueNrmse'])} | [图]({image}) |"
        )
    report = f"""# 123456.ZMX：Workbench 与 Zemax 2026 R1 当前版全面对比

## 覆盖

- 当前 Workbench 页面：{comparison['currentRun']['captured']}/{comparison['currentRun']['total']} 成功。
- 页面截图：{len(screenshot_rows)} 张，每张均为当前 Workbench 结构化页面重绘；可映射项右侧使用已验证的 Zemax 2026 R1 截图。
- 数值对齐：{summary['analysesCompared']} 项；高度一致 {summary['highAgreement']}，接近 {summary['close']}，明显差异 {summary['different']}，未完成 {summary['notCompared']}。
- 镜头结构：23 个表面、5 个视场、3 个波长；源文件 SHA-256 `{source_hash}`。
- Huygens Through Focus MTF：本次 {current_huygens_ms / 1000.0:.2f} 秒，旧版 {previous_huygens_ms / 1000.0:.2f} 秒，耗时比 {huygens_ratio:.2f}×。

## 全部数值结果

{chr(10).join(table_lines)}

## 页面截图

[打开 HTML 截图库](COMPARISON_REPORT.html)

## 方法和边界

- Zemax 一侧来自仓库中已校验的 2026 R1 捕获基线，本次没有重新启动 OpticStudio。
- Workbench 一侧全部由本次当前代码重新计算，旧报告仅提供分析名称和物理系列映射，不复用旧 Workbench 数值。
- 曲线以 257 个归一化扫描位置重采样；二维网格统一尺寸并记录采用的坐标方向。
- “高度一致”为中位 NRMSE ≤ 3% 且 P90 ≤ 10%；“接近”为中位 ≤ 10% 且 P90 ≤ 25%。
- 页面截图用于显示内容、曲线、表格和 Zemax 参考的人工复核，不做 UI 像素相似度判定。
"""
    (output / "COMPARISON_REPORT.md").write_text(report, encoding="utf-8")

    cards = "".join(
        f"<article><h2>{html.escape(row['analysis'])}</h2>"
        f"<a href='{html.escape(row['image'])}'><img loading='lazy' "
        f"src='{html.escape(row['image'])}'></a></article>"
        for row in screenshot_rows
    )
    html_report = f"""<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>Workbench 与 Zemax 全面对比</title>
<style>
body{{font-family:Segoe UI,Microsoft YaHei,sans-serif;margin:24px;background:#f4f5f7;color:#202124}}
.summary{{background:white;padding:18px;border-radius:8px;margin-bottom:18px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(560px,1fr));gap:16px}}
article{{background:white;padding:12px;border-radius:8px;box-shadow:0 1px 4px #bbb}}
h2{{font-size:16px;margin:4px 0 10px}} img{{width:100%;height:auto}}
</style></head><body>
<section class="summary"><h1>123456.ZMX 当前 Workbench / Zemax 2026 R1</h1>
<p>页面 {comparison['currentRun']['captured']}/{comparison['currentRun']['total']}；
数值 {summary['analysesCompared']} 项；截图 {len(screenshot_rows)} 张。</p>
<p><a href="COMPARISON_REPORT.md">数值表与方法</a> ·
<a href="comparison.json">机器可读结果</a></p></section>
<section class="grid">{cards}</section></body></html>"""
    (output / "COMPARISON_REPORT.html").write_text(html_report, encoding="utf-8")
    (output / "README.md").write_text(
        "# Current Workbench / Zemax comparison\n\n"
        "Open `COMPARISON_REPORT.md` for numeric results and "
        "`COMPARISON_REPORT.html` for all page screenshots.\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False))
    print(f"screenshots={len(screenshot_rows)} output={output}")
    return 0 if comparison["currentRun"]["failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
