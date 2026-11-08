import csv
import hashlib
import json
import sys
from pathlib import Path
from zipfile import ZipFile

import numpy as np

root = Path(__file__).parent
sys.path.insert(0, str(root / 'scripts'))
from BoundedReadout import Grid, energy, curve_metrics


def sha(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


prior = Path('validation/rechecks/2026-11-06/grid-grin-bounded-fft-reference')
archive = prior / 'native-bounded-fft.zip'
baseline = root / 'grid-full-512-step0.001'
baseline.mkdir(exist_ok=True)
with ZipFile(archive) as saved:
    for name in ('psf-grid.json', 'grid-f64le.bin', 'captured-settings.json', 'model.json',
                 'result-diagnostics.json', 'batch-result.json'):
        (baseline / name).write_bytes(saved.read(f'refinement-fine/capture/{baseline.name}/{name}'))

source1 = root / 'sources/grid-0.001.ZMX'
source2 = root / 'sources/grid-0.0005.ZMX'
assert source1.read_text(encoding='utf-16').replace('PARM 1 0.001', 'PARM 1 0.0005') == source2.read_text(encoding='utf-16')
gate = (1e-5, .002, .003, .01)
results = dict(index=502, radiusMicrometers=5, sourceSha256={p.name: sha(p) for p in (source1, source2)},
               priorArchiveSha256=sha(archive), grids={}, pairs={})
curves = {}
identity = None
for directory in (baseline, root / 'grid-256-step0.001', root / 'grid-256-step0.0005'):
    if not (directory / 'batch-result.json').is_file():
        continue
    receipt = json.loads((directory / 'batch-result.json').read_text(encoding='utf-8-sig'))
    if receipt['exitCode'] != 0:
        raise ValueError(f'Native run failed {directory.name}')
    diagnostics = json.loads((directory / 'result-diagnostics.json').read_text(encoding='utf-8-sig'))
    assert not diagnostics['messages']
    captured = json.loads((directory / 'captured-settings.json').read_text(encoding='utf-8-sig'))
    properties = captured['properties']
    n = captured['request']['pupilSampling']
    assert properties['SampleSize'] == f'PsfS_{n}x{n}' and properties['OutputSize'] == f'PsfS_{2*n}x{2*n}'
    assert properties['ImageDelta'] == -1 and properties['Type'] == 'Linear'
    config = tuple(captured['request'][key] for key in ('configuration', 'field', 'wavelength', 'surface'))
    properties = {key: val for key, val in properties.items() if key not in {'SampleSize', 'OutputSize'}}
    case_identity = (config, properties, captured['selectors'])
    if identity is None:
        identity = case_identity
    assert identity == case_identity
    grid = Grid.load(directory)
    whole = grid.whole()
    coarse_r, coarse = energy(grid, whole, 5, 4, 256, 1000)
    r, fine = energy(grid, whole, 5, 8, 512, 2000)
    assert np.array_equal(r[::2], coarse_r)
    whole['energyQuadrature'] = curve_metrics(coarse, fine[::2], *gate)
    assert whole['energyQuadrature']['verdict'] == 'Pass'
    whole['energyAt5'] = float(fine[-1])
    whole['rawSha256'] = {name: sha(directory / name) for name in ('psf-grid.json', 'grid-f64le.bin',
                            'captured-settings.json', 'model.json', 'result-diagnostics.json', 'batch-result.json')}
    results['grids'][directory.name] = whole
    curves[directory.name] = fine

for key, a, b in (('pupil256To512At0p001', 'grid-256-step0.001', 'grid-full-512-step0.001'),
                  ('step0p001To0p0005At256', 'grid-256-step0.001', 'grid-256-step0.0005'),
                  ('finerVersusDenser', 'grid-256-step0.0005', 'grid-full-512-step0.001')):
    if a in curves and b in curves:
        results['pairs'][key] = curve_metrics(curves[a], curves[b], *gate)

with (prior / 'energy-reference.csv').open(encoding='utf-8-sig', newline='') as stream:
    control = list(csv.DictReader(stream))
assert np.allclose(r[::5], [float(row['radius_um']) for row in control], rtol=0, atol=1e-14)
results['archived512Reconstruction'] = curve_metrics(curves[baseline.name][::5],
                        [float(row['actual_encircled_fraction']) for row in control], *gate)
(root / 'results.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf-8')
with (root / 'curves.csv').open('w', encoding='utf-8', newline='') as stream:
    writer = csv.writer(stream)
    writer.writerow(['radius_um', *curves])
    writer.writerows(zip(r, *curves.values()))
print(json.dumps(results['pairs'], indent=2))
