"""Independent mixed-coherence duplex references only; never a product dependency."""
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
            for rear_count in (0, 1, 2):
                for polarization in ("s", "p"):
                    front = [([2.1, .01], 70), ([1.45, .001], 95)]
                    # Each face is represented exterior -> substrate, matching the C# file contract.
                    back = [([1.7, .004], 51), ([2.25, .02], 84)][:rear_count]
                    n0, ns, ne = [1, 0], [1.52, substrate_k], [1.33, 0]
                    travel_back = list(reversed(back))
                    result = reference.inc_tmm(polarization,
                        np.array([complex(*n0)] + [complex(*n) for n, _ in front] + [complex(*ns)]
                                 + [complex(*n) for n, _ in travel_back] + [complex(*ne)]),
                        np.array([np.inf] + [d for _, d in front] + [thickness] + [d for _, d in travel_back] + [np.inf]),
                        ["i"] + ["c"] * len(front) + ["i"] + ["c"] * len(back) + ["i"], np.deg2rad(angle), wavelength)
                    cases.append(dict(wavelength=wavelength, angle=angle, polarization=polarization,
                        n0=n0, ns=ns, ne=ne, thickness=thickness,
                        front=[dict(n=n, d=d) for n, d in front], back=[dict(n=n, d=d) for n, d in back],
                        r=float(result["R"]), t=float(result["T"]), a=float(1-result["R"]-result["T"])))
payload = dict(source="sbyrnes321/tmm inc_tmm", commit="9c265c20e2c5f75b5ac46d63ab466665948eb821",
    source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(), license="MIT", numpy=np.__version__, cases=cases)
Path(sys.argv[2]).write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
print(f"Generated {len(cases)} independent duplex cases")
