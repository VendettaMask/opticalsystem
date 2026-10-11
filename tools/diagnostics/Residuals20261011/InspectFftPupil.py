"""Independent inversion of native complex FFT captures; no runtime inputs."""
import json, pathlib, sys
import numpy as np
root=pathlib.Path(sys.argv[1]).resolve()
out=pathlib.Path(sys.argv[2]).resolve(); out.mkdir(parents=True,exist_ok=False)
base=root/'artifacts/validation/residuals-20261011/followup'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
records=[]
for factor in [4,32,10000]:
    r=read(base/f'gaussian-fft-complex/{factor}-real/data.json')['dataGrids'][0]
    i=read(base/f'gaussian-fft-complex/{factor}-imaginary/data.json')['dataGrids'][0]
    field=np.array(r['values'])+1j*np.array(i['values'])
    pupil=np.fft.fftshift(np.fft.ifft2(field))
    amplitude=abs(pupil); amplitude/=amplitude.max()
    active=np.argwhere(amplitude>1e-8)
    peak=np.unravel_index(amplitude.argmax(),amplitude.shape)
    line=amplitude[peak[0]].tolist()
    native=np.array(read(base/f'gaussian-fft-dee2/fft-{factor}/data.json')['dataGrids'][0]['values'])
    linear_error=float(abs(abs(field)**2-native).max())
    record=dict(factor=factor,peak=tuple(map(int,peak)),supportMinimum=active.min(axis=0).tolist(),supportMaximum=active.max(axis=0).tolist(),
                line=line,amplitude=amplitude.tolist(),nativeLinearComplexMaximumDifference=linear_error)
    records.append(record)
    print(factor,'support',record['supportMinimum'],record['supportMaximum'],'peak',peak,'linear/complex',linear_error)
    print('line',[(j,v) for j,v in enumerate(line) if v>1e-8])
(out/'fft-pupil.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
