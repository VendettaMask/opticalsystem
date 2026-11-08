"""Independent slit-pupil reference and phase-sampling audit for #450."""
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
parser.add_argument('--replay', action='append', nargs=2, metavar=('DATA', 'REQUEST'), type=Path, default=[])
args = parser.parse_args()
model = runpy.run_path(str(Path(__file__).resolve().parent.parent /
                           'spectrometer-fft-psf-reference/SpectrometerReference.py'),
                       run_name='spectrometer_geometry')
trace, wave_phase = model['trace'], model['wave_phase']
wave, na_mean, na = model['wave'], model['na_mean'], model['NA']
sphere, chief_point, chief_direction = model['sphere'], model['pc'], model['dc']
sha, path_sha = model['sha'], model['path_sha']
slit_half_width = model['slit'][0]/model['pupil_stop_scale']
radius_axis = np.arange(401)*5/400
area = 2*(slit_half_width*np.sqrt(1-slit_half_width**2)+np.arcsin(slit_half_width))
total_power = wave**2/(np.prod(na_mean)*area)

with ZipFile(model['archive']) as archive:
    member = next(path for path in archive.namelist() if '/0450-' in path and path.endswith('/workbench/request.json'))
    original_bytes = archive.read(member)
    policy_bytes = archive.read('run/comparison-settings.json')
    native_member = member.replace('/workbench/request.json', '/native/captured-settings.json')
    native_settings_bytes = archive.read(native_member)
    native_settings = json.loads(native_settings_bytes)
    native_grid_member = member.replace('/workbench/request.json', '/native/data.json')
    native_data = archive.read(native_grid_member)
    native_messages = json.loads(native_data)['messages']

original = json.loads(original_bytes)['request']
assert original['canonicalAnalysisKey'] == 'Diffraction Encircled Energy'
assert original['configuration'] == original['field'] == 1 and original['wavelength'] == 2
assert original['reference'] == 'Centroid' and original['surface'] == -1
assert not original['polarization'] and not original['useRayAiming']
assert original['workbenchSettings']['MaximumDistanceMicrometers'] == '5'
assert original['workbenchSettings']['NumPoints'] == '401'
properties = native_settings['properties']
assert not properties['UseHuygensPSF'] and properties['ShowDiffractionLimit']
assert properties['RadiusMaximum'] == 5 and properties['ReferTo'] == 'Centroid'
assert native_messages

gate = dict(json.loads(policy_bytes)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction'],
            minimumCoverage=.95)
assert gate['nrmse'] == .003

def metric(candidate, reference):
    difference = np.abs(candidate-reference)
    assert np.all(np.isfinite(difference))
    rms = float(np.sqrt(np.mean(difference**2))/max(np.max(np.abs(reference)), gate['absolute']))
    pointwise = bool(np.all(difference <= gate['absolute']+gate['relative']*np.abs(reference)))
    verdict = 'Pass' if pointwise or rms <= gate['nrmse'] else 'Close' if rms <= gate['closeNrmse'] else 'Difference'
    return {'count': int(difference.size), 'coverage': 1, 'nrmse': rms,
            'maximumAbsolute': float(difference.max()), 'pointwisePass': pointwise, 'conclusion': verdict}

def pupil_rule(n, m):
    gx, wx = roots_legendre(n)
    gy, wy = roots_legendre(m)
    px = slit_half_width*gx
    extent = np.sqrt(1-px**2)
    x = np.broadcast_to(px[:, None], (n,m)).ravel()
    y = (extent[:, None]*gy).ravel()
    weight = (slit_half_width*(wx*extent)[:, None]*wy).ravel()/area
    assert abs(np.sum(weight)-1) < 1e-13
    return x,y,weight

def parabasal_sphere(offset):
    p,d = trace(np.zeros(1),np.zeros(1),object_y=-offset)[-1][:2]
    p,d = p[0],d[0]
    axial = -(p[1]-chief_point[1])/(d[1]/d[2]-chief_direction[1]/chief_direction[2])
    return float(axial/(-chief_direction[2]))

def phase_gradient_center(n,m,h,radius):
    x,y,weight = pupil_rule(n,m)
    derivatives = []
    for dx,dy in [(h,0),(0,h)]:
        positive = wave_phase(trace(x+dx,y+dy)[-1],radius)[0]
        negative = wave_phase(trace(x-dx,y-dy)[-1],radius)[0]
        derivatives.append(float(np.dot(weight,(positive-negative)/(2*h))))
    # For a real pupil mask the imaginary part of the derivative of the
    # mask vanishes. The symmetric full-plane moment is the pupil-weighted
    # mean phase gradient times the physical Fourier conjugacy factor.
    return -(wave/na_mean)*np.array(derivatives)

def full_period_centroid(n,radius):
    step = 2/(n-2)
    axis = np.arange(-n//2+1,n//2)*step
    x = axis[abs(axis)<=slit_half_width]
    logical = 2*n
    total_nodes = 0
    moments, power_errors = [], []
    for component,(rows,cols) in enumerate(((axis,x),(x,axis))):
        spectrum = np.zeros(logical)
        count = 0
        for i in range(0,len(rows),48):
            yy,xx = np.meshgrid(rows[i:i+48],cols,indexing='ij')
            if component==1:
                xx,yy = yy,xx
            mask = xx*xx+yy*yy<=1
            pupil = np.zeros(mask.shape,complex)
            final = trace(xx[mask],yy[mask])[-1]
            phase,discriminant = wave_phase(final,radius)
            assert np.all(discriminant>0) and np.all(final[-1])
            pupil[mask] = np.exp(-2j*np.pi*phase)
            # Parseval along the other image axis. Accumulate the complete
            # image-frequency marginal without a 2-D image allocation.
            spectrum += np.sum(abs(np.fft.fft(pupil,n=logical,axis=1))**2,axis=0)
            count += int(mask.sum())
        frequencies = np.fft.fftfreq(logical)
        # Symmetric finite-period moment averages the two Nyquist endpoint
        # signs. Their identical discrete coefficient has zero average.
        frequencies[logical//2] = 0.
        power_errors.append(float(abs(spectrum.sum()/(logical*count)-1)))
        moments.append(float(spectrum@frequencies/spectrum.sum()*wave/(na_mean[component]*step)))
        if component==0:
            total_nodes = count
        else:
            assert total_nodes==count
    return {'physicalPupilSampling':n,'fullPeriodLogicalSize':logical,'centroidMicrometers':moments,
            'transmittedNodes':total_nodes,'marginalParsevalRelativeErrors':power_errors}

def curve(n,m,degree,radial_samples,azimuth_samples,center,radius,ideal=False):
    px,py,weights = pupil_rule(n,m)
    nodes = np.cos(np.pi*(np.arange(degree)+.5)/degree)
    xx,yy = center[0]+5*nodes,center[1]+5*nodes
    field = np.zeros((degree,degree),complex)
    k = -2j*np.pi/wave
    for i in range(0,len(px),4096):
        chunk = slice(i,i+4096)
        amplitude = weights[chunk]
        if not ideal:
            final = trace(px[chunk],py[chunk])[-1]
            waves,discriminant = wave_phase(final,radius)
            assert np.all(final[-1]) and np.min(discriminant)>0
            amplitude = amplitude*np.exp(-2j*np.pi*waves)
        ex = np.exp(k*px[chunk,None]*xx*na_mean[0])
        ey = np.exp(k*yy[:,None]*py[chunk]*na_mean[1])
        field += ey@(amplitude[:,None]*ex)
    # The compact pupil bounds the image field's spatial bandwidth.
    # Interpolate the complex field on a bounded square before integrating
    # circular shells, then refine the Fourier and shell quadratures.
    inverse = np.linalg.inv(chebvander(nodes,degree-1))
    polynomial = inverse@field@inverse.T
    radii = np.linspace(0,5,radial_samples)
    theta = np.arange(azimuth_samples)*2*np.pi/azimuth_samples
    average = np.empty(radial_samples)
    for i in range(0,len(radii),64):
        chunk = slice(i,i+64)
        vx = chebvander((radii[chunk,None]*np.cos(theta)/5).ravel(),degree-1)
        vy = chebvander((radii[chunk,None]*np.sin(theta)/5).ravel(),degree-1)
        samples = np.sum((vx@polynomial.T)*vy,axis=1).reshape(-1,azimuth_samples)
        average[chunk] = np.mean(abs(samples)**2,axis=1)
    shell_power = 2*np.pi*radii*average/total_power
    primitive = CubicSpline(radii,shell_power).antiderivative()
    values = primitive(radius_axis)-primitive(0)
    assert values[0]==0 and np.all(np.diff(values)>0) and values[-1]<1
    return values

def witness(n,radius):
    # Native cumulative FFT nominal n gives a physical normalized pupil
    # spacing proportional to 1/sqrt(n). Both nodes and all checkerboard
    # neighbors below lie inside the exact admitted slit and unit pupil.
    rms = np.sqrt(np.mean(na**2))
    step = rms/na_mean/np.sqrt(n)
    ix = np.arange(round(-.29/step[0]),round(.29/step[0]),max(1,round(.02/step[0])))
    iy = np.arange(round(.93/step[1]),round(.996/step[1]))
    ix,iy = (p.ravel() for p in np.meshgrid(ix,iy))
    keep = (ix*step[0])**2+((iy+2)*step[1])**2 < 1
    ix,iy = ix[keep],iy[keep]

    def phase_at(x,y):
        ray = trace(x,y)[-1]
        assert np.all(ray[-1]) and np.max(x*x+y*y)<1
        return wave_phase(ray,radius)[0]

    def filtered(i,j):
        x,y = i*step[0],j*step[1]
        ph = phase_at(x,y)
        even = (i+j)%2==0
        x,y = x[even],y[even]
        ph[even] = np.mean([phase_at(x+dx,y+dy) for dx,dy in
                            [(-step[0],0),(step[0],0),(0,-step[1]),(0,step[1])]],axis=0)
        return ph

    difference = abs(filtered(ix,iy+1)-filtered(ix,iy))
    k = difference.argmax()
    return {'nominalPupilSampling':n,'normalizedPupilStepXY':step.tolist(),
            'startXY':[float(ix[k]*step[0]),float(iy[k]*step[1])],
            'neighborXY':[float(ix[k]*step[0]),float((iy[k]+1)*step[1])],
            'adjacentFilteredPhaseWaves':float(difference[k])}

gradient_inputs = [(64,96,1e-4),(96,192,5e-5),(128,256,2.5e-5)]
native_centroids = [phase_gradient_center(*parameters,sphere) for parameters in gradient_inputs]
parabasal = parabasal_sphere(1e-6)
parabasal_centroid = phase_gradient_center(*gradient_inputs[-1],parabasal)
coarse_order = (128,512,18,801,64)
fine_order = (192,768,26,1601,96)
actual_coarse = curve(*coarse_order,native_centroids[-1],sphere)
actual = curve(*fine_order,native_centroids[-1],sphere)
ideal_coarse = curve(64,96,18,801,64,np.zeros(2),sphere,True)
ideal = curve(96,192,26,1601,96,np.zeros(2),sphere,True)
parabasal_curve = curve(*fine_order,parabasal_centroid,parabasal)
print('Independent actual and ideal curves integrated.',flush=True)
moments = []
for n in (1024,2048,4096,6144,8192):
    row = full_period_centroid(n,sphere)
    moments.append(row)
    print(n,row['centroidMicrometers'],flush=True)
center_control = curve(*fine_order,np.array(moments[-1]['centroidMicrometers']),sphere)

report = {
    'index':450,'lens':model['lens'],'sourceSha256':sha(model['source']),'frozenVerdict':'Incomparable',
    'originalGate':gate,'frozenArchiveSha256':model['frozen_hash'],'frozenPolicySha256':sha(policy_bytes),
    'frozenRequestMember':member,'frozenRequestSha256':sha(original_bytes),
    'frozenNativeSettingsMember':native_member,'frozenNativeSettingsSha256':sha(native_settings_bytes),
    'frozenNativeDataMember':native_grid_member,'frozenNativeDataSha256':sha(native_data),
    'frozenNativeDiagnostics':native_messages,
    'phaseSupport':{'wavefrontMember':model['wf'],'wavefrontSha256':sha(model['wf_bytes']),
                    'nativeControlArchiveSha256':path_sha(model['control_zip']),
                    'nativeControlMemberSha256':model['member_hashes'],
                    'nativePhaseNodes':len(model['native']),'maximumOpdResidualWaves':model['phase_error']},
    'radiusMicrometers':radius_axis.tolist(),'zeroPhaseCentroidMicrometers':[0,0],
    'slitHalfWidthNormalized':slit_half_width,'admittedPupilArea':area,
    'directionalFNumbersXY':(.5/na_mean).tolist(),'unitPeakIrradianceParsevalTotalPowerMicrometersSquared':total_power,
    'referenceSphereMm':sphere,'phaseGradientSteps':gradient_inputs,
    'phaseGradientCentroidsMicrometers':[v.tolist() for v in native_centroids],
    'finiteFullPeriodMomentControls':moments,'finiteCentroidEnergyControl':metric(center_control,actual),
    'parabasalSphereControl':{'probeObjectYOffsetMm':-1e-6,'sphereRadiusMm':parabasal,
                              'centroidMicrometers':parabasal_centroid.tolist(),
                              'actualCurve':parabasal_curve.tolist(),'metrics':metric(parabasal_curve,actual)},
    'components':[
        {'name':'DiffractionLimit','referenceEnergyFraction':ideal.tolist(),'quadrature':metric(ideal_coarse,ideal)},
        {'name':'Actual','referenceEnergyFraction':actual.tolist(),'quadrature':metric(actual_coarse,actual)}],
    'existingCumulativePhaseWitnesses':[
        {'nativeSphere':witness(n,sphere),'energyParabasalSphere':witness(n,parabasal)}
        for n in (65536,131072,262144)]
}
assert all(component['quadrature']['nrmse']<1e-7 for component in report['components'])
assert report['finiteCentroidEnergyControl']['conclusion']=='Pass'
assert report['existingCumulativePhaseWitnesses'][-1]['energyParabasalSphere']['adjacentFilteredPhaseWaves']>.5

def physical_settings(request):
    return {**request,'pupilSampling':original['pupilSampling'],
            'workbenchSettings':{key:original['workbenchSettings'][key] if key in {'Sampling','PupilSampling'} else value
                                 for key,value in request['workbenchSettings'].items()}}

replays, curves = [], []
for data_path,request_path in args.replay:
    data = json.loads(data_path.read_bytes())
    request = json.loads(request_path.read_bytes())['request']
    assert physical_settings(request)==physical_settings(original)
    row = {'nominalPupilSampling':request['pupilSampling'],'outcome':data['outcome'],
           'reason':data.get('outcomeReason'),'dataSha256':path_sha(data_path),
           'requestSha256':path_sha(request_path),
           'provenance':json.loads(data_path.with_name('provenance.json').read_bytes())}
    if data['outcome']=='Success':
        assert len(data['seriesList'])==2
        values=[]
        for series in data['seriesList']:
            assert series['xUnit']=='Micrometer' and series['xQuantity']=='Radius'
            assert series['yUnit']=='Dimensionless' and series['yQuantity']=='EnergyFraction'
            series_points=np.array([[p['x'],p['y']] for p in series['points']])
            assert len(series_points)==401 and np.max(abs(series_points[:,0]-radius_axis))<1e-13
            values.append(series_points[:,1])
        row['components']=[metric(v,r) for v,r in zip(values,(ideal,actual))]
        curves.append(values)
    else:
        row['phaseSampling']=data['values'].get('PupilPhaseSampling')
    replays.append(row)
report['formalReplays']=replays
if len(curves)>1:
    report['formalFinestPair']=[metric(a,b) for a,b in zip(*curves[-2:])]
report['independentOutcome'] = ('SupplementalPass' if len(curves)>1 and
                                all(m['conclusion']=='Pass' for m in report['formalFinestPair']) and
                                all(m['conclusion']=='Pass' for m in replays[-1]['components']) else 'Open')
args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

