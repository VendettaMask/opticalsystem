import {readFileSync,writeFileSync} from 'node:fs';
const root=process.argv[2], read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const p=read(`${root}/paired-results.json`),ref=read(`${root}/reference-comparison.json`);
const trx=readFileSync(`${root}/final-tests/coating-allocation-final.trx`,'utf8');
const counts=Object.fromEntries([...trx.match(/<Counters\s+([^>]+)\/>/)[1].matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],+m[2]]));
if(counts.failed||counts.notExecuted||counts.passed!==counts.total||p.guardFailures.length||!p.integrityPassed)throw Error('Evidence has not passed');
const row=name=>p.results.find(r=>r.name===name),f=(v,n=6)=>v==null?'—':v.toFixed(n),od=a=>a.od.length?f(Math.min(...a.od)):'—';
const nbf=['holdout-violet','continued-single-24','prior-double-96','real-nbf2-96','real-nbf1-96','continued-double-96','holdout-ir'];
const nbfRows=nbf.map(name=>{const r=row(name);return `| ${name} | ${f(r.before.metrics.PeakTransmittance*100)} → ${f(r.after.metrics.PeakTransmittance*100)} | ${od(r.before)} → ${od(r.after)} | ${f(r.before.metrics.FwhmNanometers)} → ${f(r.after.metrics.FwhmNanometers)} | ${r.after.passed?'通过':'未通过'} |`;}).join('\n');
const hr=['continued-hr-96','real-hr-96','holdout-real-hr-nearir','holdout-real-hr-red','fresh-real-hr-red','fresh-real-hr-nearir'];
const hrRows=hr.map(name=>{const r=row(name);return `| ${name} | ${f(100*r.before.metrics.MinimumReflectance)} | ${f(100*r.after.metrics.MinimumReflectance)} | ${r.after.passed?'通过':'未通过'} |`;}).join('\n');
const controlNames=['holdout-violet','continued-single-24','prior-double-96','real-nbf2-96','real-nbf1-96','continued-double-96','holdout-real-hr-nearir'];
const controlRows=controlNames.map(name=>{const r=row(name);return `| ${name} | ${f(r.before.loss,10)} | ${f(r.priorBudgetOnly.loss,10)} | ${f(r.budgetOnly.loss,10)} | ${f(r.after.loss,10)} |`;}).join('\n');
const workNames=['holdout-violet','continued-single-24','prior-double-96','continued-hr-96','holdout-real-hr-nearir'];
const workRows=workNames.map(name=>{const r=row(name),a=r.before.run.GeneralWork,b=r.after.run.GeneralWork;return `| ${name} | ${a.SpectrumEvaluations} → ${b.SpectrumEvaluations} | ${a.SensitivityEvaluations} → ${b.SensitivityEvaluations} | ${a.DenseValidations} → ${b.DenseValidations} | ${a.InequalitySubproblems??0} → ${b.InequalitySubproblems??0} |`;}).join('\n');
const violet=row('holdout-violet');
const violetRows=[['上一阶段','before'],['旧恢复3sqrt(N)','priorBudgetOnly'],['旧恢复N','budgetOnly'],['本轮','after']].map(([name,key])=>{
 const r=violet[key]; return `| ${name} | ${f(r.metrics.PeakTransmittance*100)} | ${od(r)} | ${f(r.metrics.FwhmNanometers)} | ${r.passed?'通过':'未通过'} |`;
}).join('\n');
const referenceRows=['holdout-violet','prior-double-96','holdout-real-hr-nearir'].flatMap(name=>{
 const local=row(name).after;
 return [{name:'本轮',metrics:local.metrics,od:local.od},...ref.results.filter(r=>r.source===name)].map(r=>
  `| ${name} / ${r.name.replace(name+'-','')} | ${f(100*r.metrics.MinimumReflectance)} | ${f(100*r.metrics.PeakTransmittance)} | ${od(r)} | ${f(r.metrics.FwhmNanometers)} |`);
}).join('\n');
const exactWorse=p.results.filter(r=>r.after.loss>r.before.loss).map(r=>`${r.name}：分数差+${(r.after.loss-r.before.loss).toExponential(4)}，峰值T差${(r.after.metrics.PeakTransmittance-r.before.metrics.PeakTransmittance).toExponential(4)}，宽度差${((r.after.metrics.FwhmNanometers??0)-(r.before.metrics.FwhmNanometers??0)).toExponential(4)}nm`).join('；')||'无';
const text=`# 镀膜进展续算、紫光采样与高反度量（2026-10-31）

紫光项相对原3sqrt(N)旧额度控制的退步已消除。本轮统一冷复算下，47组相对上一阶段${p.vsBefore.better}组分数改善（含末位变化）、${p.vsBefore.worse}组分数末位更差、${p.vsBefore.same}组相同；通过数${p.passed.before}→${p.passed.after}。精确差异保留在下文，没有丢失原通过检查。原输入、真实n/k、厚度界、固定层和Checks/Converged验收阈值保持。完整输入、失败试探和复算见[证据](../${root}/README.md)。

## 通用原因与修正

原流程在恢复仍有收益时，按固定窗口切换到硬约束LS。紫光旧恢复第14步仍同时改善峰值和宽度，仅扩大旧额度的控制在第17步优于上一阶段最终输出；第25步曾满足全部数值目标，但旧采样仍判不收敛。另一个交接缺口是刚完成一项目标时，新目标还没有两次恢复记录，导致已有正式收益也不能续算。

当前pareto-target-correction/3每个ceil(sqrt(N))窗口检查实际正式进展：有新保留点且分数明显下降，或同目标最近恢复误差至少减半时继续。交接新目标可以依靠实际保留收益，不伪造它的两次记录。续算冻结当前最好结果的逐项超限；恢复与随后的硬约束LS共享额外N次，LS最多ceil(sqrt(N))次且用剩余额度。原主调用≤N及额外方向比较≤N独立记录，不能称同总计算量。

旧正式采样未固定指标统计窗口的物理端点，浮点筛选还可能排除名义端点。网格加密时，窗口内最大反射率由移动的最后一个样本决定，产生假不收敛。新anchored-band-domain/1显式加入窗口两端并用闭区间取点；原Core带指标、Checks和Converged代码/阈值不变。采样证据中，旧四级最大R变化约2.93e-4、1.44e-4、7.17e-5、3.57e-5，最后仍超过原1e-5；固定端点后该项变化为0。峰值和宽度仍必须通过原收敛检查。历史膜系全部用最终采样冷复算，原指标和原判定另存，避免把采样修复计作优化收益。

试验曾把正式采样的额外精确峰样本也反馈到搜索，造成其他窄带候选权衡改变；该方案未采用。最终只固定物理统计窗口，保留原局部峰/半高加密路径。

首轮完整回归176/177通过，new-ir-double的宽度回退触发原断言。原因是正式采样提前收敛后，其Spectrum还被用作搜索的初始峰值括区，导致方向改变。最终search-peak-reference/1独立重算既有搜索峰值采样，只给两条方向提供初始峰值种子；所有候选仍按当前正式Evaluate验收。查询真实计入指导工作量，批次与版本另存。该通用解耦恢复既有宽度保护，未改原光学断言。失败矩阵/完整TRX保留在stage-1-matrix和stage-1-tests，最终新臂47组及完整套件重新执行。

高反主搜索的全程度量替换曾使fresh-real-hr-red跌出98%门槛，失败结果保留在probe-2。最终worst-reflectance/6保留原主搜索，仅在完整DLS密集复验仍未达标时，用实际光谱解析灵敏度构造正半定谱变化度量，加独立阻尼续算一个≤ceil(sqrt(Q))窗口，Q为主质量额度。它不是完整Newton Hessian，也不改变最差反射损失目标。新增工作以ReflectanceMetricIterations单列；正式最差反射率不改善或丢失已获收敛则拒绝，已通过结果跳过。

## 峰值、截止与带宽

以下上一阶段和本轮均为同一最终采样的冷复算；超出目标后的富余可变化，已完成项目仍必须通过原门槛。

| 起点 | 峰值T（%）上一阶段 → 本轮 | 最差OD 上一阶段 → 本轮 | FWHM（nm）上一阶段 → 本轮 | 本轮 |
| --- | ---: | ---: | ---: | --- |
${nbfRows}

紫光由原${f(violet.before.metrics.PeakTransmittance*100)}%升至${f(violet.after.metrics.PeakTransmittance*100)}%，带宽从${f(violet.before.metrics.FwhmNanometers)}nm降至${f(violet.after.metrics.FwhmNanometers)}nm；两侧截止仍通过OD≥1。真实材料550nm窄带仍要求T≥75%、550±0.5nm、FWHM15±1nm和两侧OD≥1，剩余未达标不改判。

| 高反起点 | 上一阶段最低R（%） | 本轮最低R（%） | 原98%门槛 |
| --- | ---: | ---: | --- |
${hrRows}

仍未达标的真实材料高反和窄带保留开放状态。局部限次、未找到下降及当前吸收分解均不证明全局无解。小于原收敛尺度的末位差异不作为稳健物理增益宣传。

## 额度控制与参考实现

本轮47份输入逐字节复用上一阶段：8减反、14高反、25窄带，覆盖不同波长、角度、偏振、腔数和固定层。实测来源仍只有原SiO2/Ta2O5 Rodríguez-de Marcos 2016快照；其他配对是明确标注的合成材料，不宣称新实测材料认证，也不把公开复用输入称作未见盲验。

保留两份额度控制：prior-budget-only为上一阶段旧恢复3ceil(sqrt(N))的原输出冷复算；本轮budget-only复原该旧Engine全部源码，仅把旧恢复上限改为N，其他源码、材料和目标不动。前者定位用户指出的旧退步，后者与本轮恢复+LS共享N的上限对照；实际查询、QP及密集复验数仍不同。与本轮budget-only相比，${p.vsBudgetOnly.better}组更好、${p.vsBudgetOnly.worse}组更差、${p.vsBudgetOnly.same}组相同；更差项：${p.vsBudgetOnly.worseCases.join('、')||'无'}。相对prior-budget-only为${p.vsPriorBudgetOnly.better}好/${p.vsPriorBudgetOnly.worse}差/${p.vsPriorBudgetOnly.same}同。

| 起点 | 上一阶段 | 旧恢复3sqrt(N)控制 | 旧恢复N控制 | 本轮 |
| --- | ---: | ---: | ---: | ---: |
${controlRows}

紫光完整指标统一冷复算如下，避免只比较分数掩盖峰值、截止和带宽的区别：

| 路径 | 峰值T（%） | 最差OD | FWHM（nm） | 原全部门槛 |
| --- | ---: | ---: | ---: | --- |
${violetRows}

更大的旧恢复N控制在紫光的峰值、OD与宽度富余上仍略优于本轮，两者均通过原门槛、目标超限分数均为0。因此这里只确认原3sqrt(N)控制的退步已修复，不声称完全支配多算旧方向。所有控制的完整指标保留。

表中窄带损失为原归一化超限平方和，高反为1−最低R，不跨规格比较分数。候选逐项保护以目标超限为准，不要求已达标富余永远单调增加。

精确分数较差项仍按原数值列出，不改列相同：${exactWorse}。历史未完成指标另按原Converged分辨率核对（T 1e-5、宽度/峰位min(0.02nm,各自目标容差/10)、OD 0.005），仅用于判断可分辨的历史回退；所有原已通过Checks仍须严格通过，输入逐项超限保护也保持严格。原目标门槛和通过判定没有放宽。

另有new-red-double的一个未达标截止OD减少约1.12e-11，分数同时略改善；该逐项末位变化也保存在exactPreviousTargetRegressions，未隐去。两组差异均远小于原收敛分辨率，不据此宣称物理进步或新增通过。

固定TFStudio 8b942b3原DLS/Newton/SQP重新运行${ref.runs}次，最终Core按原门槛复验${ref.passed}份通过、${ref.targetRegressions}份增加至少一个输入目标超限。其Newton使用真实Hessian和独立阻尼，SQP使用硬厚度箱界；参考点平方目标不同于本地实际FWHM/最差OD/极小极大目标，不能声称同问题或同总计算量排名。固定源归档和许可保持。

| 输入 / 路径 | 最低R（%） | 峰值T（%） | 最差OD | FWHM（nm） |
| --- | ---: | ---: | ---: | ---: |
${referenceRows}

## 验证身份与代价

| 起点 | 指导光谱查询 上阶段 → 本轮 | 灵敏度查询 上阶段 → 本轮 | 优化内密集调用 上阶段 → 本轮 | 不等式子问题 上阶段 → 本轮 |
| --- | ---: | ---: | ---: | ---: |
${workRows}

这些是原运行中保存的实际工作量，冷复算调用单列而未回填历史Run。达到目标后的原主反射率质量优化仍继续使用其剩余N额度；仅新增度量窗口跳过已通过结果。

${p.freshOptimizations}份新优化完成，另对上一阶段、本轮旧额度控制及本轮新输出统一冷复算；原始通过判定保留。输入逐项目标超限严格保持；与上一阶段的未完成量按原收敛分辨率核对，已通过Checks严格保持，所有精确末位差异另列。材料、目标、设置、层角色、固定层及厚度界检查通过。原光学断言保持，资源断言随明确的共享额度契约更新；新增紫光交接和假不收敛回归。默认测试输出完整镀膜${counts.passed}/${counts.total}通过，跳过0、失败0。

两条新优化臂使用字节相同的Core/Application，最终Engine与测试一致。并行非镀膜工作改变了整个Core二进制；因此另以最终测试的Core/Application/Engine逐点冷复算。本轮输出的光谱、指标和Checks与原新优化输出逐位相同，共享Core/Coatings和材料源未改。该范围不认证并行工作的其他功能。独立tmmcore ${p.rta.cases}点R/T/A对照最大差${Math.max(...Object.values(p.rta.maximumErrors)).toExponential(3)}，通过原2e-11门槛。

初始Debug矩阵在continued-hr-96触及诊断五分钟时限，两臂均记为未完成；保留前四份已完成膜系，用同一冻结Engine/Core/Application续跑，将离线时限延至20分钟，产品迭代额度和验收不变。恢复载入的四份原优化用时记null，不把读文件时间充当优化时间。新增测试曾因含数组record的引用相等断言失败，改为结构比较；旧聚焦测试6通过/1失败后由本任务主动中止以释放自己的testhost输出锁，未计通过。失败/取消信息见interrupted-runs.json及原TRX，最终完整测试另存。全程后台，无用户桌面操作、DeepSeek、提交或推送。
`;
writeFileSync('docs/COATING_PROGRESS_ALLOCATION_2026-10-31.md',text);
writeFileSync(`${root}/README.md`,`# 镀膜进展续算证据（2026-10-31）

本轮${p.cases}规格，${p.freshOptimizations}份新优化；所有比较以最终测试Core及anchored-band-domain/1冷复算。${counts.passed}/${counts.total}镀膜测试通过。完整结论、失败试探和剩余未达标项见[报告](../../../../docs/COATING_PROGRESS_ALLOCATION_2026-10-31.md)。

- inputs逐字节复用上一阶段47份；baseline-source为本轮开始时Engine，control-source只把上一阶段旧额度控制的恢复上限改为N。
- final-matrix/after-raw与budget-only-raw保存新优化原输出；after、budget-only、before、prior-budget-only保存统一冷复算。冷复算保留原Run工作量，另列原指标/判定及逐点一致性；不是重复优化。
- final-matrix/*-runtime本地冻结运行时被gitignore排除，SHA-256见integrity及最终manifest；evaluator-runtime与最终测试三DLL相同。报告披露优化Core与最终测试Core的非镀膜二进制变化，未隐瞒身份差异。
- probe-1保存旧采样数值目标完成但未收敛的紫光；probe-2保存全程高反度量替换的退步；其余probe仅诊断，不替代最终矩阵。
- sampling-trace-final保留原网格及固定端点的五级差异；原阈值不变。tests保存未完成/失败聚焦TRX，final-tests保存完整测试；interrupted-runs明确取消、测试修正与输出锁。
- paired-results记录逐项超限、固定层、bounds、预算、判定及回归守卫。reference-results为54次固定上游DLS/Newton/SQP；reference-native按原Core门槛复验，目标定义不同。

复跑（仓库根目录；纯C#产品，Node仅离线对照）：

~~~powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run ${root}/inputs <new-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck ${root}/inputs <raw-output> <cold-output>
node tools/diagnostics/CoatingAudit20261030/reference-continuation.mjs ${root}/reference-materials.json <reference-results>
node tools/diagnostics/CoatingAudit20261030/summarize-allocation.mjs ${root} .tmp/coating-edge-audit-20261030/package
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj --no-restore
~~~

quality-resume仅复用已有完整实验文件，重新收集指导/验收身份，不把旧优化计作新执行；冷复算和优化计数分别披露。最终manifest列出本范围文件哈希，不包含自身、忽略的运行时/构建目录及已复制到正式目录的final2-matrix工作副本。源与旧报告保持可追溯，不从验证输出向产品注入候选。
`);
writeFileSync(`${root}/verification-counts.json`,JSON.stringify(counts,null,2)+'\n');
console.log(JSON.stringify({tests:counts,report:'docs/COATING_PROGRESS_ALLOCATION_2026-10-31.md'}));
