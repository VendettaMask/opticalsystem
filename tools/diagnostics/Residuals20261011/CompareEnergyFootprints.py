"""Independent offline pixel-footprint hypotheses; never product inputs."""
import json, pathlib, sys
import numpy as np
root = pathlib.Path(sys.argv[1]).resolve()
out = pathlib.Path(sys.argv[2]).resolve()
out.mkdir(parents=True, exist_ok=False)
base = root / 'artifacts/validation/residuals-20261011'
grids = json.loads((base/'followup/dee-phase-controls/dee-phase-controls.json').read_text(encoding='utf-8-sig'))
records = []

def spline(axis, y, x):
    h = np.diff(axis)
    matrix = np.diag(2*(h[:-1]+h[1:])) + np.diag(h[1:-1],1) + np.diag(h[1:-1],-1)
    second = np.r_[0, np.linalg.solve(matrix, 6*np.diff(np.diff(y)/h)), 0]
    j = np.clip(np.searchsorted(axis, x)-1, 0, len(axis)-2)
    a = (axis[j+1]-x)/h[j]; b = (x-axis[j])/h[j]
    return np.clip(a*y[j]+b*y[j+1]+((a**3-a)*second[j]+(b**3-b)*second[j+1])*h[j]**2/6,0,1)

for grid in grids:
    if not grid['aiming'] or not grid['launch']: continue
    if grid['ideal'] and grid['phase'] != 'raw': continue
    for size in [32,64]:
        start = (64-size)//2
        power = np.array(grid['values'])[start:start+size,start:start+size]
        pitch = grid['spacing']; co = (np.arange(size)+start-31)*pitch
        center = (co*power.sum(axis=0)).sum()/power.sum()
        yy,xx = np.meshgrid(co-center,co-center,indexing='ij'); rho = np.hypot(xx,yy)
        c = np.divide(abs(xx),rho,out=np.zeros_like(rho),where=rho>1e-9)
        s = np.divide(abs(yy),rho,out=np.zeros_like(rho),where=rho>1e-9)
        for kind in ['linear','annular','tangent','disk','centers']:
            for maximum in [2.5,5,10]:
                slug = 'dee-32' if maximum == 5 else 'dee-range-2p5' if maximum == 2.5 else 'dee-range-10'
                native = json.loads((root/f'validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{slug}/data.json').read_text(encoding='utf-8-sig'))['dataSeries'][0 if grid['ideal'] else 1]
                axis = np.linspace(0,maximum,32)
                knots = []
                for radius in axis:
                    d = radius-rho
                    if kind == 'linear': f = np.clip(d/pitch+.5,0,1)
                    elif kind == 'centers': f = (rho<=radius).astype(float)
                    elif kind == 'annular':
                        lo = np.maximum(0,rho-pitch/2);hi = rho+pitch/2
                        f = np.clip((radius**2-lo**2)/(hi**2-lo**2),0,1)
                    elif kind == 'tangent':
                        a = pitch*c; b = pitch*s; z = d+(a+b)/2
                        f = np.divide(np.maximum(z,0)**2-np.maximum(z-a,0)**2-np.maximum(z-b,0)**2+np.maximum(z-a-b,0)**2,
                                      2*a*b,out=np.zeros_like(rho),where=a*b>1e-14)
                        single = np.maximum(a,b)
                        f = np.where(a*b<=1e-14,np.clip(np.divide(d,single,out=np.zeros_like(d),where=single>0)+.5,0,1),f)
                        f = np.where(rho<pitch/4,np.clip(np.pi*radius**2/pitch**2,0,1),f)
                    else:
                        cell = pitch/np.sqrt(np.pi)
                        safe = np.maximum(rho,1e-20)
                        x = (safe**2+cell**2-radius**2)/(2*safe)
                        a = np.arccos(np.clip(x/cell,-1,1)); b = np.arccos(np.clip((safe-x)/max(radius,1e-20),-1,1))
                        area = cell**2*a+radius**2*b-safe*np.sqrt(np.maximum(0,cell**2-x*x))
                        f = area/(np.pi*cell**2)
                        f = np.where(rho+cell<=radius,1,np.where(rho+radius<=cell,radius**2/cell**2,np.where(rho>=cell+radius,0,f)))
                    knots.append(float(np.sum(power*f)/power.sum()))
                knots = np.array(knots); knots[0]=0; knots[1]=knots[2]/4
                x = np.array(native['x']); target = np.array(native['y'])[:,0]
                values = spline(axis,knots,x)
                nrmse = float(np.sqrt(np.mean((values-target)**2))/(target.max()-target.min()))
                record = dict(ideal=grid['ideal'],phase=grid['phase'],size=size,kind=kind,maximum=maximum,center=center,nrmse=nrmse,
                              maximumDifference=float(abs(values-target).max()),knots=knots.tolist(),values=values.tolist())
                records.append(record)
                if maximum == 5: print({k:v for k,v in record.items() if k not in ['values','knots']})
(out/'footprints.json').write_text(json.dumps(records),encoding='utf-8')
