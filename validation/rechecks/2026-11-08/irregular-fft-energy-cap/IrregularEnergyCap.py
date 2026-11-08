"""Check the current #1210 cumulative-grid phase cap using the native-tied prescription."""
import json
from pathlib import Path
import runpy
import sys
from zipfile import ZipFile

import numpy as np


here=Path(__file__).resolve().parent
model=runpy.run_path(str(here.parent/'irregular-fft-psf-reference/IrregularReference.py'),
                     run_name='irregular_geometry')
trace, phase = model['trace'], model['phase']
edge_na = model['edge_na']
data_path, request_path = Path(sys.argv[3]), Path(sys.argv[4])
data_bytes, request_bytes = data_path.read_bytes(), request_path.read_bytes()
job = json.loads(request_bytes)['request']
data = json.loads(data_bytes)
with ZipFile(model['frozen_archive']) as archive:
    member=next(p for p in archive.namelist() if '/1210-' in p and p.endswith('/workbench/request.json'))
    original_bytes=archive.read(member)
original=json.loads(original_bytes)['request']

def physical_settings(request):
    return {**request,'pupilSampling':original['pupilSampling'],
            'workbenchSettings':{k:original['workbenchSettings'][k] if k in {'PupilSampling','Sampling'} else v
                                 for k,v in request['workbenchSettings'].items()}}

assert physical_settings(job)==physical_settings(original)
assert original['canonicalAnalysisKey']=='Diffraction Encircled Energy'
assert original['configuration']==original['field']==original['wavelength']==1
assert original['reference']=='Centroid' and original['surface']==-1
assert original['workbenchSettings']['MaximumDistanceMicrometers']=='5'
assert original['workbenchSettings']['NumPoints']=='401'
n=job['pupilSampling']
assert n==262144 and data['outcome']=='Unavailable'
formal_phase=data['values']['PupilPhaseSampling']
assert formal_phase['status']=='UndersampledPhase' and formal_phase['maximumStepWaves']>.5

step=np.sqrt(np.mean(edge_na**2))/np.mean(edge_na,axis=1)/np.sqrt(n)
axes=[np.arange(-int(np.floor(1/s)),int(np.floor(1/s))+1)*s for s in step]
xx,yy=np.meshgrid(*axes)
mask=xx**2+yy**2<=1
paths=trace(xx[mask],yy[mask])

def witness(radius):
    values=np.full(mask.shape,np.nan)
    values[mask]=phase(paths,radius)
    row,col=np.indices(mask.shape)
    parity=((row-len(axes[1])//2+col-len(axes[0])//2)%2)==0
    parity[len(axes[1])//2,len(axes[0])//2]=False
    connected=(mask[1:-1,1:-1]&mask[:-2,1:-1]&mask[2:,1:-1]&mask[1:-1,:-2]&mask[1:-1,2:])
    eligible=connected&parity[1:-1,1:-1]
    average=(values[:-2,1:-1]+values[2:,1:-1]+values[1:-1,:-2]+values[1:-1,2:])/4
    values[1:-1,1:-1][eligible]=average[eligible]
    candidates=[]
    for dimension in (1,0):
        delta=np.abs(np.diff(values,axis=dimension))
        i,j=np.unravel_index(np.nanargmax(delta),delta.shape)
        candidates.append({'direction':'X' if dimension==1 else 'Y',
                           'startXY':[float(xx[i,j]),float(yy[i,j])],
                           'neighborXY':[float(xx[i+(dimension==0),j+(dimension==1)]),
                                         float(yy[i+(dimension==0),j+(dimension==1)])],
                           'maximumFilteredPhaseStepWaves':float(delta[i,j])})
    return {'sphereRadiusMm':float(radius),'byAxis':candidates}

native=witness(model['radius'])
nominal=witness(model['parax_R'])
peak=max(row['maximumFilteredPhaseStepWaves'] for row in nominal['byAxis'])
assert abs(peak-formal_phase['maximumStepWaves'])<1e-6
sha=model['sha']
report={
    'index':1210,'lens':model['lens'],'sourceSha256':sha(model['source']),'frozenVerdict':'Incomparable',
    'frozenRequestMember':member,'frozenRequestSha256':sha(original_bytes),
    'frozenArchiveSha256':model['frozen_sha'],'nativeWavefrontMember':model['native_wavefront'],
    'nativeWavefrontSha256':sha(model['phase_bytes']),'nominalPupilSampling':n,
    'normalizedPupilStepXY':step.tolist(),'illuminatedNodes':int(mask.sum()),
    'nativeSphereControl':native,'nominalEnergySphereControl':nominal,
    'formalPhaseSampling':formal_phase,'formalDataSha256':sha(data_bytes),'formalRequestSha256':sha(request_bytes),
    'formalProvenance':json.loads(data_path.with_name('provenance.json').read_bytes()),'independentOutcome':'Open'
}
Path(sys.argv[2]).write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

