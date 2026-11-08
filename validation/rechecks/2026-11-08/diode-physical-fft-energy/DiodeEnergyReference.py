"""Independent #1100 encircled energy from the diode pupil's closed-form phase."""
import argparse
import json
from pathlib import Path
import runpy
from zipfile import ZipFile

import numpy as np
from numpy.polynomial.chebyshev import chebvander
from scipy.interpolate import CubicSpline
from scipy.special import roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument('repo', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--replay', action='append', nargs=2, metavar=('DATA','REQUEST'), type=Path, default=[])
args = parser.parse_args()
model = runpy.run_path(str(Path(__file__).resolve().parent.parent/'diode-fft-axis-reference/DiodeAxisReference.py'),
                       run_name='diode_geometry')
trace = model['trace']
wave,na,gaussian = model['wave'],model['na'],model['gaussian']
sha = model['sha']
# Integrals over the complete disk of amplitude A and squared amplitude |A|².
amplitude_area = -np.pi*np.expm1(-gaussian)/gaussian
power_area = -np.pi*np.expm1(-2*gaussian)/(2*gaussian)
total_power = wave**2/np.prod(na)*power_area/amplitude_area**2
radii = np.arange(401)*5/400

with ZipFile(model['frozen']) as archive:
    member = next(path for path in archive.namelist() if '/1100-' in path and path.endswith('/workbench/request.json'))
    request_bytes = archive.read(member)
    policy_bytes = archive.read('run/comparison-settings.json')
    native_member = member.replace('/workbench/request.json','/native/captured-settings.json')
    native_settings_bytes = archive.read(native_member)
    native_data_member = member.replace('/workbench/request.json','/native/data.json')
    native_data = archive.read(native_data_member)
    messages = json.loads(native_data)['messages']
original = json.loads(request_bytes)['request']
assert original['canonicalAnalysisKey']=='Diffraction Encircled Energy'
assert original['configuration']==original['field']==original['wavelength']==1
assert original['reference']=='Centroid' and original['surface']==-1
assert not original['polarization'] and not original['useRayAiming']
assert original['workbenchSettings']['MaximumDistanceMicrometers']=='5'
assert original['workbenchSettings']['NumPoints']=='401'
native_settings = json.loads(native_settings_bytes)['properties']
assert native_settings['ReferTo']=='Centroid' and native_settings['RadiusMaximum']==5
assert not native_settings['UseHuygensPSF'] and native_settings['ShowDiffractionLimit']
assert messages
gate = dict(json.loads(policy_bytes)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction'],
            minimumCoverage=.95)
assert gate['nrmse']==.003

def metric(candidate,reference):
    delta=np.abs(candidate-reference)
    assert np.all(np.isfinite(delta))
    nrmse=float(np.sqrt(np.mean(delta*delta))/max(reference.max(),gate['absolute']))
    pointwise=bool(np.all(delta<=gate['absolute']+gate['relative']*np.abs(reference)))
    verdict='Pass' if pointwise or nrmse<=gate['nrmse'] else 'Close' if nrmse<=gate['closeNrmse'] else 'Difference'
    return {'count':int(delta.size),'coverage':1,'nrmse':nrmse,
            'maximumAbsolute':float(delta.max()),'pointwisePass':pointwise,'conclusion':verdict}

def curve(n,m,nx,ny,radial_samples,azimuth_samples,ideal):
    radial,weight=roots_legendre(n)
    radial=(radial+1)/2
    theta=np.arange(m)*2*np.pi/m
    px=(radial[:,None]*np.cos(theta)).ravel()
    py=(radial[:,None]*np.sin(theta)).ravel()
    # 2*pi from the annular Jacobian and 1/2 from mapping Gauss nodes cancel.
    weights=np.pi*np.repeat(weight*radial*np.exp(-gaussian*radial**2)/m,m)/amplitude_area
    assert abs(weights.sum()-1)<1e-13
    xx=np.cos(np.pi*(np.arange(nx)+.5)/nx)
    yy=np.cos(np.pi*(np.arange(ny)+.5)/ny)
    grid=np.zeros((ny,nx),complex)
    kernel=-2j*np.pi/wave*5
    for i in range(0,len(px),4096):
        chunk=slice(i,i+4096)
        amplitude=weights[chunk]
        if not ideal:
            amplitude=amplitude*np.exp(-2j*np.pi*trace(px[chunk],py[chunk])[2])
        ex=np.exp(kernel*px[chunk,None]*xx*na[0])
        ey=np.exp(kernel*yy[:,None]*py[chunk]*na[1])
        grid+=ey@(amplitude[:,None]*ex)
    inverse_x=np.linalg.inv(chebvander(xx,nx-1))
    inverse_y=np.linalg.inv(chebvander(yy,ny-1))
    polynomial=inverse_y@grid@inverse_x.T
    rr=np.linspace(0,5,radial_samples)
    angles=np.arange(azimuth_samples)*2*np.pi/azimuth_samples
    angular_average=np.empty_like(rr)
    for i in range(0,len(rr),64):
        chunk=slice(i,i+64)
        vx=chebvander((rr[chunk,None]*np.cos(angles)/5).ravel(),nx-1)
        vy=chebvander((rr[chunk,None]*np.sin(angles)/5).ravel(),ny-1)
        amplitude=np.sum((vx@polynomial.T)*vy,axis=1)
        angular_average[chunk]=np.mean(abs(amplitude.reshape(-1,azimuth_samples))**2,axis=1)
    primitive=CubicSpline(rr,2*np.pi*rr*angular_average/total_power).antiderivative()
    result=primitive(radii)-primitive(0)
    assert result[0]==0 and np.all(np.diff(result)>0) and result[-1]<1
    return result

def symmetric_centroid(ideal):
    # Phase and Gaussian magnitude are even in each coordinate, so the
    # full continuous moments vanish. Confirm with complete finite FFT
    # marginals, using symmetric treatment of the single Nyquist bin.
    n=1024
    axis=(np.arange(n+1)-n/2)/(n/2)
    xx,yy=np.meshgrid(axis,axis)
    r2=xx*xx+yy*yy
    mask=r2<=1
    field=np.zeros(r2.shape,complex)
    value=np.exp(-gaussian*r2[mask])
    if not ideal:
        value=value*np.exp(-2j*np.pi*trace(xx[mask],yy[mask])[2])
    field[mask]=value
    logical=n*2
    total=np.sum(abs(field)**2)
    moments,errors=[],[]
    for i,grid in enumerate((field,field.T)):
        spectrum=np.zeros(logical)
        for row in range(0,grid.shape[0],64):
            block=grid[row:row+64]
            spectrum+=np.sum(abs(np.fft.fft(block,n=logical,axis=1))**2,axis=0)
        frequency=np.fft.fftfreq(logical)
        frequency[logical//2]=0
        moments.append(float(spectrum@frequency/spectrum.sum()*wave/(na[i]*(2/n))))
        errors.append(float(abs(spectrum.sum()/(logical*total)-1)))
    assert max(map(abs,moments))<1e-11 and max(errors)<1e-12
    return {'physicalIntervals':n,'fullPeriodLogicalSize':logical,'centroidMicrometers':moments,
            'marginalParsevalRelativeErrors':errors}

def phase_check(n):
    step=np.sqrt(np.mean(na**2))/na/np.sqrt(n)
    vectors=[np.arange(-int(np.floor(1/s)),int(np.floor(1/s))+1)*s for s in step]
    xx,yy=np.meshgrid(*vectors)
    mask=xx*xx+yy*yy<=1
    phase=np.full(mask.shape,np.nan)
    phase[mask]=trace(xx[mask],yy[mask])[2]
    raw=[float(np.nanmax(abs(np.diff(phase,axis=i)))) for i in (1,0)]
    ij=np.indices(mask.shape)
    parity=((ij[0]-len(vectors[1])//2+ij[1]-len(vectors[0])//2)%2)==0
    parity[len(vectors[1])//2,len(vectors[0])//2]=False
    connected=mask[1:-1,1:-1]&mask[:-2,1:-1]&mask[2:,1:-1]&mask[1:-1,:-2]&mask[1:-1,2:]
    eligible=connected&parity[1:-1,1:-1]
    mean=(phase[:-2,1:-1]+phase[2:,1:-1]+phase[1:-1,:-2]+phase[1:-1,2:])/4
    phase[1:-1,1:-1][eligible]=mean[eligible]
    filtered=[float(np.nanmax(abs(np.diff(phase,axis=i)))) for i in (1,0)]
    return {'nominalPupilSampling':n,'normalizedPupilStepXY':step.tolist(),'transmittedNodes':int(mask.sum()),
            'maximumRawAdjacentPhaseStepXYWaves':raw,'maximumFilteredAdjacentPhaseStepXYWaves':filtered}

components=[]
references=[]
for name,ideal,orders in [('DiffractionLimit',True,[(64,128,28,48,801,96),(96,192,36,64,1601,144)]),
                          ('Actual',False,[(512,1024,28,48,801,96),(768,1536,36,64,1601,144)])]:
    coarse=curve(*orders[0],ideal)
    fine=curve(*orders[1],ideal)
    convergence=metric(coarse,fine)
    assert convergence['nrmse']<1e-7
    components.append({'name':name,'quadratureStages':orders,'quadrature':convergence,
                       'referenceEnergyFraction':fine.tolist(),'centroidControl':symmetric_centroid(ideal)})
    references.append(fine)

def physical_settings(settings):
    controls={k:original['workbenchSettings'][k] if k in {'PupilSampling','Sampling'} else v
              for k,v in settings['workbenchSettings'].items()}
    return {**settings,'pupilSampling':original['pupilSampling'],'workbenchSettings':controls}

replays,curves=[],[]
for data_path,request_path in args.replay:
    raw=data_path.read_bytes()
    request_raw=request_path.read_bytes()
    data=json.loads(raw)
    job=json.loads(request_raw)['request']
    assert physical_settings(job)==physical_settings(original)
    receipt={'nominalPupilSampling':job['pupilSampling'],'outcome':data['outcome'],
             'reason':data.get('outcomeReason'),'dataSha256':sha(raw),'requestSha256':sha(request_raw),
             'provenance':json.loads(data_path.with_name('provenance.json').read_bytes())}
    if data['outcome']=='Success':
        assert len(data['seriesList'])==2 and data['seriesList'][0]['name']=='衍射极限'
        values=[]
        for series in data['seriesList']:
            assert series['xUnit']=='Micrometer' and series['xQuantity']=='Radius'
            assert series['yUnit']=='Dimensionless' and series['yQuantity']=='EnergyFraction'
            points=np.asarray([[p['x'],p['y']] for p in series['points']])
            assert len(points)==401 and np.max(abs(points[:,0]-radii))<1e-13
            values.append(points[:,1])
        receipt['versusReference']=[metric(v,r) for v,r in zip(values,references)]
        curves.append((job['pupilSampling'],values,receipt['versusReference']))
    else:
        receipt['phaseSampling']=data['values'].get('PupilPhaseSampling')
    replays.append(receipt)
curves.sort(key=lambda c:c[0])
pair=[metric(a,b) for a,b in zip(curves[-2][1],curves[-1][1])] if len(curves)>1 else []
passed=bool(pair) and all(m['conclusion']=='Pass' for m in pair+curves[-1][2])
report={
    'index':1100,'lens':model['lens'],'sourceSha256':sha(model['source']),'frozenVerdict':'Incomparable',
    'frozenArchiveSha256':model['frozen_sha'],'frozenRequestMember':member,'frozenRequestSha256':sha(request_bytes),
    'frozenNativeSettingsMember':native_member,'frozenNativeSettingsSha256':sha(native_settings_bytes),
    'frozenNativeDataMember':native_data_member,'frozenNativeDataSha256':sha(native_data),'frozenDiagnostics':messages,
    'frozenComparisonPolicySha256':sha(policy_bytes),'originalGate':gate,
    'nativeWavefrontMember':model['wf_member'],'nativeWavefrontSha256':sha(model['wf_raw']),
    'nativeWavefrontNodes':int(model['mask'].sum()),'nativePhaseMaximumResidualWaves':model['phase_residual'],
    'fullPupilGaussianAmplitudeIntegral':amplitude_area,'fullPupilGaussianPowerIntegral':power_area,
    'unitPeakIrradianceParsevalTotalPowerMicrometersSquared':total_power,
    'directionalWorkingFNumbersXY':(.5/na).tolist(),'symmetricPhaseGradientCentroidMicrometers':[0,0],
    'radiusMicrometers':radii.tolist(),'components':components,
    'independentPhaseChecks':[phase_check(n) for n in (32768,65536,131072,262144)],
    'formalReplays':replays,'formalFinestPair':pair,'independentOutcome':'SupplementalPass' if passed else 'Open'
}
args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

