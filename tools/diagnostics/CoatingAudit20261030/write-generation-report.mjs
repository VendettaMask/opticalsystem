import {readFileSync,writeFileSync} from 'node:fs';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const p=read(`${root}/paired-generation.json`),v=read(`${root}/verification.json`);
if(p.guardFailures.length||v.tests.total!==6||v.tests.passed!==6||v.tests.failed||!v.integrityPassed)throw Error('Final verification is incomplete');
const f=(x,n=6)=>x.toFixed(n),ods=a=>a.map(x=>f(x)).join(' / ');
const table=p.results.map(x=>`| ${x.name} | ${f(x.before.metrics.PeakTransmittance*100)} → ${f(x.after.metrics.PeakTransmittance*100)} | ${f(x.before.metrics.FwhmNanometers)} → ${f(x.after.metrics.FwhmNanometers)} | ${ods(x.before.od)} → ${ods(x.after.od)} | ${x.before.passed?'通过':'未过'} → ${x.after.passed?'通过':'未过'} |`).join('\n');
const hr=p.highReflector.remaining.map(x=>`| ${x.name} | ${f(x.reflectance*100)}% | ${f(x.target*100,0)}% |`).join('\n');
const report=`# 镀膜生成候选的正式复验与有界精化（2026-10-31）

本轮正式修改只进入显式协议4的窄带生成（Generate）：相同热起点的6组比较由0组达标变为${p.afterPassed}组，新增730 nm真实双腔和650 nm真实单腔。**两组旧版完整Search原本均能通过；本轮没有新增旧完整Search无法达到的规格，也没有新增98%真实材料高反通过。** 550 nm、500 nm真实双腔仍未达标。真实n/k、原Checks、采样收敛、层数/厚度界及固定变量保持。

## 通用修改与代价

原离散探索、结构邻域、多根匹配和12项短程筛选保持。生成的最终名额先给每种实际镜段/腔阶/基底顺序骨架一个较好代表，再补其余变体，避免同一骨架的匹配根占满名额。按真实完整Evaluate认证，随后最多对min(Starts,12)个未通过候选调用既有精化器，每次主迭代≤min(N,ceil(sqrt(N)))。保留候选自身的逐项目标超限，最终结果仍受原输入目标超限保护。按正式通过状态、超限和原merit排序，Candidate.Passed来自实际认证。规则不引用材料名/ID、某个固定膜层序号或示例签名。

新增身份为dense-feasibility-allocation/1。每个候选保存原层、精化后层、正式指标、各截止OD、收敛/通过、超限及实际子运行，可重开复算。父记录汇总子运行评价/主迭代；Guidance、灵敏度、约束、加密、密集调用等仍分开计费，既有恢复/比较的额外工作保留原字段和上限。**这是新增生成成本，不是等总计算量胜出**。短程主迭代上限不代表总光谱查询上限；详细计数及实测时长见各臂summary.json和paired-generation.json。

完整Search继续使用原GeneralPool与原Optimize，额外认证/精化只用于Generate；新的两个可空记录字段在Search为null，不补造旧记录。独立Optimize、AR/HR、默认AR/HR协议2及默认窄带协议3保持。源码逐文件比较、精确撤销GeneralSearch新增分支后与旧版一致的检查见verification.json。

## 相同起点的6组生成对照

使用上一阶段after-cold最终膜系作热起点，原Target、Settings、Materials不变。五组来自原SiO2/Ta2O5真实材料表的不同波段/单双腔，另一组为1550 nm合成材料控制；不能称为多套独立实测材料的认证。每臂保存后重新打开，由同一固定Core正式冷复算。下表OD逐截止带列示，比例T转为百分数显示；判定使用未舍入数。

| 规格身份 | 峰值T（%），旧→新 | FWHM（nm），旧→新 | 截止OD，旧→新 | 原整组门槛 |
|---|---:|---:|---|---|
${table}

730 nm的原要求为峰值T≥75%、中心730±0.663636 nm、FWHM 19.909091±1.327273 nm、两个截止带OD≥1；650 nm原要求同样是T≥75%、两个OD≥1，带宽17.727273±1.181818 nm。两组峰值T与部分OD余量下降，带宽恢复通过，**不是所有原始量共同提高**。其完整中心、最小OD、厚度界、采样状态均见逐项Checks；不把显示舍入后的1.000000当作判定输入。

完整Search的旧控制6组已有730/650 nm两组通过，本轮未改此路径。试验性把新排序/恢复用于完整Search未稳定胜出，存在T/OD/带宽退步，已拒绝。allocation/family/feedback/budgeted Search等中间结果、停止记录保留；不计为最终算法成果或已完成验证。

## 98%真实高反与参考实现

原19组HR最终膜系冷复算仍为11组通过、8组未过，指标、光谱与Checks逐值保持；通过集合含不同门槛及合成控制，不能称11组真实98%通过。剩余8项均要求98%：

| 身份 | 当前最低R | 原门槛 |
|---|---:|---:|
${hr}

单层/邻层实际相位、互补终止层及冻结曲率试探仅作为离线诊断；没有推广到产品。Core解析梯度的差分Hessian与固定tmmcore解析Hessian共28点比较，最大绝对相位尺度差${v.curvature.maximumError}，边界使用单侧二阶差分；旧的漏算边界列结果保留为作废中间证据。修正后冻结曲率试探未新增98%通过。局部正曲率、有限扫描及吸收占比都不是全局无解证明。

固定TFStudio提交8b942b3/tmmcore 0.4.2的18组未修改DLS/Newton/SQP运行，从相同6组热起点及真实n/k出发，按本地原正式门槛复验0/18通过。其点平方目标与本地实际FWHM/最差OD目标不同，也没有同一结构探索/总成本，因此只作参考结果和定义差异核对，不宣称算法整体优越。归档与所用参考源哈希保持。

## 验证范围

- 默认Release Engine正常依赖图构建0警告、0错误；隔离测试项目构建0警告、0错误。
- 最终实际发现并通过6项：新增4项及相邻生成/搜索目标保护2项。覆盖正式证书独立Evaluate、预算、固定层、原材料/目标、保存重开和真实730 nm完整门槛。只含2项的旧程序集运行不冒充6项；共享输出依赖被覆盖导致MissingMethod的失败记录保留。最终用固定Core与当前完整测试程序集隔离复验，不称最终默认测试输出或全模块/全仓认证。
- 六组生成两臂正式冷复算逐值一致；19组HR保持。${v.physics.cases}个真实n/k、S/P/非偏振R/T/A点独立比较，最大差R=${v.physics.maximumErrors.R}、T=${v.physics.maximumErrors.T}、A=${v.physics.maximumErrors.A}，通过既有2e-11物理检查标准。
- 未操作原生桌面，未提交、推送或发布。源码/输入/测试/参考/中间失败与哈希清单见[本轮证据](../${root}/README.md)。前一阶段[非线性约束修正](COATING_NONLINEAR_CORRECTION_2026-10-31.md)保持原日期和范围。
`;
writeFileSync('docs/COATING_GENERATION_CERTIFICATION_2026-10-31.md',report);
const status='docs/CURRENT_STATUS.md';let s=readFileSync(status,'utf8');
const title='## 2026-10-31 镀膜生成候选正式认证与有界精化';
if(!s.includes(title))s=s.replace(/^(# [^\n]+\r?\n)/,`$1\n${title}\n\n显式协议4窄带Generate按物理骨架分配名额、正式复验并限次精化。相同6组热起点由0/6变为2/6通过（730 nm真实双腔、650 nm真实单腔）；两组旧完整Search本来可过，当前保留原Search，未新增旧Search无法通过的规格。两组用部分T/OD余量换取带宽达标，不称原始指标共同上升。真实550/500 nm双腔及8组98%高反仍未过；真实n/k、原门槛和默认协议保持。\n\n默认Release Engine构建通过；最终隔离定向6/6、${v.physics.cases}点独立R/T/A、19组HR及六组生成双臂冷复算通过各自检查。完整搜索替代策略及曲率试探未采用，未全模块/全仓或原生桌面验收。详见[实现、权衡、代价与剩余项](COATING_GENERATION_CERTIFICATION_2026-10-31.md)和[证据](../${root}/README.md)。\n\n`);
s=s.replace('## 2026-10-31 镀膜非线性约束修正与原方向余量保护','## 2026-10-31 镀膜非线性约束修正与原方向余量保护（上阶段）');writeFileSync(status,s);
const guide='labs/CoatingDesign/README.md';s=readFileSync(guide,'utf8');
const notice='2026-10-31 当前生成：协议4窄带按实际骨架分配最终候选，正式认证并做有界精化；6组同起点对照新增730 nm真实双腔与650 nm真实单腔生成通过。旧完整搜索本来已可通过这两组，继续保留原路径；98%真实高反及550/500 nm双腔仍未达标，部分T/OD余量用于换取带宽通过。真实n/k及原验收保持。见[本轮结果、成本与边界](../../docs/COATING_GENERATION_CERTIFICATION_2026-10-31.md)。';
if(!s.includes('2026-10-31 当前生成：'))s=s.replace(/^(# [^\n]+\r?\n)/,`$1\n${notice}\n`);
s=s.replace('2026-10-31 当前：共享Core新增','2026-10-31 上阶段：共享Core新增');writeFileSync(guide,s);
const lab='docs/COATING_DESIGN_LAB.md';s=readFileSync(lab,'utf8');
const contract='协议4窄带Generate使用`dense-feasibility-allocation/1`：在既有探索/匹配/短程筛选后，每种实际镜段、腔阶及基底顺序骨架优先取得一个代表，正式认证最多12项，并对最多min(Starts,12)个失败候选调用既有有保护精化，每次主迭代≤min(N,ceil(sqrt(N)))。新增光谱、密集和子运行工作均记录，是额外生成成本；固定变量、原目标上界及完整验收保持。候选通过状态来自正式Evaluate。完整Search保留原GeneralPool及Optimize，不使用此生成专用排序；独立Optimize和AR/HR保持。保存的`SearchSeedValidations`含初始/最终膜系、正式指标、逐截止OD及实际子运行，可重开复算。两组新增生成通过并非旧完整Search能力突破，详见[结果与剩余项](COATING_GENERATION_CERTIFICATION_2026-10-31.md)。';
if(!s.includes('Generate使用`dense-feasibility-allocation/1`'))s=s.replace('窄带正式采样记录`anchored-band-domain/1`',contract+'\n\n窄带正式采样记录`anchored-band-domain/1`');
s=s.replace('完整搜索候选调用相同Optimize流程；模板生成不因此增加隐式优化。','完整搜索候选调用相同Optimize流程；新增生成阶段精化按上文认证协议单列记录，不调用完整Optimize。');writeFileSync(lab,s);
const formats='docs/FILE_FORMATS_AND_PLUGINS.md';s=readFileSync(formats,'utf8');
const fields='2026-10-31 生成认证记录：协议4窄带Generate可保存`Run.SeedRankingProtocol="dense-feasibility-allocation/1"`及`SearchSeedValidations`。每项含`Layers`、`Metrics`、各`OpticalDensities`、`SamplingConverged`、`Passed`、`Violations`，可空`Prototype`、`InitialLayers`、`RefinementRun`保存实际起源及子调用。分别保存原层和最终层；Passed仍由原正式采样与Checks决定。最多12项认证、最多min(Starts,12)项新增精化，每项主迭代≤min(N,ceil(sqrt(N)))；额外约束/恢复工作按既有子运行字段另计。父评价/主迭代汇总子运行，异质光谱工作分开。完整Search不写这两个新字段，旧字段为空不补造；schema及输入指纹保持。见[当前生成流程](COATING_GENERATION_CERTIFICATION_2026-10-31.md)。';
if(!s.includes('2026-10-31 生成认证记录：'))s=s.replace('内置镀膜模块使用 `.coating.json` schema 1',fields+'\n\n内置镀膜模块使用 `.coating.json` schema 1');writeFileSync(formats,s);
writeFileSync(`${root}/README.md`,`# 镀膜生成认证证据（2026-10-31）\n\n本目录保存本轮基线、最终生成、独立复算与未采用试验；产品结论见[报告](../../../../docs/COATING_GENERATION_CERTIFICATION_2026-10-31.md)。Generate 0/6→2/6，旧完整Search已能通过同两组；98%真实高反无新增通过。\n\n- paired-generation.json：6组逐量、原门槛与预算守卫。reference-inputs为实际相同热起点，inputs保留61个原始输入。\n- baseline-generate-all / final-generate-all及各自-cold：最终两臂。final-generate-primary/a/b保存实际调用时长和工作量；不是等成本比较。\n- baseline-search / baseline-cross：旧完整Search六组控制。verification.json核对最终Search路径保持、源身份、测试实际发现数及独立物理检查。\n- reference-results.json / reference-native：固定参考18组原始输出与本地原门槛复验；点平方目标不同，不是等目标竞赛。\n- hr-cold：19组旧HR最终膜系冷复算；curvature.json / curvature-reference-comparison.json及curvature-trial-final仅为离线诊断，未进入产品。\n- family / feedback / allocation / certify / budgeted等目录是中间试探，未完成的已在停止JSON中记录，不能计作最终验证。rejected-restoration-source保留未采用源码。旧边界差分诊断明确以before-boundary标记，最终使用修正后的证据。\n- tests保存最终构建日志、共享输出依赖覆盖失败、仅2项旧程序集和最终6项运行；首次测试断言写法导致的编译失败另记入execution-ledger.json，按实际身份分别计数。final-source及final-tests为本轮最终源码快照；final-manifest.json只封存本目录，运行时目录忽略但二进制摘要另存。\n\n未重写此前非线性约束修正证据，未全仓测试、操作原生桌面、提交或推送。\n`);
