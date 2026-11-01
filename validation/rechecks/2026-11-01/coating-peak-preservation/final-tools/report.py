from pathlib import Path
import json
import shutil
import sys

r=Path(sys.argv[1]);read=lambda p:json.loads(Path(p).read_text(encoding="utf-8-sig"));v=read(r/"verification.json")
rows=read(r/"root-cause-paired.json")
def od(d):return [min(s["Power"]["OpticalDensity"] for s in d["Result"]["Spectrum"] if b["MinimumNm"]<=s["WavelengthNanometers"]<=b["MaximumNm"]) for b in d["Target"].get("StopBands") or []]
fmt=lambda a:" / ".join(f"{x:.6f}" for x in a)
matrix=[]
for row in rows:
 for label,m,o in [("冻结起点",row["Input"],row["InputOd"]),("原Optimize N",row["OldN"],row["OldNOd"]),("实际指标修正探针",row["ActualCorrectionProof"]["Final"],row["ActualCorrectionProof"]["FinalOd"])]:
  matrix.append(f"| {row['Name']} · {label} | {100*m['PeakTransmittance']:.6f} | {m['FwhmNanometers']:.6f} | {fmt(o)} |")

body=f"""# 镀膜损耗与停滞根因审计（2026-11-01）

本轮按用户“仔细想想问题原因”的要求转为根因审计，**没有继续改产品优化器**。已查实：当前原材料膜系以吸收为主要损失；内层优化保护原验收线、外层却要求保护实际余量，两者的可行域不一致；线性可行步骤可能在真实非线性带宽/OD曲面外，单纯缩步仍被拒绝。已有局部可行增透反例，因此既不能把停滞当作“额度不足”，也不能据此宣判75%在原规格下不可能。

## 1. 主要损失是吸收，继续入口匹配不能单独补足差距

同一真实n/k及正式采样/2，冻结起点实际功率分配如下，A只含有限膜层吸收，T为进入半无限基底的功率：

| 实际处方/位置 | R（%） | T（%） | A（%） |
|---|---:|---:|---:|
| 550 nm双腔峰值550.497176 nm | 2.661178 | 67.153007 | 30.185814 |
| 500 nm双腔峰值499.589542 nm | 0.673381 | 61.648970 | 37.677649 |
| 550 nm高反最差点520 nm | 96.334481 | 0.011270 | 3.654249 |
| 710 nm高反最差点671.272727 nm | 97.778922 | 0.012606 | 2.208472 |

若550 nm候选的吸收不变，即使将当前反射全部消掉，T也只能到69.814186%；要75%，哪怕最终R=0，A仍至少需减少5.185814个百分点。500 nm的相应最低吸收削减为12.677649个百分点。这是**当前结构的损耗账本**，不是所有可允许拓扑的物理上界。入口匹配会同时改变场和吸收，不能假定“降R全部变为T”。

原材料表在550.497 nm的Ta2O5约n=2.11558、k=0.0108964，SiO2约n=1.46612、k=0.00192225；在499.590 nm的Ta2O5 k约0.0157386。不是无损介质近似，不能删k。窄带共振场分布会放大吸收，单看入口反射或几何膜厚不能准确分配各层损失；本轮没有做分层场吸收积分，不将总吸收擅自归到某一层。

原高反最差点的透射只有约0.01%，而吸收为2.2%–3.7%；继续降低这点透射不能补足98%缺口。6组真实材料、两种终止方向、原32层界内共192个周期参考膜系显示原材料族的低透射/吸收饱和；新实测组合在同层数界内可达98%。这只证明该周期族的趋势，不是所有啁啾、非周期或匹配结构的全局极限。

已用未修改固定tmmcore独立对照246个真实n/k的R/T/A点及对应全部膜厚导数，最大功率差{max(v['referenceMaximumErrors'][k] for k in ['R','T','A']):.3g}、最大导数差{max(v['referenceMaximumErrors'][k] for k in ['dRdd','dTdd','dAdd']):.3g}，通过原2e-11标准。没有发现基础散射求解或点功率膜厚导数错误；这不等于指导网格的带宽/峰位导数已获得同样的全局保证。

## 2. 内外约束不一致，原优化可消耗用户希望保住的实际改善

对新550 nm候选直接调用原Optimize(N=96)，T从67.153007%升到67.829380%，但FWHM由15.965667变15.968693 nm，两侧OD从1.053155/1.676242降至1.049400/1.651300。都仍满足原宽度/OD验收，所以基于`Violations`的内层允许；但外层`JointStructureFeedbackImprovement`保护实际宽度误差和各OD，必然拒绝。这是可重复的**约束集不一致**，不是材料参数错误。

源代码中Optimize最终保护采用原目标超限包络，随后RefinePassbandQuality从这个已选结果建立实际Bounds；这些Bounds没有从本次调用的原起点贯穿目标修复、方向比较与精化。质量阶段逐步收紧实际边界还可能封住后续重分配余量的路径，但它对最终75%可达性的独立影响尚未量化，不列为已证全局根因。

本轮15组原N控制已完成，SiO2/Nb2O5与SiO2/TiO2的新材料暖起点已有原Optimize达标；这些规格在前轮完整旧Search已通过，不计新修正突破。更大旧N批次在根因审计转向后保留{v['largerNBatch']['completedAtStop']}份完成处方并中止，不能称15组完成。单独聚焦的新550 nm N272运行也在根因证据明确后中止，未完成，不计控制通过或可行性结论。原N96已足以复现余量冲突；更大N的未完成状态不能用于证明75%不可达，增加N本身也不会改变两类约束的语义。

## 3. 线性切面上的可行方向不一定落在真实约束曲面内

在原实际宽度误差/各OD上限下，用两档有限差分和共享Core不等式步计算局部增T方向。550 nm相位信赖半径0.001的线性预测T增加0.000159，实际T也增加，但宽度变大、OD下降。缩至1/128后仍有二阶越界，严格外层继续拒绝；这不表示真实空间已没有增透方向。

使用**同一个现有Core非线性修正器**，把约束读数改成正式实际指标（本轮诊断用有限差分，未移植到产品），两个候选均得到受保护的增T反例。相位半径0.01的结果：

| 起点/流程 | T（%） | FWHM（nm） | 左/右OD |
|---|---:|---:|---|
{chr(10).join(matrix)}

550 nm的稳定探针只有T67.173921%，500 nm为T61.677477%。它们证明“仍存在可行增透方向”，**不是达到75%、不是新通用优化器完成、更不是相对旧N的全面性能胜利**。改正后的实际指标反馈需要贯穿内层，而不是等到外层拒绝后再加随机起点。

4个探针默认正式采样均共同保留，4份磁盘原值/运行元数据重放一致；30次400/1600/3200和独立8192/32768采样复核中，3个探针在全部对应网格共同保留。550 nm半径0.001的微小宽度收益在8192点发生约4.96e-7 nm反转，保留原数据，**不计稳定进展**。半径0.01的两组在全部对应采样保持改善。该反转不改变原15±1 nm验收，只说明微小余量差不能超出测量分辨率作解释；原门槛未放宽。

固定TFStudio结构突变/Metropolis/DLS也从这两个新起点运行，550 nm得到T66.572242%、宽18.318564 nm，500 nm得到T62.148228%、宽33.373442 nm，按原正式门槛均失败。参考点平方目标不等于实际峰值/FWHM/最差OD约束，不能拿其内部merit下降当原规格可行证明。

## 下一步与未证明事项

修正顺序应为：先把“调用起点实际带宽/各OD界”作为明确的约束对象贯穿匹配、目标修复、方向比较、短程和最终保留；再使非线性可行性修正读取与最终验收一致的实际极值/带宽，并控制采样不确定性；随后才比较保住这些界的低吸收场分布/拓扑搜索。实际收益仍须对照旧N及追加旧额度，不能靠逐层特例、去k或放宽门槛。

**75%在原真实材料、双腔、层数、带宽和OD约束下是否可达，仍未证明；98%对全部允许拓扑也未作不可行证明。** 本轮没有改产品优化器，原550/500 nm重点及原98%高反未新增整组达标，也没有把诊断有限差分当成已验证的生产修复。新增诊断工具构建通过，验证范围为上述246点、192个周期族样例、局部方向/修正和30次采样；未重跑产品/全仓测试、原生GUI、提交或发布。前轮737份冻结证据逐项保持，见[原始处方、参考、对照和哈希](../{r.as_posix()}/README.md)。
"""
report=Path("docs/COATING_LOSS_AND_STAGNATION_2026-11-01.md");report.write_text(body,encoding="utf-8")
status=Path("docs/CURRENT_STATUS.md");text=status.read_text(encoding="utf-8-sig");title="## 2026-11-01 镀膜损耗与停滞根因审计"
if title not in text:
 first,rest=text.split("\n",1)
 status.write_text(first+f"\n\n{title}\n\n新550/500 nm处方峰值吸收30.19%/37.68%，原高反最差点吸收2.2%–3.7%，入口匹配/减少透射不足以单独填补目标缺口。已复现内层只保护原验收、外层保护实际带宽/OD的约束集不一致：原N96将550 nm T推至67.83%，但OD和宽度退步。实际指标非线性修正探针证明局部仍可增T，尚未证明75%或全局不可行。\n\n本轮未改产品优化器。246个R/T/A及全部点膜厚导数对固定参考通过原2e-11，192个界内周期族、30次采样及4份重开完成；仅3个微步在全部网格稳定，反转点不计进展。15组原N控制完成，旧大批次及单独550 nm N272中止范围明确，不计完成或可行性结论。见[根因、证据及修正顺序](COATING_LOSS_AND_STAGNATION_2026-11-01.md)。\n"+rest,encoding="utf-8")
(r/"README.md").write_text(f"""# 镀膜损耗与停滞根因证据

见[报告](../../../../docs/COATING_LOSS_AND_STAGNATION_2026-11-01.md)。用户要求先核查原因，本轮没有改产品优化器或原验收门槛。

- inputs/primary-inputs：15组冻结输入及550/500 nm重点；profiles-inputs额外含3组已优化实测组合，全部n/k保留。
- profiles.json、reference-points、reference-comparison：18个损耗剖面及4个修正探针，共246点实际R/T/A和全部膜厚导数，固定tmmcore原2e-11标准。
- periodic.json：6组真实材料、两种终止、原层数/厚度界内192个周期膜系；只说明该族，不是全局上界。
- directions.json：两档实际指标有限差分、局部线性可行步及48次原门槛复验；restored/summary和实际处方：同一Core修正器接入实际约束后的4个反例。
- fine-sampling、dense-restoration-comparison：30次相同门槛复核，3个稳定微步；550 nm半径0.001宽度有反转，不计稳定改善。restored-replay直接对照磁盘原值/元数据4/4。
- old-n：15组完整原N控制；larger-old为中止批次的{v['largerNBatch']['completedAtStop']}份已完成文件，不计整个批次完成；focus-larger-old为未完成的新550 nm起点N272，不计完成。budget-control-scope.json保留原因。
- reference-structural/reference-native：固定TFStudio从两个新重点起点运行，再以原指标验收，均未通过；点平方目标和实际约束不同。
- verification.json及final-manifest.json：源码未改、Core/Engine二进制和前轮737份证据完整性；final-source/tools/docs为本次快照。

微步是根因反例，不是75%或98%突破、生产修复或等总成本优势。未操作桌面、产品/全仓测试、提交、推送或发布。
""",encoding="utf-8")
for folder in ["final-tools","final-docs","final-source"]:(r/folder).mkdir(exist_ok=True)
for p in Path("tools/diagnostics/CoatingPeakPreservation20261101").iterdir():
 if p.is_file():shutil.copyfile(p,r/"final-tools"/p.name)
for p in [report,status]:shutil.copyfile(p,r/"final-docs"/p.name)
for p in (r/"baseline-source").glob("*.cs"):shutil.copyfile(p,r/"final-source"/p.name)
print(json.dumps({"report":str(report),"productOptimizerChanged":False}))
