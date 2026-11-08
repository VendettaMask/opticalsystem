import argparse
import hashlib
from pathlib import Path
import json, re
from zipfile import ZipFile
import numpy as np
from numpy.polynomial import Chebyshev
from scipy.optimize import brentq
from scipy.special import j0, j1, jv, roots_legendre

R = np.linspace(0,5,401)

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(data): return hashlib.sha256(data).hexdigest()

def decode(raw): return raw.decode('utf-16' if raw.startswith(b'\xff\xfe') else 'utf-8-sig')

def frozen(i): return next((CORPUS/'analyses').glob(f'{i:04d}-*'))/'native'

roles = set()

def configure(repo, corpus, manifest):
    global CORPUS, CONTROLS, POLICY_PATH, policy, archive_map
    CORPUS = corpus or repo/'artifacts/zemax-sequential-corpus/20261021/full-v1'
    CONTROLS = manifest.parent
    POLICY_PATH = repo/'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
    policy = read(POLICY_PATH)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction']
    archive_map = {row['logicalPath']: row for row in read(manifest)['logicalPayloads']}
    roles.clear()

def archived(role):
    row = archive_map[role]
    with ZipFile(CONTROLS/row['archive']) as archive:
        raw = archive.read(row['member'])
    assert len(raw)==row['bytes'] and sha(raw)==row['sha256']
    roles.add(role)
    return raw

def archive_json(role): return json.loads(decode(archived(role)))

def legendre(n,lo=0,hi=1):
    x,w = roots_legendre(n)
    return lo+(hi-lo)*(x+1)/2,(hi-lo)*w/2

def compare(a,b):
    diff=abs(a-b)
    rms=float(np.sqrt(np.mean(diff**2))/max(np.max(abs(b)),policy['absolute']))
    point=bool(np.all(diff<=policy['absolute']+policy['relative']*abs(b)))
    verdict='Pass' if rms<=policy['nrmse'] or point else 'Close' if rms<=policy['closeNrmse'] else 'Difference'
    return dict(nodes=len(R),coverage=1,maxAbs=float(diff.max()),nrmse=rms,pointwise=point,verdict=verdict)

def source(n):
    cap=read(frozen(n)/'captured-settings.json')
    relative = cap['request']['input'].replace('\\','/').split('/sources/',1)[1]
    path=CORPUS/'sources'/relative
    text=decode(path.read_bytes())
    sections=re.split(r'^SURF (\d+)\s*$',text,flags=re.M)
    return path,relative,sections[0],{int(i):s for i,s in zip(sections[1::2],sections[2::2])}

def setting(text,key,default=0):
    m=re.search(r'^\s*'+key+r'\s+([^\s]+)',text,re.M)
    return float(m.group(1)) if m else default

def bk7(w):
    # Schott N-BK7 Sellmeier coefficients in micrometers, as used by the prescription.
    b=np.array([1.03961212,.231792344,1.01046945])
    c=np.array([.00600069867,.0200179144,103.560653])
    return np.sqrt(1+np.sum(b*w*w/(w*w-c)))

def trace(surfaces,indices,position,direction,w,unit=1):
    previous_index=1.
    z=0.
    states={}
    for i,text in surfaces.items():
        if i not in indices:
            z+=setting(text,'DISZ')*unit
            if i==0 and np.isinf(z): z=0.
            continue
        c=setting(text,'CURV')/unit
        k=setting(text,'CONI')
        poly=[(2*j,setting(text,f'PARM {j}')/unit**(2*j-1)) for j in range(1,9)] if 'TYPE EVENASPH' in text else []
        def sag(y):
            return c*y*y/(1+np.sqrt(1-(1+k)*(c*y)**2))+sum(a*y**power for power,a in poly)
        def slope(y):
            return c*y/np.sqrt(1-(1+k)*(c*y)**2)+sum(power*a*y**(power-1) for power,a in poly)
        guess=(z-position[1])/direction[1]
        def intersection(t):
            y,actual_z=position+t*direction
            return actual_z-z-sag(y)
        t=brentq(intersection,guess-60*unit,guess+60*unit,xtol=2e-13) if c or poly else guess
        position=position+t*direction
        normal=np.array([slope(position[0]),-1])
        normal/=np.linalg.norm(normal)
        if np.dot(direction,normal)>0: normal=-normal
        cosine=-np.dot(direction,normal)
        if 'GLAS MIRROR' in text:
            direction=direction+2*cosine*normal
        else:
            index=bk7(w) if 'GLAS BK7' in text else 1.
            mu=previous_index/index
            direction=mu*direction+(mu*cosine-np.sqrt(1-mu*mu*(1-cosine*cosine)))*normal
            previous_index=index
        states[i]=(position.copy(),direction.copy())
        z+=setting(text,'DISZ')*unit
    return states

def curve(phase, f, w, nodes, orders, lo, alpha):
    p,pw=legendre(nodes,lo,1)
    r,rw=legendre(nodes)
    q=(np.pi*R/(w*f))
    v=(q[:,None]*r).reshape(-1)
    angle=alpha(p)
    # Fourier coefficients of three equally spaced angular obstructions with
    # half width alpha(r). For a full circular pupil alpha is zero.
    c0=1-3*angle/np.pi
    total=np.dot(pw*p,c0)
    wave=np.exp(-2j*np.pi*phase(p*p))
    norm=np.zeros(v.shape)
    for m in orders:
        cm=c0 if m==0 else -3*np.sin(m*angle)/(np.pi*m)
        transform=jv(m,v[:,None]*p)@(pw*p*cm*wave)
        norm+=(1 if m==0 else 2)*abs(transform)**2
    # Azimuth orthogonality gives the sum over orders; Parseval makes total
    # energy proportional to the full illuminated pupil area, 2*pi*total.
    return q*q*(norm.reshape(len(R),nodes)@(rw*r))/total

def core(e,n,frozen_settings):
    folder=f'core/{e}-{n}'
    data=archive_json(f'{folder}/data.json')
    request=archive_json(f'{folder}/request.json')['request']
    provenance=archive_json(f'{folder}/provenance.json')
    assert provenance['executorId']=='Application.WorkbenchRuntime.BuildAnalysisData/v1'
    assert provenance['canonicalAnalysisKey']==request['canonicalAnalysisKey']=='Diffraction Encircled Energy'
    assert data['outcome']=='Success' and request['pupilSampling']==n and request['settingsOrigin']=='CapturedSettings'
    original=frozen_settings['request']
    for key in ('configuration','field','wavelength','surface','polarization','useRayAiming','reference','zemaxSettings'):
        assert request[key]==original[key], key
    assert request['workbenchSettings']['MaximumDistanceMicrometers']=='5'
    assert request['workbenchSettings']['NumPoints']=='401'
    series=data['seriesList']
    assert len(series)==2 and series[0]['name']=='衍射极限'
    curves=[]
    for s in series:
        assert s['xUnit']=='Micrometer' and s['yUnit']=='Dimensionless'
        x,y=np.asarray([(q['x'],q['y']) for q in s['points']]).T
        assert len(x)==len(R) and np.max(abs(x-R))<1e-12 and np.all(np.isfinite(y))
        curves.append(y)
    return data['values'].get('BoundedImagePitch'),curves,provenance

def native(e,n,frozen_settings):
    folder=f'native/{e}-{n}'
    data=archive_json(f'{folder}/data.json')
    cap=archive_json(f'{folder}/captured-settings.json')
    assert not data['messages'] and cap['selectors']==frozen_settings['selectors']
    assert cap['properties']['SampleSize']==f'S_{n}x{n}'
    def exclude_sampling(settings): return {k:v for k,v in settings.items() if k!='SampleSize'}
    assert exclude_sampling(cap['properties'])==exclude_sampling(frozen_settings['properties'])
    for key in ('configuration','input','useRayAiming','primaryWavelengthMicrometers'):
        assert cap['request'][key]==frozen_settings['request'][key]
    series=data['dataSeries']
    assert len(series)==2 and series[0]['description']=='衍射极限'
    for s in series:
        assert len(s['x'])==len(R) and np.max(abs(np.asarray(s['x'])-R))<1e-10
    curves=[np.asarray(s['y'])[:,0] for s in series]
    assert np.all(np.isfinite(curves))
    report=decode(archived(f'{folder}/data.txt'))
    centers=[[1000*float(x),1000*float(y)] for x,y in re.findall(r'X\s*=\s*([+-]?[\d.E+-]+)\s+Y\s*=\s*([+-]?[\d.E+-]+)',report)]
    assert len(centers)==2
    return curves,centers

def finite(phase, n, f, wavelength, transmission, *, interpolate_phase=False, native_knots=False):
    radius = int(np.ceil(np.sqrt(n)))
    axis = np.arange(-radius, radius+1)
    px,py = np.meshgrid(axis/np.sqrt(n),axis/np.sqrt(n))
    t = px*px+py*py
    valid=transmission(px,py)
    pupil=np.zeros(t.shape,dtype=complex)
    values=np.zeros(t.shape)
    values[valid]=phase(t[valid])
    if interpolate_phase:
        # The archived native complex fields support cardinal averaging at
        # interior even-parity points. Preserve the chief and clipped edge.
        x,y=np.meshgrid(axis[1:-1],axis[1:-1])
        selected=((x+y)%2==0)&((x!=0)|(y!=0))
        selected&=valid[1:-1,1:-1]&valid[:-2,1:-1]&valid[2:,1:-1]&valid[1:-1,:-2]&valid[1:-1,2:]
        average=(values[:-2,1:-1]+values[2:,1:-1]+values[1:-1,:-2]+values[1:-1,2:])/4
        values[1:-1,1:-1][selected]=average[selected]
    pupil[valid]=np.exp(-2j*np.pi*values[valid])
    m=2*n
    pitch=wavelength*f/np.sqrt(n)
    power=np.sum(abs(pupil)**2)
    period=np.arange(-m//2+1,m//2+1)
    def centroid(rows):
        marginal=np.zeros(m)
        for block in np.array_split(rows,(len(rows)+31)//32):
            marginal+=np.sum(abs(np.fft.fft(block,n=m,axis=1))**2,axis=0)
        assert np.isclose(marginal.sum(),m*power,rtol=2e-14)
        return pitch*np.dot(period,marginal[period%m])/(m*power)
    cx,cy=centroid(pupil),centroid(pupil.T)
    half=int(np.ceil((5+max(abs(cx),abs(cy)))/pitch+.5))
    k=np.arange(-half,half+1)
    e=np.exp(-2j*np.pi*k[:,None]*axis[None,:]/m)
    weight=(abs(e@pupil@e.T)**2/(m*m*power)).ravel()
    distance=np.hypot(k[None,:]*pitch-cx,k[:,None]*pitch-cy).ravel()
    order=np.argsort(distance)
    distance=distance[order]
    weight=weight[order]
    mass=np.r_[0.,np.cumsum(weight)]
    moment=np.r_[0.,np.cumsum(weight*distance)]
    radii=np.linspace(0,5,n) if native_knots else R
    lower=np.searchsorted(distance,radii-pitch/2)
    upper=np.searchsorted(distance,radii+pitch/2)
    fraction=mass[lower]+((radii+pitch/2)*(mass[upper]-mass[lower])-(moment[upper]-moment[lower]))/pitch
    fraction[0]=0
    if native_knots: fraction=np.interp(R,radii,fraction)
    return fraction, [float(cx),float(cy)]

def run(e,w_id,ray_id,core_stages,native_stages,request_fingerprint,expected_source):
    unit=1 if e==1122 else 25.4
    path,lens,header,surfaces=source(e)
    assert sha(path.read_bytes())==expected_source
    energy_settings=read(frozen(e)/'captured-settings.json')
    wave_settings=read(frozen(w_id)/'captured-settings.json')
    ray_settings=read(frozen(ray_id)/'captured-settings.json')
    assert wave_settings['selectors']==energy_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for settings in (wave_settings,ray_settings):
        for key in ('configuration','input','useRayAiming','primaryWavelengthMicrometers','wavelength','field'):
            assert settings['request'][key]==energy_settings['request'][key]
    assert not energy_settings['request']['useRayAiming']
    w=energy_settings['request']['primaryWavelengthMicrometers']
    model=read(frozen(w_id)/'model.json')
    assert model['aperture']['ApodizationType']=='Uniform'
    opt=set(surfaces)-({0,3,4,6} if e==2438 else {0})
    def tracer(p):
        if e==1122:
            na=setting(header,'OBNA')
            d=np.array([p*na/np.sqrt(1-na*na),1.]);d/=np.linalg.norm(d)
            return trace(surfaces,opt,np.array([0.,0.]),d,w,unit)
        enpd=setting(header,'ENPD')
        return trace(surfaces,opt,np.array([p*enpd*unit/2,0.]),np.array([0.,1.]),w,unit)
    assert [ray_settings['properties'][key] for key in ('Hx','Hy','Px','Py')]==[0,0,0,.8]
    sample=tracer(.8)
    rim=tracer(1)
    end=max(opt)
    f=.5/abs(rim[end][1][0])
    ray_report=decode((frozen(ray_id)/'data.txt').read_bytes())
    row=re.search(rf'^\s*{end}\s+([^\r\n]+)',ray_report,re.M)
    native_ray=np.asarray(list(map(float,row.group(1).split()[:6])))
    independent_ray=np.array([0,sample[end][0][0],0,0,*sample[end][1]])
    assert max(abs(independent_ray[:3]-native_ray[:3]))<1e-8
    assert max(abs(independent_ray[3:]-native_ray[3:]))<1e-10
    wave_data=read(frozen(w_id)/'data.json')
    assert not wave_data['messages']
    z=np.asarray(wave_data['dataGrids'][0]['values'],float)
    assert z.shape==(64,64)
    axes=(np.arange(64)-32)/31
    y,x=np.meshgrid(axes,axes,indexing='ij')
    radius=np.hypot(x,y)
    mask=np.isfinite(z)
    phase=Chebyshev.fit(radius[mask]**2,z[mask],10,domain=[0,1])
    subset=mask&(x>=0)&(y>=0)
    alternate=Chebyshev.fit(radius[subset]**2,z[subset],8,domain=[0,1])
    fit_error=float(np.max(abs(phase(radius[mask]**2)-z[mask])))
    alternate_error=float(np.max(abs(alternate(radius[mask]**2)-z[mask])))
    assert fit_error<1e-7 and alternate_error<1e-7
    if e==1122:
        lo=0
        alpha=lambda r:np.zeros_like(r)
        coarse_orders=fine_orders=(0,)
        expected_mask=radius<=1+1e-12
        def transmission(x,y): return x*x+y*y <= 1
    else:
        def spider_r(p): return tracer(p)[5][0][0]
        half=setting(surfaces[5],'SPID')*unit/2
        central=re.search(r'OBSC\s+0\s+([^\s]+)',surfaces[4])
        lo=brentq(lambda p:spider_r(p)-float(central.group(1))*unit,.3,.5)
        def alpha(p): return np.arcsin(half/np.array([spider_r(z) for z in p]))
        coarse_orders=range(0,10,3)
        fine_orders=range(0,19,3)
        phi=np.arctan2(y,x)
        base=(radius>=lo)&(radius<=1+1e-12)
        blocked=np.zeros(base.shape,dtype=bool)
        warp=np.array([spider_r(q) for q in radius[base]])
        rotation=setting(surfaces[3],'PARM 5')
        for arm in np.deg2rad(rotation+120*np.arange(3)):
            d=phi[base]-arm
            blocked[base]|=(np.cos(d)>=0)&(abs(warp*np.sin(d))<=half)
        expected_mask=base&~blocked
    assert np.array_equal(mask,expected_mask)
    cases=[(lambda r:np.zeros_like(r)),phase]
    coarse=[curve(p,f,w,32,coarse_orders,lo,alpha) for p in cases]
    fine=[curve(p,f,w,48,fine_orders,lo,alpha) for p in cases]
    independent_fit=curve(alternate,f,w,48,fine_orders,lo,alpha)
    quadrature=[compare(a,b) for a,b in zip(coarse,fine)]
    fit_agreement=compare(independent_fit,fine[1])
    assert all(r['verdict']=='Pass' for r in quadrature+[fit_agreement])
    ideal_closed_error=None
    if e==1122:
        u=np.pi*R/(w*f)
        ideal_closed_error=float(np.max(abs(fine[0]-(1-j0(u)**2-j1(u)**2))))
        assert ideal_closed_error<1e-13
    native_results=[]
    native_curves=[]
    core_results=[]
    for n in native_stages:
        ref,centers=native(e,n,energy_settings)
        stage=dict(n=n,centroidsMicrometers=centers,continuum=[compare(a,b) for a,b in zip(ref,fine)])
        if e==1122:
            finite_curves=[finite(p,n,f,w,transmission) for p in cases]
            stage['finite']=[dict(compare(a,b),centroidMicrometers=center) for (a,center),b in zip(finite_curves,ref)]
        native_results.append(stage)
        native_curves.append(ref)
    core_curves=[]
    for n in core_stages:
        pitch,current,provenance=core(e,n,energy_settings)
        assert n<2048 or len(pitch)==1
        if pitch: assert abs(pitch[0]*np.sqrt(n)/w-f)<1e-9
        core_results.append(dict(n=n,provenance=provenance,pitchMicrometers=pitch[0] if pitch else None,
            continuum=[compare(a,b) for a,b in zip(current,fine)]))
        core_curves.append(current)
    finer=[compare(a,b) for a,b in zip(core_curves[-2],core_curves[-1])]
    assert all(m['verdict']=='Pass' for m in finer+core_results[-1]['continuum'])
    control=native_results[-1]['finite' if e==1122 else 'continuum']
    assert all(m['verdict']=='Pass' for m in control)
    return dict(index=e,lens=lens,configuration=1,requestFingerprint=request_fingerprint,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',sourceSha256=expected_source,
        frozenNative=dict(dataSha256=sha((frozen(e)/'data.json').read_bytes()),diagnostics=read(frozen(e)/'data.json')['messages'],
            settingsSha256=sha((frozen(e)/'captured-settings.json').read_bytes())),
        independent=dict(wavefrontIndex=w_id,rayIndex=ray_id,
            wavefrontSha256=sha((frozen(w_id)/'data.json').read_bytes()),
            wavefrontModelSha256=sha((frozen(w_id)/'model.json').read_bytes()),
            wavefrontSettingsSha256=sha((frozen(w_id)/'captured-settings.json').read_bytes()),
            rayReportSha256=sha((frozen(ray_id)/'data.txt').read_bytes()),
            raySettingsSha256=sha((frozen(ray_id)/'captured-settings.json').read_bytes()),
            wavelengthMicrometers=w,workingFNumber=f,nativeImageRay=native_ray.tolist(),independentImageRay=independent_ray.tolist(),
            phaseFitMaxWaves=fit_error,alternateFitMaxWaves=alternate_error,illuminatedNodes=int(mask.sum()),
            innerPupilRadius=lo,quadrature=quadrature,alternateReference=fit_agreement,idealClosedFormMaxError=ideal_closed_error,
            ideal=fine[0].tolist(),actual=fine[1].tolist()),
        native=native_results,core=core_results,finestCorePair=finer,
        finestNativePair=[compare(a,b) for a,b in zip(native_curves[-2],native_curves[-1])])

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument('--corpus', type=Path, help='Frozen 95-example corpus directory containing analyses/ and sources/')
    args = parser.parse_args()
    archive_manifest=args.repo/'validation/rechecks/2026-11-08/diffraction-energy-fourth-cohort/manifest.json'
    configure(args.repo,args.corpus,archive_manifest)
    results=[
        run(1122,1114,1110,(16384,32768,65536,131072),(2048,4096),
            '806dd2659a6949cbf4e44d65b1496f088954c9993d9baa15b0cabc4aebe12120',
            '71ddcd6d95bfa4dd596aeb10e51f78787a4025cca8526bc9f0bb635c053a7b69'),
        run(2438,2430,2426,(1024,2048,4096,8192,16384),(1024,2048,4096),
            'a2e22a9bbc3212889a2ab1cf8c658e22284f544918b5356e40260f49c555070a',
            '76e61a4974019a9c4473fa9c8f37d357b6380994da5437e7b4e9ffb6aa8a1b0d'),
    ]
    args.output.write_text(json.dumps(dict(
        policy=dict(policy,minimumCoverage=.95),policySha256=sha(POLICY_PATH.read_bytes()),originalRadiusMicrometers=R.tolist(),
        archiveManifest='../diffraction-energy-fourth-cohort/manifest.json',archiveManifestSha256=sha(archive_manifest.read_bytes()),
        archivedLogicalPaths=sorted(roles),results=results),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    main()

