import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial.chebyshev import chebder, chebval2d, chebvander2d
from scipy.interpolate import CubicSpline
from scipy.special import jv


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_SHA = '5c9cf177112ee81f9ac34992fb5264cf4d4caf9d0a7f41a42a4d609ba04c2f7c'
FINGERPRINT = '6848e1c800a9f3cb2577d872e164e8c8871f29a2470f1f30a4b66644f59d8df2'


def trace_factory(surfaces, indices, pupil_radius):
    frames = {}
    vertex, rotation = np.zeros(3), np.eye(3)
    for i in sorted(surfaces)[1:]:
        surface = surfaces[i]
        if 'TYPE COORDBRK' in surface:
            assert all(energy.setting(surface, f'PARM {k}') == 0 for k in (1, 2, 4, 5, 6))
            angle = np.deg2rad(energy.setting(surface, 'PARM 3'))
            c,s = np.cos(angle),np.sin(angle)
            rotation = rotation @ np.array([[1,0,0],[0,c,-s],[0,s,c]])
        frames[i] = vertex.copy(),rotation.copy()
        vertex = vertex + energy.setting(surface,'DISZ')*rotation[:,2]

    def trace(px,py,ax=0.,ay=0.):
        point = np.array([px*pupil_radius,py*pupil_radius,0.])
        direction = np.array([ax,ay,1.])
        direction /= np.linalg.norm(direction)
        length,states = 0.,{}
        for i,(vertex,rotation) in frames.items():
            position = rotation.T @ (point-vertex)
            outgoing = rotation.T @ direction
            c = energy.setting(surfaces[i],'CURV')
            t = -position[2]/outgoing[2]
            if c:
                for _ in range(7):
                    candidate = position+t*outgoing
                    r2 = np.dot(candidate[:2],candidate[:2])
                    root = np.sqrt(1-c*c*r2)
                    t -= (candidate[2]-c*r2/(1+root))/(outgoing[2]-c/root*np.dot(candidate[:2],outgoing[:2]))
            intercept = position+t*outgoing
            length += t*indices[i-1]
            normal = np.r_[c*intercept[:2]/np.sqrt(1-c*c*np.dot(intercept[:2],intercept[:2])),-1.]
            normal /= np.linalg.norm(normal)
            cosine = -np.dot(normal,outgoing)
            ratio = indices[i-1]/indices[i]
            if ratio != 1:
                outgoing = ratio*outgoing + (ratio*cosine-np.sqrt(1-ratio*ratio*(1-cosine*cosine)))*normal
            point = vertex+rotation@intercept
            direction = rotation@outgoing
            states[i] = intercept,outgoing,length
        return states
    return trace


def finite(n, wavelength, f, fx, fy, coefficients, interpolate_phase):
    sx,sy = fx/f,fy/f
    extent = int(np.ceil(np.sqrt(n)/min(sx,sy)))
    axis = np.arange(-extent,extent+1)
    x,y = np.meshgrid(sx*axis/np.sqrt(n),sy*axis/np.sqrt(n))
    active = x*x+y*y<=1
    value = np.zeros(x.shape)
    value[active] = chebval2d(x[active]/(fx/fy),y[active],coefficients)
    if interpolate_phase:
        ix,iy = np.meshgrid(axis[1:-1],axis[1:-1])
        eligible = ((ix+iy)%2==0)&((ix!=0)|(iy!=0))
        eligible &= active[1:-1,1:-1]&active[:-2,1:-1]&active[2:,1:-1]&active[1:-1,:-2]&active[1:-1,2:]
        average = (value[:-2,1:-1]+value[2:,1:-1]+value[1:-1,:-2]+value[1:-1,2:])/4
        value[1:-1,1:-1][eligible] = average[eligible]
    pupil = active*np.exp(-2j*np.pi*value)
    m = 2*n
    pitch = wavelength*f/np.sqrt(n)
    power = int(active.sum())
    period = np.arange(-m//2+1,m//2+1)
    def centroid(arr):
        marginal = np.sum(abs(np.fft.fft(arr,n=m,axis=1))**2,axis=0)
        assert abs(marginal.sum()/(m*power)-1)<1e-13
        return pitch*np.dot(period,marginal[period%m])/(m*power)
    cx,cy = centroid(pupil),centroid(pupil.T)
    half = int(np.ceil((5+max(abs(cx),abs(cy)))/pitch+.5))
    k = np.arange(-half,half+1)
    basis = np.exp(-2j*np.pi*k[:,None]*axis[None,:]/m)
    weight = (abs(basis@pupil@basis.T)**2/(m*m*power)).ravel()
    distance = np.hypot(k[None,:]*pitch-cx,k[:,None]*pitch-cy).ravel()
    ix = np.argsort(distance)
    distance,weight = distance[ix],weight[ix]
    mass = np.r_[0.,np.cumsum(weight)]
    moment = np.r_[0.,np.cumsum(weight*distance)]
    radii = np.linspace(0,5,n)
    low,high = np.searchsorted(distance,radii-pitch/2),np.searchsorted(distance,radii+pitch/2)
    curve = mass[low]+((radii+pitch/2)*(mass[high]-mass[low])-(moment[high]-moment[low]))/pitch
    curve[0] = 0
    return np.interp(energy.R,radii,curve),np.array([cx,cy]),power


def continuum(coefficients, wavelength, fx, fy, fine):
    n_rad,n_ang,n_order,n_freq = (64,64,28,1025) if fine else (48,48,20,513)
    r,wr = energy.legendre(n_rad)
    theta = 2*np.pi*np.arange(128)/128
    sx = fx/fy
    x = r[:,None]*np.cos(theta)/sx
    y = r[:,None]*np.sin(theta)
    opd = chebval2d(x,y,coefficients)
    dx = chebval2d(x,y,chebder(coefficients,axis=0))/sx
    dy = chebval2d(x,y,chebder(coefficients,axis=1))
    # The Fourier first moment gives the complete-image centroid from the
    # average phase gradient on the uniformly illuminated physical pupil.
    slope = np.array([2*np.dot(wr*r,dx.mean(axis=1)),2*np.dot(wr*r,dy.mean(axis=1))])
    center = -2*wavelength*np.array([fx,fy])*slope
    angular = np.fft.fft(np.exp(-2j*np.pi*opd),axis=1)/len(theta)
    q_axis = np.linspace(0,np.pi*(np.linalg.norm(center)+5)/(wavelength*min(fx,fy))+.01,n_freq)
    orders = np.arange(-n_order,n_order+1)
    transforms = np.empty((n_freq,len(orders)),complex)
    for i,m in enumerate(orders):
        transforms[:,i] = (jv(m,q_axis[:,None]*r)@(angular[:,m%len(theta)]*wr*r))*(-1j)**int(m)
    harmonic = CubicSpline(q_axis,transforms,axis=0)
    t,tw = energy.legendre(n_rad)
    angle = 2*np.pi*np.arange(n_ang)/n_ang
    result = []
    # The normalized 2D Fourier amplitudes integrate to pi in q space.
    # Apply the Jacobian and integrate a circle in the original image plane.
    normalizer = np.pi/(wavelength**2*fx*fy)
    for bounds in np.array_split(energy.R,32):
        rr = bounds[:,None,None]*t[None,:,None]
        qx = (center[0]+rr*np.cos(angle))*np.pi/(wavelength*fx)
        qy = (center[1]+rr*np.sin(angle))*np.pi/(wavelength*fy)
        q,phi = np.hypot(qx,qy),np.arctan2(qy,qx)
        amplitude = np.einsum('...m,...m->...',harmonic(q),np.exp(1j*phi[...,None]*orders),optimize=True)
        result.extend(normalizer*2*np.pi*bounds**2*((abs(amplitude)**2).mean(axis=2)@(tw*t)))
    return np.asarray(result),center


def run():
    index = 2572
    path,lens,header,surfaces = energy.source(index)
    assert energy.sha(path.read_bytes())==SOURCE_SHA and 'UNIT MM' in header
    e_dir,w_dir,r_dir,f_dir = (energy.frozen(i) for i in (2572,2564,2560,2559))
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
    assert settings['request']['configuration']==1 and not settings['request']['useRayAiming']
    assert [ray_settings['properties'][k] for k in ('Hx','Hy','Px','Py')]==[0,0,0,.8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir/'model.json')
    assert model['aperture']['ApodizationType']=='Uniform'
    pupil_radius = energy.setting(header,'ENPD')/2
    assert pupil_radius==10 and model['aperture']['ApertureValue']==2*pupil_radius

    ray_raw = (r_dir/'data.txt').read_bytes()
    report = energy.decode(ray_raw).split('近轴光线追迹数据')[0]
    rays = {int(i):np.asarray(list(map(float,row.split()[:9])))
            for i,row in re.findall(r'^\s*(\d+)\s+([^\r\n]+)',report,re.M)}
    indices = {i:1. for i in surfaces}
    for i in (4,5):
        ray = rays[i]
        tangent = np.array([-ray[8],ray[7]])
        indices[i] = indices[i-1]*np.dot(rays[i-1][4:6],tangent)/np.dot(ray[4:6],tangent)
    assert abs(indices[4]-energy.bk7(wavelength))<2e-10
    trace = trace_factory(surfaces,indices,pupil_radius)
    sample = trace(0,.8)
    errors = abs(np.asarray([np.r_[point-rays[i][:3],direction-rays[i][3:6]]
                            for i,(point,direction,_) in sample.items()]))
    assert errors[:,:3].max()<2e-8 and errors[:,3:].max()<2e-10
    chief,chief_direction,chief_path = trace(0,0)[10]
    marginal = [trace(x,y)[10][1] for x,y in [(0,1),(0,-1),(1,0),(-1,0)]]
    aperture = np.asarray([np.linalg.norm(np.cross(chief_direction,d)) for d in marginal])
    f = .5/np.sqrt(np.mean(aperture**2))
    fx,fy = .5/np.mean(aperture[2:]),.5/np.mean(aperture[:2])
    sx = fx/fy

    wave_raw = (w_dir/'data.json').read_bytes()
    wave = json.loads(energy.decode(wave_raw))
    assert not wave['messages']
    z = np.asarray(wave['dataGrids'][0]['values'],float)
    axes = (np.arange(64)-32)/31
    x,y = np.meshgrid(axes,axes)
    valid = np.isfinite(z)
    assert np.array_equal(valid,(sx*x)**2+y*y<=1+1e-12)

    def fit(values):
        return np.linalg.lstsq(chebvander2d(x[valid],y[valid],[14,14]),values,rcond=1e-12)[0].reshape(15,15)
    coefficients = fit(z[valid])
    fit_error = float(np.max(abs(chebval2d(x[valid],y[valid],coefficients)-z[valid])))
    assert fit_error<1e-8
    positions,directions,paths = (np.asarray(q) for q in zip(*[trace(a*sx,b)[10] for a,b in zip(x[valid],y[valid])]))

    # Estimate the physical reference sphere from the meridional stop pencil.
    # Centered differences at two perturbations cancel the leading finite-step error.
    pencil = []
    for h in (1e-3,5e-4):
        a,d0,_ = trace(0,0,0,-h)[10]
        b,d1,_ = trace(0,0,0,h)[10]
        pencil.append((b[1]-a[1],d1[1]/d1[2]-d0[1]/d0[2]))
    radius = (8*pencil[1][0]-pencil[0][0])/(8*pencil[1][1]-pencil[0][1])/chief_direction[2]
    first = (f_dir/'data.txt').read_bytes()
    reported_radius = abs(float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)',energy.decode(first)).group(1)))
    delta = positions-chief
    dot = np.einsum('ij,ij->i',delta,directions)
    transverse = np.einsum('ij,ij->i',delta,delta)-dot*dot
    def physical(r):
        chord = transverse/(r+np.sqrt(r*r-transverse))
        return -(paths-chief_path-dot+chord)/(wavelength*.001)
    direct_phase = physical(radius)
    phase_error = float(np.max(abs(direct_phase-z[valid])))
    reported_phase_error = float(np.max(abs(physical(reported_radius)-z[valid])))
    assert phase_error<2e-6 and reported_phase_error<5e-6

    ideal = np.zeros(coefficients.shape)
    coarse = [continuum(c,wavelength,fx,fy,False)[0] for c in (ideal,coefficients)]
    continuous = [continuum(c,wavelength,fx,fy,True) for c in (ideal,coefficients)]
    curves,centers = [a[0] for a in continuous],[a[1] for a in continuous]
    optical_curve,optical_center = continuum(fit(direct_phase),wavelength,fx,fy,True)
    independent_phase = energy.compare(optical_curve,curves[1])
    quadrature = [energy.compare(a,b) for a,b in zip(coarse,curves)]
    assert all(x['verdict']=='Pass' for x in quadrature+[independent_phase])

    native,native_curves = [],[]
    for n in (256,512):
        capture,native_center = energy.native(index,n,settings)
        native_center = np.asarray(native_center)
        stage = dict(n=n,centroidMicrometers=native_center.tolist(),continuum=[energy.compare(a,b) for a,b in zip(capture,curves)])
        for interpolate in (False,True):
            fit_curves = [finite(n,wavelength,f,fx,fy,c,interpolate) for c in (ideal,coefficients)]
            name = 'nativeSampling' if interpolate else 'directPhase'
            stage[name] = [dict(energy.compare(a,b),centroidMicrometers=(c+chief[:2]*1000).tolist(),pupilCells=power)
                           for (a,c,power),b in zip(fit_curves,capture)]
        values = stage['nativeSampling']
        center_error = abs(np.asarray([s['centroidMicrometers'] for s in values])-native_center)
        assert center_error[:,0].max()<5e-5 and center_error[:,1].max()<.5
        assert all(x['verdict']=='Pass' for x in values) and max(x['nrmse'] for x in values)<1e-4
        native.append(stage)
        native_curves.append(capture)

    core,core_curves = [],[]
    for n in (32768,65536,131072):
        pitch,product,provenance = energy.core(index,n,settings)
        assert abs(pitch[0]*np.sqrt(n)/wavelength-f)<1e-8
        core.append(dict(n=n,provenance=provenance,pitchMicrometers=pitch[0],
            continuum=[energy.compare(a,b) for a,b in zip(product,curves)],
            independentOpticalPath=energy.compare(product[1],optical_curve)))
        core_curves.append(product)
    pair = [energy.compare(a,b) for a,b in zip(*core_curves[-2:])]
    assert all(x['verdict']=='Pass' for x in pair+core[-1]['continuum']+[core[-1]['independentOpticalPath']])
    return dict(index=index,lens=lens,configuration=1,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',
        frozenNative=dict(dataSha256=energy.sha((e_dir/'data.json').read_bytes()),diagnostics=energy.read(e_dir/'data.json')['messages']),
        independent=dict(wavelengthMicrometers=wavelength,workingFNumber=f,directionalFNumbers=[fx,fy],pupilWavefrontXScale=sx,
            pupilNodes=int(valid.sum()),phaseFitMaximumWaves=fit_error,stopPencilRadiusMillimeters=radius,
            firstOrderExitPupilDistanceMillimeters=reported_radius,reportedRadiusPhaseErrorWaves=reported_phase_error,
            tracedOpticalPathMaxPhaseErrorWaves=phase_error,rayReportSha256=energy.sha(ray_raw),
            firstOrderReportSha256=energy.sha(first),wavefrontSha256=energy.sha(wave_raw),
            wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()),
            maximumRayCoordinateErrorMillimeters=float(errors[:,:3].max()),maximumRayDirectionError=float(errors[:,3:].max()),
            chiefPositionMicrometers=(chief[:2]*1000).tolist(),continuousCentroidShiftMicrometers=[c.tolist() for c in centers],
            opticalPathCentroidShiftMicrometers=optical_center.tolist(),independentOpticalPath=independent_phase,quadrature=quadrature,
            ideal=curves[0].tolist(),actualNativeWavefront=curves[1].tolist(),actualOpticalPath=optical_curve.tolist()),
        native=native,finestNativePair=[energy.compare(a,b) for a,b in zip(*native_curves)],core=core,finestCorePair=pair)


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
