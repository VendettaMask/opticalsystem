"""Independent reflection-plane algebra on unchanged frozen POB vertices."""
import math
import json
import hashlib
from pathlib import Path
root = Path(__file__).resolve().parents[3]
source = root / 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Objects/Polygon Objects/PORRO_PRISM-LH.POB'
vertices, faces = {}, []
for line in source.read_text().splitlines():
    parts = line.split('!')[0].split()
    if not parts:
        continue
    if parts[0] == 'V':
        vertices[int(parts[1])] = list(map(float, parts[2:]))
    elif parts[0] == 'R':
        faces.append(list(map(int, parts[1:5])))
def dot(a, b):
    return sum(x*y for x, y in zip(a, b))
def normal(face):
    a = [y-x for x, y in zip(vertices[face[0]], vertices[face[1]])]
    b = [y-x for x, y in zip(vertices[face[0]], vertices[face[2]])]
    n = [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
    return [x/math.sqrt(dot(n, n)) for x in n]
def reflect(d, n):
    return [x-2*dot(d, n)*y for x, y in zip(d, n)]
rows = []
for i, j in [(1, 2), (4, 5)]:
    a, b = normal(faces[i]), normal(faces[j])
    first = reflect(reflect([0, 0, 1], a), b)
    second = reflect(reflect([0, 0, 1], b), a)
    rows.append({'faces': [faces[i], faces[j]], 'normalDot': dot(a, b),
                 'reflectionOrderDifference': math.sqrt(sum((x-y)**2 for x, y in zip(first, second))),
                 'normalA': a, 'normalB': b})
result = {'source': str(source.relative_to(root)), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'rows': rows,
          'note': 'Frozen rounded face coordinates, first-three-vertex plane convention; no geometry modified. Different reflection orders can differ even in exact arithmetic. This alone does not prove the actual native or Core hit sequence.'}
(root / 'validation/rechecks/2026-10-28/afocal-psf-contract/polygon-normal-diagnostic.json').write_text(json.dumps(result, indent=2) + '\n')
