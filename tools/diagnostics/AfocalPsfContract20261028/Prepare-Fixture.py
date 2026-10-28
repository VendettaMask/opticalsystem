"""Copy exact existing native captures; never recapture or transform reference arrays."""
import hashlib
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[3]
source = root / 'validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip'
target = root / 'validation/zemax/2026-r1/afocal-psf-contract-2026-10-28/native-grids.zip'
if target.exists():
    raise SystemExit('Fresh fixture path required')
manifest = {'sourceArchive': str(source.relative_to(root)),
            'sourceArchiveSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
            'referenceBytesUnchanged': True, 'cases': [], 'entries': []}
with zipfile.ZipFile(source) as archive, zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as output:
    for batch in ['final-150-v2', 'final-fixed-v2']:
        prefix = 'artifacts/validation/afocal-fft-energy-20261027/' + batch
        items = json.loads(archive.read(prefix + '/items.json'))
        outcomes = {r['id']: r['conclusion'] for r in json.loads(archive.read(prefix + '/summary.json'))}
        for row in items:
            if row['key'] not in ['PSF', 'Huygens PSF']:
                continue
            native = row['native'].replace('D:\\Projects\\opticalsystem\\', '').replace('\\', '/')
            manifest['cases'].append({k: row[k] for k in ['id', 'lens', 'configuration', 'key', 'request']} |
                                     {'originalConclusion': outcomes[row['id']], 'batch': batch})
            for name in ['data.json', 'model.json', 'captured-settings.json']:
                original = native + '/' + name
                destination = row['id'] + '/' + name
                data = archive.read(original)
                output.writestr(destination, data)
                manifest['entries'].append({'path': destination, 'sourcePath': original,
                                           'sha256': hashlib.sha256(data).hexdigest()})
    output.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
print(f'{len(manifest["cases"])} cases; {target.stat().st_size} bytes')
