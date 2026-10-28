"""Independent plane geometry and vector Snell/reflection checks of recorded hits.
No trace replacement, fitted coefficient, model editing or native execution.
"""
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[3]
folder = root / 'validation/rechecks/2026-10-28/afocal-psf-contract'
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tree = ET.parse(folder / 'polygon-paths-final.trx')
records = [json.loads(line) for node in tree.findall('.//t:StdOut', ns)
           for line in (node.text or '').splitlines() if line.startswith('{')]
def dot(a, b): return sum(x*y for x, y in zip(a, b))
def sub(a, b): return [x-y for x, y in zip(a, b)]
def norm(a): return math.sqrt(dot(a, a))
def unit(a): return [x/norm(a) for x in a]
def cross(a, b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
def normal(vertices): return unit(cross(sub(vertices[1], vertices[0]), sub(vertices[2], vertices[0])))
def propagate(d, n, event):
    d, n = unit(d), unit(n)
    if event['interactionKind'] in ['TotalInternalReflection', 'Reflected']:
        return unit([x-2*dot(d, n)*y for x, y in zip(d, n)])
    assert event['interactionKind'] == 'Transmitted'
    if dot(d, n) > 0: n = [-x for x in n]
    cosine = -dot(d, n)
    ratio = event['refractiveIndexBefore']/event['refractiveIndexAfter']
    discriminant = 1-ratio*ratio*(1-cosine*cosine)
    assert discriminant >= 0
    return unit([ratio*x+(ratio*cosine-math.sqrt(discriminant))*y for x, y in zip(d, n)])
maximum = dict(planeResidualMm=0., distanceErrorMm=0., normalError=0., directionError=0., rayPointErrorMm=0.)
branches, creases = [], []
paths = [r for r in records if r['kind'] == 'path']
assert len(paths) == 42
for config in [1, 2]:
    faces = next(r['faces'] for r in records if r['kind'] == 'faces' and r['configuration'] == config)
    normals = [normal(face['vertices']) for face in faces]
    for a, b in [(2, 3), (4, 5), (10, 11), (12, 13)]:
        creases.append({'configuration': config, 'triangles': [a, b],
                        'normalDifference': norm(sub(normals[a], normals[b]))})
    group = [r for r in paths if r['configuration'] == config]
    chief = next(r for r in group if r['px'] == r['py'] == 0)
    for path in group:
        sequence = []
        limit = chief['events'][0]['direction']
        for event in path['events']:
            if event['kind'] != 'face': continue
            face_id = event['faceId']; sequence.append(face_id)
            vertices, n = faces[face_id]['vertices'], normals[face_id]
            origin, direction, point = event['origin'], event['direction'], event['point']
            distance = dot(n, sub(vertices[0], origin))/dot(n, direction)
            computed_point = [x+distance*y for x, y in zip(origin, direction)]
            checks = dict(planeResidualMm=abs(dot(n, sub(point, vertices[0]))),
                          distanceErrorMm=abs(distance-event['distance']),
                          normalError=norm(sub(n, event['normal'])),
                          directionError=norm(sub(propagate(direction, n, event), event['outgoing'])),
                          rayPointErrorMm=norm(sub(computed_point, point)))
            for key, value in checks.items(): maximum[key] = max(maximum[key], value)
            assert event['distance'] == min(c['distance'] for c in event['candidates'] if c['eligible'])
            assert not event['StopTracing'] and not event['Vignetted'] and event['Intensity'] > 0
            limit = propagate(limit, n, event)
        branches.append({'configuration': config, 'px': path['px'], 'py': path['py'], 'triangleSequence': sequence,
                         'quadSequence': [i//2 for i in sequence], 'chiefDirectionThroughFixedBranch': limit})
    for axis in ['px', 'py']:
        for sign in [-1, 1]:
            selected = [b for b in branches if b['configuration'] == config and sign*b[axis] > 0]
            assert len({tuple(b['triangleSequence']) for b in selected}) == 1
assert maximum['planeResidualMm'] < 1e-10
assert maximum['distanceErrorMm'] < 1e-10
assert maximum['rayPointErrorMm'] < 1e-10
assert maximum['normalError'] < 2e-14
assert maximum['directionError'] < 2e-12
jumps = []
for config in [1, 2]:
    for axis in ['px', 'py']:
        minus = next(b for b in branches if b['configuration'] == config and b[axis] == -.001)
        plus = next(b for b in branches if b['configuration'] == config and b[axis] == .001)
        assert minus['quadSequence'] == plus['quadSequence']
        jump = sub(plus['chiefDirectionThroughFixedBranch'], minus['chiefDirectionThroughFixedBranch'])
        scan = []
        for h in [.01, .003, .001, .0003, .0001]:
            negative = next(r for r in paths if r['configuration'] == config and r[axis] == -h)
            positive = next(r for r in paths if r['configuration'] == config and r[axis] == h)
            raw = [x/(2*h) for x in sub(positive['events'][-1]['direction'], negative['events'][-1]['direction'])]
            regular = [x-y/(2*h) for x, y in zip(raw, jump)]
            scan.append({'h': h, 'rawDirectionDerivative': raw, 'afterRemovingIndependentBranchJumpForDiagnosisOnly': regular})
        jumps.append({'configuration': config, 'axis': axis, 'fixedBranchDirectionJump': norm(jump),
                      'jumpVector': jump, 'scan': scan})
result = {'source': 'polygon-paths-final.trx', 'paths': 42, 'maximumIndependentErrors': maximum,
          'creases': creases, 'branches': branches, 'branchLimits': jumps,
          'scope': 'Local group coordinates. Fixed-branch limits use the same chief incident direction; they are one-sided direction-map diagnostics, not alternate physical rays or fitted reference values.',
          'conclusion': 'Same parent-quad order, different triangles across pupil sign, stable sequence over each signed h series. No plane/Snell mismatch within stated gates; frozen noncoplanar quad triangulation produces distinct branch limits.'}
(folder / 'polygon-path-verification.json').write_text(json.dumps(result, indent=2)+'\n')
print(json.dumps({'maximum': maximum, 'creases': creases, 'branchLimits': jumps}, indent=2))
