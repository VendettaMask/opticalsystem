"""Seal completed evidence and guides. No optical calculations or changes to search results."""
from pathlib import Path
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
import zipfile

repo = Path(sys.argv[1]).resolve()
base = repo / "validation/rechecks/2026-10-31/f8-completion"
study = base / "final-cold-v6"
def read(path):
    return json.loads(path.read_text(encoding="utf-8"))
def sha(data):
    return hashlib.sha256(data).hexdigest()

audits = {name: read(base / name / "audit.json") for name in ("audit-v6-final-frozen", "audit-v6-current-product")}
for name, audit in audits.items():
    assert audit["completeStudy"] and len(audit["runs"]) == 10 and not audit["errors"], name
    assert all(r["withinBudget"] for r in audit["runs"]), name
    assert sum(r["qualified"] for r in audit["runs"] if r["arm"] == "candidate") == 5, name
rows = []
for seed in range(42, 47):
    row = {"seed": seed}
    for arm in ("candidate", "control"):
        row[arm] = read(study / arm / f"seed{seed}" / "summary.json")
    assert row["candidate"]["qualified"] and not row["candidate"]["violations"]
    assert row["candidate"]["lostBoundary"] == 0 and row["candidate"]["minimumValid"] == 1
    rows.append(row)
test_names = ["final-v6-model-regression.trx", "final-v6-desktop-regression.trx", "final-v6-versioned-entry.trx", "actual-product-pipeline-v6.trx", "final-v6-new-ui-default.trx"]
cases, test_counts = {}, {}
for name in test_names:
    root = ET.parse(base / "tests" / name).getroot()
    ns = {"t": root.tag.partition("}")[0][1:]}
    results = root.findall(".//t:UnitTestResult", ns)
    assert results and all(r.attrib["outcome"] == "Passed" for r in results), name
    test_counts[name] = len(results)
    for r in results:
        cases[r.attrib["testId"]] = {"name": r.attrib["testName"], "source": name, "outcome": r.attrib["outcome"]}
pipeline = read(base / "actual-product-pipeline-v6/summary.json")
assert pipeline["checkedAgain"]["meetsTargets"] and not pipeline["targetsChanged"] and not pipeline["physicalCapsChanged"]
frozen_sources = {f["path"]: f["sha256"] for f in read(study / "source-sha256.json")}
for name in ("ColdStartDesignSearch.cs", "TopologyGateCompensation.cs", "TopologyGateCompensation.Constrained.cs", "TopologyGateCompensation.Inward.cs", "FixedTopologyOptimizer.cs"):
    name = "labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/" + name
    assert sha((repo / name).read_bytes()) == frozen_sources[name], "Final numerical algorithm changed: " + name

table = ["| 种子 | 原对照合格 | 新策略合格 | 函数 / 梯度 | 实际光线 | 秒 | 最差 RMS / 最大半径（µm） |", "| --- | --- | --- | --- | --- | --- | --- |"]
def seconds(value):
    h, m, s = value.split(":")
    return int(h)*3600 + int(m)*60 + float(s)
for row in rows:
    c, w = row["candidate"], row["candidate"]["work"]
    table.append(f"| {row['seed']} | {'通过' if row['control']['qualified'] else '失败'} | 通过 | {w['functionEvaluations']} / {w['gradientEvaluations']} | {w['tracedRays']:,} | {seconds(w['elapsed']):.2f} | {c['worstRms']*1000:.6f} / {c['worstMaximum']*1000:.6f} |")
counts = len(cases)
summary = {"version": "f8-completion-final-v6", "candidateQualified": 5, "candidateRuns": 5,
    "historicalControlQualified": sum(r["control"]["qualified"] for r in rows), "historicalControlRuns": 5,
    "sameRuntimeControl": False, "physicalGatesChanged": False, "budget": read(study / "protocol.json")["budget"],
    "rows": rows, "audits": {k: {"replayCount": v["replayCount"], "errors": v["errors"]} for k,v in audits.items()},
    "testCounts": test_counts, "distinctPassingTests": counts, "testCases": cases,
    "actualProductPipeline": pipeline, "nativeDesktopInteraction": False, "f4": "OriginalExternalStopApertureConflict retained"}
(base / "completion-summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
report = f"""# F/8 固定口径失光与像差补偿补完

本次用户指定范围已完成：原五种子42–46在同一冻结 ColdStart v6 / GateCompensation v4 策略下全部从零构造，**5/5通过**原完整验收，并接入自主拓扑设计自动准备、交接、保存和续跑流程。历史同后焦探索对照为1/5；它复用前一批完整冷启动对照，运行时来源不同，不称同二进制新对照或一般提速。原1/5候选、其他失败批次及开发续接均保留，未拼接成功终点。

## 规格、资源和原门槛

原任务为八片、50mm、F/8、全视场25°，各面净/机械半径5mm，固定前置光阑半径3.125mm、z=0，像面z=65mm。焦长与F数各±2%，RMS≤50µm、最大半径≤150µm、畸变≤3%，原积分有效率≥98%、所有中心/边缘检查通过，原厚度、空气间隔、后焦、轨长和斜率门槛均保留。原材料、曲率、轴向及逐坐标系数边界未扩大；仅使用原已许可的四/六阶偶次面型，仍为八片，不宣称制造复杂度下降。

每个冷启动统一上限600次函数、600次梯度、10,000,000条实际CPU光线、600秒；构造、筛检、各补偿阶段及原完整终验全部计入。每个种子为新进程、新RNG、新布局，不导入修复终点、检查点或优化器状态。两个有界后台工作槽，抑制嵌套计算并行；用时为本机观察，不作速度收益保证。

## 最终完整结果

{chr(10).join(table)}

五份新终点均为原21视场积分100%有效、边界零丢失、无原制造/像质违例。最终冻结运行时独立复验{audits['audit-v6-final-frozen']['replayCount']}次，当前产品运行时再复验{audits['audit-v6-current-product']['replayCount']}次，完整十次运行中所有已完成的构造/阶段评估逐项哈希一致；原始边界/系数与预算结算亦通过。每个合格终点另重复原验收，并导出STAROPT后读回原验收通过。后验审计成本独列，不补入冷启动额度。

完整协议、源/运行时哈希、所有输入、结果和历史对照来源在[最终证据](../validation/rechecks/2026-10-31/f8-completion/README.md)。对照来自`paired-v3/control`，逐字节副本及原source/runtime/protocol清单随附。同期其他任务修改了Core的GRIN/MEMS/导入序列化代码，源差异完整披露；因此不声称运行时相同，所有相关原光学评估由最终冻结与当前产品各自严格复算确认。

## 修正及失败记录

1. Gate v2原先按逐行1e-10拒绝硬约束越界，却只在平方和大于1e-16时启动修正；新显式版本统一逐行判断，解除微小真实越界造成的停滞。
2. 极小失光曾被平方和分支当作已清除，像差又未通过；新模型使用Core有符号通光值和导数联合优化，保留每条焦长和制造门槛。
3. 单个最差质量指标的低秩求步加入Core光斑坐标导数的零位移Gauss–Newton稳定项；接受仍按失败门槛下降。Gate v4另增加更严格的训练内余量，正式物理验收不增加epsilon。
4. Cold v4提前进行稀疏A4训练、将剩余时间交给密集A6；其整批种子45在稀疏训练收敛后仍有原密集RMS超限，之后申请419次更新，超过求解器原有400次上限而失败。Cold v6仅将阶段更新限制在既有400次内，总额度不增，整批重新从零运行。未采用的Gate v5原门槛抛光原型保留源码和诊断，产品不开放其版本。

历史范围分别保留：上一批候选1/5、控制0/5见[原冷启动报告](INITIAL_STRUCTURE_F8_COLD_START_2026-10-31.md)；`paired-v2`首对失败后停止，两个种子43中断；`paired-v3`候选4/5、控制1/5，73次独立重算通过；`final-cold-v4-confirmed`三个通过、种子45失败，种子46中断，不能称完整五样本；`development-v4/seed46`为先期单例通过，未导入最终搜索。所有warm repair均为开发诊断，未计最终成功数。

## 自动流程与使用

在主程序“自主拓扑设计”打开[本任务计划](../validation/rechecks/2026-10-31/f8-completion/F8-final-v6.topology-plan.json)后运行：先按声明的独立额度冷构造和补偿，保存准备证据，仅将预算内完整原规格合格种子交接主搜索；失败或取消保存并停止。主搜索从已保存检查点续跑时不重复冷构造。该显式实验入口默认关闭，新勾选采用v6；加载旧v1–v4配置保留原版本及指纹，历史空配置与其他默认入口保持兼容。

真实产品`AutonomousDesktopSession`用原种子45从零执行v6，冷准备189函数/128梯度/430,800光线/{seconds(pipeline['preparationCost']['elapsed']):.2f}秒，之后执行一个主搜索试验，验证合格归档、实际保存和工程读回。主搜索费用单列，未计作冷启动对照样本。定向最终回归按用例ID去重为**{counts}/{counts}通过**，分项见机器账本；包括数字求步/旧策略、各版本入口、取消、预算、交接、续跑及离屏界面。早期旧保存失败测试的超时、零匹配调用和中断运行均保留，不计通过。

原21°减片任务已在[第二轮](INITIAL_STRUCTURE_LOSS_COMPENSATION_ROUND2_2026-10-31.md)完成五种子5/5，去重一份通过原35视场验收的二片；原三片更好的像质/更少非球面仍保留。此次不改写该任务范围或费用。F/4继续保留光阑半径6.25mm大于各面5mm的规格冲突，不放宽口径、视场或验收。更广的M3后续结构搜索能力仍按总计划保留，五个种子不构成一般成功率证明。

全部验证在后台/离屏执行；未控制原生桌面、认证GPU、提交、推送、发布或调用DeepSeek。
"""
(repo / "docs/INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md").write_text(report, encoding="utf-8")
current = repo / "docs/CURRENT_STATUS.md"
text = current.read_text(encoding="utf-8")
old = '按用户“全完成”继续进行 [F/8 稳定性补完](INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)：修正逐行硬约束接受/修正阈值不一致、极小失光训练停滞，并加入共享 Core 光斑导数稳定项。新五种子同预算冷启动已另冻结运行，尚未据开发续接宣称全部解决。下述 1/5 保留为上一批原始结论。'
assert old in text
new = f'本次[F/8 补完范围](INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)已完成：同一冻结Cold v6 / Gate v4策略下，原42–46五种子从零构造**5/5通过**，每次仍为600函数/600梯度/一千万实际光线/600秒，21视场积分100%有效、边界零丢失，原口径、像质、制造门槛保持。历史同后焦探索控制1/5，明确保留不同运行时来源；最终冻结和当前产品分别{audits["audit-v6-final-frozen"]["replayCount"]}/{audits["audit-v6-current-product"]["replayCount"]}次独立复验通过。真实产品冷启动→自动交接→归档→STAROPT读回通过，定向去重**{counts}/{counts}通过**，默认Release构建零警告/错误。新显式冷启动使用v6，旧配置保持原版本；原21°减片5/5及F/4规格冲突保持。下述1/5和较早统计均为历史阶段，不改写为本轮结果。'
current.write_text(text.replace(old,new,1), encoding="utf-8")
readme = repo / "labs/InitialStructure/README.md"
text = readme.read_text(encoding="utf-8")
old = '正在执行 [F/8 稳定性补完](../../docs/INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)：新显式求步版本及同预算五种子冷启动对照尚在运行，原物理门槛与 F/4 冲突保持，下方上一批统计不改写。'
assert old in text
readme.write_text(text.replace(old,f'[F/8补完](../../docs/INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)已完成：原五种子同一冻结v6策略冷启动**5/5通过**，历史同后焦探索控制1/5（保留不同运行时来源）；原600/600/一千万/600秒额度、口径、像质、制造门槛均不变。真实产品自动准备、合格交接、保存和STAROPT读回通过，定向去重{counts}/{counts}通过，默认Release零警告/错误。新显式实验冷启动使用v6，默认关闭；旧配置保留原版本，F/4冲突保持。下方原1/5等记录为此前阶段。',1),encoding="utf-8")
plan = repo / "docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md"
text = plan.read_text(encoding="utf-8")
text = text.replace('新的约束补偿仍为底层显式研究策略，尚无新增原规格合格解，不能升为默认或把 M2 效果问题列为完成。','本专项已完成原21°减片5/5和原F/8五种子冷启动5/5，保持原全部门槛并接入显式自动流程，见[F/8最终补完](INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)。此前各阶段失败和资源范围保留；不切换其他默认入口，也不将更广M3能力标为完成。',1)
old = '- [ ] 提高 F/8 冷启动稳定性。当前四个失败种子最差 RMS 50.08–53.49 µm，三个有边界丢失；维持原口径、像质、制造门槛和 F/4 规格冲突，不据 1/5 宣称全面解决。'
assert old in text
text = text.replace(old,'- [x] 本专项原五种子F/8冷启动稳定性补完：同一冻结v6候选5/5、历史同后焦探索控制1/5，原口径、像质、制造及统一600/600/一千万/600秒门槛保持。自动流程和实际产品工程读回通过；原四失败及F/4冲突保留，见[最终报告](INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)。不推论一般成功率或完成其他M3结构搜索范围。',1)
plan.write_text(text, encoding="utf-8")
old_report = repo / "docs/INITIAL_STRUCTURE_F8_COLD_START_2026-10-31.md"
text = old_report.read_text(encoding="utf-8")
old_report.write_text(text.replace('\n','\n\n后续补完已另以同一冻结策略重跑原五种子5/5，见[最终报告](INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)。本报告保留此前1/5的原身份和失败范围。\n',1),encoding="utf-8")
evidence_readme = f"""# F/8 本次补完证据

最终`final-cold-v6`候选五种子全部新冷启动、5/5通过；历史控制1/5。完整说明见[报告](../../../../docs/INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)。

- `protocol.json`在本批执行前声明；`source.zip`、`source-sha256.json`、`runtime-sha256.json`冻结策略，运行中未修改。
- `candidate/seed42`至`seed46`包含独立输入、完整结果、摘要、进度；无终点或检查点导入。
- `control`为`../paired-v3/control`完整字节副本；`historical-control-provenance`保留原协议、源/运行时哈希和复制清单。不同运行时，未冒称同二进制重跑。
- `audit-v6-final-frozen`和`audit-v6-current-product`各自核对完整十次运行、原预算/边界/系数、全部已完成原验收及合格工程读回。
- `actual-product-pipeline-v6`为额外真实App自动交接/保存/导出/读回检查，不计入五个冷启动样本。主搜索费用单列。
- `completion-summary.json`含逐项结果、成本、去重{counts}个通过测试及范围。`tests/INCOMPLETE_RUNS.md`保留零匹配、中断和早期旧UI超时范围。
- 所有`development-*`、`endpoint-diagnostics`及失败/中断批次单独保留；Gate5未采用，Cold5保留为不开放原型编号。最终Cold6仍使用Gate4，阶段更新≤原有400，总预算不增。
- F4原规格冲突、以前21°二片结果均保持；未进行原生桌面、GPU或更广搜索认证。
"""
(base / "README.md").write_text(evidence_readme.replace('../../../../..//docs','../../../../../docs'),encoding="utf-8")
print(f"Sealed 5/5 candidates; {counts} distinct passing tests; independent audits and product handoff passed.")
