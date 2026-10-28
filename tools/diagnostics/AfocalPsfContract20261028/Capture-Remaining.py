"""Prepare frozen-input diagnostics; --run requires licensed Windows 2026 R1.
Never updates a baseline or launches native software without --run.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(output, api):
    output.mkdir(parents=True, exist_ok=False)
    archives = ROOT / 'validation/rechecks/2026-10-27'
    evidence = {}
    jobs = []
    with zipfile.ZipFile(archives / 'afocal-fft-energy/evidence-current.zip') as grids, zipfile.ZipFile(archives / 'afocal-reference-focus/evidence.zip') as rays:
        controls = [n for n in rays.namelist() if any('native-reference-controls/Centroid-' + key in n for key in ['0013-', '0065-', '0095-']) and n.endswith('/request.json')]
        assert len(controls) == 3
        for control in sorted(controls):
            request = json.loads(rays.read(control))
            tag = control.split('/')[-2].removeprefix('Centroid-')
            filename = request['input'].replace('\\', '/').split('/')[-1]
            members = [n for n in grids.namelist() if n.split('/')[-1] == filename and n.startswith('src/')]
            assert len(members) == 1
            raw = grids.read(members[0])
            source = output / filename
            if source.exists():
                assert source.read_bytes() == raw
            else:
                source.write_bytes(raw)
            request.update(input=str(source.resolve()), zosApiPath=api, captureScreenshots=False, inputSha256=sha(raw))
            # Independent host processes make each analysis a fresh instance.
            for reference in ['Centroid', 'ChiefRay']:
                item = copy.deepcopy(request)
                item['field'] = 2
                item['reference'] = reference
                item['zemaxSettings'].update(Field=2, ReferTo=reference)
                item['workbenchSettings'].update(FieldNumber='2', Reference=reference.lower())
                jobs.append((tag + '-' + reference + '-field2', item))
            pupil = copy.deepcopy(request)
            pupil['adapter'] = 'afocal-diagnosis'
            jobs.append((tag + '-pupil', pupil))
        endpoints = []
        for config, key in [(1, '0065'), (2, '0095')]:
            names = [n for n in rays.namelist() if '/native-centroid-rays/' + key in n and n.endswith('/request.json')]
            assert len(names) == 1
            name = names[0]
            req = json.loads(rays.read(name))
            geometry_name = name.replace('request.json', 'image-geometry.json')
            geometry = json.loads(rays.read(geometry_name))
            assert len(req['pointInputs']) == len(geometry)
            selected = []
            for i, (point, ray) in enumerate(zip(req['pointInputs'], geometry)):
                if point['fieldNumber'] == 1 and (point['px'], point['py']) in [(0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)]:
                    assert ray['number'] == i + 1
                    selected.append({'input': point, 'rowIndex': i, 'ray': {k: v for k, v in ray.items() if k != 'opdWaves'}})
            assert len(selected) == 5
            endpoints.append({'configuration': config, 'requestMember': name, 'requestSha256': sha(rays.read(name)),
                              'geometrySha256': sha(rays.read(geometry_name)), 'points': selected,
                              'opdExcluded': 'image-geometry uses OPDMode.None; historical global header is misleading'})
            # Exact endpoints already exist. New samples bracket each boundary at a prescribed,
            # reference-independent epsilon; no deletion, fitting, or axis permutation.
            points = [dict(fieldNumber=1, hx=0, hy=0, px=0, py=0)]
            for x, y in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
                for r in [1 - 1e-5, 1, 1 + 1e-5]:
                    points.append(dict(fieldNumber=1, hx=0, hy=0, px=x*r, py=y*r))
            source = output / 'Binocular_System.ZMX'
            assert sha(source.read_bytes()) == req['inputSha256']
            req.update(input=str(source.resolve()), zosApiPath=api, pointInputs=points,
                       imageGeometryOnly=True, captureScreenshots=False)
            jobs.append(('binocular-c' + str(config) + '-boundary13', req))
        evidence['existingEndpoints'] = endpoints
        pupil_text = []
        for member in grids.namelist():
            if '/expanded/' in member and '-first-order/native/data.txt' in member:
                raw = grids.read(member)
                decoded = raw.decode('utf-16') if raw[:2] in [b'\xff\xfe', b'\xfe\xff'] else raw.decode('utf-8-sig')
                lines = [line for line in decoded.splitlines() if 'pupil' in line.lower() or '瞳' in line]
                pupil_text.append({'member': member, 'sha256': sha(raw), 'lines': lines})
        evidence['roundedSystemDataPupils'] = pupil_text
    evidence['jobCount'] = len(jobs)
    evidence['nativeExecuted'] = False
    (output / 'existing-evidence.json').write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    manifest = []
    for name, request in jobs:
        path = output / (name + '.request.json')
        path.write_text(json.dumps(request, indent=2), encoding='utf-8')
        manifest.append({'id': name, 'request': path.name, 'inputSha256': request['inputSha256']})
    (output / 'jobs.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


def run(output, api, jobs):
    if os.name != 'nt':
        raise SystemExit('--run requires Windows; no native process started')
    api = Path(api).resolve()
    dlls = [api / n for n in ['ZOSAPI.dll', 'ZOSAPI_Interfaces.dll', 'ZOSAPI_NetHelper.dll']]
    if not all(p.is_file() for p in dlls):
        raise SystemExit('API root must contain all three ZOSAPI assemblies')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    exe = output / 'ZemaxHost.exe'
    sources = sorted((ROOT / 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost').glob('*.cs'))
    command = [str(compiler), '/nologo', '/target:exe', '/platform:x64', '/out:' + str(exe), '/r:System.Web.Extensions.dll']
    command += ['/r:' + str(p) for p in dlls] + [str(p) for p in sources]
    with (output / 'build.log').open('w') as log:
        subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
    Path(str(exe) + '.config').write_text('<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
    results = []
    for job in jobs:
        request_path = output / job['request']
        request = json.loads(request_path.read_text())
        source = Path(request['input'])
        if sha(source.read_bytes()) != job['inputSha256']:
            raise RuntimeError('Source changed before job ' + job['id'])
        destination = output / job['id']
        with (output / (job['id'] + '.log')).open('w') as log:
            try:
                process = subprocess.run([str(exe), str(request_path), str(destination)], stdout=log,
                                         stderr=subprocess.STDOUT, timeout=120)
                status = {'id': job['id'], 'exitCode': process.returncode}
            except subprocess.TimeoutExpired:
                # Stop the batch; do not target any unrelated running application.
                status = {'id': job['id'], 'timeout': True, 'nativeChildCleanupUncertain': True}
        status['sourceUnchanged'] = sha(source.read_bytes()) == job['inputSha256']
        results.append(status)
        (output / 'execution.json').write_text(json.dumps(results, indent=2))
        if status.get('timeout') or status.get('exitCode') != 0 or not status['sourceUnchanged']:
            raise SystemExit('Capture stopped; inspect execution.json and logs')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New directory only')
    parser.add_argument('--api', default='', help='Windows native API root; required with --run')
    parser.add_argument('--run', action='store_true')
    args = parser.parse_args()
    if args.run and (os.name != 'nt' or not args.api):
        parser.error('--run requires Windows and --api; nothing executed')
    destination = args.output.resolve()
    jobs = prepare(destination, args.api)
    print(f'Prepared {len(jobs)} bounded jobs; native execution requested: {args.run}')
    if args.run:
        run(destination, args.api, jobs)
