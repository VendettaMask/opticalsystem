from pathlib import Path
import json
import shutil
import sys

r=Path(sys.argv[1]);read=lambda p:json.loads(Path(p).read_text(encoding="utf-8-sig"));v=read(r/"verification.json")
paired=read(r/"formal-paired.json");old={x["Name"]:x for x in read(r/"larger-old/summary.json")}
od=lambda a:" / ".join(f"{x:.6f}" for x in a)
table=[]
for x in paired:
    for label,key,okey in [("起点","Initial","InitialOd"),("旧Search","Baseline","BaselineOd"),("反馈研究流程","Final","FinalOd"),("旧Search后追加旧日程","Append","AppendOd")]:
        m=x[key];table.append(f"| {x['Name']} · {label} | {100*m['PeakTransmittance']:.6f} | {m['FwhmNanometers']:.6f} | {od(x[okey])} |")
rematch=read(r/"primary-rematch/summary.json");green=next(x for x in rematch if x["Name"]=="new-real-nbf-green")
hr=read(r/"hr-chirp/summary.json");hr_old={x["Name"]:x for x in read(r/"hr-old-schedule/summary.json")}
hrtable=[f"| {x['Name']} | {100*x['Final']['MinimumReflectance']:.6f} | {100*hr_old[x['Name']]['Final']['MinimumReflectance']:.6f} |" for x in hr]
passed=sum(x["Passed"] for x in paired);accepted=v["acceptedWindows"]
focus=next(x for x in paired if x["Name"]=="real-nbf2-96")
body=f"""# 修正采样下的匹配后反馈与实测材料组合（2026-11-01）

**550 nm真实双腔新增一个共同改善处方：T约67.15%、FWHM约15.966 nm、OD约1.053/1.676，带宽进入原15±1 nm门槛，但峰值T仍未75%。** 它相对同一起点的完整旧Search、追加旧日程及单次旧N272共同更好；此前较宽的67.80%候选仍有更高T，不能称对所有历史候选支配。500 nm离线重新匹配相对原起点共同改善，但左OD仍略低于追加旧额度控制。原材料550/500 nm双腔及98%高反均未整组突破。新增三套实测组合的双腔可以通过原规格；完整旧Search同样能通过，不能将材料更换或旧搜索能力归功于新反馈。

## 产品修正和启用边界

Engine新增显式`SearchMatchedFeedback`研究API，仅协议4窄带。先完整执行原Search，再最多两轮拟合角色反馈：按声明镜段/腔阶转移实际膜厚比例、保留可核验的材料顺序/匹配前缀、更换内部结构后重新求解真实透射匹配根；短程精化后必须给新挑战者一次完整优化。未保留的完整处方仍可成为下一轮探索点，正式保留结果独立，必须同时守住T、每侧OD、带宽误差、原超限和收敛/通过状态。固定层或禁止结构变化时不改拓扑。

初次把新候选直接替换旧Search名额，在730 nm产生T/带宽退步，已停止并保留证据。最终研究方法保护完整旧Search结果；{v['researchSpecifications']}组、{v['feedbackWindows']}个窗口有{accepted}个获共同保护接受，{v['jointBaselinePreserved']}/9保持旧Search实际指标边界。追加旧连续额度仍在多组更好，550 nm的一次共同改善尚未证明跨规格普遍优势，**普通Search/Generate和桌面入口不自动启用额外计算**。原优化试运行使用冻结研究构建；最终显式API只改变启用入口，反馈主体保持，入口定向测试及所有实际处方由最终二进制重评。不同二进制身份和源码边界留在activation-boundary.json及verification.json，不冒称九组都重新调用最终API完成优化。

每轮最多256个指导候选、min(4,Starts)个ceil(sqrt(N))主额度短程、一个N主额度完整试探，最多两轮。这些在旧Search以外。透射匹配根各最多min(32,N)主迭代，内部恢复/质量、灵敏度、密集复验和打开文件复算另有工作，不能称等总查询/算力。Run.StructureFeedbackWindows保存每轮父处方/原型、转移、匹配根、短程、完整起止、正式基线及接受；StructureFeedback是末轮。历史值可空，schema 1和输入指纹不变。默认协议与正式采样extrema-anchored-band-domain/2、指导采样search-peak-reference/2保持；Core求解、n/k、Checks及数值门槛未改。

## 原双腔和98%高反

500 nm独立离线“原型转移后重新匹配”T {100*green['Initial']['PeakTransmittance']:.6f}%→{100*green['Final']['PeakTransmittance']:.6f}%，FWHM {green['Initial']['FwhmNanometers']:.6f}→{green['Final']['FwhmNanometers']:.6f} nm，OD {od(green['InitialOd'])}→{od(green['FinalOd'])}。仍未达75%及13.636±0.909 nm；旧同日程左OD为1.005005，单次旧N152左OD为1.005165，均略高于反馈的1.004650，不放宽门槛或用舍入判作支配。

550 nm两轮研究流程第二轮实际保留T{100*focus['Final']['PeakTransmittance']:.6f}%、FWHM{focus['Final']['FwhmNanometers']:.6f} nm、OD {od(focus['FinalOd'])}。从同一原暖起点的单次旧N272仅得T66.524376%、宽17.187328 nm、OD1.001239/1.211860，新处方三者共同更好；仍只有T未达到原75%门槛，不能称整组通过。

原角色反馈在修正采样下复现T67.799319%、FWHM17.196751 nm、OD1.025524/1.393329。对这个实际处方保留可核验原型[3,7,3]/[4,5]，再给两轮重新匹配或同日程旧控制，均停在原保留结果。它是一次实际试验的物理角色记录，不是优化器中的固定层/例子分支。新处方的宽度/OD更好但T低于这个历史保留点，故不能冒称跨起点共同支配。

三项真实高反追加了自由删/分裂/插入/边界匹配及通用带内光学相位啁啾、相邻异材料相位交换，实际n/k和厚度/层数界保持，仍无98%新增通过。相位变化用共享Core特征厚度换算，不按材料名或某层位置指定常数。有限预算不是全局不可行证明。

| 原高反 | 啁啾/相位交换后Rmin（%） | 同日程旧控制Rmin（%） |
|---|---:|---:|
{chr(10).join(hrtable)}

## 不同实测组合及比较

原SiO2/Ta2O5之外，加入SiO2/TiO2、SiO2/Nb2O5及HfO2/TiO2。三组新窄带分别继承原550/500 nm双腔门槛、角度、偏振、层数/厚度界，仅按共享Core特征光学厚度转移暖起点并按原界裁定，准备时的边界夹取数留在material-inputs/summary.json。原始表逐值保留n/k，只换波长单位，不去掉吸收、不把缺失k补零；仅有n的Al2O3-Boidin表明确排除。

原表来源为固定refractiveindex.info数据库c5c2f188e848453def5970e347399d653df2ffc2：TiO2的350 nm ALD薄膜测量/色散拟合见[原始数据](https://raw.githubusercontent.com/polyanskiy/refractiveindex.info-database/c5c2f188e848453def5970e347399d653df2ffc2/database/data/main/TiO2/nk/Siefke.yml)；Nb2O5为500 nm磁控溅射膜、作者提供的n/k及测量方法，见[原始数据](https://raw.githubusercontent.com/polyanskiy/refractiveindex.info-database/c5c2f188e848453def5970e347399d653df2ffc2/database/data/main/Nb2O5/nk/Lemarchand.yml)；HfO2为220 nm PEALD薄膜，见[原始数据](https://raw.githubusercontent.com/polyanskiy/refractiveindex.info-database/c5c2f188e848453def5970e347399d653df2ffc2/database/data/main/HfO2/nk/Siefke.yml)。HfO2表仅到619.921 nm，只用被覆盖的500/550 nm规格；数据描述相应样品及报告拟合，不是任意制程常数或实物产品认证。原YAML、CC0声明、论文和哈希已归档。

三组新双腔在单独重新匹配臂均达标，同日程旧厚度连续控制均未达标；但完整旧Search分别已有约99.76%、92.48%、91.29%的T并通过，研究反馈不应拿更弱控制冒称优势。三组新高反在输入准备阶段就已98%通过，后续保持通过，不能计搜索新增突破。合成1550 nm控制单列，不算新增实测材料组合。

九个窄带完整旧Search及研究流程均为{passed}/9通过：原730/650 nm和三个新组合。追加旧控制从完整旧Search出发，仍用两轮，每轮4个短程及一个原N完整优化；另15组从暖起点做单次旧额度2N+2min(4,Starts)ceil(sqrt(N))控制。都保留原验收门槛，额度差异明确记录。

| 规格/流程 | T（%） | FWHM（nm） | 各截止OD |
|---|---:|---:|---|
{chr(10).join(table)}

固定未修改TFStudio的结构突变、清理、Metropolis及DLS跑15组相同起点，再用相同Core及正式采样验收；参考3项通过均为输入已通过的新高反，窄带0/9。参考点平方目标不同于实测FWHM/最差OD/最差R，未称优化器全面优越或总算力相同。

## 验证与剩余范围

默认Release Engine正常依赖图构建0警告/错误。最终显式API及相邻定向18/18，包括四组跨波长/折射率/腔数角色转移、固定/错误角色拒绝、两轮有界API与保存重开，以及采样、排序、生成/搜索保护。不是完整模块/全仓或原生GUI验收。

最终二进制对{v['coldReplays']}个保留实验直接核对磁盘原指标/光谱/Checks及运行元数据，再保存重开；对{v['stageRechecks']}个反馈实际基线、短程及完整处方独立重评。{v['physics']['points']}个实际n/k的S/P/非偏振R/T/A比较点（含各臂重复处方）对固定tmmcore通过原2e-11标准，最大差R={v['physics']['maximumErrors']['R']}、T={v['physics']['maximumErrors']['T']}、A={v['physics']['maximumErrors']['A']}。前轮421份证据逐项哈希保持。失败构建、误用诊断CLI、停止的集成、分批中止和500 nm旧诊断缺少可核验原型的限制均保留，未猜测补造来源。

原550/500 nm真实双腔、原三项98%高反、反馈相对追加旧额度的跨规格稳定优势仍开放。研究API不等于桌面已默认启用，也不是全局光谱/可行性证明。见[可复算实验、成本、原始来源与哈希](../{r.as_posix()}/README.md)。
"""
report=Path("docs/COATING_RESAMPLED_FEEDBACK_2026-11-01.md");report.write_text(body,encoding="utf-8")
status=Path("docs/CURRENT_STATUS.md");s=status.read_text(encoding="utf-8-sig");title="## 2026-11-01 镀膜修正采样反馈与新增实测组合"
if title not in s:
 first,rest=s.split("\n",1)
 status.write_text(first+f"\n\n{title}\n\n550 nm真实双腔新增共同改善处方：T67.15%、FWHM15.966 nm、OD1.053/1.676，相对同暖起点的完整旧Search、追加旧日程及单次旧N272三者共同改善，带宽进入原15±1 nm门槛，T仍未75%。此前宽带67.80%候选的T仍更高，未称跨起点支配。原550/500 nm及98%高反均未整组突破。\n\n显式Engine研究API SearchMatchedFeedback支持两轮真实角色转移、重新匹配、探索/正式结果分离和逐项共同保护；9组研究流程、{v['feedbackWindows']}窗口、{accepted}次共同接受，未证明全面优势，普通Search/Generate及桌面入口不自动增加工作。补充TiO2/Nb2O5/HfO2原始n/k，三个新实测组合双腔通过；完整旧Search同样能通过，新高反在输入阶段已通过，不冒称新算法突破。\n\n默认Release Engine构建零警告错误，最终API及相邻定向18/18；{v['coldReplays']}个保留实验原磁盘值/元数据重放、{v['stageRechecks']}个反馈处方重评、{v['physics']['points']}点独立R/T/A通过原标准。非完整模块/全仓或原生GUI验收。见[对照、成本和剩余项](COATING_RESAMPLED_FEEDBACK_2026-11-01.md)及[证据](../{r.as_posix()}/README.md)。\n"+rest,encoding="utf-8")
(r/"README.md").write_text(f"""# 修正采样后的匹配反馈与实测组合证据

见[报告](../../../../docs/COATING_RESAMPLED_FEEDBACK_2026-11-01.md)。全部优化使用正式采样/2，真实n/k与原验收门槛保留。

- all-inputs及materials：15个冻结起点，4套实测材料组合和一个合成红外控制；原表/提交/CC0/哈希、n-only排除原因及准备时的相位转移与界内夹取单列。
- formal-baseline-all、formal-final-all、formal-append-all：九组完整旧Search、两轮反馈研究流程和追加旧日程。formal-paired.json逐项比较；研究构建与最终显式API的启用边界见activation-boundary.json，未冒称重跑最终API的全部优化。
- primary-*、material-*、hr-*、continued-*、larger-old、reference-*：独立角色/重新匹配、不同实测组合、高反相位探针、550 nm继续、15组单次更大旧额度及15组固定参考。
- feedback-stage-prescriptions.json、stages：{v['stageRechecks']}个实际反馈基线/短程/完整处方重评；cold-index、cold-inputs、cold-output：{v['coldReplays']}个磁盘原结果及元数据重放。物理比较见physics-comparison.json（{v['physics']['points']}点，含重复处方）。
- verification.json、execution-notes.json、superseded-*、one-generation-*：全部边界、失败和停止记录；logs/api-structure-feedback.trx为最终18项。额外匹配、恢复/质量、加密、打开复算另计，同日程主额度不等总查询成本。
- final-source、final-tests、final-tools、final-docs为最终源码/测试/工具/文档快照；final-manifest.json校验本轮，前轮421项保持。

普通Search/Generate及桌面未默认开启新反馈；原材料550/500 nm及98%高反仍未突破。未提交、推送、发布或原生GUI验收。
""",encoding="utf-8")
for folder in ["final-tests","final-tools","final-docs"]:(r/folder).mkdir(exist_ok=True)
shutil.copyfile("labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/StructureFeedbackTests.cs",r/"final-tests/StructureFeedbackTests.cs")
for p in Path("tools/diagnostics/CoatingFeedbackResampled20261101").iterdir():
 if p.is_file():shutil.copyfile(p,r/"final-tools"/p.name)
for p in [report,status,Path("docs/COATING_DESIGN_LAB.md"),Path("docs/FILE_FORMATS_AND_PLUGINS.md")]:shutil.copyfile(p,r/"final-docs"/p.name)
shutil.copyfile("labs/CoatingDesign/README.md",r/"final-docs/CoatingDesign-README.md")
shutil.copyfile("third_party/coating-reference/README.md",r/"final-docs/coating-reference-README.md")
shutil.copyfile("third_party/coating-reference/MATERIAL-NOTICE.txt",r/"final-docs/MATERIAL-NOTICE.txt")
print(json.dumps({"report":str(report),"passed":passed,"acceptedWindows":accepted}))
