# Superseded report draft, never used for final evidence.
from pathlib import Path
import hashlib
import json
import math
import shutil
import sys
import tarfile

root = Path(sys.argv[1])
read = lambda p: json.loads(Path(p).read_text(encoding="utf-8-sig"))
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
write = lambda p, d: Path(p).write_text(json.dumps(d, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
paired = read(root / "paired-raw.json")
assert {a: len(v) for a, v in paired.items()} == {"feedback": 9, "control": 9, "prototype": 6}
inputs = {p.stem.removesuffix(".coating"): read(p) for p in (root / "inputs").glob("*.coating.json")}

def dominates(a, b, target):
    if target["Kind"] == "HighReflector":
        return a["Final"]["MinimumReflectance"] >= b["Final"]["MinimumReflectance"]
    x, y = a["Final"], b["Final"]
    return x["PeakTransmittance"] >= y["PeakTransmittance"] and abs(x["FwhmNanometers"]-target["FwhmNm"]) <= abs(y["FwhmNanometers"]-target["FwhmNm"]) and all(x >= y for x, y in zip(a["FinalOd"], b["FinalOd"]))

guards, comparisons, passing_trials = [], [], []
for arm, rows in paired.items():
    for row in rows:
        original = inputs[row["Name"]]
        n = original["Settings"]["MaximumIterations"]
        assert row["ShortAllowance"] == min(n, math.ceil(math.sqrt(n)))
        assert len(row["Runs"]) == 2 * (row["Starts"]+1)
        assert len([x for x in row["Runs"] if x["Stage"] == "full"]) == 2
        assert all(x["Run"]["Iterations"] <= x["Allowance"] for x in row["Runs"])
        assert row["Joint"]
        if not row["StructuralAllowed"]:
            assert row["Proposals"] == 0
        for run in row["Runs"]:
            assert len(run["FinalLayers"]) <= original["Target"]["MaximumLayers"]
            assert all(original["Settings"]["MinimumThicknessNm"] <= x["ThicknessNm"] <= original["Settings"]["MaximumThicknessNm"] for x in run["FinalLayers"])
            if not row["StructuralAllowed"]:
                assert all(x["Variable"] or x == run["FinalLayers"][i] for i, x in enumerate(original["Layers"]))
            if run["Passed"] and not original["Result"]["Passed"]:
                passing_trials.append({"arm": arm, "name": row["Name"], "stage": run["Stage"], "round": run["Round"], "metrics": run["Metrics"], "od": run["Od"]})
        for full in [x for x in row["Runs"] if x["Stage"] == "full"]:
            if arm != "control":
                assert any(s["Stage"] == "short" and s["Round"] == full["Round"] and s["FinalLayers"] == full["InitialLayers"] for s in row["Runs"])
        # DenseCalls in the probe counts outer validations and full calls. Add the separately recorded short-call internals.
        row["VerifiedDenseCalls"] = row["DenseCalls"] + sum((x["Run"].get("GeneralWork") or {}).get("DenseValidations", 0) for x in row["Runs"] if x["Stage"] == "short")
        row["VerifiedMainIterations"] = sum(x["Run"]["Iterations"] for x in row["Runs"])
        if arm != "control":
            old = next(x for x in paired["control"] if x["Name"] == row["Name"])
            assert row["InputFingerprint"] == old["InputFingerprint"]
            a, b = dominates(row, old, original["Target"]), dominates(old, row, original["Target"])
            comparisons.append({"arm": arm, "name": row["Name"], "comparison": "same" if a and b else "feedback-dominates" if a else "old-dominates" if b else "tradeoff", "feedback": row["Final"], "feedbackOd": row["FinalOd"], "old": old["Final"], "oldOd": old["FinalOd"]})

cold_count = 0
for arm in paired:
    summaries = read(root / (arm + "-cold") / "summary.json")
    assert len(summaries) == len(paired[arm])
    assert all(x["IdenticalReportedMetrics"] and x["IdenticalReportedSpectrum"] and x["IdenticalReportedChecks"] for x in summaries)
    for p in (root / (arm + "-cold")).glob("*.coating.json"):
        d = read(p); original = inputs[d["Name"]]
        check = {"target": d["Target"] == original["Target"], "settings": d["Settings"] == original["Settings"], "materials": d["Materials"] == original["Materials"], "gateRequirements": [(x["Name"], x["Requirement"]) for x in d["Result"]["Checks"]] == [(x["Name"], x["Requirement"]) for x in original["Result"]["Checks"]], "verdict": d["Result"]["Passed"] == (d["Result"]["SamplingConverged"] and all(x["Passed"] for x in d["Result"]["Checks"]))}
        assert all(check.values()), (arm, d["Name"], check)
        guards.append({"arm": arm, "name": d["Name"], "guards": check}); cold_count += 1

def od(d):
    return [min(s["Power"]["OpticalDensity"] for s in d["Result"]["Spectrum"] if band["MinimumNm"] <= s["WavelengthNanometers"] <= band["MaximumNm"]) for band in d["Target"].get("StopBands") or []]

stages = read(root / "native-stage-prescriptions.json")
assert len([x for x in stages if x["Stage"] == "full"]) == 48
for s in stages:
    folder = "prototype-stages" if s["Arm"] == "prototype" else "native-stages"
    d = read(root / folder / (s["Name"] + ".coating.json"))
    assert d["Result"]["Metrics"] == s["ReportedMetrics"] and od(d) == s["ReportedOd"] and d["Result"]["Passed"] == s["ReportedPassed"], s["Name"]
    assert d["Materials"] == inputs[s["Source"]]["Materials"] and d["Target"] == inputs[s["Source"]]["Target"]

larger = read(root / "larger-old-primary/summary.json")
assert len(larger) == 2
for p in (root / "larger-old-primary").glob("*.coating.json"):
    d = read(p); original = inputs[d["Name"]]; settings = dict(d["Settings"]); settings["MaximumIterations"] = original["Settings"]["MaximumIterations"]
    assert settings == original["Settings"] and d["Target"] == original["Target"] and d["Materials"] == original["Materials"]

engine = Path("labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine")
source = [{"file": p.name, "unchanged": p.read_bytes() == (engine / p.name).read_bytes(), "sha256": sha(p)} for p in (root / "baseline-source").glob("*.cs")]
assert all(x["unchanged"] for x in source)
assert {p.name for p in (root / "baseline-source").glob("*.cs")} == {p.name for p in engine.glob("*.cs")}
physics = read(root / "physics-comparison.json")
assert all(math.isfinite(v) and v <= 2e-11 for v in physics["maximumErrors"].values())
ref = read(root / "reference-native/summary.json")
assert len(ref) == 8

prefix = "TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428"
ref_sources = []
with tarfile.open("third_party/coating-reference/TFStudio-8b942b3.tar.gz") as archive:
    for name in ["src/utils/synthesis/structuralOptimizer.js", "src/utils/physics/optimizer/layerOps.js", "src/utils/physics/optimizer/lsqEngine.js", "src/utils/physics/optimizer/operandModel.js"]:
        p = Path(".tmp/coating-infeasible-repair") / prefix / name
        ref_sources.append({"path": str(p), "unchanged": sha(p) == hashlib.sha256(archive.extractfile(prefix + "/" + name).read()).hexdigest()})
with tarfile.open("third_party/coating-reference/tmmcore-0.4.2.tgz") as archive:
    assert sha(".tmp/coating-edge-audit-20261030/package/src/tmm.js") == hashlib.sha256(archive.extractfile("package/src/tmm.js").read()).hexdigest()
assert all(x["unchanged"] for x in ref_sources)
binaries = [{"runtime": runtime, "file": f, "sha256": sha(root / runtime / f)} for runtime in ["baseline-runtime", "audit-runtime", "control-runtime", "feedback-runtime", "prototype-runtime"] for f in ["OpticalSystemDesign.Core.dll", "OpticalSystemDesign.CoatingDesign.Engine.dll", "OpticalSystemDesign.Application.dll"]]
for f in ["OpticalSystemDesign.Core.dll", "OpticalSystemDesign.CoatingDesign.Engine.dll", "OpticalSystemDesign.Application.dll"]:
    assert len({x["sha256"] for x in binaries if x["file"] == f}) == 1
previous = Path("validation/rechecks/2026-11-01/coating-phase-order")
previous_manifest = read(previous / "final-manifest.json")
assert all(sha(previous / x["path"]) == x["sha256"] for x in previous_manifest["entries"])
counts = {"distinctInputs": 9, "topologyCases": 9, "prototypeCases": 6, "scheduledOldControls": 9, "largerOldControls": 2, "referenceStructuralCases": 8, "topologyProposals": sum(x["Proposals"] for x in paired["feedback"]), "prototypeProposals": sum(x["Proposals"] for x in paired["prototype"]), "shortCalls": sum(sum(z["Stage"] == "short" for z in x["Runs"]) for rows in paired.values() for x in rows), "fullCalls": 48, "coldRetained": cold_count, "coldFullAndNewPassStages": len(stages), "newPassingTrials": passing_trials, "physicsPoints": physics["cases"]}
verification = {"scope": "Offline matched topology/prototype feedback, not production optimizer changes", "counts": counts, "comparisons": comparisons, "guards": guards, "source": source, "binaries": binaries, "referenceSources": ref_sources, "referencePasses": sum(x["Passed"] for x in ref), "physics": {"cases": physics["cases"], "maximumErrors": physics["maximumErrors"]}, "previousEvidence": {"files": previous_manifest["files"], "mismatches": 0}, "buildScope": "Four standalone probe builds; no default product build or new product unit-test execution", "budgetScope": "Same requested short/full continuous schedule; actual iterations and extra restoration/quality work are separate; new feedback also spends coarse optical queries, so total cost is not equal"}
write(root / "verification.json", verification)
write(root / "paired-final.json", paired)

f = lambda x: f"{x:.6f}"
od_text = lambda values: " / ".join(f(x) for x in values)
nb_rows, hr_rows = [], []
for x in paired["feedback"]:
    c = next(y for y in paired["control"] if y["Name"] == x["Name"])
    p = next((y for y in paired["prototype"] if y["Name"] == x["Name"]), None)
    if inputs[x["Name"]]["Target"]["Kind"] == "HighReflector":
        hr_rows.append(f"| {x['Name']} | {f(x['Initial']['MinimumReflectance']*100)} | {f(x['Final']['MinimumReflectance']*100)} | {f(c['Final']['MinimumReflectance']*100)} | 未过98% |")
    else:
        for label, a in [("起点", {"Final": x["Initial"], "FinalOd": x["InitialOd"]}), ("拓扑反馈", x), ("原型角色反馈", p), ("追加旧日程", c)]:
            if a: nb_rows.append(f"| {x['Name']} · {label} | {f(a['Final']['PeakTransmittance']*100)} | {f(a['Final']['FwhmNanometers'])} | {od_text(a['FinalOd'])} |")
large_rows = [f"| {x['Name']} | {x['Settings']['MaximumIterations']} | {f(x['AfterMetrics']['PeakTransmittance']*100)} | {f(x['AfterMetrics']['FwhmNanometers'])} | {od_text(x['AfterBlockingOd'])} | {'通过' if x['AfterPassed'] else '未过'} |" for x in larger]
report = f"""# 匹配后结构反馈与拓扑搜索对照（2026-11-01）

本轮实现并完成两类**离线诊断器**：从实际匹配/精化处方增删、分裂和交换膜层；以及按声明的镜段/腔角色转移拟合膜厚后改变原型整数邻域。正式Engine未改，真实n/k和原验收保持。9组拓扑、6组原型反馈分别对照9组追加旧日程、2组更大旧N及8组固定参考结构搜索。原未过规格的通过试探数为{len(passing_trials)}；不把已过730/650 nm的再次通过算新增。

## 通用流程与预算

拓扑臂从当前处方及较好的已匹配候选起步，在每个允许的位置删层、插入薄层或分裂宿主；仅将相邻同材料的自由层按真实厚度相加规范化，越界直接排除。层数到顶时先筛选删层结果，再在腾出的名额内插层交换。只有Search.VaryStructure允许且全部层可变才编辑拓扑；固定层近红外控制中提议数为0。

原型臂保留入射匹配前缀，并从原型生成器明确的镜段/腔角色映射拟合膜厚。改变镜段层数/腔阶时，以同角色、同材料优先、归一化段内位置匹配转移相对于原型物理厚度的比例；新原型的物理厚度仍由Core和真实n/k计算。无法核对原型与实际材料顺序、匹配前缀或厚度界时排除，不猜某层角色。不重置为未拟合的四分之一波膜系，声明腔数保持。

两条反馈均做两轮；每轮最多min(4,Starts)个短程调用，主额度各≤ceil(sqrt(N))，再对其中一个挑战者做原N完整Optimize。完整结果反馈进下一轮，最终保留点另行管理：峰值T、每个截止OD不降，距目标带宽的绝对误差不增，原逐项超限不增，并保留已有采样收敛/通过；HR保护实际最小R。探索点可暂时较差，不能覆盖正式最好结果。所有材料/层号仅用于遍历和角色映射，无材料名、固定膜层序号或例子签名分支。

旧日程臂使用同样8个短程+2个完整调用请求，连续传递旧探索点，独立保留共同改善的最好结果，避免用最终保护规则限制旧探索。旧单次额度另按2N+2min(4,Starts)ceil(sqrt(N))设置：500 nm为152，550 nm为272；仅该控制的N改变，材料、目标、采样和厚度界保持。新反馈还支出粗指导查询，内部恢复/质量窗口与实际调用次数分别记录，**不称相同总迭代、总查询或总算力胜出**。

初版诊断器曾让旧粗分遮住挑战者完整求解，并曾把旧控制探索点回滚到最终保留点；两项在正式对照前修正。被停止的版本单列留档，不计入最终集合。它们是诊断器问题，不是已合入正式产品的修复。

## 原始量逐项对照

| 规格/臂 | 峰值T（%） | FWHM（nm） | 各截止OD |
|---|---:|---:|---|
{chr(10).join(nb_rows)}

| 真实高反 | 起点Rmin（%） | 拓扑反馈Rmin（%） | 追加旧日程Rmin（%） | 原门槛 |
|---|---:|---:|---:|---|
{chr(10).join(hr_rows)}

650 nm拓扑反馈相对起点有共同改善，但旧日程的T、OD、带宽均更好。近红外高反的两臂差异约几个1e-9的R，低于既有R采样收敛分辨率，不能称稳定算法增益。550/500 nm双腔和98%高反仍未达标。固定层近红外验证只证明变量/拓扑限制保持，不当作已过规格。实际材料范围为原SiO2/Ta2O5实测表跨波段，以及1550 nm合成材料控制，不称多套独立实测材料认证。

任意增删层臂从双腔起点出发，但仅保留Target.Cavities=2不能另行证明最终自由拓扑仍有两个物理腔；不据此冒称真正双腔突破。原型角色臂保留生成器的声明腔关系，仍按原正式Checks验收，未新增场分布/模态数认证门槛。

## 更大旧N控制

| 原规格 | 旧N提高到 | 峰值T（%） | FWHM（nm） | 各截止OD | 原整组门槛 |
|---|---:|---:|---:|---|---|
{chr(10).join(large_rows)}

这两项是明确提高旧额度的控制，不是相同Settings指纹的反馈臂，也不是新算法成果。若单项改善伴随某截止OD余量下降，仍不计入共同改善；原通过门槛本身未改变。

## 参考、核对与保留范围

实际运行固定TFStudio 8b942b3未修改的proposeMutation、tidyLayers、Metropolis接受和DLS：原真实n/k、层数/厚度界、固定标记、随机种子和同类两轮连续额度；最终由本地Core原验收重评，{sum(x['Passed'] for x in ref)}/8通过。参考点平方目标与本地实测FWHM/最差OD/最差R不同，且不是等光谱查询成本，不宣称算法整体优越。源归档及所用结构/优化源码完整性保持。

本轮计{counts['topologyProposals']}项通用拓扑提议、{counts['prototypeProposals']}项角色原型提议；最终三臂共{counts['shortCalls']}个短程、48个完整调用，旧单次更大N另2项。24个保留结果重开后光谱、指标、Checks逐值一致；48个完整处方及所有新通过短程候选另用冻结Core从实际膜厚独立重评，指标、各OD和通过逐值一致。{physics['cases']}个独立R/T/A点对固定tmmcore核对，最大差R={physics['maximumErrors']['R']}、T={physics['maximumErrors']['T']}、A={physics['maximumErrors']['A']}，通过既有2e-11标准。

四次独立诊断构建零警告/错误；没有本轮新默认产品构建、产品单元测试或全仓通过数，未操作原生桌面，未提交/推送/发布。正式Engine所有C#与开始快照逐字节一致；上一阶段169项证据保持。详见[完整账目、处方和失败记录](../{root.as_posix()}/README.md)。有限两轮搜索不证明全局无解；本轮反馈未取得足以替换旧流程的通用收益，继续保留[当前正式实现](COATING_GENERATION_CERTIFICATION_2026-10-31.md)及原门槛。
"""
Path("docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md").write_text(report, encoding="utf-8")
status = Path("docs/CURRENT_STATUS.md"); text = status.read_text(encoding="utf-8-sig")
title = "## 2026-11-01 镀膜匹配后结构/拓扑反馈与追加旧额度对照"
if title not in text:
    first, rest = text.split("\n", 1)
    text = first + f"\n\n{title}\n\n完成9组通用拓扑、6组角色原型反馈，对照9组同连续日程旧控制、2组更大旧N（152/272）及8组固定参考结构搜索。原未过规格的新通过试探数{len(passing_trials)}；550/500 nm真实双腔及98%高反仍未过。650 nm反馈相对起点改善，但追加旧日程在T、各OD、带宽上更好；近红外R的微小差异不称稳定收益。真实n/k、原门槛和正式Engine保持，两条反馈仅为离线验证。\n\n24个保留结果及48个完整处方复算通过各自一致性检查，{physics['cases']}点独立R/T/A通过既有标准；四次独立诊断构建，无本轮新产品测试/默认产品构建结论。见[流程、原始量、成本与剩余项](COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)和[证据](../{root.as_posix()}/README.md)。\n" + rest
    status.write_text(text, encoding="utf-8")
guide = Path("labs/CoatingDesign/README.md"); text = guide.read_text(encoding="utf-8-sig")
if "2026-11-01 结构反馈对照：" not in text:
    first, rest = text.split("\n", 1)
    guide.write_text(first + "\n\n2026-11-01 结构反馈对照：匹配后处方拓扑增删与按角色转移的原型邻域均完成独立验证，未取得替换旧流程的通用收益；550/500 nm双腔及98%真实高反仍未达标。追加旧额度控制、真实n/k、原门槛和正式实现保持，见[完整结果](../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。\n" + rest, encoding="utf-8")
(root / "README.md").write_text(f"""# 匹配后结构/拓扑反馈证据（2026-11-01）

本轮为独立诊断，正式Engine未修改。见[报告](../../../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。

- inputs / baseline-source：9个实际热起点和开始源码；反馈/旧日程指纹相同，真实n/k与原门槛保持。
- *-feedback-final / *-prototype-final / *-control-final：最终24个组合；原型反馈6例（HR不适用），固定层时0个结构提议。
- paired-final.json / verification.json：逐项目标、对照、完整工作量、子运行预算及源码/参考/固定层完整性。DenseCalls原字段含外部及full调用，VerifiedDenseCalls另外加short内部密集调用；不相加异质计数冒充等成本。
- native-stage-prescriptions.json及native-stages / prototype-stages：48个完整优化处方和所有新通过短程候选的独立物理重评，原算法运行身份在主summary中保存，重评不冒充重新优化。
- larger-old-primary-inputs / larger-old-primary：仅旧N提高到152/272，原门槛/材料/采样/厚度界保持；与同指纹旧日程分开。
- reference-structural.json / reference-native：固定参考突变/退火/DLS及本地原门槛重评，点目标定义不同。
- superseded-*.json、initial-probe-source.cs、pre-challenger-fix-source.cs保留初版诊断器问题与停止任务，不当作最终完成。
- physics-comparison.json：{physics['cases']}个实际n/k独立R/T/A点。final-manifest.json封存本轮；运行时忽略但二进制摘要单列。

没有全局不可行证明、原生桌面或本轮新产品测试结论；未提交/推送/发布。原型声明腔关系和任意自由拓扑的物理腔数不可混同。
""", encoding="utf-8")
for folder in ["final-tools", "final-docs"]: (root / folder).mkdir(exist_ok=True)
for p in Path("tools/diagnostics/CoatingTopologyFeedback20261101").iterdir():
    if p.is_file(): shutil.copyfile(p, root / "final-tools" / p.name)
for p in [Path("docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md"), status]: shutil.copyfile(p, root / "final-docs" / p.name)
shutil.copyfile(guide, root / "final-docs/CoatingDesign-README.md")
print(json.dumps({"counts": counts, "comparisons": [{"arm": x["arm"], "name": x["name"], "comparison": x["comparison"]} for x in comparisons], "physics": verification["physics"]}, ensure_ascii=False))
