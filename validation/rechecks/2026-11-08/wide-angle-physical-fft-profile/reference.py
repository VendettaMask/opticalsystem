import argparse
import importlib.util
import json
from pathlib import Path

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_SHA = 'fd8a4a96d1dea380462078281b2db5c39185b087b6c85f36f298abbfd74e5c49'
FINGERPRINT = '6c78ece94ef33f69e0577a68f06c2a28d75bcbec1fa346b259243368d8b0dede'


def run(policy):
    def compare(actual,reference):
        error = abs(actual-reference)
        nrmse = float(np.sqrt(np.mean(error**2))/max(abs(reference).max(),policy['absolute']))
        pointwise = bool(np.all(error<=policy['absolute']+policy['relative']*abs(reference)))
        verdict = 'Pass' if pointwise or nrmse<=policy['nrmse'] else 'Close' if nrmse<=policy['closeNrmse'] else 'Difference'
        return dict(nodes=len(error),coverage=1,maxAbsolute=float(error.max()),nrmse=nrmse,pointwise=pointwise,verdict=verdict)

    index = 1991
    source,lens,_,_ = energy.source(index)
    assert energy.sha(source.read_bytes())==SOURCE_SHA
    frozen = energy.frozen(index)
    wavefront = energy.frozen(1986)
    original = energy.read(frozen/'captured-settings.json')
    wave_settings = energy.read(wavefront/'captured-settings.json')
    assert original['properties']==dict(SampleSize='S_32x32',Type='X_Linear',UsePolarization=False,Normalize=False,PlotScale=0,RowCol=0)
    assert original['selectors']=={k:wave_settings['selectors'][k] for k in ('Field','Wavelength')}
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for key in ('configuration','field','wavelength','surface','input','useRayAiming','primaryWavelengthMicrometers'):
        assert wave_settings['request'][key]==original['request'][key]
    assert original['request']['useRayAiming']
    wavelength = original['request']['primaryWavelengthMicrometers']

    prior = HERE.parent/'wide-angle-physical-fft-energy/results.json'
    optics = energy.read(prior)['result']
    assert optics['sourceSha256']==SOURCE_SHA and optics['configuration']==original['request']['configuration']
    assert optics['reference']['wavelengthMicrometers']==wavelength
    f_number = optics['reference']['workingFNumber']
    zraw = (wavefront/'data.json').read_bytes()
    assert energy.sha(zraw)==optics['reference']['wavefrontSha256']
    z = np.asarray(json.loads(energy.decode(zraw))['dataGrids'][0]['values'],float)
    pupil_axis = (np.arange(64)-32)/31
    x,y = np.meshgrid(pupil_axis,pupil_axis)
    t = x*x+y*y
    mask = np.isfinite(z)
    assert np.array_equal(mask,t<=1+1e-12)
    phase = Chebyshev.fit(t[mask],z[mask],14,domain=[0,1])
    subset = mask&(x>=0)&(y>=0)
    alternate = Chebyshev.fit(t[subset],z[subset],12,domain=[0,1])

    # The historical Workbench data supplies only the original axis coordinates.
    # Its 32-sample intensities are excluded because phase was unresolved.
    old = energy.archive_json('frozen-domain/data.json')
    old_request = energy.archive_json('frozen-domain/request.json')['request']
    old_receipt = energy.archive_json('frozen-domain/provenance.json')
    assert old_receipt['requestFingerprint']==FINGERPRINT
    domain = old['seriesList'][0]
    axis = np.asarray([p['x'] for p in domain['points']])
    assert len(axis)==201 and domain['xUnit']=='Micrometer' and domain['yUnit']=='Dimensionless'
    assert old['values']['GridSize']==64 and old_request['pupilSampling']==32
    physical_half_window = wavelength*f_number*(old_request['pupilSampling']-2)/2
    assert np.max(abs(axis-np.linspace(-physical_half_window,physical_half_window,len(axis))))<1.1e-8
    frequency = np.pi*abs(axis)/(wavelength*f_number)
    def integrate(p,n):
        r2,weights = energy.legendre(n)
        # r² coordinates give unit ideal chief-ray amplitude for the clear pupil.
        amplitude = j0(frequency[:,None]*np.sqrt(r2))@(weights*np.exp(-2j*np.pi*p(r2)))
        return abs(amplitude)**2
    reference = integrate(phase,256)
    quadrature = compare(integrate(phase,128),reference)
    independent_fit = compare(integrate(alternate,256),reference)
    assert quadrature['pointwise'] and independent_fit['pointwise']

    adaptive_request = energy.archive_json('adaptive-open/request.json')['request']
    adaptive = energy.archive_json('adaptive-open/data.json')
    adaptive_proof = adaptive['values']['FourierSamplingValidation']
    assert adaptive['outcome']=='Unavailable' and not adaptive_proof['validated'] and adaptive_request['useRayAiming']
    stages,projected = [],[]
    scale = float(max(abs(axis)))
    for n in (4096,8192,16384):
        prefix = f'core/{index}-{n}'
        data = energy.archive_json(f'{prefix}/numerical-data.json')
        request = energy.archive_json(f'{prefix}/request.json')['request']
        provenance = energy.archive_json(f'{prefix}/provenance.json')
        assert data['outcome']=='Success' and data['analysis']=='FFT PSF Cross Section'
        assert provenance['executorId']=='Application.WorkbenchRuntime.BuildAnalysisData/v1'
        assert request['pupilSampling']==n
        assert {k:v for k,v in request.items() if k not in ('pupilSampling','workbenchSettings')} == {
            k:v for k,v in adaptive_request.items() if k not in ('pupilSampling','workbenchSettings')}
        omit = {'PupilSampling','Sampling','GraphScaleMicrometers','FourierSamplingMode'}
        assert {k:v for k,v in request['workbenchSettings'].items() if k not in omit} == {
            k:v for k,v in adaptive_request['workbenchSettings'].items() if k not in omit}
        assert request['workbenchSettings']['FourierSamplingMode']=='Fixed'
        assert abs(float(request['workbenchSettings']['GraphScaleMicrometers'])-scale)<1e-12
        metadata = data['values']
        assert metadata['AxisFourierReadout'] and metadata['UseRayAiming']==[True]
        assert metadata['Normalized'] is False and metadata['UsePolarization'] is False
        assert metadata['ProfileIdealPeak']==1 and abs(metadata['WorkingFNumber']-f_number)<2e-9
        assert data['axis']=={k:domain[k] for k in ('xQuantity','xUnit','yQuantity','yUnit')}
        px,py = np.asarray(data['points']).T
        assert len(px)==4*n+1 and np.all(np.diff(px)>0) and np.all(np.isfinite(py))
        assert abs(px[0]-axis[0])<1e-12 and abs(px[-1]-axis[-1])<1e-12
        # Linear readback to the original axis, with unchanged normalization.
        measured = np.interp(axis,px,py)
        central_error = abs(metadata['StrehlRatio']-reference[len(axis)//2])
        assert central_error<policy['absolute']
        stages.append(dict(n=n,provenance=provenance,numericSourceSha256=data['dataSourceSha256'],
            physicalDomainMicrometers=[px[0],px[-1]],rawNodes=len(px),storedPupilSize=metadata['StoredPupilSize'],
            projected=measured.tolist(),comparison=compare(measured,reference),
            reportedCentralStrehl=metadata['StrehlRatio'],centralStrehlAbsoluteError=central_error))
        projected.append(measured)
    pair = compare(*projected[-2:])
    strehl_absolute = abs(stages[-2]['reportedCentralStrehl']-stages[-1]['reportedCentralStrehl'])
    strehl_relative = strehl_absolute/abs(stages[-1]['reportedCentralStrehl'])
    assert pair['pointwise'] and pair['nrmse']<.001
    assert all(s['comparison']['verdict']=='Pass' for s in stages)
    assert strehl_absolute<policy['absolute'] and strehl_relative>.001

    native256 = energy.archive_json('native/1991-256/data.json')
    cap = energy.archive_json('native/1991-256/captured-settings.json')
    assert not native256['dataSeries'] and not native256['dataGrids']
    assert cap['properties']==dict(original['properties'],SampleSize='S_256x256')
    native_error = energy.read(frozen/'error.json')
    return dict(index=index,lens=lens,configuration=1,sourceSha256=SOURCE_SHA,requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable',supplementalVerdict='SupplementalPass',supplementalMode='FixedPhysicalInterval',
        nativeDirect=dict(originalNativeError=native_error,checkedNativeSampling=256,validSeries=0),
        originalDomain=dict(provenance=old_receipt,coordinatesMicrometers=axis.tolist(),
            physicallyDerivedHalfWidthMicrometers=physical_half_window,usedForCoordinatesOnly=True),
        independent=dict(workingFNumber=f_number,wavelengthMicrometers=wavelength,unaberratedPeak=1,
            originalIrradiance=reference.tolist(),centralIntensity=float(reference[len(axis)//2]),
            wavefrontSha256=energy.sha(zraw),phaseFitMaximumWaves=float(np.max(abs(phase(t[mask])-z[mask]))),
            priorOpticalReferenceSha256=energy.sha(prior.read_bytes()),quadrature=quadrature,alternatePhaseFit=independent_fit),
        fixedStages=stages,finestFixedPair=pair,auxiliaryStrehlPair=dict(absolute=strehl_absolute,relative=strehl_relative),
        existingAdaptive=adaptive_proof)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output',type=Path)
    parser.add_argument('--repo',type=Path,default=HERE.parents[3])
    parser.add_argument('--corpus',type=Path)
    args = parser.parse_args()
    manifest = HERE/'manifest.json'
    energy.configure(args.repo,args.corpus,manifest)
    policy = energy.read(energy.POLICY_PATH)['analyses']['FFT PSF Cross Section']['quantities']['Irradiance']
    result = run(policy)
    args.output.write_text(json.dumps(dict(policy=dict(policy,minimumCoverage=.95),
        policySha256=energy.sha(energy.POLICY_PATH.read_bytes()),manifestSha256=energy.sha(manifest.read_bytes()),
        archivedLogicalPaths=sorted(energy.roles),result=result),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    main()
