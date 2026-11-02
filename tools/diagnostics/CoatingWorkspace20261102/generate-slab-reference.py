"""Independent validation only. The product uses C# Core, never this Python reference."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import numpy as np

source = Path(sys.argv[1])
spec = importlib.util.spec_from_file_location("byrnes_tmm_reference", source)
reference = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reference)
cases = []
for wavelength in (450, 600, 950):
    for angle in (0, 35, 57):
        for substrate_k, thickness in ((0, 1e6), (0.0001, 1e4), (0.02, 1500)):
            for polarization in ("s", "p"):
                layers = [([2.1, .01], 70), ([1.45, .001], 95)]
                n0, ns, ne = [1, 0], [1.52, substrate_k], [1.33, 0]
                result = reference.inc_tmm(polarization,
                    np.array([complex(*n0)] + [complex(*n) for n, _ in layers] + [complex(*ns), complex(*ne)]),
                    np.array([np.inf] + [d for _, d in layers] + [thickness, np.inf]),
                    ["i"] + ["c"] * len(layers) + ["i", "i"], np.deg2rad(angle), wavelength)
                cases.append(dict(wavelength=wavelength, angle=angle, polarization=polarization, n0=n0, ns=ns, ne=ne,
                    thickness=thickness, layers=[dict(n=n, d=d) for n, d in layers],
                    r=float(result["R"]), t=float(result["T"]), a=float(1-result["R"]-result["T"])))
payload = dict(source="sbyrnes321/tmm inc_tmm", commit="9c265c20e2c5f75b5ac46d63ab466665948eb821",
    source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(), license="MIT", numpy=np.__version__, cases=cases)
Path(sys.argv[2]).write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
print(f"Generated {len(cases)} independent mixed coherent/incoherent cases")
