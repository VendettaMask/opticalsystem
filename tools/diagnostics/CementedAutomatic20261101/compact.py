"""Deduplicate verified archive entries; no product or optical computation."""
from pathlib import Path
import json, hashlib, zipfile, sys
root = Path(sys.argv[1]).resolve()
base = root / 'final'
current = root / 'final-authority'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(b): return hashlib.sha256(b).hexdigest()
receipt = []
for kind in ('source', 'runtime'):
    original = current / f'default-{kind}.zip'
    retained = base / f'default-{kind}.zip'
    changed = current / f'default-{kind}-delta.zip'
    rows = read(current / f'default-{kind}-sha256.json')
    previous = {r['path']: r['sha256'] for r in read(base / f'default-{kind}-sha256.json')}
    mapping = []
    with zipfile.ZipFile(original) as src, zipfile.ZipFile(retained) as old, zipfile.ZipFile(changed, 'x', zipfile.ZIP_DEFLATED) as dst:
        assert len(src.namelist()) == len(rows)
        for r in rows:
            data = src.read(r['path']); assert digest(data) == r['sha256']
            reuse = previous.get(r['path']) == r['sha256']
            if reuse: assert digest(old.read(r['path'])) == r['sha256']
            else: dst.writestr(r['path'], data)
            mapping.append({**r, 'archive': (retained if reuse else changed).relative_to(root).as_posix()})
    # Independently resolve the retained entry graph before deleting this task's redundant container.
    for r in mapping:
        with zipfile.ZipFile(root / r['archive']) as z: assert digest(z.read(r['path'])) == r['sha256']
    index = current / f'default-{kind}-storage.json'
    with index.open('x', encoding='utf8') as f: json.dump(mapping, f, indent=2)
    assert original.resolve().parent == current and root in original.resolve().parents
    removed = original.stat().st_size
    original.unlink()
    receipt.append({'kind': kind, 'completeEntries': len(rows), 'reusedEntries': sum(r['archive'].startswith('final/') for r in mapping),
        'changedEntries': sum(r['archive'].startswith('final-authority/') for r in mapping), 'removedDuplicateContainerBytes': removed,
        'everyRetainedEntryHashChecked': True})
with (root / 'archive-reuse.json').open('x', encoding='utf8') as f: json.dump(receipt, f, indent=2)
print(receipt)
