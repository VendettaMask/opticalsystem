from pathlib import Path
import json
import sys

root = Path(sys.argv[1]); read = lambda p: json.loads(Path(p).read_text(encoding="utf-8-sig"))
paired = read(root / "paired-raw.json")
def od(d): return [min(s["Power"]["OpticalDensity"] for s in d["Result"]["Spectrum"] if b["MinimumNm"] <= s["WavelengthNanometers"] <= b["MaximumNm"]) for b in d["Target"].get("StopBands") or []]
def violations(d):
    t, m = d["Target"], d["Result"]["Metrics"]
    return [max(0,t["Reflectance"]-m["MinimumReflectance"])/(1-t["Reflectance"])] if t["Kind"]=="HighReflector" else [max(0,t["PeakTransmittance"]-m["PeakTransmittance"])/t["PeakTransmittance"], max(0,abs(m["FwhmNanometers"]-t["FwhmNm"])-t["FwhmToleranceNm"])/t["FwhmNm"] if m["FwhmNanometers"] is not None else 1, max(0,abs(m["PeakWavelengthNanometers"]-t["CenterNm"])-t["CenterToleranceNm"])/t["FwhmNm"], *[max(0,t["BlockingOd"]-x)/max(1,t["BlockingOd"]) for x in od(d)]]
def joint(a,b):
    r,s,t=a["Result"],b["Result"],b["Target"]
    if s["SamplingConverged"] and not r["SamplingConverged"] or s["Passed"] and not r["Passed"]: return False
    if t["Kind"]=="HighReflector":return r["Metrics"]["MinimumReflectance"]>=s["Metrics"]["MinimumReflectance"]
    x,y=r["Metrics"],s["Metrics"]
    return x["PeakTransmittance"]>=y["PeakTransmittance"] and x["FwhmNanometers"] is not None and abs(x["FwhmNanometers"]-t["FwhmNm"])<=abs(y["FwhmNanometers"]-t["FwhmNm"]) and all(x>=y for x,y in zip(od(a),od(b))) and all(x<=y for x,y in zip(violations(a),violations(b)))
for arm,rows in paired.items():
    for row in rows:
        original=read(root/"current-input"/(row["Name"]+".coating.json"));value=read(root/("current-"+arm)/(row["Name"]+".coating.json"))
        row["HistoricalSampling1"]={k:row[k] for k in ["Initial","InitialOd","Final","FinalOd","Passed","Joint"]}
        row.update(Initial=original["Result"]["Metrics"],InitialOd=od(original),Final=value["Result"]["Metrics"],FinalOd=od(value),Passed=value["Result"]["Passed"],Joint=joint(value,original),InitialPassed=original["Result"]["Passed"],SamplingProtocol=value["Result"].get("SamplingProtocol"))
Path(root/"current-paired.json").write_text(json.dumps(paired,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps({a:{"cases":len(v),"joint":sum(x["Joint"] for x in v),"passed":sum(x["Passed"] for x in v)} for a,v in paired.items()}))
