import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.optimize import newton
from scipy.special import j0, j1


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_SHA = '6a40b596ea7f4a8ee47f9038613fa887bbdea38ae195b1404e3c9da848dd2af9'
FINGERPRINT = '59251a2d6b84fb87463a8d106d9b21ac9328f9ed2b1b36b50554d21fd305684a'


def refractive_indices(surfaces, native_rays):
    material = {0:'Air'}
    for i in sorted(surfaces)[1:]:
        match = re.search(r'GLAS\s+([^\s]+)',surfaces[i])
        material[i] = match.group(1) if match else 'Air'
    glasses = sorted(set(material.values()) - {'Air'})
    matrix, rhs = [], []
    for i in sorted(native_rays):
        if material[i] == material[i-1]:
            continue
        ray = native_rays[i]
        tangent = np.array([-ray[8],ray[7]])
        incident = native_rays[i-1][4:6] if i>1 else np.array([0.,1.])
        sine_in, sine_out = np.dot(incident,tangent), np.dot(ray[4:6],tangent)
        weight = min(abs(sine_in),abs(sine_out))
        matrix.append([weight*((material[i]==g)-(material[i-1]==g)) for g in glasses])
        rhs.append(weight*np.log(sine_in/sine_out))
    assert np.linalg.matrix_rank(matrix) == len(glasses)
    indices = dict(zip(glasses,map(float,np.exp(np.linalg.lstsq(matrix,rhs,rcond=None)[0]))),Air=1.)
    return indices,{i:indices[name] for i,name in material.items()}


def trace_meridian(surfaces, indices, entrance_height):
    pos, direction = np.array([entrance_height,0.]), np.array([0.,1.])
    z, optical_path = 0.,0.
    states = {}
    for i in sorted(surfaces)[1:]:
        s = surfaces[i]
        c,k = energy.setting(s,'CURV'),energy.setting(s,'CONI')
        asphere = [(2*j,energy.setting(s,f'PARM {j}')) for j in range(1,9)] if 'TYPE EVENASPH' in s else []
        def radical(r): return np.sqrt(1-(1+k)*(c*r)**2)
        def sag(r): return c*r*r/(1+radical(r)) + sum(a*r**power for power,a in asphere)
        def slope(r): return c*r/radical(r) + sum(power*a*r**(power-1) for power,a in asphere)
        gap = (z-pos[1])/direction[1]
        if c or asphere:
            def hit(t):
                y,actual_z = pos + t*direction
                return actual_z-z-sag(y)
            def derivative(t): return direction[1]-slope(pos[0]+t*direction[0])*direction[0]
            gap = newton(hit,gap,fprime=derivative,tol=1e-13)
        pos = pos + gap*direction
        optical_path += gap*indices[i-1]
        normal = np.array([slope(pos[0]),-1.])
        normal /= np.linalg.norm(normal)
        cosine = -np.dot(direction,normal)
        mu = indices[i-1]/indices[i]
        direction = mu*direction + (mu*cosine-np.sqrt(1-mu*mu*(1-cosine*cosine)))*normal
        states[i] = np.array([pos[0],pos[1]-z,*direction,optical_path])
        z += energy.setting(s,'DISZ')
    return states


def run():
    index = 1298
    path,lens,header,surfaces = energy.source(index)
    assert energy.sha(path.read_bytes())==SOURCE_SHA and 'UNIT MM' in header
    e_dir,w_dir,r_dir,f_dir = (energy.frozen(i) for i in (1298,1290,1286,1285))
    settings,wave_settings,ray_settings,first_settings = (energy.read(d/'captured-settings.json')
                                                          for d in (e_dir,w_dir,r_dir,f_dir))
    assert settings['selectors']==wave_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for control in (wave_settings,ray_settings,first_settings):
        for key in ('configuration','input','primaryWavelengthMicrometers'):
            assert control['request'][key]==settings['request'][key]
    for control in (wave_settings,ray_settings):
        for key in ('useRayAiming','field','wavelength'):
            assert control['request'][key]==settings['request'][key]
    assert not settings['request']['useRayAiming'] and settings['request']['configuration']==1
    assert [ray_settings['properties'][k] for k in ('Hx','Hy','Px','Py')]==[0,0,0,.8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir/'model.json')
    assert model['aperture']['ApodizationType']=='Uniform'
    assert np.isposinf(energy.setting(surfaces[0],'DISZ'))
    pupil_radius = energy.setting(header,'ENPD')/2
    assert pupil_radius==5 and model['aperture']['ApertureValue']==2*pupil_radius

    ray_raw = (r_dir/'data.txt').read_bytes()
    report = energy.decode(ray_raw).split('近轴光线追迹数据')[0]
    native_rays = {int(i):np.array(list(map(float,row.split()[:9])))
                   for i,row in re.findall(r'^\s*(\d+)\s+([^\r\n]+)',report,re.M)}
    assert sorted(native_rays)==sorted(surfaces)[1:]
    glasses,indices = refractive_indices(surfaces,native_rays)
    def trace(p): return trace_meridian(surfaces,indices,p*pupil_radius)
    sample = trace(.8)
    ray_error = abs(np.array([sample[i][:4]-native_rays[i][[1,2,4,5]] for i in sample]))
    assert ray_error[:,:2].max()<1e-8 and ray_error[:,2:].max()<1e-10
    end = max(surfaces)
    f_number = .5/abs(trace(1)[end][2])
    aperture_checks = {s['SurfaceNumber']:[] for s in model['surfaces'][1:] if s['SemiDiameter']}
    for p in np.linspace(0,1,129):
        states = trace(p)
        for i in aperture_checks:
            aperture_checks[i].append(abs(states[i][0]))
    margins = {i:model['surfaces'][i]['SemiDiameter']-max(v) for i,v in aperture_checks.items()}
    assert min(margins.values())>0

    wave_raw = (w_dir/'data.json').read_bytes()
    wave = json.loads(energy.decode(wave_raw))
    assert not wave['messages']
    z = np.asarray(wave['dataGrids'][0]['values'],float)
    axis = (np.arange(64)-32)/31
    x,y = np.meshgrid(axis,axis)
    t = x*x+y*y
    mask = np.isfinite(z)
    assert np.array_equal(mask,t<=1+1e-12)
    phase = Chebyshev.fit(t[mask],z[mask],10,domain=[0,1])
    fit_error = float(np.max(abs(phase(t[mask])-z[mask])))
    assert fit_error<1e-8

    first_order = (f_dir/'data.txt').read_bytes()
    exit_pupil = abs(float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)',energy.decode(first_order)).group(1)))
    phase_axis = np.unique(t[mask])
    height,_,direction,_,optical_path = np.asarray([trace(np.sqrt(q))[end] for q in phase_axis]).T
    transverse = height**2*(1-direction**2)
    chord = transverse/(exit_pupil+np.sqrt(exit_pupil**2-transverse))
    physical_phase = -(optical_path-optical_path[0]-height*direction+chord)/(wavelength*.001)
    phase_difference = float(np.max(abs(physical_phase-phase(phase_axis))))
    assert phase_difference<5e-7
    trace_fit = Chebyshev.fit(phase_axis,physical_phase,12,domain=[0,1])

    def ideal(q): return np.zeros_like(q)
    def integrate(p,n): return energy.curve(p,f_number,wavelength,n,[0],0,ideal)
    continuum = [integrate(p,96) for p in (ideal,trace_fit)]
    native_wavefront_continuum = integrate(phase,96)
    phase_reference = energy.compare(native_wavefront_continuum,continuum[1])
    quadrature = [energy.compare(integrate(p,64),b) for p,b in zip((ideal,trace_fit),continuum)]
    u = np.pi*energy.R/(wavelength*f_number)
    airy_error = float(np.max(abs(continuum[0]-(1-j0(u)**2-j1(u)**2))))
    assert airy_error<1e-13 and all(m['verdict']=='Pass' for m in quadrature+[phase_reference])

    core,core_curves = [],[]
    for n in (8192,32768,65536,131072):
        pitch,curves,provenance = energy.core(index,n,settings)
        values = energy.archive_json(f'core/{index}-{n}/data.json')['values']
        bin_width = values['DistanceBinWidth'][0]
        assert abs(bin_width*np.sqrt(n)/wavelength-f_number)<1e-8
        assert abs(pitch[0]/bin_width-2*n/values['BoundedFourierSampling'][0])<1e-12
        core.append(dict(n=n,provenance=provenance,pitchMicrometers=pitch[0],distanceBinMicrometers=bin_width,
            tracedContinuum=[energy.compare(a,b) for a,b in zip(curves,continuum)],
            nativeWavefrontContinuum=energy.compare(curves[1],native_wavefront_continuum)))
        core_curves.append(curves)
    pair = [energy.compare(a,b) for a,b in zip(*core_curves[-2:])]
    assert all(m['verdict']=='Pass' and m['pointwise'] for m in pair+core[-1]['tracedContinuum'])

    native = []
    for n in (32,256,512):
        folder = f'native/1298-{n}'
        if n==32:
            data,cap = energy.read(e_dir/'data.json'),settings
        else:
            data = energy.archive_json(f'{folder}/data.json')
            cap = energy.archive_json(f'{folder}/captured-settings.json')
            diag = energy.archive_json(f'{folder}/result-diagnostics.json')
            assert diag['seriesCount']==len(data['dataSeries']) and diag['gridCount']==len(data['dataGrids'])
            for a,b in zip(diag['messages'],data['messages'],strict=True):
                assert {k.lower():v for k,v in a.items()}=={k.lower():v for k,v in b.items()}
        assert data['messages']
        assert cap['selectors']==settings['selectors'] and cap['properties']['SampleSize']==f'S_{n}x{n}'
        def active(p): return {k:v for k,v in p.items() if k!='SampleSize'}
        assert active(cap['properties'])==active(settings['properties'])
        for key in ('configuration','field','wavelength','input','useRayAiming'):
            assert cap['request'][key]==settings['request'][key]
        native.append(dict(n=n,valid=False,rawSeriesCount=len(data['dataSeries']),diagnostics=data['messages']))

    return dict(index=index,lens=lens,configuration=1,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',nativeDirect=native,
        frozenNativeSha256=energy.sha((e_dir/'data.json').read_bytes()),
        independent=dict(workingFNumber=f_number,exitPupilDistanceMillimeters=exit_pupil,wavelengthMicrometers=wavelength,
            glassIndices=glasses,rayReportSha256=energy.sha(ray_raw),
            maximumRayCoordinateErrorMillimeters=float(ray_error[:,:2].max()),maximumRayDirectionError=float(ray_error[:,2:].max()),
            firstOrderReportSha256=energy.sha(first_order),wavefrontSha256=energy.sha(wave_raw),
            wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()),illuminatedNodes=int(mask.sum()),
            radialClearanceCheckSamples=129,clearanceMarginMillimeters=margins,
            wavefrontFitMaxWaves=fit_error,rayPhaseVsWavefrontMaxWaves=phase_difference,phaseRadiusSquared=phase_axis.tolist(),
            physicalOpdWaves=physical_phase.tolist(),nativeWavefrontOpdWaves=phase(phase_axis).tolist(),
            quadrature=quadrature,phaseReference=phase_reference,idealClosedFormMaxError=airy_error,
            ideal=continuum[0].tolist(),actualOpticalPath=continuum[1].tolist(),actualNativeWavefront=native_wavefront_continuum.tolist()),
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
