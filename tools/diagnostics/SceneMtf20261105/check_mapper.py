"""Run official MTF Mapper against the exact 16-bit crop of each frozen linear ROI."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
from tempfile import TemporaryDirectory
from zipfile import ZipFile

import numpy as np
from PIL import Image

BAND = 0.10
TOOL_CURVE_LIMIT = 0.04
FORMAL_CURVE_LIMIT = 0.05
MTF50_LIMIT = 0.005
CONVERGENCE_CURVE_LIMIT = 0.025
CONVERGENCE_MTF50_LIMIT = 0.003


def sha(data):
    return hashlib.sha256(data).hexdigest()


def samples(curve):
    return np.array([(p["CyclesPerPixel"], p["Response"]) for p in curve])


def crossing(curve, level=0.5):
    for (f0, y0), (f1, y1) in zip(curve[:-1], curve[1:]):
        if y0 >= level > y1:
            return float(f0 + (f1 - f0) * (y0 - level) / (y0 - y1))
    return None


def compare(left, right, band=BAND):
    # Equal frequency grid for every comparison, including the upper endpoint.
    frequencies = np.linspace(0, band, round(band * 1000) + 1)
    diff = np.interp(frequencies, *left.T) - np.interp(frequencies, *right.T)
    c1, c2 = crossing(left), crossing(right)
    return errors(diff, c1, c2)


def errors(diff, left50, right50):
    return {
        "max_abs": float(np.max(np.abs(diff))),
        "rms": float(np.sqrt(np.mean(diff**2))),
        "mtf50_abs_cycles_per_pixel": None if left50 is None or right50 is None else abs(left50 - right50),
    }


def compare_mapper(native, external, reported_mtf50, band=BAND):
    # Compare on the actual tool frequency grid. Interpolation across a sharp null
    # on the coarser external grid is a separate diagnostic.
    external = external[external[:, 0] <= band]
    diff = np.interp(external[:, 0], *native.T) - external[:, 1]
    return errors(diff, crossing(native), reported_mtf50)


def passes(error, curve_limit, crossing_limit):
    mtf50 = error["mtf50_abs_cycles_per_pixel"]
    return error["max_abs"] <= curve_limit and mtf50 is not None and mtf50 <= crossing_limit


def mapper_check(executable, pixels, orientation, scratch):
    with TemporaryDirectory(prefix="scene-mtf-", dir=scratch) as temp:
        folder = Path(temp)
        crop = folder / "roi.png"
        Image.fromarray(pixels.astype(np.uint16)).save(crop)
        run = subprocess.run(
            [str(executable), "--single-roi", "--linear", "--esf-model", "loess", "--sfr", "--edges", str(crop), temp],
            capture_output=True, text=True, timeout=60, check=False,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        result = {"exit_code": run.returncode, "crop_png_sha256": sha(crop.read_bytes()),
                  "stdout": run.stdout, "stderr": run.stderr}
        if run.returncode or "16-bit linear grayscale" not in run.stdout:
            return result | {"failure": "MTF Mapper did not confirm 16-bit linear processing"}
        output = folder / "edge_sfr_values.txt"
        mtf_file = folder / "edge_mtf_values.txt"
        if not output.exists() or not mtf_file.exists():
            return result | {"failure": "MTF Mapper produced no edge files"}
        lines = [list(map(float, line.split())) for line in output.read_text().splitlines() if line.strip()]
        mtf_rows = [list(map(float, line.split())) for line in mtf_file.read_text().splitlines() if line.strip()]
        valid = [(row, mtf) for row in lines for mtf in mtf_rows
                 if len(row) >= 38 and row[:3] == mtf[:3] and row[5] > 0 and 0 < mtf[3] < 0.5]
        if len(valid) != 1 or not re.search(r"total=1\)", run.stdout):
            return result | {"failure": "Expected exactly one valid reported edge"}
        row, mtf = valid[0]
        # MTF Mapper samples at 1/64 c/p. Retain the 33 samples through sensor Nyquist.
        curve = np.column_stack((np.arange(33) / 64.0, np.asarray(row[5:38]) / row[5]))
        raw_crossing = crossing(curve)
        if not np.isfinite(curve).all() or raw_crossing is None or abs(raw_crossing - mtf[3]) > .003:
            return result | {"failure": "Raw curve grid does not agree with tool-reported MTF50"}
        return result | {"edge_centre_in_crop": row[1:3], "edge_angle_deg": row[3],
                         "declared_orientation": orientation, "mtf50_reported": mtf[3],
                         "curve": curve.tolist()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", type=Path)
    parser.add_argument("mapper", type=Path)
    args = parser.parse_args()
    repo, executable = args.repo.resolve(), args.mapper.resolve(strict=True)
    evidence = repo / "validation/rechecks/2026-11-05/scene-mtf"
    scratch = repo / "artifacts/scene-reference-external"
    scratch.mkdir(parents=True, exist_ok=True)
    formal = evidence / "formal-mtf.json"
    entries = json.loads(formal.read_bytes())
    receipt = {
        "tool": subprocess.check_output([str(executable), "--version"], text=True).strip().split("version:")[-1].strip(),
        "executable_sha256": sha(executable.read_bytes()), "numpy": np.__version__, "formal_mtf_sha256": sha(formal.read_bytes()),
        "flags": "--single-roi --linear --esf-model loess --sfr --edges",
        "gates": {"frequency_band_cpp": [0, BAND], "diagnostic_band_cpp": [0, 0.5],
                  "tool_max_abs": TOOL_CURVE_LIMIT, "formal_max_abs": FORMAL_CURVE_LIMIT, "mtf50_abs_cpp": MTF50_LIMIT,
                  "convergence_max_abs": CONVERGENCE_CURVE_LIMIT, "convergence_mtf50_abs_cpp": CONVERGENCE_MTF50_LIMIT},
        "results": [], "convergence": [],
    }
    native = {}
    for entry in entries:
        archive_path = repo / entry["Archive"]
        assert sha(archive_path.read_bytes()) == entry["ArchiveSha256"]
        with ZipFile(archive_path) as archive:
            manifest_bytes = archive.read("manifest.json")
            assert sha(manifest_bytes) == entry["SourceManifestSha256"]
            manifest = json.loads(manifest_bytes)
            for task in entry["Tasks"]:
                paths = manifest["Outputs"][task["TaskIndex"]]
                raw = archive.read(paths["IrradiancePath"])
                assert sha(raw) == task["IrradianceSha256"]
                shape = (entry["Height"], entry["Width"])
                linear = np.frombuffer(raw, dtype="<f8").reshape(shape)
                png_bytes = archive.read(paths["PngPath"])
                with Image.open(io.BytesIO(png_bytes)) as png:
                    pixels = np.array(png)
                    assert pixels.shape == shape and png.mode == "I;16" and not png.info
                scale = manifest["Png"]["CommonMaximumWattsPerSquareMeter"] / 65535
                quant_error = float(np.max(np.abs(pixels * scale - linear)))
                assert quant_error <= 0.500000001 * scale
                cache = {}
                for region in task["Regions"]:
                    roi = region["Roi"]
                    r = roi["Region"]
                    key = (r["X"], r["Y"], r["Width"], r["Height"])
                    x, y, w, h = key
                    if key not in cache:
                        cache[key] = mapper_check(executable, pixels[y:y+h, x:x+w], roi["Orientation"], scratch)
                    mapper = cache[key]
                    result = {"scene": entry["Name"], "task": task["TaskIndex"], "roi": roi, "native_status": region["Status"],
                              "native_failure": region["Failure"], "reference_failure": region["ReferenceFailure"],
                              "source_png_sha256": sha(png_bytes), "float_sha256": sha(raw), "max_quantization_error": quant_error,
                              "mapper": mapper, "mtf50_native": region["Mtf50"]}
                    if region["Curve"]:
                        c = samples(region["Curve"])
                        native[(entry["Name"], roi["Id"])] = c
                        if "curve" in mapper:
                            mc = np.asarray(mapper["curve"])
                            geometry = region["Geometry"]
                            cx, cy = mapper["edge_centre_in_crop"]
                            location_error = float(np.hypot(x + cx - geometry["Column"], y + cy - geometry["Row"]))
                            tool50 = mapper["mtf50_reported"]
                            error = compare_mapper(c, mc, tool50)
                            result.update(tool_error=error, tool_nyquist_error=compare_mapper(c, mc, tool50, 0.5),
                                          tool_interpolated_band_error=compare(c, mc),
                                          tool_centre_error_pixels=location_error,
                                          tool_band_pass=location_error <= 1 and passes(error, TOOL_CURVE_LIMIT, MTF50_LIMIT))
                        if region["Reference"]:
                            optical = samples(region["Reference"]["Optical"])
                            pixel = samples(region["Reference"]["WithPixelAperture"])
                            error = compare(c, pixel)
                            result.update(mtf50_optical=crossing(optical), mtf50_pixel=crossing(pixel),
                                          optical_error=compare(c, optical), pixel_error=error, pixel_nyquist_error=compare(c, pixel, 0.5),
                                          pixel_band_pass=passes(error, FORMAL_CURVE_LIMIT, MTF50_LIMIT))
                            if region["DenseReference"]:
                                dense = samples(region["DenseReference"]["WithPixelAperture"])
                                result["reference_density_error"] = compare(pixel, dense, 0.5)
                    receipt["results"].append(result)

    pairs = [
        ("pupil-low", "edge", "baseline", "edge"),
        ("baseline", "edge", "pupil-high", "edge"),
        ("pupil-high", "edge", "pupil-16", "edge"),
        ("pupil-16", "edge", "pupil-24", "edge"),
        ("pupil-24", "edge", "pupil-32", "edge"),
        ("pupil-16", "edge", "pupil-azimuth-32", "edge"),
        ("pixel-centre", "edge", "baseline", "edge"),
        ("baseline", "edge", "pixel-high", "edge"),
        ("baseline", "edge", "joint", "edge"),
        ("pupil-high", "edge", "joint", "edge"),
        ("pixel-high", "edge", "joint", "edge"),
        ("joint", "edge", "joint-phase", "edge"),
        ("joint", "edge", "joint-angle", "edge"),
    ] + [("joint", "edge", "joint", v) for v in ["window-12", "window-20", "roi-short", "roi-narrow"]]
    for ls, lr, rs, rr in pairs:
        left, right = native.get((ls, lr)), native.get((rs, rr))
        result = {"left": [ls, lr], "right": [rs, rr]}
        if left is None or right is None:
            result.update(passed=False, failure="At least one ROI failed the native measurement quality gate")
        else:
            error = compare(left, right)
            result.update(band_error=error, nyquist_error=compare(left, right, 0.5),
                          passed=passes(error, CONVERGENCE_CURVE_LIMIT, CONVERGENCE_MTF50_LIMIT))
        receipt["convergence"].append(result)
    bin_file = evidence / "bin-sensitivity.json"
    profiles = json.loads(bin_file.read_bytes())
    reference_bins = samples(next(r["Curve"] for r in profiles if r["Density"] == 8))
    assert np.array_equal(reference_bins, native[("joint", "edge")])
    receipt["bin_sensitivity"] = []
    for row in profiles:
        if row["Density"] == 8:
            continue
        if "Failure" in row:
            result = {"passed": False, "failure": row["Failure"]}
        else:
            curve = samples(row["Curve"])
            error = compare(reference_bins, curve)
            result = {"band_error": error, "nyquist_error": compare(reference_bins, curve, 0.5),
                      "passed": passes(error, CONVERGENCE_CURVE_LIMIT, CONVERGENCE_MTF50_LIMIT)}
        receipt["bin_sensitivity"].append({"bins_per_pixel": row["Density"]} | result)
    (evidence / "cross-check.json").write_text(json.dumps(receipt, indent=2, allow_nan=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
