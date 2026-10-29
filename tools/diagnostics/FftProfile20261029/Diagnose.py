"""Offline counterfactuals only: isolate accepted mask, traced phase and display."""
from pathlib import Path
import importlib.util
import json
import math
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
RUN=ROOT/'artifacts/validation/fft-profile-20261029'
spec=importlib.util.spec_from_file_location('energy_replay',ROOT/'tools/diagnostics/DiffractionEnergy20261029/Diagnose.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


def smooth(raw):
    result=dict(raw)
    for (x,y),phase in raw.items():
        neighbors=[(x-1,y),(x+1,y),(x,y-1),(x,y+1)]
        if (x+y)%2==0 and (x,y)!=(0,0) and all(p in raw for p in neighbors):
            result[x,y]=sum(raw[p] for p in neighbors)/4
    return result


def image(pupil):
    array=np.zeros((64,64),complex)
    for (x,y),value in pupil.items():array[y+32,x+32]=value
    result=abs(np.fft.fftshift(np.fft.fft2(array)))**2/sum(abs(v) for v in pupil.values())**2
    return np.roll(result,(-1,-1),(0,1))


def profile(pixels,pitch,vertical=False):
    knots=[(i-32)*pitch for i in range(64)]
    targets=[-32*pitch+64*pitch*i/200 for i in range(201)]
    values=pixels[:,31] if vertical else pixels[31,:]
    values=[float(values[-1])]+[float(v) for v in values[:-1]]
    return targets,[max(0,v) for v in module.spline(knots,values,[min(t,knots[-1]) for t in targets])]


def main():
    summaries=[]
    for path in sorted((RUN/'circular-diagnosis').glob('*binocular*.json')):
        d=json.loads(path.read_text(encoding='utf-8-sig'));v=d['diagnosis']
        native={(p['x']-32,p['y']-32):p['amplitude']*np.exp(2j*math.pi*p['phaseWaves']) for p in v['pupil'] if p['amplitude']>1e-8}
        raw={(round(s['normalizedPupilX']/v['x']*15),round(s['normalizedPupilY']/v['y']*15)):s['opdWaves'] for s in v['raw'] if s['intensity']>0}
        circular={(round(s['normalizedPupilX']*15),round(s['normalizedPupilY']*15)):s['opdWaves'] for s in v['circularRaw'] if s['intensity']>0}
        actual={(round(s['normalizedPupilX']/v['x']*15),round(s['normalizedPupilY']/v['y']*15)):np.exp(-2j*math.pi*s['opdWaves']) for s in v['smooth'] if s['intensity']>0}
        variants={'current':actual,'native':native,
            'native_mask_circular_trace':{p:np.exp(-2j*math.pi*phase) for p,phase in smooth({p:circular[p] for p in native}).items()},
            'circular_closed':{p:np.exp(-2j*math.pi*phase) for p,phase in smooth(circular).items()}}
        pitch=v['values']['ImageDeltaMilliradians']
        expected=d['expected']['series'][0]
        points=v['source'][0]['points'];source=np.array([p['value'] for p in points]).reshape(64,64)
        for name,pupil in variants.items():
            pixels=image(pupil)
            if name=='current':assert np.max(abs(pixels-source))<1e-12
            x,y=profile(pixels,pitch)
            metric=module.compare(x,y,expected['x'],expected['y'])
            x,vy=profile(pixels,pitch,True)
            vertical=module.compare(x,vy,expected['x'],expected['y'])
            phase_errors=[float(np.angle(pupil[p]/native[p])/(2*math.pi)) for p in pupil if p in native]
            summaries.append({'id':d['id'],'variant':name,'nodes':len(pupil),'metric':metric,
                'verticalMetric':vertical,'phaseRms':math.sqrt(sum(e*e for e in phase_errors)/len(phase_errors))})
    (RUN/'decomposition.json').write_text(json.dumps(summaries,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(summaries,indent=2))


if __name__=='__main__':main()
