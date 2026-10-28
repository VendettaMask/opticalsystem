"""Read bounded test output; never computes optical expectations or changes gates."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
folder = root / 'validation/rechecks/2026-10-28/afocal-psf-contract'
name = 'independent-final.trx'
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tree = ET.parse(folder / name)
records = []
for stdout in tree.findall('.//t:StdOut', ns):
    records += [json.loads(line) for line in (stdout.text or '').splitlines() if line.startswith('{')]
assert len(records) == 50
pencils = []
for lens, config in sorted({(r['lens'], r['configuration']) for r in records if r['kind'] == 'pencil'}):
    rows = sorted((r for r in records if r['kind'] == 'pencil' and r['lens'] == lens and r['configuration'] == config), key=lambda r: r['h'])
    pencils.append({'lens': lens, 'configuration': config, 'aspectSpanPpm': 1e6*(max(r['aspect'] for r in rows)-min(r['aspect'] for r in rows)),
                    'rows': rows})
compound = []
for config in [1, 2]:
    rows = [r for r in records if r['kind'] == 'compound' and r['configuration'] == config and r['h'] == .001]
    standard = next(r['diameter'] for r in rows if r['angle'] == .0001)
    compound.append({'configuration': config, 'atPupilProbe': .001,
                     'directionProbeDiameterSpanPpm': 1e6*(max(r['diameter'] for r in rows)-min(r['diameter'] for r in rows))/standard,
                     'rows': rows})
result = {'source': name, 'testCounters': tree.find('.//t:Counters', ns).attrib,
          'times': tree.find('.//t:Times', ns).attrib,
          'idealDisk': sorted((r for r in records if r['kind'] == 'ideal-disk'), key=lambda r: r['n']),
          'invariants': [r for r in records if r['kind'] in ['padding', 'rotation90']],
          'pencilSweep': pencils, 'compoundAngleSensitivity': compound,
          'allRecords': records,
          'scope': {'scalarParaxialAir': True, 'newNativeCapture': False, 'coreModified': False,
                    'samplingConvergenceIsNotNativeParity': True, 'probeTestsAssertFiniteNotStability': True,
                    'cropFractionsAreDiagnosticsNotGatedNativeComparisons': True}}
(folder / 'independent-physics-summary.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps({'records': len(records), 'pencilSpansPpm': [r['aspectSpanPpm'] for r in pencils],
                  'compoundSpansPpm': [r['directionProbeDiameterSpanPpm'] for r in compound]}))
