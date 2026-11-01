from pathlib import Path
import hashlib, json, math, shutil, sys, tarfile
import xml.etree.ElementTree as ET

r=Path(sys.argv[1]);read=lambda p:json.loads(Path(p).read_text(encoding="utf-8-sig"));sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
write=lambda p,x:Path(p).write_text(json.dumps(x,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
raw,pair=read(r/"paired-raw.json"),read(r/"current-paired.json")
assert {a:len(v) for a,v in pair.items()}=={"feedback":9,"control":9,"prototype":6}
inputs={p.name:read(p) for p in (r/"inputs").glob("*.coating.json")}
for arm,rows in raw.items():
    for row in rows:
        d=inputs[row["Name"]+".coating.json"];n=d["Settings"]["MaximumIterations"]
        assert row["ShortAllowance"]==min(n,math.ceil(math.sqrt(n))) and len(row["Runs"])==2*(row["Starts"]+1)
        assert all(x["Run"]["Iterations"]<=x["Allowance"] for x in row["Runs"])
        if not row["StructuralAllowed"]:assert row["Proposals"]==0
        for x in row["Runs"]:
            assert len(x["FinalLayers"])<=d["Target"]["MaximumLayers"] and all(d["Settings"]["MinimumThicknessNm"]<=l["ThicknessNm"]<=d["Settings"]["MaximumThicknessNm"] for l in x["FinalLayers"])
            if not row["StructuralAllowed"]:assert all(l["Variable"] or l==x["FinalLayers"][i] for i,l in enumerate(d["Layers"]))
        row["VerifiedDenseCalls"]=row["DenseCalls"]+sum((x["Run"].get("GeneralWork") or {}).get("DenseValidations",0) for x in row["Runs"] if x["Stage"]=="short")
        if arm!="control":
            assert row["InputFingerprint"]==next(x for x in raw["control"] if x["Name"]==row["Name"])["InputFingerprint"]
            for full in [x for x in row["Runs"] if x["Stage"]=="full"]:
                assert any(x["Stage"]=="short" and x["Round"]==full["Round"] and x["FinalLayers"]==full["InitialLayers"] for x in row["Runs"])
    replay=read(r/("replay-"+arm)/"summary.json")
    assert len(replay)==len(rows) and all(x["IdenticalReportedMetrics"] and x["IdenticalReportedSpectrum"] and x["IdenticalReportedChecks"] for x in replay)
    assert all(x["Joint"] for x in pair[arm])
    for p in (r/("current-"+arm)).glob("*.coating.json"):
        d=read(p);old=inputs[p.name]
        assert d["Materials"]==old["Materials"] and d["Target"]==old["Target"] and d["Settings"]==old["Settings"]
        assert [(c["Name"],c["Requirement"]) for c in d["Result"]["Checks"]]==[(c["Name"],c["Requirement"]) for c in old["Result"]["Checks"]]
stages=read(r/"current-stages/summary.json");assert len(stages)==240
new=[x for x in stages if x["Passed"] and not read(r/"current-input"/(x["Source"]+".coating.json"))["Result"]["Passed"]];assert not new
assert len(list((r/"current-stages").glob("*.coating.json")))==48
engine=Path("labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine")
source=[{"file":p.name,"unchanged":p.read_bytes()==(engine/p.name).read_bytes(),"sha256":sha(engine/p.name)} for p in (r/"baseline-source").glob("*.cs")]
assert {x["file"] for x in source if not x["unchanged"]}=={"DesignService.cs","Experiment.cs"}
assert {p.name for p in engine.glob("*.cs")}-{p.name for p in (r/"baseline-source").glob("*.cs")}=={"DesignService.DenseExtrema.cs"}
norm=lambda p:Path(p).read_text(encoding="utf-8-sig").replace("\r\n","\n")
marker="    private static ThinFilmBandMetrics Metrics("
assert norm(engine/"DesignService.cs").split(marker,1)[1]==norm(r/"baseline-source/DesignService.cs").split(marker,1)[1]
assert norm(engine/"Experiment.cs").split("public sealed record Experiment(",1)[1]==norm(r/"baseline-source/Experiment.cs").split("public sealed record Experiment(",1)[1]
tests=next(x.attrib for x in ET.parse(r/"logs/dense-extrema-focused.trx").iter() if x.tag.endswith("Counters"));assert int(tests["total"])==12 and int(tests["passed"])==12 and int(tests["failed"])==0
assert sha(r/"test-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")==sha(r/"final-runtime/OpticalSystemDesign.CoatingDesign.Engine.dll")
physics=read(r/"physics-comparison-current.json");assert all(math.isfinite(v) and v<=2e-11 for v in physics["maximumErrors"].values())
before,after=read(r/"fine-sampling-before.json"),read(r/"fine-sampling-after.json")
centre=lambda x:next(c["Passed"] for c in x["Checks"] if c["Name"]=="峰值位置 (nm)")
for file in ["warm550.coating.json","role550.coating.json","old272.coating.json"]:
    a=[x for x in before if x["File"]==file and "PreviewIntervals" in x];b=[x for x in after if x["File"]==file and "PreviewIntervals" in x]
    assert {centre(x) for x in a}=={True,False} and all(centre(x) and x["SamplingConverged"] for x in b)
prefix="TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428";refs=[]
with tarfile.open("third_party/coating-reference/TFStudio-8b942b3.tar.gz") as archive:
    for f in ["src/utils/synthesis/structuralOptimizer.js","src/utils/physics/optimizer/layerOps.js","src/utils/physics/optimizer/lsqEngine.js","src/utils/physics/optimizer/operandModel.js"]:
        p=Path(".tmp/coating-infeasible-repair")/prefix/f;refs.append({"path":str(p),"unchanged":sha(p)==hashlib.sha256(archive.extractfile(prefix+"/"+f).read()).hexdigest()})
assert all(x["unchanged"] for x in refs)
with tarfile.open("third_party/coating-reference/tmmcore-0.4.2.tgz") as archive:assert sha(".tmp/coating-edge-audit-20261030/package/src/tmm.js")==hashlib.sha256(archive.extractfile("package/src/tmm.js").read()).hexdigest()
previous=Path("validation/rechecks/2026-11-01/coating-phase-order");manifest=read(previous/"final-manifest.json");assert all(sha(previous/x["path"])==x["sha256"] for x in manifest["entries"])
ref=read(r/"current-reference/summary.json");large=read(r/"current-larger-old/summary.json");assert len(ref)==8 and len(large)==2
for p in (r/"current-larger-old").glob("*.coating.json"):
    d=read(p);old=inputs[p.name];s=dict(d["Settings"]);s["MaximumIterations"]=old["Settings"]["MaximumIterations"]
    assert s==old["Settings"] and d["Target"]==old["Target"] and d["Materials"]==old["Materials"]
counts={"inputs":9,"topologyCases":9,"prototypeCases":6,"oldScheduleCases":9,"largerOldCases":2,"referenceCases":8,"topologyProposals":sum(x["Proposals"] for x in raw["feedback"]),"prototypeProposals":sum(x["Proposals"] for x in raw["prototype"]),"shortCalls":192,"fullCalls":48,"stageRechecks":240,"replayedRetained":24,"newSpecificationsPassed":0,"focusedTests":12,"physicsPoints":physics["cases"]}
binaries=[{"runtime":runtime,"core":sha(r/runtime/"OpticalSystemDesign.Core.dll"),"engine":sha(r/runtime/"OpticalSystemDesign.CoatingDesign.Engine.dll")} for runtime in ["baseline-runtime","feedback-runtime","prototype-runtime","final-runtime","test-runtime"]];assert len({x["core"] for x in binaries})==1
write(r/"verification.json",{"scope":"Feedback optimized under frozen sampling/1; all compared stacks re-evaluated under final sampling/2. Production correction is formal sampling only.","counts":counts,"source":source,"numericChecksConvergenceThresholdsAndMeasureUnchanged":True,"inputSchemaAndFingerprintUnchanged":True,"tests":tests,"physics":{"cases":physics["cases"],"maximumErrors":physics["maximumErrors"]},"referenceSources":refs,"referencePasses":sum(x["Passed"] for x in ref),"previousEvidence":{"files":manifest["files"],"mismatches":0},"binaries":binaries})
write(r/"paired-final.json",pair);write(r/"execution-ledger.json",{"counts":counts,"incomplete":"Superseded control/probe processes are not final completed runs; source and stop records preserved.","cost":"Same requested continuous schedule is not equal total compute. Coarse queries, internal restoration/quality and OpenAsync recalculation cost extra; VerifiedDenseCalls adds short-call internal dense fields to the original partial DenseCalls counter.","validation":"240-stage recertification and RTA are separate validation work; Result.ExtremaRefinementEvaluations is per Evaluate, not a complete optimizer cost total.","initialFineBuild":"Shared obj conflict then nullable warning; both corrected, original logs retained."})
f=lambda x:f"{x:.6f}";ods=lambda a:" / ".join(f(v) for v in a)
nb=[];hr=[]
for x in pair["feedback"]:
    c=next(z for z in pair["control"] if z["Name"]==x["Name"]);p=next((z for z in pair["prototype"] if z["Name"]==x["Name"]),None)
    if inputs[x["Name"]+".coating.json"]["Target"]["Kind"]=="HighReflector":hr.append(f"| {x['Name']} | {f(x['Initial']['MinimumReflectance']*100)} | {f(x['Final']['MinimumReflectance']*100)} | {f(c['Final']['MinimumReflectance']*100)} |")
    else:
        for label,v in [("起点",{"Final":x["Initial"],"FinalOd":x["InitialOd"]}),("自由拓扑",x),("角色原型",p),("追加旧日程",c)]:
            if v:nb.append(f"| {x['Name']} · {label} | {f(v['Final']['PeakTransmittance']*100)} | {f(v['Final']['FwhmNanometers'])} | {ods(v['FinalOd'])} |")
lr=[f"| {x['Name']} | {x['Settings']['MaximumIterations']} | {f(x['AfterMetrics']['PeakTransmittance']*100)} | {f(x['AfterMetrics']['FwhmNanometers'])} | {ods(x['AfterBlockingOd'])} |" for x in large]
report=f"""# 匹配后反馈与正式极值采样修正（2026-11-01）

**550 nm真实双腔的角色反馈获得共同改善**：T约66.45%→67.80%，FWHM约17.217→17.197 nm，左右OD约1.016/1.236→1.026/1.393，优于同连续日程旧控制的共同保留结果。N=272单次旧控制带宽仍稍窄，不能称对所有旧额度控制共同胜出。仍未到75%及15±1 nm；500 nm和98%高反也无新增整组通过。反馈方法尚未接入正式搜索。

产品代码已加入试验暴露的通用采样修正：同一膜系在原550±0.5 nm门槛下，网格400/1600档中心项通过、3200档失败，旧采样都报告收敛。共享Core局部极值细化后各档判定稳定。真实n/k、Core光学及Measure公式、Checks/Converged数值阈值均保持，没有材料名、固定膜层序号或例子签名分支。

## 正式采样与文件契约

窄带正式采样身份更新为`extrema-anchored-band-domain/2`，覆盖各窄带设计协议，默认协议选择仍为AR/HR 2、窄带3。保留原窗口端点及半高边缘细网格，调用现有Core `RefineTransmissionPeak`细化已观测通带和截止局部透射极大值，从而准确定位峰位及最差OD；相邻两级Checks判定也须稳定。每级最多64个候选，超限/括区不足不确认收敛。有限网格和局部括区不是全局光谱证明。

`Result.ExtremaRefinementEvaluations`记录本次Evaluate各级极值额外查询，`ExtremaRefinementComplete`记录最终级候选预算完成状态；不冒充完整优化成本。可空字段不补造历史值，schema 1、输入指纹不变。旧文件打开本就按材料快照复算，现在记录新身份。指导采样`search-peak-reference/2`保持独立；AR/HR取样及所有其他优化器源未改。

同一550 nm起点、角色候选、旧N272结果在400/1600/3200档复验：旧中心项均有翻转，新中心项均稳定。8192/32768均匀网格及Core局部极值支持T/OD/FWHM改善。角色候选的峰位非常接近中心容差边界，保留全部数据而不加宽门槛。新采样下各臂的整组通过数量仍为原2组，没有制造新增达标。

## 反馈实现、旧控制与成本

自由拓扑遍历删层、宿主分裂插层、边界匹配；只按同材料真实厚度合并自由相邻层，越界排除。到层数上限时先删后插。仅VaryStructure允许且全部层可变时编辑拓扑，固定5层近红外提议数为0。

角色原型改变声明镜段层数/腔阶，按同角色、同材料优先及归一化段内位置转移已拟合膜厚比例，保留入射匹配前缀；基准厚度来自Core真实n/k和角度。无法核对原型、材料顺序、前缀或厚度界即排除，不猜某层角色或重置为未拟合四分之一波结构。此臂保留声明腔关系；任意增删层臂仅保留Target.Cavities=2不足以另证最终物理腔数，不能混称真正双腔突破。

每臂两轮，每轮min(4,Starts)个短程（各主额度≤ceil(sqrt(N)))，再给新挑战者原N完整Optimize并反馈下一轮。探索点可暂时较差，正式保留点严格保护T、每个OD、带宽误差、原超限及已有采样/通过；HR保护最小R。旧控制使用相同连续调用请求并传递探索点。初版诊断器的旧粗分遮挡挑战者、旧控制回滚探索点问题已在最终集合前修正，中止版本另列，不冒称正式旧产品修复。

9组自由拓扑、6组角色原型、9组旧日程，共{counts['topologyProposals']}个拓扑提议、{counts['prototypeProposals']}个原型提议、192次短程和48次完整调用。另按2N+2min(4,Starts)ceil(sqrt(N))给单次旧流程N=152/272，仅改变该控制N，其他材料、目标、采样/厚度界保持。粗指导、内部恢复/质量及打开文件复算有额外成本，不称等总迭代/查询/算力胜出。

原优化轨迹由冻结采样/1产生；下表**实际膜厚全部按最终采样/2冷复算**，没有将旧指标混作新结果，也未冒称重跑240次新采样优化。

| 规格/臂 | T（%） | FWHM（nm） | 各截止OD |
|---|---:|---:|---|
{chr(10).join(nb)}

650 nm自由拓扑相对起点共同改善，但旧日程在T、各OD、带宽均更好。550 nm角色反馈是可继续研究的方向，尚无替换正式搜索的全面优势。材料范围为原SiO2/Ta2O5实测表跨波段，1550 nm合成控制单列，不称多套实测材料认证。

| 真实高反 | 起点Rmin（%） | 自由拓扑Rmin（%） | 旧日程Rmin（%） |
|---|---:|---:|---:|
{chr(10).join(hr)}

三项仍未98%。近红外两臂差约几个1e-9 R，低于既有1e-5采样分辨率，不称稳定增益。有限两轮不证明全局无解。

| 原规格 | 单次旧N | T（%） | FWHM（nm） | 各截止OD |
|---|---:|---:|---:|---|
{chr(10).join(lr)}

## 参考和验证边界

固定TFStudio 8b942b3未修改的突变、清理、Metropolis接受及DLS运行8组相同起点，真实n/k、原层数/厚度界及同类连续预算，最终原门槛{sum(x['Passed'] for x in ref)}/8通过。其点平方目标不同于实测FWHM/最差OD/最差R，不等总查询成本，不宣称优化器整体优越。原归档和所用源码保持。

默认Release Engine正常依赖图构建0警告/错误；最终隔离定向**12/12**：新增4项独立解析单层共振内/外边界、2项真实色散网格稳定与保存重开，相邻生成/搜索6项，非完整模块或全仓。加密探针初次obj冲突及可空警告修正后通过，原始日志保留。

24个保留结果保存重开逐值一致；全部240个实际处方以新采样独立重评，48个完整处方光谱保留，无原未过规格的新通过。{physics['cases']}个实际n/k、S/P/非偏振独立R/T/A比较点（含各臂重复处方）对固定tmmcore通过既有2e-11标准，最大差R={physics['maximumErrors']['R']}、T={physics['maximumErrors']['T']}、A={physics['maximumErrors']['A']}。上一阶段169项证据保持。源码修改限正式采样、可空结果元数据及回归测试，Core光学未改；未操作原生桌面、提交、推送或发布。见[账目、处方、反例与哈希](../{r.as_posix()}/README.md)。
"""
Path("docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md").write_text(report,encoding="utf-8")
status=Path("docs/CURRENT_STATUS.md");s=status.read_text(encoding="utf-8-sig");title="## 2026-11-01 镀膜匹配后反馈与正式极值采样修正"
if title not in s:
    first,rest=s.split("\n",1);s=first+f"\n\n{title}\n\n角色反馈让550 nm真实双腔T约66.45%→67.80%、FWHM17.217→17.197 nm、OD1.016/1.236→1.026/1.393，共同优于同日程旧控制；N=272旧控制带宽稍窄，未称全面胜出。550/500 nm及98%高反仍无新增整组通过，反馈未接入正式搜索。9组拓扑、6组原型对照9组旧日程、2组更大旧N和8组参考，真实n/k和原门槛保持。\n\n正式修复网格导致的峰位验收跳变：Core细化局部透射极值、Checks判定稳定和有界极值完成检查，原Checks/Converged数值阈值不变，采样身份extrema-anchored-band-domain/2。默认Release Engine构建通过，隔离定向12/12；240个处方同新采样重评、24个保留结果重开一致、{physics['cases']}点独立R/T/A通过。未全模块/全仓或原生桌面验收。见[结果、权衡与剩余项](COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)和[证据](../{r.as_posix()}/README.md)。\n"+rest;status.write_text(s,encoding="utf-8")
guide=Path("labs/CoatingDesign/README.md");s=guide.read_text(encoding="utf-8-sig")
if "2026-11-01 正式极值采样：" not in s:
    first,rest=s.split("\n",1);guide.write_text(first+"\n\n2026-11-01 正式极值采样：修复峰位/截止极值受网格影响的验收不稳定，真实n/k、原门槛及默认协议保持。角色反馈在550 nm获得T、各OD和带宽共同改善，仍未达标且未接入正式搜索；较大旧N控制带宽略优。见[本轮对照与98%剩余项](../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。\n"+rest,encoding="utf-8")
(r/"README.md").write_text(f"""# 匹配后反馈与正式极值采样证据

正式修正为通用极值锚点、验收稳定性与结果元数据；结构反馈仍为离线实验。见[报告](../../../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。

- inputs/baseline-source：9个原实际起点、真实n/k及开始源码。*-feedback-final / *-prototype-final / *-control-final为采样/1下9+6+9个实际运行；较大旧N另2项。
- current-*、current-paired.json、paired-final.json：同采样/2按实际膜厚重评；原优化轨迹未改写成新采样运行。
- all-stage-prescriptions.json / current-stages：240个实际处方重评、48个full及新通过short光谱，实际成本留原summary。
- fine-sampling-before/after.json：同门槛400/1600/3200的实际Checks及8192/32768均匀网格核对。
- verification.json / execution-ledger.json：输入、数值门槛/Measure、固定层、源码/参考及旧169项证据完整性。physics-comparison-current.json含{physics['cases']}个独立R/T/A比较点，含各臂重复处方。
- 日程主额度相同不等总成本；粗指导、恢复/质量及OpenAsync本来就会复算。DenseCalls原字段不含short内部，VerifiedDenseCalls补加；新Result极值查询按Evaluate单列，不冒称全搜索精确总查询。
- superseded-*.json及初版源码/日志为停止的诊断器问题，不计最终完成；首次加密obj冲突/可空警告保留，修正后构建通过。logs/dense-extrema-focused.trx为最终12项。

无新增整组达标、全局不可行证明、完整模块/全仓或原生桌面验收，未提交/推送/发布。final-manifest.json封存本轮，运行时忽略但摘要单列。
""",encoding="utf-8")
for folder in ["final-source","final-tests/Fixtures","final-tools","final-docs"]:(r/folder).mkdir(parents=True,exist_ok=True)
for p in engine.glob("*.cs"):shutil.copyfile(p,r/"final-source"/p.name)
tr=Path("labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests")
for p in [tr/"DenseExtremaTests.cs",tr/"OpticalSystemDesign.CoatingDesign.Tests.csproj",tr/"Fixtures/dense-extrema-20261101.json"]:shutil.copyfile(p,r/"final-tests"/("Fixtures/"+p.name if p.suffix==".json" else p.name))
for p in Path("tools/diagnostics/CoatingTopologyFeedback20261101").iterdir():
    if p.is_file():shutil.copyfile(p,r/"final-tools"/p.name)
for p in [Path("docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md"),status]:shutil.copyfile(p,r/"final-docs"/p.name)
shutil.copyfile(guide,r/"final-docs/CoatingDesign-README.md")
for p in [Path("docs/COATING_DESIGN_LAB.md"),Path("docs/FILE_FORMATS_AND_PLUGINS.md")]:shutil.copyfile(p,r/"final-docs"/p.name)
shutil.copyfile("third_party/coating-reference/README.md",r/"final-docs/coating-reference-README.md")
print(json.dumps({"counts":counts,"physics":physics["maximumErrors"]},ensure_ascii=False))
