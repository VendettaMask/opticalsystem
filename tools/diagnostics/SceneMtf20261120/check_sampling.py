"""Independent tool and unchanged engineering gates for the full-frame camera controls."""
import argparse
import importlib.util
import io
import json
from pathlib import Path
from zipfile import ZipFile

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repo", type=Path)
parser.add_argument("mapper", type=Path)
args = parser.parse_args()
repo = args.repo.resolve()
spec = importlib.util.spec_from_file_location("previous_protocol", repo / "tools/diagnostics/SceneMtf20261105/check_mapper.py")
protocol = importlib.util.module_from_spec(spec)
spec.loader.exec_module(protocol)
evidence = repo / "validation/rechecks/2026-11-20/scene-mtf-sampling"
scratch = repo / "artifacts/scene-reference-external"
entries = json.loads((evidence / "sampling.json").read_bytes())
receipt = {"protocol": "2026-11-05 control gates unchanged", "frequency_band_cpp": [0, .1],
    "tool_curve_limit": .04, "formal_curve_limit": .05, "mtf50_limit_cpp": .005,
    "convergence_curve_limit": .025, "convergence_mtf50_limit_cpp": .003,
    "mapper_sha256": protocol.sha(args.mapper.read_bytes()),
    "sampling_sha256": protocol.sha((evidence / "sampling.json").read_bytes()),
    "results": [], "convergence": []}
curves = {}
for entry in entries:
    report = entry["report"]
    archive_path = repo / entry["Archive"]
    assert protocol.sha(archive_path.read_bytes()) == entry["ArchiveSha256"]
    with ZipFile(archive_path) as archive:
        manifest_bytes = archive.read("manifest.json")
        assert protocol.sha(manifest_bytes) == report["SourceManifestSha256"]
        manifest = json.loads(manifest_bytes)
        paths = manifest["Outputs"][0]
        raw = archive.read(paths["IrradiancePath"])
        assert protocol.sha(raw) == report["Tasks"][0]["IrradianceSha256"]
        shape = (entry["Request"]["Height"], entry["Request"]["Width"])
        original = np.frombuffer(raw, dtype="<f8").reshape(shape)
        png_bytes = archive.read(paths["PngPath"])
        with Image.open(io.BytesIO(png_bytes)) as image:
            pixels = np.array(image)
            assert image.mode == "I;16" and not image.info and pixels.shape == shape
        scale = manifest["Png"]["CommonMaximumWattsPerSquareMeter"] / 65535
        quantization = float(np.max(np.abs(pixels * scale - original)))
        assert quantization <= .500000001 * scale
        for index, region in enumerate(report["Tasks"][0]["Regions"]):
            measured = region["Measurement"]
            roi = measured["Roi"]
            r = roi["Region"]
            x, y, w, h = (r[k] for k in ["X", "Y", "Width", "Height"])
            mapper = protocol.mapper_check(args.mapper.resolve(), pixels[y:y+h, x:x+w], roi["Orientation"], scratch)
            result = {"case": entry["Name"], "roi": roi, "native_status": measured["Status"],
                "native_failure": measured["Failure"], "reference_failure": region["ReferenceFailure"],
                "float_sha256": protocol.sha(raw), "png_sha256": protocol.sha(png_bytes),
                "max_quantization_error": quantization, "mapper": mapper}
            if measured["Edge"]:
                curve = protocol.samples(measured["Edge"]["Measurement"]["Curve"])
                curves[(entry["Name"], roi["Id"])] = curve
                result["mtf50"] = protocol.crossing(curve)
                if "curve" in mapper:
                    error = protocol.compare_mapper(curve, np.array(mapper["curve"]), mapper["mtf50_reported"])
                    centre = mapper["edge_centre_in_crop"]
                    geometry = measured["Geometry"]
                    location = float(np.hypot(x + centre[0] - geometry["Column"], y + centre[1] - geometry["Row"]))
                    result.update(tool_error=error, tool_centre_error_pixels=location,
                        tool_pass=location <= 1 and protocol.passes(error, .04, .005),
                        tool_nyquist_error=protocol.compare_mapper(curve, np.array(mapper["curve"]), mapper["mtf50_reported"], .5))
                if region["Reference"]:
                    reference = protocol.samples(region["Reference"]["WithPixelAperture"])
                    error = protocol.compare(curve, reference)
                    result.update(reference_mtf50=protocol.crossing(reference), formal_error=error,
                        formal_pass=protocol.passes(error, .05, .005),
                        formal_nyquist_error=protocol.compare(curve, reference, .5))
                    if entry["Dense"]:
                        dense = protocol.samples(entry["Dense"]["Tasks"][0]["Regions"][index]["Reference"]["WithPixelAperture"])
                        result["reference_refinement"] = protocol.compare(reference, dense, .5)
            receipt["results"].append(result)

pairs = [("area-4", "area-8"), ("area-8", "area-16"), ("area-16", "area-24"), ("area-24", "area-32"),
    ("pixel-centre", "area-8"), ("area-8", "pixel-4"), ("area-4", "pixel-4"), ("pixel-centre", "area-16"),
    ("area-24", "angle-7"), ("area-24", "phase-quarter"), ("area-24", "horizontal"),
    ("gaussian-24", "gaussian-32")]
for left, right in pairs:
    error = protocol.compare(curves[(left, "edge")], curves[(right, "edge")])
    receipt["convergence"].append({"left": left, "right": right, "error": error,
        "passed": protocol.passes(error, .025, .003)})
for roi in ["window-12", "window-20", "roi-short"]:
    error = protocol.compare(curves[("area-24", "edge")], curves[("area-24", roi)])
    receipt["convergence"].append({"left": "area-24/edge", "right": f"area-24/{roi}", "error": error,
        "passed": protocol.passes(error, .025, .003)})
(evidence / "cross-check.json").write_text(json.dumps(receipt, indent=2, allow_nan=False) + "\n", encoding="utf-8")
for row in receipt["results"]:
    print(row["case"], row["roi"]["Id"], "tool", row.get("tool_pass"), "formal", row.get("formal_pass"), row.get("formal_error"))
for row in receipt["convergence"]:
    print(row["left"], "->", row["right"], row["passed"], row["error"])
