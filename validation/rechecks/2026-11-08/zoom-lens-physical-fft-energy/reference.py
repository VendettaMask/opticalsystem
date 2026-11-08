import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0, j1


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('meridional', HERE.parent / 'optimization-demo-physical-fft-energy/reference.py')
optics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(optics)
energy = optics.energy

SOURCE_SHA = '7103aaf9130acc1b94a864267348ce1df6a7651910adc07379a9321a4670adc9'
FINGERPRINT = '84e537733dd618d375e7ffceb9797e85303c622b525d82105e91e66d1eed6b76'


def run():
    index = 3008
    path,lens,header,surfaces = energy.source(index)
    raw = path.read_bytes()
    assert energy.sha(raw)==SOURCE_SHA and 'UNIT MM' in header
    source = energy.decode(raw)
    e_dir,w_dir,r_dir,f_dir = (energy.frozen(i) for i in (3008,3000,2996,2995))
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
    assert not settings['request']['useRayAiming'] and settings['request']['configuration']==3
    assert [ray_settings['properties'][k] for k in ('Hx','Hy','Px','Py')]==[0,0,0,.8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir/'model.json')
    assert model['aperture']['ApodizationType']=='Uniform' and model['aperture']['ApertureType']=='ImageSpaceFNum'
    assert np.isposinf(energy.setting(surfaces[0],'DISZ'))
    gaps = {int(i):float(v) for i,v in re.findall(r'^THIC\s+(\d+)\s+3\s+([^\s]+)',source,re.M)}
    assert set(gaps)=={8,15}
    for i,gap in gaps.items():
        assert model['surfaces'][i]['Thickness']==gap
        surfaces[i] = re.sub(r'^\s*DISZ\s+[^\n]+',f'  DISZ {gap}',surfaces[i],flags=re.M)
    aspheres = sorted(i for i,s in surfaces.items() if 'TYPE EVENASPH' in s)
    assert aspheres==[5,8,9]

    ray_raw = (r_dir/'data.txt').read_bytes()
    report = energy.decode(ray_raw).split('近轴光线追迹数据')[0]
    native_rays = {int(i):np.array(list(map(float,row.split()[:9])))
                   for i,row in re.findall(r'^\s*(\d+)\s+([^\r\n]+)',report,re.M)}
    assert sorted(native_rays)==sorted(surfaces)[1:]
    glasses,indices = optics.refractive_indices(surfaces,native_rays)
    stop = model['surfaces'][1]
    assert stop['IsStop'] and abs(stop['SemiDiameter']*.8-native_rays[1][1])<1e-9
    radius = stop['SemiDiameter']
    def trace(p): return optics.trace_meridian(surfaces,indices,p*radius)
    sample = trace(.8)
    errors = abs(np.array([sample[i][:4]-native_rays[i][[1,2,4,5]] for i in sample]))
    assert errors[:,:2].max()<3e-8 and errors[:,2:].max()<5e-10
    end = max(surfaces)
    f_number = .5/abs(trace(1)[end][2])

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
    assert fit_error<2e-8

    first = (f_dir/'data.txt').read_bytes()
    exit_pupil = abs(float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)',energy.decode(first)).group(1)))
    phase_axis = np.unique(t[mask])
    height,_,direction,_,optical_path = np.asarray([trace(np.sqrt(q))[end] for q in phase_axis]).T
    transverse = height**2*(1-direction**2)
    chord = transverse/(exit_pupil+np.sqrt(exit_pupil**2-transverse))
    physical_phase = -(optical_path-optical_path[0]-height*direction+chord)/(wavelength*.001)
    phase_difference = float(np.max(abs(physical_phase-phase(phase_axis))))
    assert phase_difference<2e-6
    trace_fit = Chebyshev.fit(phase_axis,physical_phase,14,domain=[0,1])

    def ideal(q): return np.zeros_like(q)
    def integrate(p,n): return energy.curve(p,f_number,wavelength,n,[0],0,ideal)
    continuum = [integrate(p,96) for p in (ideal,phase)]
    optical_path_continuum = integrate(trace_fit,96)
    phase_reference = energy.compare(optical_path_continuum,continuum[1])
    quadrature = [energy.compare(integrate(p,64),b) for p,b in zip((ideal,phase),continuum)]
    u = np.pi*energy.R/(wavelength*f_number)
    airy_error = float(np.max(abs(continuum[0]-(1-j0(u)**2-j1(u)**2))))
    assert airy_error<1e-13 and all(m['verdict']=='Pass' for m in quadrature+[phase_reference])

    def inclusive(x,y): return x*x+y*y<=1
    def exclusive(x,y): return x*x+y*y<1-1e-13
    native,native_curves = [],[]
    for n in (2048,4096):
        curves,centers = energy.native(index,n,settings)
        stage = dict(n=n,centroidMicrometers=centers,continuum=[energy.compare(a,b) for a,b in zip(curves,continuum)])
        for method,boundary,interpolate in [('directPhase',inclusive,False),('nativeSampling',inclusive,True),
                                            ('strictOuterNativeSampling',exclusive,True)]:
            finite = [energy.finite(p,n,f_number,wavelength,boundary,interpolate_phase=interpolate,
                                   native_knots=interpolate) for p in (ideal,phase)]
            stage[method] = [dict(energy.compare(a,b),centroidMicrometers=center) for (a,center),b in zip(finite,curves)]
        assert all(m['verdict']=='Pass' for m in stage['nativeSampling'])
        assert max(m['nrmse'] for m in stage['nativeSampling'])<1e-5
        assert np.max(abs(np.asarray(centers)-np.asarray([m['centroidMicrometers'] for m in stage['nativeSampling']])))<5e-8
        native.append(stage)
        native_curves.append(curves)

    core,core_curves = [],[]
    for n in (32768,65536,131072):
        pitch,curves,provenance = energy.core(index,n,settings)
        values = energy.archive_json(f'core/{index}-{n}/data.json')['values']
        bin_width = values['DistanceBinWidth'][0]
        assert abs(bin_width*np.sqrt(n)/wavelength-f_number)<1e-7
        assert abs(pitch[0]/bin_width-2*n/values['BoundedFourierSampling'][0])<1e-12
        core.append(dict(n=n,provenance=provenance,pitchMicrometers=pitch[0],distanceBinMicrometers=bin_width,
            continuum=[energy.compare(a,b) for a,b in zip(curves,continuum)],
            opticalPathContinuum=energy.compare(curves[1],optical_path_continuum)))
        core_curves.append(curves)
    pair = [energy.compare(a,b) for a,b in zip(*core_curves[-2:])]
    assert all(m['verdict']=='Pass' and m['pointwise'] for m in pair+core[-1]['continuum'])
    _,core4096,provenance4096 = energy.core(index,4096,settings)
    same4096 = [energy.compare(a,b) for a,b in zip(core4096,native_curves[-1])]
    assert all(m['verdict']=='Pass' for m in same4096)

    return dict(index=index,lens=lens,configuration=3,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',
        frozenNative=dict(dataSha256=energy.sha((e_dir/'data.json').read_bytes()),diagnostics=energy.read(e_dir/'data.json')['messages']),
        independent=dict(workingFNumber=f_number,exitPupilDistanceMillimeters=exit_pupil,wavelengthMicrometers=wavelength,
            stopRadiusMillimeters=radius,asphericalSurfaces=aspheres,glassIndices=glasses,rayReportSha256=energy.sha(ray_raw),
            maximumRayCoordinateErrorMillimeters=float(errors[:,:2].max()),maximumRayDirectionError=float(errors[:,2:].max()),
            firstOrderReportSha256=energy.sha(first),wavefrontSha256=energy.sha(wave_raw),
            wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()),illuminatedNodes=int(mask.sum()),
            wavefrontFitMaxWaves=fit_error,rayPhaseVsWavefrontMaxWaves=phase_difference,phaseRadiusSquared=phase_axis.tolist(),
            physicalOpdWaves=physical_phase.tolist(),nativeWavefrontOpdWaves=phase(phase_axis).tolist(),quadrature=quadrature,
            phaseReference=phase_reference,idealClosedFormMaxError=airy_error,ideal=continuum[0].tolist(),
            actualNativeWavefront=continuum[1].tolist(),actualOpticalPath=optical_path_continuum.tolist()),
        native=native,finestNativePair=[energy.compare(a,b) for a,b in zip(*native_curves)],
        core=core,finestCorePair=pair,same4096=dict(provenance=provenance4096,metrics=same4096))


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
