import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex'),norm=s=>s.replace(/\r\n/g,'\n');
const p=read(`${root}/paired-results.json`),integrity=read(`${root}/integrity.json`),rta=read(`${root}/tmmcore-comparison.json`),replay=read(`${root}/full-replay-equivalence.json`);
const xml=readFileSync(`${root}/tests/portfolio-final-focused.trx`,'utf8'),attrs=s=>Object.fromEntries([...s.matchAll(/([\w]+)="([^"]*)"/g)].map(m=>[m[1],m[2]]));
const counters=attrs(xml.match(/<Counters\b([^>]*)/)[1]);
const tests={scope:'Final focused regressions, not a full module or repository run',total:+counters.total,passed:+counters.passed,failed:+counters.failed,notExecuted:+counters.notExecuted,trx:'tests/portfolio-final-focused.trx',
 initialRun:{total:50,passed:48,failed:2,reason:'New HR tests incorrectly called the NBF-only preservation helper (peak T and non-null stop bands); only the new tests were corrected. Original acceptance gates and original tests remain unchanged.'},
 intermediateRecheck:{total:5,passed:5,trx:'tests/nonlinear-regression-recheck.trx'}};
const reference=[]; const summaries=read(`${root}/after-cold/summary.json`);
const deficits=(t,m,od)=>t.Kind==='HighReflector'?[Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)]:[Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,...od.map(v=>Math.max(0,t.BlockingOd-v)/Math.max(1,t.BlockingOd))];
for(const file of readdirSync(`${root}/reference-native`).filter(f=>f.endsWith('.coating.json'))){
 const d=read(`${root}/reference-native/${file}`),record=read(`${root}/reference-results.json`).find(x=>x.Name===file.replace('.coating.json','')),input=summaries.find(x=>x.Name===record.Source);
 const od=(d.Target.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 const a=deficits(d.Target,input.BeforeMetrics,input.BeforeBlockingOd),b=deficits(d.Target,d.Result.Metrics,od);
 reference.push({name:record.Name,source:record.Source,passed:d.Result.SamplingConverged&&d.Result.Checks.every(x=>x.Passed),metrics:d.Result.Metrics,od,inputViolations:a,violations:b,targetsPreserved:b.every((v,i)=>v<=a[i]),iterations:record.Iterations});
}
const referenceResult={runs:reference.length,passed:reference.filter(x=>x.passed).length,targetRegressions:reference.filter(x=>!x.targetsPreserved).length,
 scope:'Unmodified pinned TFStudio DLS/Newton/SQP; point-squared objective differs from native measured-band and minimax targets. 78 historical runs reused, 18 new runs; all 96 rechecked with final optical Core.',results:reference};
const solverPath='src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs',old=norm(execFileSync('git',['show',`3b43bb7f:${solverPath}`],{encoding:'utf8'})),now=norm(readFileSync(solverPath,'utf8')),marker='public static double QuarterWaveThickness';
integrity.coherentOpticalAlgebraUnchanged=old.slice(old.indexOf(marker))===now.slice(now.indexOf(marker));
const tmmHash=createHash('sha256').update(execFileSync('tar',['-xOf','third_party/coating-reference/tmmcore-0.4.2.tgz','package/src/tmm.js'])).digest('hex');
integrity.tmmSources=['.tmp/coating-edge-audit-20261030/package/src/tmm.js','.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/node_modules/tmmcore/src/tmm.js'].map(path=>({path,unchanged:sha(path)===tmmHash}));
integrity.fullReplayEquivalence=replay.length===6&&replay.every(x=>Object.entries(x).every(([k,v])=>k==='name'||v===true));
integrity.passed&&=integrity.coherentOpticalAlgebraUnchanged&&integrity.tmmSources.every(x=>x.unchanged)&&integrity.fullReplayEquivalence;
const physical=p.results.filter(r=>r.kind==='NarrowBand'&&r.before.metrics.FwhmNanometers!==null&&r.guard.previousRawNarrow&&(
 r.after.metrics.PeakTransmittance-r.before.metrics.PeakTransmittance>1e-5||Math.abs(r.before.metrics.FwhmNanometers-r.target.FwhmNm)-Math.abs(r.after.metrics.FwhmNanometers-r.target.FwhmNm)>Math.min(.02,r.target.FwhmToleranceNm/10)||r.after.od.some((x,i)=>x-r.before.od[i]>.005))).map(x=>x.name);
p.physicalNarrowBandImprovements=physical;p.rta={cases:rta.cases,maximumErrors:rta.maximumErrors};p.integrityPassed=integrity.passed;
const ledger=read(`${root}/execution-ledger.json`);ledger.finalFullOptimizationReplays=6;ledger.coldStacks=183;ledger.coldEvaluationCalls=366;
if(!integrity.passed||p.guardFailures.length||tests.failed||tests.notExecuted||tests.total!==tests.passed||rta.cases!==7998||Object.values(rta.maximumErrors).some(x=>!Number.isFinite(x)||x>2e-11))throw Error('Incomplete final verification');
for(const [name,data] of [['integrity',integrity],['paired-results',p],['reference-comparison',referenceResult],['verification-counts',tests],['execution-ledger',ledger]])writeFileSync(`${root}/${name}.json`,JSON.stringify(data,null,2)+'\n');
const pct=x=>(100*x).toFixed(6),f=x=>x.toFixed(6),find=n=>p.results.find(x=>x.name===n);
const hr=['real-hr-96','continued-hr-96','holdout-real-hr-red','holdout-real-hr-nearir','new-real-hr-green','correction-real-hr-nearir-fixed'];
const nb=['real-nbf2-96','real-nbf1-96','prior-double-96','correction-real-nbf-red','holdout-violet'];
const absorption=['real-hr-96','holdout-real-hr-red','real-nbf2-96'].map(name=>{
 const d=read(`${root}/after-cold/${name}.coating.json`),point=d.Target.Kind==='HighReflector'?d.Result.Spectrum.reduce((a,b)=>a.Power.Reflectance<b.Power.Reflectance?a:b):d.Result.Spectrum.find(s=>s.WavelengthNanometers===d.Result.Metrics.PeakWavelengthNanometers);
 return{name,wavelength:point.WavelengthNanometers,power:point.Power};
});
const report=`# 真实材料高反与窄带：非线性约束修正及方向比较（2026-10-31）

## 结果与边界

61项（原55项逐字节保留，加输出前冻结的6项）按原门槛复算：旧版、增加一个旧余量窗口的控制、最终版均为**29/61达标**，新增整组达标**0**。按原目标超限分数（窄带）或最差反射损失（AR/HR），最终版相对两控制均为**29改善、0退步、32持平**。窄带还逐项保住了旧版及该窗口控制的实际峰值T、每个截止OD、距目标带宽的绝对误差；中心保护的是原超限，并非原始中心偏移余量。

真实材料高反继续向98%推进，但剩余项未关闭；550 nm双腔仍未同时达到75%峰值和15±1 nm带宽。没有改n/k、原始验收、固定层、厚度界、默认协议或参考实现。下面表格保留未达标事实，不把优化方向改善等同于达标。

## 通用原因与修正

1. 线性约束步在曲面边界附近反复回溯。共享Core新增\`trial-constraint-correction/1\`，在目标已下降的试探点最多做两次有界修正，用第一次修正的实测非线性余项给第二次提供向内缓冲。高反在原额外Q窗口内比较回溯可接受步及其前一拒绝步的修正；只取实际最差损失更小者。它不是完整光学Hessian，也不证明全局最优。
2. 新冻结730 nm双腔暴露了跨迭代的权衡：初版修正T和部分OD更好，却比旧窗口控制的宽度进展差；仅改为优先拟合未完成目标也未消除它。两轮失败源码、输出及\`pre-target-priority-paired.json\`保留。最终版先保留原质量窗口，再从同一正式点分别运行旧方向和修正方向；只有修正方向在实际T、每个OD、宽度误差、原超限及已有采样/达标状态上支配旧方向结果，才替换它。否则保留旧方向，再在剩余额度内共同抛光。分支按物理指标比较，未引入材料名、层号或例子签名分支。
3. 共享薄膜求解器增加\`WithThicknesses\`，在私有材料快照内复用精确波长/角度对应的n/q准备工作，按复数元素总数有界。厚度、R/T/A及导数仍重新计算；所有光学代数、导数及材料数据保持。独立新求解器与复用求解器在真实材料、多厚度/波长/角度/偏振下逐值一致。

协议为\`worst-reflectance/8\`、\`passband-margin-quality/2\`；设计协议仍为4，原Checks、Converged、Core带指标及\`anchored-band-domain/1\`均保持。正式保留仍逐值执行原物理边界。指导比较容纳sqrt(machine epsilon)量级的归一化舍入误差，只用于让候选进入正式复验，不放宽正式判据。

## 真实材料高反

均保留原98%门槛，列目标波段内最小R：

| 规格 | 旧版Rmin/% | 最终Rmin/% | 最终达标 |
| --- | ---: | ---: | --- |
${hr.map(n=>{const r=find(n);return`| ${n} | ${pct(r.before.metrics.MinimumReflectance)} | ${pct(r.after.metrics.MinimumReflectance)} | ${r.after.passed?'是':'否'} |`;}).join('\n')}

最后一项为预冻结的新固定层近红外规格，旧版已经达标，不能计作新增98%达标。其余表中未通过项继续保留失败。

## 窄带的共同改善

以下T、FWHM和最差截止OD均为同一正式采样下的原定义；各截止带分别受保护，表中仅为便于阅读列其最小值。

| 规格 | 峰值T/% 旧→新 | FWHM/nm 旧→新 | 最差OD 旧→新 |
| --- | --- | --- | --- |
${nb.map(n=>{const r=find(n);return`| ${n} | ${pct(r.before.metrics.PeakTransmittance)} → ${pct(r.after.metrics.PeakTransmittance)} | ${f(r.before.metrics.FwhmNanometers)} → ${f(r.after.metrics.FwhmNanometers)} | ${f(Math.min(...r.before.od))} → ${f(Math.min(...r.after.od))} |`;}).join('\n')}

紫光为明确标注的合成n/k比较，不冒充实测材料。真实膜材料来自原SiO₂/Ta₂O₅实测表；跨材料覆盖还包括原先明确标注的多组合成n/k，未新增另一套实测材料。${physical.length}项窄带在不退步的同时，至少一项改善超过原收敛分辨率。小于原T=1e−5、带宽min(0.02 nm,容差/10)、OD=0.005分辨率的增量只记数值方向，不宣称显著改善；这些分辨率没有被改成新的验收容差。

原双腔仍仅T=${pct(find('real-nbf2-96').after.metrics.PeakTransmittance)}%，FWHM=${f(find('real-nbf2-96').after.metrics.FwhmNanometers)} nm，对75%、15±1 nm要求仍失败。730 nm项的最终值也逐项不差于旧窗口控制，但宽度仍未达标。完整各带值及控制差异见\`paired-results.json\`。

## 残差分解与未关闭项

当前局部解的最差R点/通带峰点：

| 规格 | 波长/nm | R/% | T/% | 有限膜层A/% |
| --- | ---: | ---: | ---: | ---: |
${absorption.map(x=>`| ${x.name} | ${f(x.wavelength)} | ${pct(x.power.Reflectance)} | ${pct(x.power.Transmittance)} | ${pct(x.power.Absorptance)} |`).join('\n')}

这些点的高反缺口与低透射主要受当前膜堆的有限膜层吸收影响；真实k原样保留。此分解不能证明在相同材料/层数/界内98%或75%全局不可达。剩余局部停滞、窄带峰值与带宽缺口及更广结构搜索仍是开放项，没有以参考实现同样失败替代原验收。

## 额度、成本与对照公平性

N仍为输入\`MaximumIterations\`。主调用≤N，原方向比较另≤N。恢复、LS及所有余量窗口的**实际迭代总计仍≤额外N**；LS≤ceil(sqrt(N))，余量最多四窗口（原质量、旧方向、修正方向、联合抛光），各≤ceil(sqrt(N))，实际合计≤min(剩余N,4ceil(sqrt(N)))。\`PassbandCorrectionIterations\`是余量迭代子集，最多两窗口。两个比较方向都计费，即使未选用。高反额外度量仍累计≤Q，未新增Q额度。修正内部子问题、导数、指导光谱查询与密集复验另计，不能与窗口迭代重复相加。

窗口控制为原质量结果后再运行一个同额度、无修正的旧余量方向。最终版还要比较另一方向并可能抛光，**总成本增加，不是等总查询/等时间排名**。同起点同窗口的方向结果与额外成本保存在\`PassbandDirectionTrials\`及工作计数中；没有隐藏补算或用离线候选作为运行时答案。

61次初轮完整优化及其源码/结果保留；最终窄带更新以未变的前置阶段结果为起点，实际完成${ledger.finalPortfolioContinuations}次方向比较/抛光复算。6项跨材料/规格另完整调用最终Optimize，膜厚、光谱、指标、Checks、Run及候选历史全部逐值一致。不能把阶段复算、冷复验或历史复用称作新的完整优化。旧版55项历史优化复用、6项新旧优化；全部三臂183个膜堆、366次Evaluate使用同一固定光学二进制冷复算，原报告值与判定均保留。

## 参考实现与验证

- 未修改固定TFStudio源码，DLS/Newton/SQP共${referenceResult.runs}组（32规格），${referenceResult.passed}组按原门槛通过，${referenceResult.targetRegressions}组增加至少一项输入超限。78组历史优化原样复用，18组新运行，96组全部用当前光学Core重算。它的点平方目标与本机测得FWHM、最差OD、minimax目标不同，不能据胜率宣称等目标/等成本优劣。
- 独立未修改tmmcore 0.4.2：**${rta.cases}个R/T/A点通过原2e−11门槛**，最大误差R=${rta.maximumErrors.R}、T=${rta.maximumErrors.T}、A=${rta.maximumErrors.A}。包括各臂、实际n/k、S/P/非偏振、极值与指标窗口端点。
- 最终默认依赖图构建零警告、零错误；最终定向回归**${tests.passed}/${tests.total}**，无失败/跳过，含Core数值、真实材料工作区一致性、冻结旧控制、保存重开、资源上限和生成/搜索保护。不是全模块/全仓认证。首轮新增高反测试误用窄带断言的两项失败及测试分析器失败保留；纠正的只是本轮新增测试，未改原光学断言或门槛。
- ${p.guardFailures.length}项输入/物理/控制余量/额度/重算守卫失败；原55输入、全部材料快照、固定层、界与原门槛一致。三比较臂Core/Application一致，最终Engine与测试Engine一致。参考归档与实际使用源码指纹通过。

全程后台、纯C#产品计算；Node仅用于离线固定参考、归档与审计。未运行原生桌面验收、安装或发布。

## 证据与重放

[范围与入口](../${root}/README.md)、[逐项结果](../${root}/paired-results.json)、[完整重放一致性](../${root}/full-replay-equivalence.json)、[R/T/A](../${root}/tmmcore-comparison.json)、[参考](../${root}/reference-comparison.json)、[工作账目](../${root}/execution-ledger.json)、[指纹](../${root}/final-manifest.json)。输入、三臂冷复算及各分支实参均保留，可按归档命令重开复算。上阶段结果保留在[实际余量与状态续算](COATING_MARGIN_REFINEMENT_2026-10-31.md)。
`;
writeFileSync('docs/COATING_NONLINEAR_CORRECTION_2026-10-31.md',report.replaceAll('\\`','`'));
writeFileSync(`${root}/README.md`,`# 镀膜非线性约束修正验证（2026-10-31）\n\n最终结果、成本、限制与失败记录见[报告](../../../../docs/COATING_NONLINEAR_CORRECTION_2026-10-31.md)。61项三臂冷复算，29/61达标、无新增整组达标；29改善、0退步、32持平。最终25/25定向测试，7998个独立R/T/A点通过。\n\n- inputs：原55逐字节保持，加6预冻结新规格；before-raw含55历史输出和6新旧版完整优化。\n- group-results及initial-full-summary：初轮61次完整Optimize；portfolio-phase为最终窄带阶段复算。after-raw候选历史与完整Optimize相同，6项final-full-replay逐值确认。\n- control-phase、holdout-control-phase：旧质量点后一个同窗口无修正控制；总成本不与最终多方向/抛光相等。\n- before/control/after-cold：同一最终Core的183个冷复算膜堆，保留原报告与判定。所有最终窗口仍共享原额外N。\n- pre-target-priority-paired、rejected-target-priority-source及probe目录：未被采用的反例与探索；probe1中断、probe2/3失败均不算验证通过。\n- reference-results：78历史+18新固定参考，96份结果均当前Core复验；不是等目标/等成本排名。\n- tests：最终portfolio-final-focused.trx为25/25；首轮50项中两项新增HR测试误用NBF断言，修正后另有5/5及最终25/25。未执行本阶段完整模块测试。\n\n重放：先构建工具，再运行 quality-run、allocation-recheck、allocation-physics；nonlinear-phase仅用于明确标注的最终阶段隔离，不能称完整优化。固定源码的构建引用同一Core/Application，绝不换n/k或门槛。\n\n\`\`\`powershell\ndotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore\ndotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run ${root}/inputs <new-output-directory>\ndotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck ${root}/inputs <new-output-directory> <cold-output-directory>\n\`\`\`\n\n忽略的*-runtime目录是本地构建，不是输入来源；版本、源码、输入与测试/比较二进制SHA记录于final-manifest。\n`.replaceAll('\\`','`'));
const statusPath='docs/CURRENT_STATUS.md';let status=readFileSync(statusPath,'utf8');
status=status.replace('## 2026-10-31 镀膜实际余量与状态连续优化','## 上阶段：2026-10-31 镀膜实际余量与状态连续优化');
const heading='## 2026-10-31 镀膜非线性约束修正与原方向余量保护';
if(!status.includes(heading))status=status.replace('# 当前实现与验证状态',`# 当前实现与验证状态\n\n${heading}\n\n共享Core新增有界试探约束修正及同材料快照n/q复用；协议4高反在原额外Q窗口内改进回溯，窄带比较旧/修正方向并保护旧方向实际峰值、每个截止OD、带宽误差，再按原共享额外N抛光。无材料名、层号或例子签名特判，真实n/k、固定层、厚度界与Checks/Converged保持。\n\n61项（55不变+6预冻结）相对旧版及一个旧窗口控制均29改善、0退步、32持平，达标仍29/61、无新增整组达标。550 nm真实高反Rmin 96.294924%→96.334481%，520 nm新规格95.641461%→95.844151%；真实双腔T 66.379651%→66.399852%，FWHM 17.244582→17.241248 nm、各OD数值同向，仍未过98%高反和75%/15±1 nm双腔要求。细小增量不夸大为超过原采样分辨率。\n\n最终默认依赖图构建零警告/错误，定向25/25、7998个独立R/T/A点及6项完整/阶段重放逐值一致；不是完整模块或全仓认证。总成本增加，虽未扩大共享N/Q，也不称等成本控制胜出。详见[结果、代价及开放项](COATING_NONLINEAR_CORRECTION_2026-10-31.md)和[证据](../${root}/README.md)。下列阶段保留原范围。`);
writeFileSync(statusPath,status);
console.log(JSON.stringify({tests,reference:{runs:referenceResult.runs,passed:referenceResult.passed,targetRegressions:referenceResult.targetRegressions},physicalNarrowBandImprovements:physical.length,integrity:integrity.passed}));
