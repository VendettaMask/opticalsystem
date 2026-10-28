"""Summarize retained red/green TRX and locate probe sensitivity by surface."""
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
p = root / 'validation/rechecks/2026-10-28/afocal-psf-contract'
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
runs = {}
for name in ['conditioning-before', 'conditioning-after', 'conditioning-related', 'conditioning-focal-regression', 'conditioning-energy-regression']:
    tree = ET.parse(p / (name + '.trx'))
    rows, fits = [], []
    for stdout in tree.findall('.//t:StdOut', ns):
        for line in (stdout.text or '').splitlines():
            if line.startswith('{'):
                rows.append(json.loads(line))
            elif line.startswith('angle='):
                fits.append({key: float(value) for key, value in re.findall(r'(\w+)=([^;]+)', line)})
    runs[name] = {'counters': tree.find('.//t:Counters', ns).attrib,
                  'times': tree.find('.//t:Times', ns).attrib, 'fitErrors': fits, 'records': rows}
(p / 'conditioning-summary.json').write_text(json.dumps({'runs': runs,
    'coreChange': 'ExitPupilGeometryEngine.FitLines only: same least-squares objective, local frame and cancellation-free projector diagonal',
    'limits': ['Binocular probe sensitivity not resolved', 'Actual internal polygon hit sequence not captured',
               'No change to reference, optical gates, geometry or probe sizes']}, indent=2) + '\n')
print({name: run['counters']['passed'] for name, run in runs.items()})
