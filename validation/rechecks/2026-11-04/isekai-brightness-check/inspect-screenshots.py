"""Read-only measurements of the user's three screenshots; no raster edits."""
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

inputs = [
    ('OPD', r'C:\Users\19851\AppData\Local\Temp\codex-clipboard-deb9fbd4-9880-4219-bee2-f8e29bb67736.png'),
    ('Pupil', r'C:\Users\19851\AppData\Local\Temp\codex-clipboard-4490b8b0-056e-4a11-a535-8b4679759273.png'),
    ('PSF', r'C:\Users\19851\AppData\Local\Temp\codex-clipboard-8436c7df-417c-4611-b82a-cadaf7451897.png'),
]
regions = {
    'toolbar': (.06, .04, .91, .077),
    'main_paper': (.05, .09, .94, .74),
    'navigation': (.22, .91, .72, .945),
    'caption': (.06, .81, .4, .89),
}
result = []
for label, filename in inputs:
    image = Image.open(filename).convert('RGB')
    pixels = np.asarray(image)
    measurements = {}
    for name, (left, top, right, bottom) in regions.items():
        area = pixels[round(top*image.height):round(bottom*image.height), round(left*image.width):round(right*image.width)]
        # Remove ink/curves; compare the paper's upper three luminance quartiles.
        rgb = area.reshape(-1, 3).astype(float)
        luminance = rgb @ np.array([.2126, .7152, .0722])
        rgb = rgb[luminance >= np.percentile(luminance, 25)]
        measurements[name] = {'mean_rgb': np.mean(rgb, axis=0).round(2).tolist(),
                              'median_rgb': np.median(rgb, axis=0).round(2).tolist(),
                              'luma': round(float(np.mean(rgb @ np.array([.2126, .7152, .0722]))), 2)}
    result.append({'label': label, 'path': filename, 'sha256': hashlib.sha256(Path(filename).read_bytes()).hexdigest(),
                   'pixels': image.size, 'regions': measurements})
output = Path(__file__).with_name('screenshot-measurements.json')
output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
