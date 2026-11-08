import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0, j1


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_SHA = '6531e67cfdc292d68ad4c5bd23fef5912f69ad9a69879047ec52e818a8b399a2'
FINGERPRINT = '53f78d84360f85ba6418ca59499e86582630bebe866c16d6714221a12d5233b0'


def run():
    index = 996
    path, lens, header, surfaces = energy.source(index)
    raw = path.read_bytes()
    assert energy.sha(raw) == SOURCE_SHA
    source = energy.decode(raw)
    e_dir, w_dir, r_dir, f_dir = (energy.frozen(n) for n in (996, 988, 984, 983))
    settings, wave_settings, ray_settings, first_settings = (energy.read(d/'captured-settings.json')
                                                             for d in (e_dir,w_dir,r_dir,f_dir))
    assert settings['selectors'] == wave_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for control in (wave_settings, ray_settings, first_settings):
        for key in ('configuration', 'input', 'primaryWavelengthMicrometers'):
            assert control['request'][key] == settings['request'][key]
    for control in (wave_settings, ray_settings):
        for key in ('useRayAiming', 'field', 'wavelength'):
            assert control['request'][key] == settings['request'][key]
    assert not settings['request']['useRayAiming'] and settings['request']['configuration'] == 2
    assert [ray_settings['properties'][k] for k in ('Hx','Hy','Px','Py')] == [0,0,0,.8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir/'model.json')
    assert model['aperture']['ApodizationType'] == 'Uniform'

    # Unfold the reflected axis into a single meridian. The 45-degree diagonal
    # interchanges the axial and meridional direction components. The 90-degree
    # break gives the reflected arm negative local propagation; reversing that
    # axis makes every subsequent configured gap positive and negates curvature.
    assert re.search(r'^GLSS\s+4\s+2\s+"MIRROR"', source, re.M)
    assert re.search(r'^GLSS\s+10\s+2\s+""', source, re.M)
    assert float(re.search(r'^PRAM\s+5\s+2\s+([^\s]+)', source, re.M).group(1)) == 90
    assert float(re.search(r'^PRAM\s+6\s+2\s+([^\s]+)', source, re.M).group(1)) == -1
    assert energy.setting(surfaces[4], 'PARM 2') == 1
    assert all(energy.setting(surfaces[5], f'PARM {i}') == 0 for i in (1,2,3,5,6))
    assert energy.setting(surfaces[4], 'DISZ') == energy.setting(surfaces[5], 'DISZ') == 0
    assert model['surfaces'][4]['Material'] == 'MIRROR' and model['surfaces'][10]['Material'] == ''
    assert all(model['surfaces'][i]['Material'] == 'BK7' for i in (3,6))
    gaps = {int(i): float(value) for i,value in re.findall(r'^THIC\s+(\d+)\s+2\s+([^\s]+)', source, re.M)}
    curvatures = {int(i): float(value) for i,value in re.findall(r'^CRVT\s+(\d+)\s+2\s+([^\s]+)', source, re.M)}
    end = max(surfaces)
    assert set(gaps) == set(range(6,end)) and set(curvatures) == {8,9}
    for i in range(6,end):
        readback = model['surfaces'][i]
        assert readback['Thickness'] == gaps[i] < 0
        curvature = curvatures.get(i, energy.setting(surfaces[i], 'CURV'))
        assert abs(curvature - (1/readback['Radius'] if readback['Radius'] else 0)) < 1e-15
        surfaces[i] = re.sub(r'^\s*DISZ\s+[^\n]+', f'  DISZ {-gaps[i]}', surfaces[i], flags=re.M)
        surfaces[i] = re.sub(r'^\s*CURV\s+[^\n]+', f'  CURV {-curvature}', surfaces[i], flags=re.M)
    surfaces[10] = re.sub(r'^\s*GLAS\s+[^\n]+', '', surfaces[10], flags=re.M)
    pupil_radius = energy.setting(header, 'ENPD') / 2
    object_distance = energy.setting(surfaces[0], 'DISZ')
    assert pupil_radius == 10 and object_distance == 60

    def trace(p):
        direction = np.array([p*pupil_radius/object_distance, 1.])
        return energy.trace(surfaces,set(surfaces)-{0,4,5},np.zeros(2),direction/np.linalg.norm(direction),wavelength)

    ray_raw = (r_dir/'data.txt').read_bytes()
    row = re.search(rf'^\s*{end}\s+([^\r\n]+)', energy.decode(ray_raw), re.M)
    native_ray = np.array(list(map(float,row.group(1).split()[:6])))
    point, direction = trace(.8)[end]
    native_frame_ray = np.array([0,point[0],0,0,-direction[0],direction[1]])
    assert np.max(abs(native_frame_ray[:3]-native_ray[:3])) < 1e-8
    assert np.max(abs(native_frame_ray[3:]-native_ray[3:])) < 1e-10
    f_number = .5 / abs(trace(1)[end][1][0])

    # The diagonal aperture bound covers every azimuth. Its tilted radial
    # coordinate is at most sqrt(2) times the projected intercept, whose
    # denominator is at least one minus the absolute internal ray slope.
    aperture_checks = {i: [] for i in range(1,13) if model['surfaces'][i]['SemiDiameter'] and i != 5}
    for p in np.linspace(0,1,129):
        states = trace(p)
        at_cube, cube_direction = states[3]
        slope = abs(cube_direction[0]/cube_direction[1])
        tilted_bound = np.sqrt(2)*(abs(at_cube[0])+20*slope)/(1-slope)
        for i in aperture_checks:
            aperture_checks[i].append(tilted_bound if i in (4,6) else abs(states[i][0][0]))
    pupil_margins = {i: model['surfaces'][i]['SemiDiameter'] - max(v) for i,v in aperture_checks.items()}
    assert min(pupil_margins.values()) > 0

    wave_raw = (w_dir/'data.json').read_bytes()
    wave = json.loads(energy.decode(wave_raw))
    assert not wave['messages']
    z = np.asarray(wave['dataGrids'][0]['values'],float)
    axis = (np.arange(64)-32)/31
    x,y = np.meshgrid(axis,axis)
    t = x*x+y*y
    mask = np.isfinite(z)
    full_mask = t <= 1+1e-12
    missing = np.argwhere(full_mask & ~mask)
    assert not np.any(mask & ~full_mask) and missing.tolist() == [[1,32],[63,32]]
    assert all(t[i,j] == 1 for i,j in missing)
    phase = Chebyshev.fit(t[mask],z[mask],10,domain=[0,1])
    fit_error = float(np.max(abs(phase(t[mask])-z[mask])))
    assert fit_error < 1e-8

    first = (f_dir/'data.txt').read_bytes()
    radius = float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)',energy.decode(first)).group(1))
    assert radius > 0
    def optical_path(r):
        states = trace(r)
        last = np.zeros(2)
        length,index = 0.,1.
        for i,(position,direction) in states.items():
            length += index*np.linalg.norm(position-last)
            last = position
            index = energy.bk7(wavelength) if 'GLAS BK7' in surfaces[i] else 1.
        return length,position[0],direction[0]
    phase_axis = np.unique(np.r_[t[mask], 1.])
    length,height,direction = np.asarray([optical_path(np.sqrt(q)) for q in phase_axis]).T
    transverse = height**2*(1-direction**2)
    chord = transverse/(radius+np.sqrt(radius**2-transverse))
    traced_phase = -(length-length[0]-height*direction+chord)/(wavelength*.001)
    phase_difference = float(np.max(abs(traced_phase-phase(phase_axis))))
    assert phase_difference < 3e-8
    trace_fit = Chebyshev.fit(phase_axis,traced_phase,14,domain=[0,1])

    def ideal(q): return np.zeros_like(q)
    def integrate(p,n): return energy.curve(p,f_number,wavelength,n,[0],0,ideal)
    continuum = [integrate(p,96) for p in (ideal,trace_fit)]
    wavefront_continuum = integrate(phase,96)
    phase_reference = energy.compare(wavefront_continuum,continuum[1])
    quadrature = [energy.compare(integrate(p,64),b) for p,b in zip((ideal,trace_fit),continuum)]
    u = np.pi*energy.R/(wavelength*f_number)
    airy_error = float(np.max(abs(continuum[0]-(1-j0(u)**2-j1(u)**2))))
    assert airy_error < 1e-13 and all(m['verdict']=='Pass' for m in quadrature+[phase_reference])

    core, core_curves = [], []
    for n in (32768,65536,131072):
        pitch,curves,provenance = energy.core(index,n,settings)
        values = energy.archive_json(f'core/{index}-{n}/data.json')['values']
        bin_width = values['DistanceBinWidth'][0]
        assert abs(bin_width*np.sqrt(n)/wavelength-f_number) < 1e-9
        assert abs(pitch[0]/bin_width-2*n/values['BoundedFourierSampling'][0]) < 1e-12
        core.append(dict(n=n,provenance=provenance,pitchMicrometers=pitch[0],distanceBinMicrometers=bin_width,
            tracedContinuum=[energy.compare(a,b) for a,b in zip(curves,continuum)],
            nativeWavefrontContinuum=energy.compare(curves[1],wavefront_continuum)))
        core_curves.append(curves)
    pair = [energy.compare(a,b) for a,b in zip(*core_curves[-2:])]
    assert all(m['verdict']=='Pass' for m in pair+core[-1]['tracedContinuum'])
    assert all(m['pointwise'] for m in core[-1]['tracedContinuum'])
    coarse = energy.archive_json('core/996-8192/data.json')
    assert coarse['outcome']=='Unavailable' and coarse['values']['PupilPhaseSampling']['maximumStepWaves'] > .5

    native = []
    for n in (32,256,512):
        folder = f'native/996-{n}'
        if n == 32:
            data = energy.read(e_dir/'data.json')
            cap = settings
        else:
            data = energy.archive_json(f'{folder}/data.json')
            cap = energy.archive_json(f'{folder}/captured-settings.json')
            diag = energy.archive_json(f'{folder}/result-diagnostics.json')
            assert diag['seriesCount']==0 and diag['gridCount']==0
            for a,b in zip(diag['messages'],data['messages'],strict=True):
                assert {k.lower():v for k,v in a.items()}=={k.lower():v for k,v in b.items()}
        assert not data['dataSeries'] and data['messages']
        assert cap['selectors']==settings['selectors'] and cap['properties']['SampleSize']==f'S_{n}x{n}'
        def active(p): return {k:v for k,v in p.items() if k!='SampleSize'}
        assert active(cap['properties'])==active(settings['properties'])
        for key in ('configuration','field','wavelength','input','useRayAiming'):
            assert cap['request'][key]==settings['request'][key]
        native.append(dict(n=n,valid=False,diagnostics=data['messages']))

    return dict(index=index,lens=lens,configuration=2,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',nativeDirect=native,
        frozenNativeSha256=energy.sha((e_dir/'data.json').read_bytes()),
        independent=dict(workingFNumber=f_number,exitPupilDistanceMillimeters=radius,wavelengthMicrometers=wavelength,
            nativeRay=native_ray.tolist(),rayInNativeFrame=native_frame_ray.tolist(),rayReportSha256=energy.sha(ray_raw),
            firstOrderReportSha256=energy.sha(first),wavefrontSha256=energy.sha(wave_raw),
            wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()),
            wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            radialClearanceCheckSamples=129,clearanceMarginMillimeters=pupil_margins,
            missingOnlyOnUnitRim=missing.tolist(),illuminatedNodes=int(mask.sum()),
            wavefrontFitMaxWaves=fit_error,rayPhaseVsWavefrontMaxWaves=phase_difference,
            phaseRadiusSquared=phase_axis.tolist(),physicalOpdWaves=traced_phase.tolist(),
            nativeWavefrontOpdWaves=phase(phase_axis).tolist(),quadrature=quadrature,phaseReference=phase_reference,
            idealClosedFormMaxError=airy_error,ideal=continuum[0].tolist(),actualOpticalPath=continuum[1].tolist(),
            actualNativeWavefront=wavefront_continuum.tolist()),
        core=core,finestCorePair=pair,core8192Phase=coarse['values']['PupilPhaseSampling'])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output',type=Path)
    parser.add_argument('--repo',type=Path,default=HERE.parents[3])
    parser.add_argument('--corpus',type=Path)
    args = parser.parse_args()
    manifest = HERE/'manifest.json'
    energy.configure(args.repo,args.corpus,manifest)
    result = run()
    args.output.write_text(json.dumps(dict(policy=dict(energy.policy,minimumCoverage=.95),
        policySha256=energy.sha(energy.POLICY_PATH.read_bytes()),originalRadiusMicrometers=energy.R.tolist(),
        manifestSha256=energy.sha(manifest.read_bytes()),archivedLogicalPaths=sorted(energy.roles),result=result),
        ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    main()
