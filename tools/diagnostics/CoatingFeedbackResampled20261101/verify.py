from pathlib import Path
import hashlib
import json
import math
import re
import sys
import tarfile
import xml.etree.ElementTree as ET

r = Path(sys.argv[1])
read = lambda p: json.loads(Path(p).read_text(encoding="utf-8-sig"))
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
write = lambda p, x: Path(p).write_text(json.dumps(x, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
load = lambda folder: {p.name: read(p) for p in (r / folder).glob("*.coating.json")}
inputs, baseline, final, append = [load(f) for f in ["all-inputs", "formal-baseline-all", "formal-final-all", "formal-append-all"]]
assert len(inputs) == 15 and len(baseline) == len(final) == len(append) == 9
def od(d):
    return [min(s["Power"]["OpticalDensity"] for s in d["Result"]["Spectrum"] if b["MinimumNm"] <= s["WavelengthNanometers"] <= b["MaximumNm"]) for b in d["Target"].get("StopBands") or []]
def violations(d):
    t, m = d["Target"], d["Result"]["Metrics"]
    if t["Kind"] == "HighReflector": return [max(0, t["Reflectance"] - m["MinimumReflectance"]) / (1-t["Reflectance"])]
    w=m["FwhmNanometers"]
    return [max(0,t["PeakTransmittance"]-m["PeakTransmittance"])/t["PeakTransmittance"], 1 if w is None else max(0,abs(w-t["FwhmNm"])-t["FwhmToleranceNm"])/t["FwhmNm"], max(0,abs(m["PeakWavelengthNanometers"]-t["CenterNm"])-t["CenterToleranceNm"])/t["FwhmNm"]] + [max(0,t["BlockingOd"]-v)/max(1,t["BlockingOd"]) for v in od(d)]
def joint(a,b):
    ar,br=a["Result"],b["Result"];am,bm=ar["Metrics"],br["Metrics"];t=b["Target"]
    if br["SamplingConverged"] and not ar["SamplingConverged"] or br["Passed"] and not ar["Passed"]:return False
    if t["Kind"]=="HighReflector":return am["MinimumReflectance"]>=bm["MinimumReflectance"]
    return am["FwhmNanometers"] is not None and bm["FwhmNanometers"] is not None and am["PeakTransmittance"]>=bm["PeakTransmittance"] and abs(am["FwhmNanometers"]-t["FwhmNm"])<=abs(bm["FwhmNanometers"]-t["FwhmNm"]) and all(a>=b for a,b in zip(od(a),od(b))) and all(a<=b for a,b in zip(violations(a),violations(b)))
rows=[];windows=0;accepted=0
stages={x["Name"]:x for x in read(r/"stages/summary.json")}
for name,d in final.items():
    old=baseline[name];source=inputs[name]
    assert all(d[k]==source[k] for k in ["Target","Settings","Materials"])
    assert joint(d,old), name
    record=d["Result"]["Run"];history=record.get("StructureFeedbackWindows") or []
    fixed=any(not l["Variable"] for l in source["Layers"])
    assert len(history)==(0 if fixed else 2),(name,len(history))
    if fixed:assert all(l["Variable"] or d["Layers"][i]==l for i,l in enumerate(source["Layers"]))
    for w in history:
        windows+=1;accepted+=w["Accepted"]
        assert w["CandidateEvaluations"]<=w["CandidateLimit"]==256
        assert w["ShortIterationLimit"]==math.ceil(math.sqrt(source["Settings"]["MaximumIterations"]))
        assert len(w["Screening"])<=min(4,source["Settings"].get("Search",{}).get("Starts",6))
        assert all(s["Run"]["Iterations"]<=w["ShortIterationLimit"] for s in w["Screening"])
        assert w["RefinementRun"]["Iterations"]<=source["Settings"]["MaximumIterations"]
        assert all(s["Run"]["Iterations"]<=min(32,source["Settings"]["MaximumIterations"]) for s in w["Matching"])
        b=stages[d["Name"]+f"-feedback-{w['Round']}-baseline"];a=stages[d["Name"]+f"-feedback-{w['Round']}-full"]
        assert b["Metrics"]==w["BaselineMetrics"] and b["Od"]==w["BaselineBlockingOd"]
        if w["Round"]==1:assert b["Metrics"]==old["Result"]["Metrics"] and w["BaselineLayers"]==old["Layers"]
        # The full stage checks and metrics are independently re-evaluated. Every
        # accepted window must dominate its actual saved baseline components.
        if w["Accepted"]:
            assert not b["SamplingConverged"] or a["SamplingConverged"]
            assert not b["Passed"] or a["Passed"]
            assert all(not old["Passed"] or new["Passed"] for new,old in zip(a["Checks"],b["Checks"]))
            assert a["Metrics"]["PeakTransmittance"]>=b["Metrics"]["PeakTransmittance"]
            assert abs(a["Metrics"]["FwhmNanometers"]-d["Target"]["FwhmNm"])<=abs(b["Metrics"]["FwhmNanometers"]-d["Target"]["FwhmNm"])
            assert all(x>=y for x,y in zip(a["Od"],b["Od"]))
        assert w["FullPrototype"]["CavityOrders"] and len(w["FullPrototype"]["CavityOrders"])==(d["Target"].get("Structure") or {}).get("Cavities",1)
    rows.append({"Name":d["Name"],"Initial":source["Result"]["Metrics"],"Baseline":old["Result"]["Metrics"],"Final":d["Result"]["Metrics"],"Append":append[name]["Result"]["Metrics"],"InitialOd":od(source),"BaselineOd":od(old),"FinalOd":od(d),"AppendOd":od(append[name]),"Passed":d["Result"]["Passed"],"BaselinePassed":old["Result"]["Passed"],"JointBaseline":True,"JointAppend":joint(d,append[name]),"AcceptedWindows":sum(w["Accepted"] for w in history)})
cold=read(r/"cold-output/summary.json")
assert len(cold)==len(read(r/"cold-index.json")) and all(x["Identical"] and x["RawIdentical"] and x["MetadataIdentical"] for x in cold)
physics=read(r/"physics-comparison.json");assert all(math.isfinite(v) and v<=2e-11 for v in physics["maximumErrors"].values())
tests=next(x.attrib for x in ET.parse(r/"logs/api-structure-feedback.trx").iter() if x.tag.endswith("Counters"));assert tests["total"]==tests["passed"]=="18" and tests["failed"]=="0"
engine=Path("labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine")
assert all(p.read_bytes()==(engine/p.name).read_bytes() for p in (r/"final-source").glob("*.cs"))
changed=[p.name for p in (r/"baseline-source").glob("*.cs") if p.read_bytes()!=(engine/p.name).read_bytes()]
assert set(changed)=={"DesignService.GeneralSearch.cs","Experiment.cs"}
marker="public sealed record Experiment("
assert (r/"baseline-source/Experiment.cs").read_text(encoding="utf-8-sig").split(marker,1)[1]==(engine/"Experiment.cs").read_text(encoding="utf-8-sig").split(marker,1)[1]
assert {p.name for p in engine.glob("*.cs")}-{p.name for p in (r/"baseline-source").glob("*.cs")}=={"DesignService.StructureFeedback.cs"}
research=(r/"research-source/DesignService.GeneralSearch.cs").read_text(encoding="utf-8-sig")
current=(engine/"DesignService.GeneralSearch.cs").read_text(encoding="utf-8-sig")
research=research.replace("IProgress<DesignProgress>? progress, CancellationToken token)\n", "IProgress<DesignProgress>? progress, CancellationToken token, bool matchedFeedback = false)\n",1)
research=research.replace("            if (structural)\n            {\n                // Preserve", "            if (structural && matchedFeedback)\n            {\n                // Preserve",1)
assert research==current
marker="    private static bool JointStructureFeedbackImprovement"
assert (r/"research-source/DesignService.StructureFeedback.cs").read_text(encoding="utf-8-sig").split(marker,1)[1]==(engine/"DesignService.StructureFeedback.cs").read_text(encoding="utf-8-sig").split(marker,1)[1]
assert sha(r/"api-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")==sha(r/"api-test-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")==sha(r/"cold-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")
assert len({sha(r/runtime/"OpticalSystemDesign.Core.dll") for runtime in ["baseline-runtime","two-window-runtime","api-runtime","api-test-runtime","cold-runtime","fine-runtime","stage-runtime"]})==1
for s in read(r/"materials/sources.json"):
    assert sha(r/"materials"/s["file"])==s["sha256"]
    if s["material"]!="Al2O3":
        assert sha(Path("third_party/coating-reference")/s["file"])==s["sha256"]
        text=(r/"materials"/s["file"]).read_text(encoding="utf-8")
        table=[list(map(float,m)) for m in re.findall(r"^\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s*$",text,re.M)]
        snapshots=[m for d in inputs.values() for m in d["Materials"] if m["Id"]==s["file"][:-4]]
        assert snapshots
        for material in snapshots:
            numbers=material["Model"]["Numbers"]
            for i,(wave,n,k) in enumerate(table):assert [numbers[f"nw{i}"],numbers[f"n{i}"],numbers[f"kw{i}"],numbers[f"k{i}"]]==[wave*1000,n,wave*1000,k]
prefix="TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428"
with tarfile.open("third_party/coating-reference/TFStudio-8b942b3.tar.gz") as archive:
    for file in ["src/utils/synthesis/structuralOptimizer.js","src/utils/physics/optimizer/layerOps.js","src/utils/physics/optimizer/lsqEngine.js","src/utils/physics/optimizer/operandModel.js"]:
        assert sha(Path(".tmp/coating-infeasible-repair")/prefix/file)==hashlib.sha256(archive.extractfile(prefix+"/"+file).read()).hexdigest()
with tarfile.open("third_party/coating-reference/tmmcore-0.4.2.tgz") as archive:
    assert sha(".tmp/coating-edge-audit-20261030/package/src/tmm.js")==hashlib.sha256(archive.extractfile("package/src/tmm.js").read()).hexdigest()
fine=read(r/"fine-sampling.json")
for record in [x for x in fine if x["File"]=="new550.coating.json"]:
    assert abs(record["Metrics"]["FwhmNanometers"]-15)<=1 and abs(record["Metrics"]["PeakWavelengthNanometers"]-550)<=.5
assert all(x["SamplingConverged"] for x in fine if "PreviewIntervals" in x)
previous=Path("validation/rechecks/2026-11-01/coating-topology-feedback")
assert sha(previous/"final-manifest.json")== (r/"previous-manifest-sha256.txt").read_text().strip()
previous_manifest=read(previous/"final-manifest.json");assert all(sha(previous/x["path"])==x["sha256"] for x in previous_manifest["entries"])
write(r/"formal-paired.json",rows)
write(r/"verification.json",{"researchSpecifications":9,"feedbackWindows":windows,"acceptedWindows":accepted,"jointBaselinePreserved":9,"coldReplays":len(cold),"stageRechecks":len(stages),"tests":tests,"physics":{"points":physics["cases"],"maximumErrors":physics["maximumErrors"]},"sourceChanged":changed+["DesignService.StructureFeedback.cs"],"oldEvidence":{"files":previous_manifest["files"],"mismatches":0},"researchEngineSha256":sha(r/"two-window-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll"),"finalEngineSha256":sha(r/"api-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll"),"coreSha256":sha(r/"api-runtime/OpticalSystemDesign.Core.dll"),"activationBoundary":read(r/"activation-boundary.json")})
print(json.dumps(read(r/"verification.json"),ensure_ascii=True))
