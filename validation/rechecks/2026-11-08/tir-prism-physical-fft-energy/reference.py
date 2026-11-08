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

SOURCE_SHA = '8a74f066e49a037aa0307c5f8a2be7f95d1940777edbe99523b74dad3f57de8d'
FINGERPRINT = '3e65da7a7f643b12e5dacf4a0fcfdb373923d8de6e269329695ad0f8388eb835'


def run():
    index = 2684
    path, lens, header, surfaces = energy.source(index)
    raw = path.read_bytes()
    assert energy.sha(raw) == SOURCE_SHA
    e_dir, w_dir, r_dir, f_dir = (energy.frozen(n) for n in (2684,2676,2672,2671))
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
    assert not settings['request']['useRayAiming'] and settings['request']['configuration'] == 1
    assert [ray_settings['properties'][k] for k in ('Hx','Hy','Px','Py')] == [0,0,0,.8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir/'model.json')
    assert model['aperture']['ApodizationType'] == 'Uniform'

    # The two 45-degree coordinate breaks and the modeled specular plane
    # keep the glass branch homogeneous through the bend. Unfolding preserves
    # the optical path and negates the post-fold local axial coordinate.
    assert all(energy.setting(surfaces[i],'PARM 3')==45 for i in (9,11))
    assert all(energy.setting(surfaces[i],f'PARM {j}')==0 for i in (9,11) for j in (1,2,4,5,6))
    assert energy.setting(surfaces[9],'DISZ')==energy.setting(surfaces[10],'DISZ')==0
    assert 'GLAS MIRROR' in surfaces[10] and energy.setting(surfaces[10],'CURV')==0
    assert 'GLAS BK7' in surfaces[8] and model['surfaces'][10]['Material']=='MIRROR'
    assert np.isposinf(energy.setting(surfaces[0],'DISZ'))
    end = max(surfaces)
    for i in (11,12):
        gap = energy.setting(surfaces[i],'DISZ')
        assert gap==model['surfaces'][i]['Thickness']==-50
        surfaces[i] = re.sub(r'^\s*DISZ\s+\S+',f'  DISZ {-gap}',surfaces[i],flags=re.M)
    pupil_radius = energy.setting(header,'ENPD')/2
    assert pupil_radius==25 and model['aperture']['ApertureValue']==2*pupil_radius

    def trace(p):
        return energy.trace(surfaces,set(surfaces)-{0,9,10,11},np.array([p*pupil_radius,0.]),
                            np.array([0.,1.]),wavelength)

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
    aperture_checks = {i: [] for i in range(1,end+1) if model['surfaces'][i]['SemiDiameter']}
    for p in np.linspace(0,1,129):
        states = trace(p)
        at_cube, cube_direction = states[8]
        slope = abs(cube_direction[0]/cube_direction[1])
        tilted_bound = np.sqrt(2)*(abs(at_cube[0])+50*slope)/(1-slope)
        for i in aperture_checks:
            aperture_checks[i].append(tilted_bound if i==10 else abs(states[i][0][0]))
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
    assert not np.any(mask & ~full_mask) and missing.tolist() == [[32,1],[32,63]]
    assert all(t[i,j] == 1 for i,j in missing)
    phase = Chebyshev.fit(t[mask],z[mask],10,domain=[0,1])
    fit_error = float(np.max(abs(phase(t[mask])-z[mask])))
    assert fit_error < 1e-8

    first = (f_dir/'data.txt').read_bytes()
    radius = float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)',energy.decode(first)).group(1))
    assert radius > 0
    def optical_path(r):
        states = trace(r)
        last = np.array([r*pupil_radius,0.])
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

    native = []
    for n in (32,256,512):
        folder = f'native/2684-{n}'
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

    return dict(index=index,lens=lens,configuration=1,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
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
        core=core,finestCorePair=pair)


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
