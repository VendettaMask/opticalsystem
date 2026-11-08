"""Independent original-axis #1097 reference from a closed-form paraxial-XY eikonal."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from zipfile import ZipFile

import numpy as np
from scipy.special import roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument('repo', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--replay', action='append', nargs=2, metavar=('DATA','REQUEST'), type=Path, default=[])
parser.add_argument('--finite-control', action='store_true')
args = parser.parse_args()
repo = args.repo
lens = 'Miscellaneous/Diode source simulation.zmx'
source = (repo/'src/OpticalSystemDesign.App/Assets/SequentialExamples'/lens).read_bytes()
encoding = 'utf-16' if source[:2]==b'\xff\xfe' else 'utf-8-sig'
head,*surfaces = re.split(r'^SURF \d+\s*$',source.decode(encoding),flags=re.M)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def get(section,key):
    return float(re.search(r'^\s*'+key+r'\s+(\S+)',section,re.M).group(1))

assert len(surfaces)==5 and 'UNIT MM ' in head and 'ROPD 2' in head
assert 'TYPE PARAX_XY' in surfaces[1] and 'STOP' in surfaces[1]
assert all('TYPE STANDARD' in s for i,s in enumerate(surfaces) if i!=1)
assert not any(re.search(r'^\s*(?:GLAS|COAT|SCBD|CLAP|FLAP|SQAP|OBSC)\b',s,re.M) for s in surfaces)
lo = get(surfaces[0],'DISZ')
li = sum(get(s,'DISZ') for s in surfaces[1:])
wave = get(head,'WAVM 1')
object_na = get(head,'OBNA')
pupil_radius = lo*object_na/np.sqrt(1-object_na*object_na)
power = np.array([get(surfaces[1],'PARM 1'),get(surfaces[1],'PARM 2')])
gaussian = get(head,r'GFAC\s+1')
assert gaussian==1 and all(get(s,'CURV')==0 for s in surfaces)
# The real image ray's impact distance to its reference sphere is bounded
# by its transverse image height for every point of the unvignetted disk.
max_image_height = pupil_radius*np.max(np.abs(1+li*(1/lo-power)))
assert max_image_height<li

def dot(a,b):
    return np.sum(a*b,axis=1)

def trace(px,py):
    stop = pupil_radius*np.column_stack((px,py))
    s = stop/lo
    t = s-stop*power
    cin = np.sqrt(1+dot(s,s))
    cout = np.sqrt(1+dot(t,t))
    direction = np.column_stack((t,np.ones(px.size)))/cout[:,None]
    image = np.column_stack((stop+li*t,np.zeros(px.size)))
    # Along a radial line from the chief, both ray slopes vary linearly.
    # Integrate their transverse direction-cosine difference in closed form.
    surface_phase = dot(stop,t)/(1+cout)-dot(stop,s)/(1+cin)
    path = lo*cin+li*cout+surface_phase
    cross = np.cross(image,direction)
    offset_squared = dot(cross,cross)
    correction = dot(image,direction)-offset_squared/(li+np.sqrt(li*li-offset_squared))
    return image,direction,((lo+li)-path+correction)/(wave*.001)

edge = trace(np.array([1.,-1.,0.,0.]),np.array([0.,0.,1.,-1.]))[1]
na = np.linalg.norm(edge[:,:2],axis=1).reshape(2,2).mean(axis=1)
scales = na/na.max()
f_numbers = .5/na
frozen = repo/'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
with frozen.open('rb') as stream:
    frozen_sha = hashlib.file_digest(stream,'sha256').hexdigest()
assert frozen_sha=='1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
with ZipFile(frozen) as archive:
    source_receipt=next(row for row in json.loads(archive.read('run/sources.json')) if row['source']['id']==lens)
    assert source_receipt['actualSha256']==sha(source)
    wf_member=next(p for p in archive.namelist() if '/1092-' in p and p.endswith('/native/data.json'))
    wf_raw=archive.read(wf_member)
    ray_member=next(p for p in archive.namelist() if '-033-diode-source' in p and
                    'primary-f1-w1-single-ray-trace/native/data.txt' in p)
    ray_raw=archive.read(ray_member)
    request_member=next(p for p in archive.namelist() if '/1097-' in p and p.endswith('/workbench/request.json'))
    request_raw=archive.read(request_member)
    policy_raw=archive.read('run/comparison-settings.json')
wavefront = json.loads(wf_raw)['dataGrids'][0]
assert wavefront['nx']==wavefront['ny']==64 and wavefront['minX']==wavefront['minY']==-1
assert wavefront['dx']==wavefront['dy']==2/63
v = np.asarray(wavefront['values'],float)
axis = (np.arange(64)-32)/31
x,y = np.meshgrid(axis/scales[0],axis/scales[1])
mask = np.isfinite(v)
assert np.array_equal(mask,x*x+y*y<=1) and mask.sum()==1043
opd = trace(x[mask],y[mask])[2]
phase_residual = float(np.max(abs(opd-v[mask])))
assert phase_residual<1e-6

ray_text = ray_raw.decode('utf-16')
assert re.search(r'\(Py\).*0\.8000000000',ray_text)
rows = {}
for line in ray_text.splitlines():
    if line.startswith('近轴'):
        break
    cols=line.split()
    if len(cols)>11 and cols[0].isdigit():
        rows[int(cols[0])]=[float(v) for v in cols[1:7]]
probe=trace(np.array([0.]),np.array([.8]))
ray_residual=np.abs(np.array(rows[4])-np.r_[probe[0][0],probe[1][0]])
assert ray_residual.max()<1e-10

original = json.loads(request_raw)['request']
assert original['canonicalAnalysisKey']=='FFT PSF Cross Section'
assert original['configuration']==original['field']==original['wavelength']==1
assert original['surface']==-1 and not original['polarization'] and not original['useRayAiming']
assert original['workbenchSettings']['GraphScaleMicrometers']=='0'
assert original['workbenchSettings']['Row']=='中心' and original['workbenchSettings']['Type'].startswith('X')
gate = dict(json.loads(policy_raw)['analyses']['FFT PSF Cross Section']['quantities']['Irradiance'],minimumCoverage=.95)
assert gate['nrmse']==.003
half_span = wave*f_numbers.min()*(original['pupilSampling']-2)/2
coordinates = np.linspace(-half_span,half_span,201)

def profile(n,m,x):
    r,weight = roots_legendre(n)
    r = (r+1)/2
    theta = np.arange(m)*(2*np.pi/m)
    px = (r[:,None]*np.cos(theta)).ravel()
    py = (r[:,None]*np.sin(theta)).ravel()
    weights = np.repeat(weight*r*np.exp(-gaussian*r*r)/m,m)
    norm = -np.expm1(-gaussian)/gaussian
    assert abs(np.sum(weights)-norm)<1e-13
    field = np.zeros(x.shape,complex)
    for i in range(0,len(px),4096):
        chunk = slice(i,i+4096)
        amplitude = weights[chunk]*np.exp(-2j*np.pi*trace(px[chunk],py[chunk])[2])/norm
        kernel = np.exp(-2j*np.pi*na[0]/wave*px[chunk,None]*x)
        field += amplitude@kernel
    return np.abs(field)**2

def metric(candidate,reference):
    delta=np.abs(candidate-reference)
    assert np.all(np.isfinite(delta))
    nrmse=float(np.sqrt(np.mean(delta*delta))/max(reference.max(),gate['absolute']))
    pointwise=bool(np.all(delta<=gate['absolute']+gate['relative']*np.abs(reference)))
    verdict='Pass' if pointwise or nrmse<=gate['nrmse'] else 'Close' if nrmse<=gate['closeNrmse'] else 'Difference'
    return {'count':int(delta.size),'coverage':1,'nrmse':nrmse,
            'maximumAbsolute':float(delta.max()),'pointwisePass':pointwise,'conclusion':verdict}

if __name__ == '__main__':
    coarse = profile(512,1024,coordinates)
    fine = profile(768,1536,coordinates)
    assert metric(coarse,fine)['nrmse']<1e-7
    assert np.max(np.abs(fine-fine[::-1]))<1e-13

    def discrete_profile(n):
        q = (np.arange(n)-n/2)/(n/2-1)
        marginal = np.zeros(n,complex)
        ideal = 0.
        raw_phase_step = 0.
        for start in range(0,n,48):
            end = min(start+48,n)
            lower,upper = max(0,start-1),min(n,end+1)
            yy,xx = np.meshgrid(q[lower:upper],q,indexing='ij')
            pupil_radius_squared = xx*xx+yy*yy
            mask = pupil_radius_squared<=1
            phase = np.full(mask.shape,np.nan)
            phase[mask] = trace(xx[mask],yy[mask])[2]
            amplitude = np.where(mask,np.exp(-gaussian*pupil_radius_squared),0)
            filtered_phase,filtered_amplitude = phase.copy(),amplitude.copy()
            row,col = np.indices(phase.shape)
            average_node = (row+lower+col)%2==0
            eligible = (mask[1:-1,1:-1]&mask[:-2,1:-1]&mask[2:,1:-1]
                        &mask[1:-1,:-2]&mask[1:-1,2:]&average_node[1:-1,1:-1])
            center = (row[1:-1,1:-1]+lower==n//2)&(col[1:-1,1:-1]==n//2)
            for target,source,selection in ((filtered_phase,phase,eligible & ~center),
                                            (filtered_amplitude,amplitude,eligible)):
                average = (source[:-2,1:-1]+source[2:,1:-1]+source[1:-1,:-2]+source[1:-1,2:])/4
                target[1:-1,1:-1][selection] = average[selection]
            kept = slice(start-lower,end-lower)
            field = np.where(mask[kept],filtered_amplitude[kept]*np.exp(-2j*np.pi*filtered_phase[kept]),0)
            marginal += field.sum(axis=0)
            ideal += filtered_amplitude[kept].sum()
            raw_phase_step = max(raw_phase_step,float(np.nanmax(abs(phase[:,1:]-phase[:,:-1]))),
                                 float(np.nanmax(abs(phase[1:]-phase[:-1]))))
        kernel = np.exp(-2j*np.pi*na[0]/wave*q[:,None]*coordinates)
        return np.abs(marginal@kernel/ideal)**2,raw_phase_step

    control_root=repo/'validation/rechecks/2026-11-08/diffraction-psf-late-window'
    manifest={row['logicalPath']:row for row in json.loads((control_root/'manifest.json').read_bytes())['logicalPayloads']}
    controls=[]
    for n in (1024,2048):
        receipt=manifest[f'native/1093-{n}/data.json']
        with ZipFile(control_root/receipt['archive']) as archive:
            raw=archive.read(receipt['member'])
        assert sha(raw)==receipt['sha256']
        grid=json.loads(raw)['dataGrids'][0]
        assert grid['nx']==grid['ny']==128 and grid['dx']==grid['dy']==.25
        xx=np.arange(128)*.25+grid['minX']
        yy=np.arange(128)*.25+grid['minY']
        zero=np.flatnonzero(yy==0)
        assert len(zero)==1
        native_row=np.asarray(grid['values'],float)[zero[0]]
        controls.append({'case':1093,'nativePupilSampling':n,'nativeSha256':sha(raw),
                         'centralRowComparison':metric(native_row,profile(512,1024,xx))})

    def physical_settings(settings):
        controls={k:v for k,v in settings['workbenchSettings'].items()
                  if k not in {'FourierSamplingMode','FourierMaximumSampling','FourierRelativeAccuracy'}}
        return {**settings,'workbenchSettings':controls}

    replays=[]
    for data_path,request_path in args.replay:
        data=json.loads(data_path.read_bytes())
        request_bytes=request_path.read_bytes()
        job=json.loads(request_bytes)['request']
        assert physical_settings(job)==physical_settings(original)
        certificate=data['values']['FourierSamplingValidation']
        row={'outcome':data['outcome'],'reason':data.get('outcomeReason'),'certificate':certificate,
             'dataSha256':sha(data_path.read_bytes()),'requestSha256':sha(request_bytes),
             'provenance':json.loads(data_path.with_name('provenance.json').read_bytes())}
        if data['outcome']=='Success':
            assert certificate['validated'] and certificate['protocol']=='FourierPhysicalDomainConvergence/v3'
            low,high=certificate['steps'][-2:]
            assert low['pupilSampling']<high['pupilSampling']==certificate['returnedPupilSampling']
            assert all(s['phaseResolved'] and s['usesStreamedPhysicalPupil']
                       and 0<s['maximumPupilPhaseStepWaves']<=.5 for s in (low,high))
            assert (high['relativeRmsChange']<=certificate['relativeRmsAccuracy']
                    or high['maximumAbsoluteChange']<=certificate['absoluteAccuracy'])
            series=data['seriesList'][0]
            assert len(data['seriesList'])==1 and series['xUnit']=='Micrometer'
            assert series['yQuantity']=='Irradiance' and series['yUnit']=='Dimensionless'
            points=np.asarray([[p['x'],p['y']] for p in series['points']])
            assert len(points)==201
            coordinate_residual=float(np.max(np.abs(points[:,0]-coordinates)))
            assert coordinate_residual<3e-10
            row.update(axisMaximumResidualMicrometers=coordinate_residual,
                       workbenchVersusReference=metric(points[:,1],fine))
        replays.append(row)

    passed=any(r.get('workbenchVersusReference',{}).get('conclusion')=='Pass' for r in replays)
    report={
        'index':1097,'lens':lens,'sourceSha256':sha(source),'frozenVerdict':'Incomparable',
        'frozenArchiveSha256':frozen_sha,'frozenComparisonPolicySha256':sha(policy_raw),
        'frozenRequestMember':request_member,'frozenRequestSha256':sha(request_raw),'originalGate':gate,
        'nativeWavefrontMember':wf_member,'nativeWavefrontSha256':sha(wf_raw),'nativeWavefrontNodes':int(mask.sum()),
        'nativeWavefrontMaximumResidualWaves':phase_residual,'nativeSingleRayMember':ray_member,
        'nativeSingleRaySha256':sha(ray_raw),'nativeRayMaxPositionResidualMm':float(ray_residual[:3].max()),
        'nativeRayMaxDirectionResidual':float(ray_residual[3:].max()),
        'referenceSphereRadiusMm':li,'workingFNumbersXY':f_numbers.tolist(),'gaussianAmplitudeFactor':gaussian,
        'profileAxisMicrometers':coordinates.tolist(),'referenceIrradiance':fine.tolist(),
        'independentQuadratureConvergence':metric(coarse,fine),'nativePsfCentralRowControls':controls,
        'formalReplays':replays,'independentOutcome':'SupplementalPass' if passed else 'Open'
    }
    if args.finite_control:
        controls = []
        curves = []
        for n in (3072,4096):
            values,raw_phase_step = discrete_profile(n)
            controls.append({'physicalPupilSampling':n,'maximumRawAdjacentPhaseStepWaves':raw_phase_step,
                             'versusContinuousReference':metric(values,fine)})
            curves.append(values)
        report['discretePupilControl'] = {'tiers':controls,'tierConvergence':metric(*curves)}
    args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

