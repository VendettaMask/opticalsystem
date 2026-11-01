"""Seal a negative search result without promoting diagnostic prescriptions to product output."""
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
write = lambda p, value: Path(p).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
scans = [x for folder in ["probe-nb-a", "probe-nb-b", "probe-hr"] for x in read(root / folder / "summary.json")]
full = [x for folder in ["full-nb-a", "full-nb-b"] for x in read(root / folder / "summary.json")]
controls = [x for folder in ["control-nb-a", "control-hr"] for x in read(root / folder / "summary.json")]
references = read(root / "reference-native/summary.json")
physics = read(root / "physics-comparison.json")
assert len(scans) == 8 and len(full) == 10 and len(controls) == 5 and len(references) == 24
for row in scans:
    limit = read(root / "inputs" / (row["Name"] + ".coating.json"))["Settings"]["MaximumIterations"]
    assert len(row["Refinements"]) <= 12
    assert all(x["Run"]["Iterations"] <= min(limit, math.ceil(math.sqrt(limit))) for x in row["Refinements"])
for row in full:
    limit = read(root / "inputs" / (row["Name"] + ".coating.json"))["Settings"]["MaximumIterations"]
    assert row["Run"]["Iterations"] <= limit

engine = Path("labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine")
base = root / "baseline-source"
source = [{"file": p.name, "unchanged": p.read_bytes() == (engine / p.name).read_bytes(), "sha256": sha(p)} for p in base.glob("*.cs")]
assert set(p.name for p in base.glob("*.cs")) == set(p.name for p in engine.glob("*.cs"))
assert all(x["unchanged"] for x in source)

gate_guards = []
for output, inputs in [(root / "probe-cold", root / "inputs"), (root / "full-cold", root / "full-inputs")]:
    cold = read(output / "summary.json")
    assert len(cold) == (8 if output.name == "probe-cold" else 10)
    for p in output.glob("*.coating.json"):
        d, original = read(p), read(inputs / p.name)
        r = d["Result"]
        guard = {
            "sameTarget": d["Target"] == original["Target"],
            "sameSettings": d["Settings"] == original["Settings"],
            "sameMaterials": d["Materials"] == original["Materials"],
            "sameMaterialOrderAndFlags": [(x["MaterialId"], x["Variable"]) for x in d["Layers"]] == [(x["MaterialId"], x["Variable"]) for x in original["Layers"]],
            "bounds": all(d["Settings"]["MinimumThicknessNm"] <= l["ThicknessNm"] <= d["Settings"]["MaximumThicknessNm"] for l in d["Layers"]),
            "fixed": all(l["Variable"] or l == d["Layers"][i] for i, l in enumerate(original["Layers"])),
            "layerCount": len(d["Layers"]) <= d["Target"]["MaximumLayers"],
            "originalGateRequirements": [(x["Name"], x["Requirement"]) for x in r["Checks"]] == [(x["Name"], x["Requirement"]) for x in original["Result"]["Checks"]],
            "verdict": r["Passed"] == (r["SamplingConverged"] and all(x["Passed"] for x in r["Checks"])),
        }
        assert all(guard.values()), (p, guard)
        gate_guards.append({"arm": output.name, "file": p.name, "guards": guard})
    assert all(x["IdenticalReportedMetrics"] and x["IdenticalReportedSpectrum"] and x["IdenticalReportedChecks"] for x in cold)

assert all(x["Joint"] for x in scans)
nb = [x for x in scans if x["Initial"]["FwhmNanometers"] is not None]
assert len(nb) == 5 and all(x["Initial"] == x["Final"] and x["InitialOd"] == x["FinalOd"] for x in nb)
assert not any(x["Joint"] for x in full)
new_passes = [x["File"] for x in full if x["Passed"] and not read(root / "inputs" / (x["Name"] + ".coating.json"))["Result"]["Passed"]]
assert not new_passes
assert all(v <= 2e-11 for v in physics["maximumErrors"].values())

archives = {
    "tmmcore-0.4.2.tgz": "fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44",
    "TFStudio-8b942b3.tar.gz": "055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6",
}
assert all(sha(Path("third_party/coating-reference") / f) == h for f, h in archives.items())
reference_sources = []
prefix = "TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428"
with tarfile.open("third_party/coating-reference/TFStudio-8b942b3.tar.gz") as archive:
    for file in ["src/utils/physics/optimizer/lsqEngine.js", "src/utils/physics/optimizer/operandModel.js", "src/utils/optimizers/newton.js", "src/utils/optimizers/sqp.js"]:
        entry = archive.extractfile(prefix + "/" + file).read()
        path = Path(".tmp/coating-infeasible-repair") / prefix / file
        reference_sources.append({"path": str(path), "unchanged": hashlib.sha256(entry).hexdigest() == sha(path)})
with tarfile.open("third_party/coating-reference/tmmcore-0.4.2.tgz") as archive:
    reference_sources.append({"path": ".tmp/coating-edge-audit-20261030/package/src/tmm.js", "unchanged": hashlib.sha256(archive.extractfile("package/src/tmm.js").read()).hexdigest() == sha(".tmp/coating-edge-audit-20261030/package/src/tmm.js")})
assert all(x["unchanged"] for x in reference_sources)

previous = Path("validation/rechecks/2026-10-31/coating-basin-restoration")
old = read(previous / "final-manifest.json")
assert all(sha(previous / x["path"]) == x["sha256"] for x in old["entries"])
binaries = [{"file": f, "baseline": sha(root / "baseline-runtime" / f), "audit": sha(root / "audit-runtime" / f), "polish": sha(root / "polish-runtime" / f)} for f in ["OpticalSystemDesign.Core.dll", "OpticalSystemDesign.Application.dll", "OpticalSystemDesign.CoatingDesign.Engine.dll"]]
assert all(x["baseline"] == x["audit"] == x["polish"] for x in binaries)
result = {
    "scope": "Offline diagnosis only; no production optimizer or acceptance changes",
    "cases": 8, "neighborCandidates": sum(x["Evaluated"] for x in scans), "shortRefinementCalls": sum(len(x["Refinements"]) for x in scans),
    "fullPhaseStarts": len(full), "fullJointImprovements": 0, "newlyPassingSpecifications": new_passes,
    "nativeContinuationControls": {"calls": 5, "scope": "3 real HR and 550/730 nm real double cavity; each has the original N, not equal total cost to the phase portfolio"},
    "reference": {"calls": 24, "passes": sum(x["Passed"] for x in references), "scope": "Unmodified TFStudio point squared operands, different objective; passes only the already-passing 730 nm input"},
    "physics": {"cases": physics["cases"], "maximumErrors": physics["maximumErrors"]},
    "gateGuards": gate_guards, "source": source, "binaries": binaries, "referenceSources": reference_sources,
    "diagnosticBinaries": [{"path": f"{folder}/PhaseOrderAudit.dll", "sha256": sha(root / folder / "PhaseOrderAudit.dll")} for folder in ["audit-runtime", "polish-runtime"]],
    "previousEvidence": {"files": old["files"], "mismatches": 0},
    "buildScope": "Standalone probe project built twice against pinned Core/Engine: logs/audit-build.log and logs/polish-build.log; no new default product build or unit-test run",
    "notImplemented": ["adaptive prototype/topology feedback after matching", "phase-order neighborhood in production", "global infeasibility certificate"],
    "scans": scans, "fullRuns": full, "controls": controls,
}
write(root / "verification.json", {k: v for k, v in result.items() if k not in ["scans", "fullRuns", "controls"]})
write(root / "diagnostic-results.json", result)

fmt = lambda value, n=6: f"{value:.{n}f}"
hr = [x for x in scans if x["Initial"]["FwhmNanometers"] is None]
hr_rows = []
for x in hr:
    c = next(c for c in controls if c["Name"] == x["Name"])
    hr_rows.append(f"| {x['Name']} | {fmt(x['Initial']['MinimumReflectance']*100)} | {fmt(x['Final']['MinimumReflectance']*100)} | {fmt(c['AfterMetrics']['MinimumReflectance']*100)} | 未过98% |")
phase_rows = [f"| {x['File'].replace('.coating.json','')} | {fmt(x['Final']['PeakTransmittance']*100)} | {fmt(x['Final']['FwhmNanometers'])} | {' / '.join(fmt(v) for v in x['FinalOd'])} | {'通过' if x['Passed'] else '未过'} | {'是' if x['Joint'] else '否'} |" for x in full]
initial_rows = [f"| {x['Name']} | {fmt(x['Initial']['PeakTransmittance']*100)} | {fmt(x['Initial']['FwhmNanometers'])} | {' / '.join(fmt(v) for v in x['InitialOd'])} |" for x in nb]
report = f"""# 真实高反与双腔的相位阶数排查（2026-11-01）

本轮**没有新增整组达标，也没有修改正式生成、搜索、优化器或验收**。8个相同热起点，491个半波阶数邻域候选、96次短程精化及10次原完整额度优化，未得到峰值T、每个截止OD及距目标带宽误差共同改善的窄带结果。高反仅有很小增量，不能稳定胜过额外调用旧优化器。所有试探保留为离线证据；当前产品保持[上一阶段实现](COATING_GENERATION_CERTIFICATION_2026-10-31.md)。

## 调查范围与执行

输入为上一阶段最终保留的真实550/500/730 nm双腔、650 nm真实单腔、1550 nm合成双腔，以及550/710 nm和近红外三个真实高反。真实薄膜数据仍为原SiO2/Ta2O5表；合成材料另作控制，不冒称新增实测材料模板或多套实测材料认证。

最初怀疑匹配后的较好结构未反馈给离散邻域。本轮先独立测试保持层次与材料顺序的半波相位阶数邻域：每个自由层按Core实际复折射率和角度计算名义半波厚度，测试降阶/升阶及所有符合条件的层对，始终保持原厚度界和变量标记。真实n/k、色散和吸收全部由冻结Core逐个正式Evaluate。名义半波相位不能作为吸收/色散膜系的物理等价化简；不会将厚度直接取模后套用旧光谱或验收。

491项均为完整正式候选复验，不是只看中心或无吸收预览。每组按原物理超限排序并分散改变的自由层位置，选12项调用旧精化器，主额度每项≤ceil(sqrt(N))；这里使用具体位置来记录已测试方向，任何自由层都有机会，不按某个膜层序号或材料名特判。再按通用指标选择两种相位起点：目标超限最小、以及截止OD已通过时峰值T最高，各用原MaximumIterations完整Optimize。扫描、子运行、完整优化、内部恢复/质量工作分别记账，**总成本增加，不是等总预算竞赛**。

窄带保留标准比原通过门槛更严格：相对原起点，T不降、每个截止OD不降、带宽绝对误差不增、逐项目标超限不增，并保留已有采样收敛/通过。5组窄带的最终保留结果均为原输入。原门槛本身保持；临时候选的通过/失败仍按原Checks和采样收敛判断。

## 完整原额度的10个相位起点

各窄带原保留起点如下，便于与后表逐项比较：

| 身份 | 原峰值T（%） | 原FWHM（nm） | 原各截止OD |
|---|---:|---:|---|
{chr(10).join(initial_rows)}

| 身份 | 峰值T（%） | FWHM（nm） | 各截止OD | 原整组验收 | 相对原起点共同改善 |
|---|---:|---:|---|---|---|
{chr(10).join(phase_rows)}

730/650 nm的第一种相位起点仍能通过原整组门槛，但带宽误差扩大，不计为共同改善；第二种提高T更多，却丢失带宽验收。绿色双腔的T有明显上升，同时带宽偏离更大；550 nm双腔仍未通过。不能将临时指标上升覆盖到产品原结果，也不能把原本已过的规格计作新增达标。

## 高反与旧续算控制

| 真实高反 | 起点Rmin（%） | 相位邻域保留Rmin（%） | 仅额外旧Optimize的Rmin（%） | 原门槛 |
|---|---:|---:|---:|---|
{chr(10).join(hr_rows)}

旧控制每例仅再执行一次原N完整优化；相位组合使用邻域正式复验和12个短程调用，成本不同。相位组合在两组上低于旧续算，近红外的差异也很小，不能推出稳定的通用算法优势。微小增量不称为98%突破；有限局部扫描也不证明全局无解。其余上一阶段未过的98%高反没有本轮新增通过证据。

## 参考实现与物理核对

同8个实际热起点执行固定TFStudio 8b942b3的DLS/Newton/SQP共24组，用同一真实n/k、原结构/厚度界和原N；最终由本地Core重新正式验收。仅Newton/SQP的既有730 nm规格通过（2/24），其余未过，没有新规格达标。它们使用点平方目标，和本地实际FWHM/最差OD/最差反射率目标不同，不据此宣称算法整体胜出。

参考最小化点平方误差时，平均目标下降仍可伴随最差R或带形退步；例如710 nm高反Newton的点merit下降，但原最小R从97.778922%降到97.551667%。这不是物理求解器差异的证据。共{physics['cases']}个实际n/k、S/P/非偏振R/T/A点独立对固定tmmcore 0.4.2核对，最大绝对差为R={physics['maximumErrors']['R']}、T={physics['maximumErrors']['T']}、A={physics['maximumErrors']['A']}，通过既有2e-11物理核对标准。参考归档和所用源文件摘要保持。

## 验证边界与剩余项

8个保留结果和10个完整相位处方保存重开后，正式光谱、指标、Checks逐值一致；原Target、Settings、材料表、层数、厚度界、变量标记及原验收要求逐项核对。正式Engine所有C#文件与本轮开始快照逐字节保持，运行时Core/Engine固定并记录SHA-256。上一阶段378项证据清单读回保持。

独立诊断项目两次构建均零警告/错误；本轮没有新默认产品构建、产品单元测试或完整模块/全仓测试，不复用上一阶段6项通过数冒充新测试。未操作原生桌面，未提交、推送或发布。构建日志、491项候选、96次子运行、10个完整处方、5个旧续算控制、24组参考和完整性见[证据](../{root.as_posix()}/README.md)。

**仍未实现/解决**：匹配后的自适应原型或拓扑反馈尚未验证，未加入产品；半波阶数邻域也未加入产品。98%真实高反与550/500 nm真实双腔仍为开放项。后续若试验结构反馈，应在原n/k和原验收下，从相同起点对照旧流程，并分别核对T、每个OD和带宽，不能以换一项余量代替共同改善。
"""
Path("docs/COATING_PHASE_ORDER_AUDIT_2026-11-01.md").write_text(report, encoding="utf-8")

status = Path("docs/CURRENT_STATUS.md")
s = status.read_text(encoding="utf-8-sig")
title = "## 2026-11-01 镀膜相位阶数排查：未新增达标，正式算法保持"
if title not in s:
    first, rest = s.split("\n", 1)
    s = first + f"\n\n{title}\n\n8组相同热起点、491个正式相位邻域候选、96个短程调用、10个原N完整优化及5个旧续算控制，没有窄带T/各截止OD/带宽共同改善或新增规格通过；高反微小增量未稳定胜过额外旧优化。24组参考仅保持既有730 nm通过。真实n/k、原门槛和正式Engine保持；相位邻域与自适应结构反馈均未加入产品。\n\n18个实际处方冷复算逐值一致，{physics['cases']}点独立R/T/A通过既有标准，旧378项证据保持。只有独立诊断构建，无本轮新产品测试数或默认产品构建结论。见[结果、成本和剩余项](COATING_PHASE_ORDER_AUDIT_2026-11-01.md)与[证据](../{root.as_posix()}/README.md)。\n" + rest
    status.write_text(s, encoding="utf-8")
guide = Path("labs/CoatingDesign/README.md")
s = guide.read_text(encoding="utf-8-sig")
if "2026-11-01 排查：" not in s:
    first, rest = s.split("\n", 1)
    guide.write_text(first + "\n\n2026-11-01 排查：相位阶数邻域及原完整额度复验未新增规格通过，也未获得T、每个截止OD和带宽误差共同改善；98%真实高反仍未过。正式生成/搜索/优化与真实n/k、原门槛保持。见[诊断、旧续算控制与参考对照](../../docs/COATING_PHASE_ORDER_AUDIT_2026-11-01.md)。\n" + rest, encoding="utf-8")

(root / "README.md").write_text(f"""# 镀膜相位阶数排查（2026-11-01）

本轮是离线诊断，产品Engine未修改，没有新增整组达标。详见[报告](../../../../docs/COATING_PHASE_ORDER_AUDIT_2026-11-01.md)。

- inputs / baseline-source：8个真实热起点、原材料/门槛和开始时Engine源码。
- probe-nb-a / probe-nb-b / probe-hr：491个正式邻域候选、96次短程精化及最终保留；probe-all / probe-cold为保存重开复算。
- full-nb-a / full-nb-b及full-cold：10个相位起点的原N完整Optimize，均未获得窄带共同改善；临时失败处方完整保留，不作为正式结果。
- control-nb-a / control-hr：5个旧流程续算控制；计入额外计算，不冒称等总成本。
- reference-inputs.json / reference-results.json / reference-native：24组参考及本地原门槛复验，点平方目标不同。
- physics-comparison.json：{physics['cases']}个独立R/T/A物理核对点；verification.json逐项记录源身份、边界、旧证据与参考完整性，diagnostic-results.json汇总完整账目。
- audit-runtime是首次扫描二进制，polish-runtime额外增加完整Optimize入口及精化处方输出字段，扫描数学保持。运行时目录忽略，摘要另存；未推广到产品或修改材料。logs保存两次独立构建原始记录。

没有本轮新产品测试通过数、默认产品构建、全仓或桌面验收；未提交、推送或发布。final-manifest.json只封存本轮证据；上一阶段证据只读保持。有限扫描不证明全局无解，自适应结构反馈仍未实现。
""", encoding="utf-8")
for folder in ["final-tools", "final-docs"]:
    (root / folder).mkdir(exist_ok=True)
for file in Path("tools/diagnostics/CoatingPhaseOrder20261101").glob("*"):
    if file.is_file(): shutil.copyfile(file, root / "final-tools" / file.name)
for name in ["reference-continuation.mjs", "compare-generation-physics.mjs"]:
    shutil.copyfile(Path("tools/diagnostics/CoatingAudit20261030") / name, root / "final-tools" / name)
for file in ["docs/COATING_PHASE_ORDER_AUDIT_2026-11-01.md", "docs/CURRENT_STATUS.md"]:
    shutil.copyfile(file, root / "final-docs" / Path(file).name)
shutil.copyfile(guide, root / "final-docs/CoatingDesign-README.md")
print(json.dumps({k: result[k] for k in ["cases", "neighborCandidates", "shortRefinementCalls", "fullPhaseStarts", "fullJointImprovements", "newlyPassingSpecifications", "reference", "physics"]}, ensure_ascii=False))
