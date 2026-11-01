from pathlib import Path
import hashlib
import json
import math
import shutil
import sys
import tarfile

r=Path(sys.argv[1]);read=lambda p:json.loads(Path(p).read_text(encoding="utf-8-sig"));sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
write=lambda p,x:Path(p).write_text(json.dumps(x,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
profiles=read(r/"profiles.json");directions=read(r/"directions.json");restored=read(r/"restored/summary.json");fine=read(r/"fine-sampling.json");physics=read(r/"reference-comparison.json")
assert len(profiles)==18 and len(directions)==2 and len(restored)==4 and len(fine)==30
assert physics["points"]==246 and all(math.isfinite(v) and v<=2e-11 for v in physics["maximumErrors"].values())
assert all(x["Joint"] for x in restored)
replay=read(r/"restored-replay/summary.json");assert len(replay)==4 and all(x["Identical"] and x["RawIdentical"] and x["MetadataIdentical"] for x in replay)
def od(d):return [min(s["Power"]["OpticalDensity"] for s in d["Result"]["Spectrum"] if b["MinimumNm"]<=s["WavelengthNanometers"]<=b["MaximumNm"]) for b in d["Target"].get("StopBands") or []]
def load(folder,name):return read(r/folder/(name+".coating.json"))
def same(d,old):return all(d[k]==old[k] for k in ["Target","Settings","Materials"])
for p in (r/"restored").glob("*.coating.json"):
 d=read(p);assert same(d,load("primary-inputs",d["Name"]))
for p in (r/"old-n").glob("*.coating.json"):
 d=read(p);assert same(d,load("inputs",d["Name"]))
assert len(list((r/"old-n").glob("*.coating.json")))==15
original=load("inputs","real-nbf2-96")
focused_input=load("focus-larger-input","real-nbf2-96");assert focused_input["Settings"]["MaximumIterations"]==272
settings=dict(focused_input["Settings"]);settings["MaximumIterations"]=96
assert settings==original["Settings"] and focused_input["Target"]==original["Target"] and focused_input["Materials"]==original["Materials"]
# Check like-for-like sampling; do not conceal the sub-micro-nm reversal.
dense=[]
for row in restored:
 file=row["Name"]+"-"+str(row["Radius"])+".coating.json";data=[]
 for x in [x for x in fine if x["File"]==file]:
  key="PreviewIntervals" if "PreviewIntervals" in x else "Intervals"
  anchor=next(z for z in fine if z["File"]=="anchor-"+row["Name"]+".coating.json" and z.get(key)==x[key])
  target=load("primary-inputs",row["Name"])["Target"];a=x["Metrics"];b=anchor["Metrics"]
  joint=a["PeakTransmittance"]>=b["PeakTransmittance"] and abs(a["FwhmNanometers"]-target["FwhmNm"])<=abs(b["FwhmNanometers"]-target["FwhmNm"]) and all(a>=b for a,b in zip(x["Od"],anchor["Od"])) and abs(a["PeakWavelengthNanometers"]-target["CenterNm"])<=target["CenterToleranceNm"]
  data.append({"Intervals":x[key],"Joint":joint,"WidthChangeNm":a["FwhmNanometers"]-b["FwhmNanometers"]})
 dense.append({"Name":row["Name"],"Radius":row["Radius"],"AllSamplingJoint":all(x["Joint"] for x in data),"Checks":data})
assert all(x["AllSamplingJoint"] for x in dense if x["Radius"]==.01)
assert not next(x for x in dense if x["Name"]=="real-nbf2-96" and x["Radius"]==.001)["AllSamplingJoint"]
engine=Path("labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine")
assert all(p.read_bytes()==(engine/p.name).read_bytes() for p in (r/"baseline-source").glob("*.cs"))
previous=Path("validation/rechecks/2026-11-01/coating-feedback-resampled");manifest=read(previous/"final-manifest.json")
assert sha(previous/"final-manifest.json")==(r/"previous-manifest-sha256.txt").read_text().strip()
assert all(sha(previous/x["path"])==x["sha256"] for x in manifest["entries"])
with tarfile.open("third_party/coating-reference/tmmcore-0.4.2.tgz") as archive:
 assert sha(".tmp/coating-edge-audit-20261030/package/src/tmm.js")==hashlib.sha256(archive.extractfile("package/src/tmm.js").read()).hexdigest()
old=load("old-n","real-nbf2-96");assert old["Result"]["Metrics"]["PeakTransmittance"]>original["Result"]["Metrics"]["PeakTransmittance"] and all(a<b for a,b in zip(od(old),od(original)))
count=sum(len(x["Family"]) for x in read(r/"periodic.json"));assert count==192
verification={"productEngineSourceChanged":False,"profiles":18,"referencePowerAndJacobianPoints":246,"referenceTolerance":2e-11,"referenceMaximumErrors":physics["maximumErrors"],"periodicFamilyCases":192,"localConeCases":2,"rawDirectionTrials":48,"nominalFormalRestorationsJoint":4,"stableAllGridRestorationsJoint":sum(x["AllSamplingJoint"] for x in dense),"fineSamplingChecks":30,"coldReplays":4,"oldNControlSpecifications":15,"largerNBatch":read(r/"budget-control-scope.json"),"focusedLargerNCompleted":False,"previousEvidence":{"files":manifest["files"],"mismatches":0},"coreSha256":sha(r/"baseline-runtime/OpticalSystemDesign.Core.dll"),"engineSha256":sha(r/"baseline-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")}
write(r/"dense-restoration-comparison.json",dense);write(r/"verification.json",verification)
rows=[]
for name in ["real-nbf2-96","new-real-nbf-green"]:
 d=load("inputs",name);o=load("old-n",name);proof=next(x for x in restored if x["Name"]==name and x["Radius"]==.01)
 rows.append({"Name":name,"Input":d["Result"]["Metrics"],"InputOd":od(d),"OldN":o["Result"]["Metrics"],"OldNOd":od(o),"ActualCorrectionProof":proof})
write(r/"root-cause-paired.json",rows)
print(json.dumps(verification,ensure_ascii=True))
