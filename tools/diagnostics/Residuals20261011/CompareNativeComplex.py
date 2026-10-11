"""Independent offline field comparison, never a runtime optical engine."""
import json, pathlib, sys
import numpy as np
root=pathlib.Path(sys.argv[1]).resolve()
out=pathlib.Path(sys.argv[2]).resolve()
out.mkdir(parents=True,exist_ok=False)
base=root/"artifacts/validation/residuals-20261011/followup"
def read(p): return json.loads(p.read_text(encoding="utf-8-sig"))
result=[]
for nodes in ["endpoints","zemax"]:
    source=read(base/f"native/pupil-{nodes}/pupil-grid.json")
    px=np.array([p["px"] for p in source["inputs"]]); py=np.array([p["py"] for p in source["inputs"]])
    rays=source["rows"]
    def column(name): return np.array([v[name] for v in rays])
    x,y,z,l,m,n,opd=[column(k) for k in ["x","y","z","l","m","n","opd"]]
    dot=x*l+y*m+z*n
    phase=-opd*2*np.pi+2*np.pi/.0004861327*(np.sqrt(dot*dot+11.33607822960428**2-x*x-y*y-z*z)-11.33607822960428)
    for factor in [0,4,32,512]:
        folder=base/"native" if factor==0 else base/"gaussian"
        real=read(folder/("huygens-real" if factor==0 else f"{factor}-real")/"Planar/data.json")["dataGrids"][0]
        imag=read(folder/("huygens-imaginary" if factor==0 else f"{factor}-imaginary")/"Planar/data.json")["dataGrids"][0]
        expected=np.array(real["values"])+1j*np.array(imag["values"])
        coords=(np.arange(32)*real["dx"]+real["minX"])/1000
        amplitudes=np.exp(-factor*(px*px+py*py))
        if nodes=="endpoints": amplitudes[(px==0)&(py==0)]=0
        xx,yy=np.meshgrid(coords,coords)
        actual=(np.exp(1j*(phase[None,:]+2*np.pi/.0004861327*(xx.reshape(-1,1)*l[None,:]+yy.reshape(-1,1)*m[None,:])))@amplitudes/amplitudes.sum()).reshape(32,32)
        # Diagnose the native phasor sign independently of intensity. A global
        # piston is unobservable here; it is removed only in this diagnostic.
        actual=actual.conjugate()
        actual*=np.exp(1j*np.angle(expected[16,16]/actual[16,16]))
        ratio=expected[16,16:]/actual[16,16:]
        error=np.linalg.norm(actual-expected)/np.linalg.norm(expected)
        line=[dict(x=float(coords[16+i]*1000),amplitude=float(abs(v)),phase=float(np.angle(v))) for i,v in enumerate(ratio)]
        result.append(dict(nodes=nodes,factor=factor,complexRelativeL2=float(error),ratio=line))
        print(nodes,factor,"complex L2",error,"ratio first 5",[(v["amplitude"],v["phase"]) for v in line[:5]])
(out/"complex-comparison.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
