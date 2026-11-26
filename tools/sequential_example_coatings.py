"""Build Coating Lab inputs for the saved sequential sample analyses."""
from bisect import bisect_left
import gzip
import json
from pathlib import Path
import uuid


def read_text(path):
    data = path.read_bytes()
    return data.decode("utf-16" if data[:2] in (b"\xff\xfe", b"\xfe\xff") else "utf-8-sig")


def prescription(path, number):
    glass, before, coating = "AIR", "AIR", None
    waves, primary, current = {}, 1, -1
    for line in read_text(path).splitlines():
        tokens = line.split()
        if len(tokens) < 2:
            continue
        key, value = tokens[:2]
        if key == "WAVM":
            waves[int(value)] = float(tokens[2]) * 1000
        elif key == "PWAV":
            primary = int(value)
        elif key == "SURF":
            if current == number:
                break
            current, before, coating = int(value), glass, None
            glass = "AIR"
        elif key == "TYPE" and value == "COORDBRK":
            glass = before
        elif key == "GLAS":
            glass = value.upper()
        elif key == "COAT":
            coating = value.upper()
    return before, glass, coating, waves[primary]


class ExampleCoatings:
    def __init__(self, catalog, assets):
        self.assets = assets
        self.definitions = {}
        self.files = {}
        section = None
        for line in read_text(catalog).splitlines():
            tokens = line.split("!", 1)[0].split()
            if not tokens:
                continue
            key = tokens[0].upper()
            if key in ("MATE", "COAT", "TABLE"):
                section = self.definitions.setdefault((key, tokens[1].upper()), [])
            elif key in ("TAPR", "IDEAL", "IDEAL2", "ENCRYPTED"):
                section = None
            elif section is not None:
                section.append(tokens)
        root = Path(__file__).resolve().parents[1]
        pack = (root / "src/OpticalSystemDesign.Core/Materials/Data/zemax-glass-catalogs.ogdb").read_bytes()
        catalogs = json.loads(gzip.decompress(pack[8:]))["catalogs"]
        self.glasses = {g["name"]: g for c in catalogs if c["catalogName"] == "SCHOTT" for g in c["glasses"]}

    def material(self, name, lens=False):
        if lens and name != "AIR":
            g = self.glasses[name]
            if g["dispersionFormulaNumber"] != 2:
                raise ValueError(f"Expected Sellmeier glass for {name}")
            lo, hi = (g[k + "WavelengthMicrometers"] * 1000 for k in ("minimum", "maximum"))
            numbers = {f"{k}{i}": g["dispersionCoefficients"][2*i+j] for i in range(3) for j, k in enumerate("bc")}
            model = {"Kind": "sellmeier", "Numbers": numbers, "Text": {"name": name}}
            source = "SCHOTT / sequential glass catalog; real bulk index at the interface"
        else:
            rows = [list(map(float, r)) for r in self.definitions["MATE", name]]
            model = {"Kind": "constant", "Numbers": {"index": rows[0][1], "extinction": -rows[0][2]}, "Text": {"name": name}}
            lo, hi, source = 1, 1e6, "COATING.DAT / MATE " + name
            if len(rows) > 1:
                lo, hi = rows[0][0] * 1000, rows[-1][0] * 1000
                numbers = {"minimumWavelengthNanometers": lo, "maximumWavelengthNanometers": hi}
                for i, (um, n, k) in enumerate(rows):
                    numbers.update({f"nw{i}": um*1000, f"n{i}": n, f"kw{i}": um*1000, f"k{i}": -k})
                model = {"Kind": "catalog_glass", "Numbers": numbers,
                         "Text": {"name": name, "manufacturer": "COATING.DAT", "formula": "tabulated nk"}}
        return {"Id": name, "Name": name, "Source": source, "MinimumNm": lo, "MaximumNm": hi, "Model": model}

    def layers(self, name, primary):
        result, group, repeat = [], [], 0
        for material, value, *flags in self.definitions["COAT", name]:
            absolute, count = (list(map(int, flags)) + [0, 0])[:2]
            rows = [list(map(float, r)) for r in self.definitions["MATE", material]]
            wavelengths = [r[0]*1000 for r in rows]
            i = bisect_left(wavelengths, primary)
            if i == 0 or i == len(rows):
                n = rows[0 if i == 0 else -1][1]
            else:
                f = (primary-wavelengths[i-1])/(wavelengths[i]-wavelengths[i-1])
                n = rows[i-1][1] + f*(rows[i][1]-rows[i-1][1])
            layer = {"MaterialId": material, "ThicknessNm": float(value)*(1000 if absolute else primary/n), "Variable": False}
            if repeat:
                group.append(layer)
                if count:
                    if count != repeat:
                        raise ValueError("Unmatched coating loop")
                    result.extend(group*repeat)
                    repeat, group = 0, []
            elif count > 1:
                repeat, group = count, [layer]
            else:
                result.append(layer)
        if repeat:
            raise ValueError("Unclosed coating loop")
        return result

    def save(self, lens, surface, sweep_range=None):
        before, after, name, primary = prescription(lens, surface)
        identifier = name or ("MIRROR" if after == "MIRROR" else None)
        if not identifier:
            return None
        if ("TABLE", identifier) in self.definitions:
            lines = self.definitions["TABLE", identifier]
            if sum(r[0] == "ANGL" for r in lines) != 1:
                raise ValueError("Expected a single-angle spectrum")
            rows = []
            for row in lines[1:]:
                if row[0] != "WAVE" or any(float(v) != 0 for v in row[6:]):
                    raise ValueError("Expected a power-only spectrum")
                rows.append(dict(zip(("WavelengthNm", "Rs", "Rp", "Ts", "Tp"), [float(row[1])*1000, *map(float, row[2:6])])))
            document = {"Format": "CoatingLab.PowerSpectrum", "Version": 1, "Name": identifier,
                        "AngleDegrees": float(lines[0][1]), "ZeroPhase": True, "Rows": rows}
        else:
            layers = self.layers(name, primary) if name else []
            media = {before: self.material(before, lens=True)}
            if after == "MIRROR":
                substrate = layers.pop()["MaterialId"] if layers else "ALUM"
                media[substrate] = self.material(substrate)
            else:
                substrate = after
                media[after] = self.material(after, lens=True)
                if before != "AIR" and after == "AIR":
                    layers.reverse()
            for layer in layers:
                key = layer["MaterialId"]
                if key not in media:
                    media[key] = self.material(key)
            lower, upper = sweep_range or (primary*.95, primary*1.05)
            thicknesses = [l["ThicknessNm"] for l in layers]
            low = layers[0]["MaterialId"] if layers else before
            high = layers[-1]["MaterialId"] if layers else substrate
            document = {"SchemaVersion": 1, "Id": str(uuid.uuid5(uuid.NAMESPACE_URL, lens.name + "/" + identifier)),
                        "Name": identifier, "ReferenceWavelengthNm": primary,
                        "Target": {"Kind": "Antireflection", "CenterNm": primary, "MinimumNm": lower,
                                   "MaximumNm": upper, "MaximumLayers": max(8, len(layers))},
                        "Settings": {"MinimumThicknessNm": min([1, *thicknesses]), "MaximumThicknessNm": max([2000, *thicknesses])},
                        "IncidentId": before, "SubstrateId": substrate, "LowId": low, "HighId": high,
                        "Materials": list(media.values()), "Layers": layers}
        filename = identifier + ".coating.json"
        if filename in self.files and self.files[filename] != document:
            raise ValueError(f"Different sample designs share the name {filename}")
        self.files[filename] = document
        directory = self.assets / "Coatings"
        directory.mkdir(exist_ok=True)
        (directory / filename).write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        return filename
