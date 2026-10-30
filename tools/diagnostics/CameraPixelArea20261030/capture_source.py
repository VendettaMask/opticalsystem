"""Capture the public manufacturer page used by the point controls; never generate RAW measurements."""
import hashlib
import json
import pathlib
import sys
import urllib.request

url = "https://softwareservices.flir.com/BFS-U3-23S6/latest/EMVA/EMVA.html"
destination = pathlib.Path(sys.argv[1])
destination.mkdir(parents=True, exist_ok=True)
extracted = len(sys.argv) == 4 and sys.argv[2] == "--extract"
if extracted:
    payload = pathlib.Path(sys.argv[3]).read_bytes()
else:
    with urllib.request.urlopen(url, timeout=60) as response:
        payload = response.read(2 * 1024 * 1024 + 1)
if len(payload) > 2 * 1024 * 1024:
    raise RuntimeError("Unexpectedly large manufacturer page")
text = payload.decode("utf-8")
for expected in ("2509.0.46.0", "67.71", "55.62", "61.75", "38.74", "6.30", "10.25", "33843", "33279"):
    if expected not in text:
        raise RuntimeError(f"Published page has changed; missing {expected}; review before replacing the fixture")
if not extracted:
    (destination / "manufacturer.html").write_bytes(payload)
(destination / "source.json").write_text(json.dumps({
    "url": url, "retrievedDate": "2026-10-30", "sha256": hashlib.sha256(payload).hexdigest(),
    "bytes": len(payload), "acquisition": "Web tool text extraction; not original HTML bytes" if extracted else "Direct original HTML",
    "directDownload": "HTTP 403; original HTML was not captured" if extracted else "Succeeded",
    "fixture": "tests/OpticalSystemDesign.Tests/Fixtures/camera-flir-bfs-u3-23s6-points.json",
    "fixtureSha256": hashlib.sha256(pathlib.Path("tests/OpticalSystemDesign.Tests/Fixtures/camera-flir-bfs-u3-23s6-points.json").read_bytes()).hexdigest(),
    "scope": "Manufacturer-reported measured points. No raw frame data or complete spectral curve. No simulated frame is labeled measured."
}, indent=2) + "\n", encoding="utf-8")
print("Captured manufacturer source:", len(payload), "bytes")
