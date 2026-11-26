"""Convert the saved OpticStudio sample windows to native workspace metadata."""
import argparse
import hashlib
import json
import re
import shutil
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

from sequential_example_coatings import ExampleCoatings

ASSETS = Path(__file__).resolve().parents[1] / "src/OpticalSystemDesign.App/Assets/SequentialExamples"
EDITORS = {
    "Lens Data": "lens-editor", "Merit Function Editor": "optimization",
    "Multi-Configuration Editor": "multi-configuration", "Tolerance Data Editor": "tolerancing",
}
# Numeric identities are independent of translated captions, transient progress and copy numbers.
WINDOWS = {
    302: "viewer-2d", 303: "viewer-3d", 307: "solid-model", 308: "optical-drawing",
    320: "Ray Fan", 321: "Optical Path Difference", 330: "Spot Diagram",
    332: "Full Field Spot Diagram", 333: "Matrix Spot Diagram", 334: "Configuration Matrix Spot Diagram",
    340: "MTF", 342: "FFT Surface MTF", 345: "Huygens MTF", 347: "Huygens Surface MTF",
    360: "PSF", 361: "FFT PSF Cross Section", 362: "FFT Line Edge Spread", 363: "Huygens PSF",
    370: "Wavefront Map", 371: "Interferogram", 372: "Foucault Analysis", 390: "RMS vs Field",
    392: "RMS vs Wavelength", 400: "Diffraction Encircled Energy", 401: "Encircled Energy",
    402: "Geometric Line Edge Spread", 410: "Relative Illumination", 411: "Vignetting Diagram",
    420: "Geometric Image Analysis", 421: "Geometric Bitmap Image Analysis", 425: "Image Simulation",
    440: "Field Curvature and Distortion", 441: "Grid Distortion", 442: "Footprint Diagram",
    443: "Axial Aberration", 444: "Lateral Color", 446: "Color Focus Shift",
    450: "Seidel Coefficients", 452: "Zernike Standard", 464: "Cardinal Points Data",
    472: "material-internal-transmission", 481: "Polarization Pupil Map",
    500: "Reflection vs. Angle", 501: "Transmission vs. Angle",
    504: "Phase vs. Angle", 507: "Transmission vs. Wavelength",
    482: "Polarization Transmission", 483: "Polarization Phase Aberration", 1122: "Prescription Report",
}


def fields(data):
    pos = 0
    while pos < len(data):
        tag, _, size = struct.unpack_from("<III", data, pos)
        pos += 12
        if size > len(data) - pos:
            raise ValueError("Truncated window record")
        yield tag, data[pos:pos + size]
        pos += size


def image_settings(blob):
    version, kind = struct.unpack_from("<ii", blob)
    if (version, kind) != (2001, 425):
        raise ValueError("Unrecognized image simulation settings")
    params = struct.unpack_from("<28i", blob)
    pitch, height = struct.unpack_from("<dd", blob, 112)
    source = blob[144:400].decode("utf-16le").split("\0")[0]
    settings = {"PsfGridColumns": str(params[8]), "PsfGridRows": str(params[9]),
                "DetectorXPixels": str(params[6]), "DetectorYPixels": str(params[7]),
                "PixelSize": str(pitch), "FieldHeight": str(height), "FieldNumber": str(params[10]),
                "UsePolarization": str(bool(params[17])).lower(),
                "SourceRotation": ("无", "90°", "180°", "270°")[params[13]],
                "AberrationMode": {0: "无", 1: "几何的", 2: "衍射"}[params[21]],
                "DisplayAs": {0: "仿真图", 1: "源位图", 2: "PSF 网格"}[params[18]]}
    if source:
        settings.update(SourceMode="外部位图", SourceFile=source)
    return settings


def read_workspace(path, images, used_images, lens, coatings):
    records = {}
    layout = None
    for tag, payload in fields(path.read_bytes()):
        if tag == 86 and payload.strip(b"\0"):
            layout = ET.fromstring(payload.decode("utf-16le").rstrip("\0"))
        if tag == 15:
            f = dict(fields(payload))
            guid = f[27].decode("utf-16le").rstrip("\0")
            kind = int.from_bytes(f[16], "little")
            window = {"kind": WINDOWS.get(kind, "unavailable"), "id": guid}
            if kind == 425:
                window["settings"] = image_settings(f[22])
            elif kind in (500, 501, 504, 507):
                header = struct.unpack_from("<8i5d", f[22])
                if header[0] != 2000 or header[1] != kind or header[7] != kind:
                    raise ValueError(f"Unknown coating settings in {path}")
                surface, minimum, maximum, angle = header[5], header[8], header[9], header[12]
                settings = {"SurfaceIndex": str(surface)}
                band = None
                if kind == 507:
                    band = (minimum*1000, maximum*1000)
                    settings.update(MinimumWavelengthNm=str(band[0]), MaximumWavelengthNm=str(band[1]), IncidenceAngle=str(angle))
                else:
                    settings.update(MinimumAngle=str(minimum), MaximumAngle=str(maximum))
                if design := coatings.save(lens, surface, band):
                    settings["CoatingFile"] = design
                window["settings"] = settings
            elif kind == 308:
                window["settings"] = {"FrontSurface": str(struct.unpack_from("<i", f[22], 20)[0])}
            elif kind in (420, 421):
                offset = 112 if kind == 420 else 92
                source = f[22][offset:offset + 256].decode("utf-16le").split("\0")[0]
                if source:
                    window["settings"] = {"SourceFile": source}
            settings = window.get("settings", {})
            if name := settings.get("SourceFile"):
                image = images[name.casefold()]
                settings["SourceFile"] = image.name
                if kind == 425:
                    if image.suffix.lower() == ".bmp":
                        width, height = struct.unpack_from("<ii", image.read_bytes(), 18)
                        height = abs(height)
                    else:
                        width = height = int(image.read_bytes().splitlines()[0])
                    settings.update(ImageWidth=str(width), ImageHeight=str(height))
                    if settings["DisplayAs"] != "PSF 网格":
                        # Interactive convolution and detector raster limits, at native field extent.
                        dx, dy = int(settings["DetectorXPixels"]), int(settings["DetectorYPixels"])
                        pixel_limit = 262144 if settings["DisplayAs"] == "源位图" else 19200
                        factor = next(n for n in (1, 2, 4, 8)
                                      if ((width+n-1)//n) * ((height+n-1)//n) <= pixel_limit
                                      and ((dx+n-1)//n) * ((dy+n-1)//n) <= 262144)
                        if factor > 1:
                            settings["PreviewReduction"] = str(factor)
                used_images.add(image)
            records[guid] = window
    if layout is None:
        # Some files retain the ordered window records without a docking tree.
        if any(w["kind"] == "unavailable" for w in records.values()):
            raise ValueError(f"Unidentified window without a saved caption in {path}")
        return {"windows": [{"kind": "lens-editor"}, *records.values()]}

    seen = set()

    def node(element, parent_orientation=None):
        if element.tag == "LayoutDocumentPane":
            windows = []
            selected = 0
            for doc in element.findall("LayoutDocument"):
                key = doc.attrib["WindowId"]
                if key in EDITORS:
                    window = {"kind": EDITORS[key]}
                else:
                    window = records[key].copy()
                    seen.add(key)
                    title = re.sub(r"\s+- (?:Working|Rendering).*", "", doc.attrib["Title"])
                    if window["kind"] == "unavailable":
                        window["title"] = re.sub(r"^\d+:\s*", "", title)
                    elif suffix := re.search(r"\s(\d+)$", title):
                        window["suffix"] = suffix.group(1)
                if doc.get("IsSelected") == "True":
                    selected = len(windows)
                if doc.get("IsLastFocusedDocument") == "True":
                    window["focused"] = True
                windows.append(window)
            if not windows:
                return None
            result = {"windows": windows}
            if selected:
                result["selected"] = selected
        elif element.tag in {"RootPanel", "LayoutDocumentPaneGroup"}:
            direction = element.get("Orientation", "Horizontal")
            children = [part for c in element if (part := node(c, direction)) is not None]
            if not children:
                return None
            result = children[0] if len(children) == 1 else {"direction": direction, "children": children}
        else:
            return None
        axis = "DockWidth" if parent_orientation == "Horizontal" else "DockHeight"
        size = element.get(axis, "*")
        if size.endswith("*") and size != "*":
            result["weight"] = float(size[:-1])
        return result

    root = node(layout.find("RootPanel"))
    if seen != records.keys():
        raise ValueError(f"Unaccounted analysis windows in {path}: {records.keys() - seen}")
    return root


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Original Samples/Sequential directory")
    parser.add_argument("--images", type=Path, help="Original Zemax IMAFiles directory")
    parser.add_argument("--coatings", type=Path, help="Source COATING.DAT")
    args = parser.parse_args()
    coatings = ExampleCoatings(args.coatings or args.source.parent.parent / "Coatings/COATING.DAT", ASSETS)
    catalog = json.loads((ASSETS / "index.json").read_text(encoding="utf-8-sig"))
    result = []
    image_dir = args.images or args.source.parent.parent / "IMAFiles"
    images = {f.name.casefold(): f for f in image_dir.iterdir() if f.is_file()}
    used_images = set()
    for entry in catalog["Files"]:
        relative = entry["RelativePath"]
        sidecar = (args.source / relative).with_suffix(".ZDA")
        if not sidecar.exists():
            sidecar = next(f for f in sidecar.parent.iterdir() if f.name.casefold() == sidecar.name.casefold())
        workspace = read_workspace(sidecar, images, used_images, args.source / relative, coatings)
        result.append((relative, {"sourceSha256": hashlib.sha256(sidecar.read_bytes()).hexdigest(), "layout": workspace}))
    # One independently reviewable record per original file.
    (ASSETS / "Images").mkdir(exist_ok=True)
    for image in used_images:
        shutil.copyfile(image, ASSETS / "Images" / image.name)
    rows = [json.dumps(k, ensure_ascii=False) + ": " + json.dumps(v, ensure_ascii=False, separators=(",", ":")) for k, v in result]
    (ASSETS / "workspaces.json").write_text("{\n  " + ",\n  ".join(rows) + "\n}\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
