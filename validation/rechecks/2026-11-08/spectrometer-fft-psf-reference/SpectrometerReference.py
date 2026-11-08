"""Independent #443 reconstruction and optional original-window Workbench comparison."""
from pathlib import Path
from zipfile import ZipFile
import hashlib
import json
import re
import sys
import numpy as np
from scipy.special import roots_legendre
from scipy.spatial.transform import Rotation

repo = Path(sys.argv[1])
output = Path(sys.argv[2])
product = Path(sys.argv[3]) if len(sys.argv) >= 4 else None
request = Path(sys.argv[4]) if len(sys.argv) >= 5 else None
axis_requested = '--axis' in sys.argv[5:]
lens = 'Diffractive components/Sample Spectrometer.ZMX'
def sha(data):
    return hashlib.sha256(data).hexdigest()

def path_sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

source=(repo/'src/OpticalSystemDesign.App/Assets/SequentialExamples'/lens).read_bytes()
head,*surfs=re.split(r'^SURF \d+\s*$',source.decode('utf-16' if source[:2]==b'\xff\xfe' else 'utf-8-sig'),flags=re.M)
def get(s,key):
    return float(re.search(r'^\s*'+key+r'\s+(\S+)',s,re.M).group(1))
def frame_ops(s,stage):
    m=re.search(r'^\s*SCBD\s+'+str(stage)+r'\s+(.+)',s,re.M)
    if m is None:return np.eye(3)
    order,pickup,x,y,rx,ry,rz=map(float,m.group(1).split())
    assert x==y==0 and (pickup==0 or stage==2 and pickup==1)
    ax,ay,az=[Rotation.from_euler(axis,angle,degrees=True).as_matrix() for axis,angle in zip('xyz',[rx,ry,rz])]
    return (az@ay@ax) if order==1 else (ax@ay@az)
z=np.zeros(3); frame=np.eye(3)
origin=[]; frames=[]
for i,section in enumerate(surfs):
    frame=frame@frame_ops(section,1)
    origin.append(z.copy())
    frames.append(frame.copy())
    frame=frame@frame_ops(section,2)
    z=z+get(section,'DISZ')*frame[:,2]
assert len(surfs)==6 and 'UNIT MM ' in head and 'ROPD 2' in head
assert 'TYPE DGRATING' in surfs[3] and all('GLAS MIRROR ' in surfs[k] for k in (2,3,4))
assert np.max(np.abs(frames[-1]-np.eye(3)))<1e-14
wave=get(head,'WAVM 2')
obj_na=get(head,'OBNA')
pupil_stop_scale=get(surfs[0],'DISZ')*obj_na/np.sqrt(1-obj_na*obj_na)
slit=np.array([get(surfs[1],'SQAP'),get(surfs[1],r'SQAP\s+\S+')])
clear=[get(surfs[i],r'FLAP\s+\S+') for i in (2,3,4)]
radii=[1/get(surfs[i],'CURV') for i in (2,4)]
grooves=get(surfs[3],'PARM 1')
order=get(surfs[3],'PARM 2')
def dot(a,b): return np.sum(a*b,axis=-1)
def trace(px,py,object_y=0.):
    p=np.zeros((px.size,3))
    p[:,1]=object_y
    d=np.column_stack((pupil_stop_scale*px,pupil_stop_scale*py-object_y,np.full(px.size,origin[1][2])))
    d/=np.linalg.norm(d,axis=1)[:,None]
    lengths=np.zeros(px.size)
    phase_path=np.zeros(px.size)
    accepted=np.ones(px.size,bool)
    states=[]
    for i in range(1,6):
        pos=(p-origin[i])@frames[i]
        direction=d@frames[i]
        if i in (2,4):
            radius=radii[(i-2)//2]
            center=pos.copy();center[:,2]-=radius
            b=dot(center,direction)
            c=dot(center,center)-radius*radius
            disc=b*b-c
            assert np.all(disc>=0)
            root=-b+np.sign(radius*direction[:,2])*np.sqrt(disc)
            # Select the vertex hemisphere of the sequential sphere.
            other=-b-np.sign(radius*direction[:,2])*np.sqrt(disc)
            hit=pos+root[:,None]*direction
            wrong=(hit[:,2]-radius)*radius>0
            root[wrong]=other[wrong]
            at=pos+root[:,None]*direction
            normal=at.copy();normal[:,2]-=radius
            direction=direction-2*dot(direction,normal)[:,None]*normal/radius**2
            accepted&=np.hypot(at[:,0],at[:,1])<=clear[(i-2)//2*2]
        else:
            root=-pos[:,2]/direction[:,2]
            at=pos+root[:,None]*direction
            if i==1:
                accepted &= np.all(np.abs(at[:,:2])<=slit,axis=1)
            if i==3:
                phase_path+=wave*grooves*order*at[:,1]
                direction[:,1]+=wave*grooves*order
                direction[:,2]=-np.copysign(np.sqrt(1-dot(direction[:,:2],direction[:,:2])),direction[:,2])
                accepted &= np.hypot(at[:,0],at[:,1])<=clear[1]
        lengths+=root
        states.append((at,direction,lengths.copy(),phase_path.copy(),accepted.copy()))
        p=at@frames[i].T+origin[i]
        d=direction@frames[i].T
    return states

native_root=repo/'validation/rechecks/2026-11-07/spectrometer-opd-reference-plane'
control_zip=native_root/'spectrometer-controls.zip'
manifest={item['path']:item for item in json.loads((native_root/'manifest.json').read_bytes())['files']}
member_hashes={}
with ZipFile(control_zip) as archive:
    def native_member(name):
        entry=manifest[name]
        blob=archive.read(entry.get('sameArchiveMember',name))
        assert sha(blob)==entry['sha256']
        member_hashes[name]=sha(blob)
        return json.loads(blob)
    d=native_member('path-f1w2/point-reference.json')
    image_geo=native_member('path-f1w2/image-geometry.json')
    paths=native_member('path-f1w2/opth.json')['surfaces']
    exit_pupil=native_member('path-f1w2/exit-pupil.json')
assert d['inputSha256']==sha(source) and d['wavelength']==2
assert all(p['hx']==p['hy']==0 for p in d['inputs'])
xy=np.array([[p['px'],p['py']] for p in d['inputs']])
states=trace(*xy.T)
ray_rows=[]
for i,(at,direction,length,phase_path,accepted) in enumerate(states):
    native=d['surfaces'][i]['rays'] if i!=4 else image_geo
    rp=np.array([[r[k] for k in 'xyz'] for r in native])
    rd=np.array([[r[k] for k in 'lmn'] for r in native])
    if i in (1,3,4): rd=-rd
    path_native=np.array([r['opthMillimeters'] for r in paths[i]['rows']])
    ray_rows.append({'surface':i+1,'maxPositionResidualMm':float(np.max(np.abs(at-rp))),
                  'maxDirectionResidual':float(np.max(np.abs(direction-rd))),
                  'maxPathResidualMm':float(np.max(np.abs(length+phase_path-path_native))),
                  'maskDifferences':int(sum(accepted!=np.array([r['vignette']==r['error']==0 for r in native])))})
assert len(xy)==83 and all(r['maskDifferences']==0 and max(r['maxPositionResidualMm'],r['maxDirectionResidual'],r['maxPathResidualMm'])<2e-12 for r in ray_rows)

archive=repo/'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
frozen_hash=path_sha(archive)
assert frozen_hash=='1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
with ZipFile(archive) as a:
    wf=next(p for p in a.namelist() if '/0442-' in p and p.endswith('/native/data.json'))
    wf_bytes=a.read(wf)
    wavefront=json.loads(wf_bytes)['dataGrids'][0]
    case_index='0447' if axis_requested else '0443'
    original_member=next(p for p in a.namelist() if f'/{case_index}-' in p and p.endswith('/workbench/request.json'))
    original_bytes=a.read(original_member)
    policy_bytes=a.read('run/comparison-settings.json')
    original=json.loads(original_bytes)['request']
v=np.asarray(wavefront['values'],float)
ax=(np.arange(64)-32)/31
xx,yy=np.meshgrid(ax,ax)
chief=trace(np.zeros(1),np.zeros(1))[-1]
pc,dc=chief[0][0],chief[1][0]
opc=chief[2][0]+chief[3][0]
axial=exit_pupil['primaryExitPupilMillimeters']
sphere=axial/abs(dc[2])
def wave_phase(final,radius=sphere):
    p,d,op,phase,valid=final
    q=p-pc
    cross=np.cross(q,d)
    off=dot(cross,cross)
    delta=radius*radius-off
    return (opc-op-phase+dot(q,d)-off/(radius+np.sqrt(np.maximum(0,delta))))/(wave*.001),delta
edges=trace(np.array([1.,-1.,0.,0.]),np.array([0.,0.,1.,-1.]))[-1][1]
NA=np.linalg.norm(np.cross(edges,dc),axis=1).reshape(2,2)
na_mean=NA.mean(axis=1)
na_rms=np.sqrt((NA**2).mean(axis=1))
scales=na_mean/na_mean.max()
u,vv=xx/scales[0],yy/scales[1]
base=u*u+vv*vv<=1
t=trace(u[base],vv[base])
valid=t[-1][-1]
predicted=np.zeros_like(base)
predicted[base]=valid
mask=np.isfinite(v)
values,delta=wave_phase(t[-1])
native=v[predicted & mask]
assert np.array_equal(predicted,mask) and len(native)==1153
phase_error=float(np.max(abs(values[valid]-native)))
assert phase_error<1e-7

class Interval:
    def __init__(self,lo,hi=None):
        self.lo=lo
        self.hi=lo if hi is None else hi
    @staticmethod
    def convert(x):
        return x if isinstance(x,Interval) else Interval(x)
    def __add__(self,other):
        other=Interval.convert(other)
        return Interval(np.nextafter(self.lo+other.lo,-np.inf),np.nextafter(self.hi+other.hi,np.inf))
    __radd__=__add__
    def __neg__(self):
        return Interval(-self.hi,-self.lo)
    def __sub__(self,other):
        return self+-Interval.convert(other)
    def __rsub__(self,other):
        return Interval.convert(other)+-self
    def __mul__(self,other):
        other=Interval.convert(other)
        ab=[self.lo*other.lo,self.lo*other.hi,self.hi*other.lo,self.hi*other.hi]
        return Interval(np.nextafter(np.minimum.reduce(ab),-np.inf),np.nextafter(np.maximum.reduce(ab),np.inf))
    __rmul__=__mul__
    def __truediv__(self,other):
        other=Interval.convert(other)
        assert np.all(other.lo*other.hi>0)
        return self*Interval(np.nextafter(1/other.hi,-np.inf),np.nextafter(1/other.lo,np.inf))
    def squared(self):
        min_sq=np.where((self.lo<=0)&(self.hi>=0),0.,np.minimum(self.lo*self.lo,self.hi*self.hi))
        max_sq=np.maximum(self.lo*self.lo,self.hi*self.hi)
        return Interval(np.maximum(0,np.nextafter(min_sq,-np.inf)),np.nextafter(max_sq,np.inf))
    def sqrt(self):
        assert np.all(self.lo>=-1e-300),float(np.min(self.lo))
        return Interval(np.maximum(0,np.nextafter(np.sqrt(np.maximum(0,self.lo)),-np.inf)),np.nextafter(np.sqrt(self.hi),np.inf))
def enclosure(x):
    return Interval(x-1e-12,x+1e-12)
def iv_dot(v,u):
    return sum(a*b for a,b in zip(v,u))
def iv_mat(v,m):
    return [iv_dot(v,[enclosure(float(m[i,j])) for i in range(3)]) for j in range(3)]
def iv_add(a,b):
    return [x+y for x,y in zip(a,b)]
def iv_minus(a,b):
    return [x-y for x,y in zip(a,b)]
def iv_scale(v,f):
    return [a*f for a in v]
def interval_certificate(xl,xh,yl,yh):
    px=Interval(xl-1e-14,xh+1e-14)
    v=Interval(yl,yh)
    py=v*(1-px.squared()).sqrt()
    slope=[px*enclosure(pupil_stop_scale),py*enclosure(pupil_stop_scale),Interval(1.)]
    d=iv_scale(slope,Interval(1.)/sum(t.squared() for t in slope).sqrt())
    p=[Interval(0.) for _ in range(3)]
    bounds=[]
    for i in range(1,6):
        o=[enclosure(float(t)) for t in origin[i]]
        loc=iv_mat(iv_minus(p,o),frames[i])
        direction=iv_mat(d,frames[i])
        if i in (2,4):
            radius=radii[(i-2)//2]
            ctr=[loc[0],loc[1],loc[2]-radius]
            b=iv_dot(ctr,direction)
            c=sum(t.squared() for t in ctr)-radius*radius
            distance=-c/(b+(b.squared()-c).sqrt())
            at=iv_add(loc,iv_scale(direction,distance))
            normal=[at[0],at[1],at[2]-radius]
            direction=iv_minus(direction,iv_scale(normal,iv_dot(direction,normal)*(2/radius**2)))
        else:
            distance=-loc[2]/direction[2]
            at=iv_add(loc,iv_scale(direction,distance))
            if i==3:
                direction[1]=direction[1]+enclosure(wave*grooves*order)
                assert np.max(direction[2].hi)<0
                direction[2]=(1-direction[0].squared()-direction[1].squared()).sqrt()
        assert np.min(distance.lo)>0
        if i in (2,3,4):
            bound=np.max((at[0].squared()+at[1].squared()).sqrt().hi)
            bounds.append(float(bound))
        if i==5:
            delta=iv_minus(at,[enclosure(float(t)) for t in pc])
            cross=[delta[(k+1)%3]*direction[(k+2)%3]-delta[(k+2)%3]*direction[(k+1)%3] for k in range(3)]
            sphere_clear=(enclosure(sphere).squared()-sum(t.squared() for t in cross)).lo
        p=iv_add(iv_mat(at,frames[i].T),o)
        d=iv_mat(direction,frames[i].T)
    return bounds,sphere_clear

aperture=slit[0]/pupil_stop_scale
x_edges=np.linspace(-aperture,aperture,65)
y_edges=np.linspace(-1,1,129)
xl,yl=(x.ravel() for x in np.meshgrid(x_edges[:-1],y_edges[:-1]))
xh,yh=(x.ravel() for x in np.meshgrid(x_edges[1:],y_edges[1:]))
lower=np.inf
visited=0
for depth in range(10):
    if not len(xl):break
    bounds,sphere_clear=interval_certificate(xl,xh,yl,yh)
    if depth==0:certified_margins=np.array(clear)-bounds
    visited+=len(xl)
    if np.any(sphere_clear>0):lower=min(lower,np.min(sphere_clear[sphere_clear>0]))
    ambiguous=sphere_clear<=0
    xl,xh,yl,yh=(x[ambiguous] for x in (xl,xh,yl,yh))
    xm=(xl+xh)/2
    ym=(yl+yh)/2
    xl,xh,yl,yh=(np.concatenate(x) for x in [(xl,xm,xl,xm),(xm,xh,xm,xh),(yl,yl,ym,ym),(ym,ym,yh,yh)])
assert not len(xl) and min(certified_margins)>0 and lower>0

if axis_requested:
    half_span=wave*(.5/max(na_rms))*(original['pupilSampling']-2)/2
    pixels=np.linspace(-half_span,half_span,201)
    transverse_pixels=np.array([0.])
else:
    pixels=(np.arange(128)-63)*.25
    transverse_pixels=pixels
aperture=slit[0]/pupil_stop_scale
chief_x=np.array([1.,0.,0.])-dc[0]*dc
chief_x*=np.sign(np.dot(chief_x,edges[0]-edges[1]))/np.linalg.norm(chief_x)
chief_y=np.cross(dc,chief_x)
chief_y*=np.sign(np.dot(chief_y,edges[2]-edges[3]))
def integral(nx,ny,radius=sphere,mode='linear'):
    gx,wx=roots_legendre(nx)
    gy,wy=roots_legendre(ny)
    px=aperture*gx
    yscale=np.sqrt(1-px*px)
    xx=np.broadcast_to(px[:,None],(nx,ny)).ravel()
    yy=(yscale[:,None]*gy).ravel()
    weights=(aperture*(wx*yscale)[:,None]*wy).ravel()
    normalization=2*(aperture*np.sqrt(1-aperture*aperture)+np.arcsin(aperture))
    assert abs(np.sum(weights)-normalization)<1e-13
    result=np.zeros((len(transverse_pixels),len(pixels)),complex)
    for i in range(0,len(xx),4096):
        section=slice(i,i+4096)
        final=trace(xx[section],yy[section])[-1]
        waves,discriminant=wave_phase(final,radius)
        assert np.all(final[-1]) and np.min(discriminant)>0
        kernel=-2j*np.pi/wave
        fx=xx[section]*na_rms[0];fy=yy[section]*na_rms[1]
        if mode=='native-directional':
            fx=xx[section]*na_mean[0];fy=yy[section]*na_mean[1]
        if mode=='actual-directions':
            fx=(final[1]-dc)@chief_x;fy=(final[1]-dc)@chief_y
        ex=np.exp(kernel*fx[:,None]*pixels)
        ey=np.exp(kernel*transverse_pixels[:,None]*fy)
        field=(weights[section]/normalization)*np.exp(-2j*np.pi*waves)
        result+=ey@(field[:,None]*ex)
    return abs(result)**2

gate=dict(json.loads(policy_bytes)['analyses']['FFT PSF Cross Section' if axis_requested else 'PSF']['quantities']['Irradiance'],minimumCoverage=.95)
assert gate['nrmse']==(.003 if axis_requested else .01)
def metric(a,b):
    d=np.abs(a-b)
    assert np.all(np.isfinite(d))
    rms=float(np.sqrt(np.mean(d*d))/max(np.max(np.abs(b)),gate['absolute']))
    pointwise=bool(np.all(d<=gate['absolute']+gate['relative']*np.abs(b)))
    verdict='Pass' if pointwise or rms<=gate['nrmse'] else 'Close' if rms<=gate['closeNrmse'] else 'Difference'
    return {'count':int(d.size),'coverage':1,'nrmse':rms,'maximumAbsolute':float(d.max()),'pointwisePass':pointwise,'conclusion':verdict}

if __name__ == '__main__':
    coarse=integral(192,768)
    fine=integral(256,1024)
    native_axes=integral(192,768,mode='native-directional')
    physical_directions=integral(192,768,mode='actual-directions')
    nominal_sphere=integral(192,768,.09277471876822646)
    result={
        'index':int(case_index),'lens':lens,'sourceSha256':sha(source),'frozenVerdict':'Incomparable',
        'frozenArchiveSha256':frozen_hash,'frozenComparisonPolicySha256':sha(policy_bytes),
        'frozenRequestMember':original_member,'frozenRequestSha256':sha(original_bytes),
        'frozenWavefrontMember':wf,'frozenWavefrontSha256':sha(wf_bytes),
        'nativeControlArchiveSha256':path_sha(control_zip),'nativeControlMemberSha256':member_hashes,
        'nativeRays':len(xy),'nativeSurfaceErrors':ray_rows,
        'nativeWavefrontNodes':len(native),'maxNativeWavefrontResidualWaves':phase_error,
        'nativeExitPupilAxialMm':axial,'referenceSphereRadiusMm':sphere,
        'apertureCertificate':{'normalizedSlitHalfWidth':float(aperture),'subdivisionsTested':visited,
                               'minimumMirrorAndGratingMarginsMm':certified_margins.tolist(),
                               'positiveSphereDiscriminantLowerBoundMm2':float(lower)},
        'chiefNormalFourierFNumberXY':(.5/na_rms).tolist(),
        'directionalArithmeticFNumberXY':(.5/na_mean).tolist(),'originalGate':gate,
        'imageGrid':{'columns':128,'rows':128,'pixelPitchMicrometers':.25,'first':-15.75,'last':16.0},
        'referenceCenterIrradiance':float(fine[0,100] if axis_requested else fine[63,63]),'referencePeakIrradiance':float(fine.max()),
        'independentQuadratureConvergence':metric(coarse,fine),
        'nativeDirectionalScaleControl':metric(native_axes,fine),
        'nonlinearDirectionMapControl':metric(physical_directions,fine),
        'nominalCoreSphereControl':metric(nominal_sphere,fine)
    }
    assert result['independentQuadratureConvergence']['nrmse']<1e-7
    if axis_requested:
        del result['imageGrid']
        result['profileAxisMicrometers']=pixels.tolist()
        result['referenceIrradiance']=fine.ravel().tolist()

    if product:
        core=json.loads(product.read_bytes())
        job=json.loads(request.read_bytes())['request']
        def physical_settings(request):
            return {**request,'workbenchSettings':{key:value for key,value in request['workbenchSettings'].items()
                if key not in {'FourierSamplingMode','FourierMaximumSampling','FourierRelativeAccuracy'}}}
        assert physical_settings(job)==physical_settings(original)
        assert core['outcome']=='Success'
        certificate=core['values']['FourierSamplingValidation']
        assert certificate['validated'] and all(step['phaseResolved'] for step in certificate['steps'][-2:])
        series=core['seriesList'][0]
        assert len(core['seriesList'])==1 and series['xUnit']=='Micrometer'
        if axis_requested:
            assert job['workbenchSettings']['GraphScaleMicrometers']=='0'
            assert series['yQuantity']=='Irradiance' and series['yUnit']=='Dimensionless'
            pts=np.array([[p[k] for k in ('x','y')] for p in series['points']])
            residual=float(np.max(np.abs(pts[:,0]-pixels)))
            assert len(pts)==201 and residual<3e-10
            result['maximumAutoAxisCoordinateResidualMicrometers']=residual
            values=pts[:,1][None,:]
        else:
            assert series['yUnit']=='Micrometer' and series['valueQuantity']=='Irradiance'
            assert series['valueUnit']=='Dimensionless'
            pts=np.array([[p[k] for k in ('x','y','value')] for p in series['points']]).reshape(128,128,3)
            expected_x,expected_y=np.meshgrid(pixels,pixels)
            assert np.array_equal(pts[:,:,0],expected_x) and np.array_equal(pts[:,:,1],expected_y)
            values=pts[:,:,2]
        result['workbenchDataSha256']=path_sha(product)
        result['workbenchRequestSha256']=path_sha(request)
        result['workbenchProvenance']=json.loads(product.with_name('provenance.json').read_bytes())
        result['workbenchCertificate']=certificate
        result['workbenchVersusIndependentReference']=metric(values,fine)

    output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')

