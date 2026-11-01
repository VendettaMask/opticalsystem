import {readFileSync,writeFileSync} from 'node:fs';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const p=read(`${root}/paired-results.json`),ref=read(`${root}/reference-comparison.json`);
const trx=readFileSync(`${root}/final-tests/coating-margin-final.trx`,'utf8');
const counts=Object.fromEntries([...trx.match(/<Counters\s+([^>]+)\/>/)[1].matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],+m[2]]));
if(counts.passed!==counts.total||counts.failed||counts.notExecuted||p.guardFailures.length||!p.integrityPassed)throw Error('Verification is incomplete or failed');
const f=(v,n=6)=>v==null?'—':v.toFixed(n),row=name=>p.results.find(r=>r.name===name),od=r=>r.od.length?Math.min(...r.od):null;
const nbf=['holdout-violet','prior-double-96','real-nbf2-96','real-nbf1-96','new-ir-double','continued-single-24','holdout-ir','holdout-telecom'];
const nbfRows=nbf.map(name=>{const r=row(name);return `| ${name} | ${f(r.before.metrics.PeakTransmittance*100)} → ${f(r.after.metrics.PeakTransmittance*100)} | ${f(od(r.before))} → ${f(od(r.after))} | ${f(r.before.metrics.FwhmNanometers)} → ${f(r.after.metrics.FwhmNanometers)} | ${r.after.passed?'通过':'未通过'} |`;}).join('\n');
const uv=row('holdout-violet');
const uvRows=[['上一阶段','before'],['旧恢复N控制','budgetOnly'],['本轮','after']].map(([label,key])=>{const r=uv[key];return `| ${label} | ${f(100*r.metrics.PeakTransmittance)} | ${f(od(r))} | ${f(r.metrics.FwhmNanometers)} | ${r.passed?'通过':'未通过'} |`;}).join('\n');
const hr=['continued-hr-96','real-hr-96','holdout-real-hr-nearir','holdout-real-hr-red','fresh-real-hr-red','fresh-real-hr-nearir','new-real-hr-green','new-real-hr-ir-fixed'];
const hrRows=hr.map(name=>{const r=row(name);return `| ${name} | ${f(100*r.before.metrics.MinimumReflectance)} | ${f(100*r.after.metrics.MinimumReflectance)} | ${r.after.passed?'通过':'未通过'} | ${r.after.run.GeneralWork.ReflectanceMetricIterations??0} |`;}).join('\n');
const newRows=p.results.filter(r=>r.newSpecification).map(r=>`| ${r.name} | ${f(r.before.loss,9)} | ${f(r.budgetOnly.loss,9)} | ${f(r.after.loss,9)} | ${r.before.passed?'通过':'未通过'} → ${r.after.passed?'通过':'未通过'} |`).join('\n');
const workRows=['holdout-violet','real-nbf2-96','prior-double-96','continued-hr-96','real-hr-96','holdout-real-hr-nearir'].map(name=>{
 const r=row(name),a=r.before.run.GeneralWork,b=r.after.run.GeneralWork;
 return `| ${name} | ${a.SpectrumEvaluations} → ${b.SpectrumEvaluations} | ${a.SensitivityEvaluations} → ${b.SensitivityEvaluations} | ${a.DenseValidations} → ${b.DenseValidations} | ${b.TargetRepairIterations??0}+${b.TargetParetoPolishIterations??0}+${b.PassbandQualityIterations??0} | ${b.ReflectanceMetricIterations??0} |`;
}).join('\n');
const referenceRows=['holdout-violet','real-nbf2-96','holdout-real-hr-nearir','new-real-nbf-red'].flatMap(name=>{
 const r=row(name),values=[{name:'本轮',metrics:r.after.metrics,od:r.after.od,passed:r.after.passed},...ref.results.filter(x=>x.source===name)];
 return values.map(x=>`| ${name} / ${x.name.replace(name+'-','')} | ${f(100*x.metrics.MinimumReflectance)} | ${f(100*x.metrics.PeakTransmittance)} | ${f(od(x))} | ${r.kind==='HighReflector'?'—':f(x.metrics.FwhmNanometers)} | ${x.passed?'通过':'未通过'} |`);
}).join('\n');
const exactWorse=p.results.filter(r=>r.after.loss>r.before.loss).map(r=>`${r.name}：分数差+${(r.after.loss-r.before.loss).toExponential(5)}`).join('；')||'无';
const totalError=Math.max(...Object.values(p.rta.maximumErrors));
writeFileSync('docs/COATING_MARGIN_REFINEMENT_2026-10-31.md',`# 镀膜实际余量与状态连续优化（2026-10-31）

紫光相对更大N额度旧控制的峰值/截止/宽度差距已收回，并有正式物理余量收益。原真实n/k、物理层/固定变量、厚度界、原目标及Checks/Converged保持；没有材料名、ID、固定层号或样例签名分支。当前${p.cases}组统一冷复算，通过数${p.passed.before}→${p.passed.after}；完整回归${counts.passed}/${counts.total}通过。完整试探、原始判定和复算见[证据](../${root}/README.md)。

## 原因与通用修正

原紫光两条路径的主方向及恢复起点膜厚逐位相同，但恢复第一步已分化。上一阶段只把主方向的初始峰值与正式采样解耦，恢复起点、目标交接和LS仍从正式Spectrum获取峰值括区。新search-peak-reference/2覆盖这些阶段，实际Core查询单列；独立指导采样不提供验收。仅此修正已复现旧N控制的紫光结果，诊断保留在probe-1。

原窄带分数为目标超限平方和，全部达标后归零，且已完成的截止项没有质量下降方向。本轮passband-margin-quality/1从正式保护后的结果继续，分别以起点归一化的峰值损失1−T、实际宽度偏差、每个截止带透过泄漏构造残差。所有光学值、带指标灵敏度和不等式数值步来自共享Core。每个正式保留点必须同时满足实际T不降、距目标宽度的绝对误差不增、每个截止OD不降、原各项目标超限不增，并且不丢失已有收敛或通过状态。目标归零仍有物理改善方向；失败试探不推进下一点。它不保证任意膜系均存在共同改善方向。

窄带额度没有另增N：恢复、硬约束LS与余量质量阶段合计≤N，LS和余量阶段各≤ceil(sqrt(N))且只能使用前面剩余额度。主调用≤N及额外方向比较≤N仍独立记录。相同的是这些非线性轮次的上限，光谱/灵敏度查询和实际运行成本仍不同，不声称相同总计算量。

高反worst-reflectance/7保留原主路径及首个光谱度量窗口。在主结果未通过时，跨窗口保留阻尼与指导网格；只要正式最差反射损失收益超过原R收敛分辨率1e-5，允许下一窗口。任何严格正式改善均可保留，1e-5只决定继续分配预算，不放宽验收。主预处理+主质量合计≤N；额外度量累计≤Q、每窗≤ceil(sqrt(Q))，Q为主质量额度，按实际工作另计。此前只有一窗，本轮允许更多计算，代价必须与收益同时披露。原主结果已经通过时跳过此附加阶段，原主质量流程仍按自己的预算改善反射率。

TFStudio固定Newton实现保留独立阻尼状态，实际使用完整Hessian；本轮借鉴状态连续性，仍使用Core解析灵敏度的正半定光谱度量，不宣称已实现同一完整Newton方法。归档源码未改。

## 紫光及窄带权衡

| 紫光路径 | 峰值T（%） | 最差截止OD | FWHM（nm） | 原全部门槛 |
| --- | ---: | ---: | ---: | --- |
${uvRows}

紫光的旧N控制与本轮目标超限分数都为0，但物理余量并不相同。本轮峰值、截止和宽度共同改善，故不再以零分数掩盖差距。

| 起点 | 峰值T（%）上一阶段 → 本轮 | 最差OD 上一阶段 → 本轮 | FWHM（nm）上一阶段 → 本轮 | 本轮 |
| --- | ---: | ---: | ---: | --- |
${nbfRows}

真实550nm窄带仍要求T≥75%、550±0.5nm、FWHM15±1nm及两侧OD≥1，未满足全部项目的结果不改判。宽度列是实际FWHM；原目标两侧都适用，内部保护的是距目标的绝对误差。余量阶段每一个接受点的实际边界、OD数组和正式超限随实验保存，可重新打开复算。

## 真实材料高反及新增规格

| 高反起点 | 上阶段最低R（%） | 本轮最低R（%） | 原98%门槛 | 本轮额外度量轮次 |
| --- | ---: | ---: | --- | ---: |
${hrRows}

真实材料高反和窄带仍有未达标项；限次、局部停滞及当前膜系的吸收分解均不证明全局无解。本轮没有替换n/k或降低原门槛。

原47份输入逐字节保持；新增8份在查看其优化输出前冻结，包含4份真实材料窄带、2份真实材料高反、2份明确标注的合成材料控制，以及不同波长、角度、偏振和固定变量。初始厚度由共享Core特征相位单位映射，材料快照完全不动；新的目标、厚度界和固定变量逐文件声明并在三臂一致。实测材料来源仍只有原SiO2/Ta2O5 Rodríguez-de Marcos 2016快照，不宣称获得新的实测材料认证。

| 新规格 | 上阶段分数/损失 | 旧恢复N控制 | 本轮 | 上阶段 → 本轮 |
| --- | ---: | ---: | ---: | --- |
${newRows}

## 对照、分数与参考方法

旧N控制源码逐字节复用前阶段完整旧Engine（其恢复额度已为N），没有把本轮质量方法混入控制。原47组的两个对照复用其冻结原始优化输出；新增8组在同一冻结Core下分别新跑两个对照，本轮新方法55组全部新跑。本轮新增正式优化${p.freshOptimizations}次，复用历史优化${p.historicalOptimizationsReused}份；全部三臂共${p.coldStacks}份结果统一用最终测试Core及同一正式采样冷复算，原通过状态、膜厚和计数保留，冷复算不算重复优化。

相对上一阶段：${p.vsBefore.better}组目标分数/反射损失更小、${p.vsBefore.worse}组更大、${p.vsBefore.same}组相同；较差项：${exactWorse}。相对旧N控制：${p.vsBudgetOnly.better}好/${p.vsBudgetOnly.worse}差/${p.vsBudgetOnly.same}同；较差项：${p.vsBudgetOnly.worseCases.join('、')||'无'}。分数只在同规格内比较；窄带为原超限平方和，高反为1−最低R。

达标后的余量不由该分数度量。严格逐项实际余量优于上一阶段且至少一个量超出原收敛分辨率的窄带共有${p.physicallyDominantNarrowBands.length}组：${p.physicallyDominantNarrowBands.join('、')||'无'}。所有原始差异、实际OD变化及精确目标超限回退均保存在paired-results；历史未完成量另按原收敛分辨率核对，原已通过Checks仍严格保持。没有把末位差异改列成通过或隐藏原数值。

固定DLS/Newton/SQP共${ref.runs}次，最终Core原门槛复验${ref.passed}份通过、${ref.targetRegressions}份增加至少一项输入超限。参考使用点平方目标，本地使用实际峰值/FWHM/最差OD/极小极大目标；物理输入、n/k、固定层及厚度界一致，目标定义和实际工作量不同，不作同问题或同总预算排名。

| 输入 / 路径 | 最低R（%） | 峰值T（%） | 最差OD | FWHM（nm） | 原门槛 |
| --- | ---: | ---: | ---: | ---: | --- |
${referenceRows}

## 计算量与验证身份

| 起点 | 指导光谱查询 上阶段 → 本轮 | 灵敏度查询 上阶段 → 本轮 | 优化内密集调用 上阶段 → 本轮 | 本轮恢复+LS+余量轮次 | 本轮额外高反轮次 |
| --- | ---: | ---: | ---: | --- | ---: |
${workRows}

查询为公开Core调用的实际计数，完整密集复验另计调用数；不能把向量评价或轮次称作所有单波长内核求解总数。原对照中恢复载入的四份优化用时为null，不用读文件时间代替优化用时。额外高反窗口可能增加大量回溯查询；当前输入已达标的余量不能挪用于违反原门槛。

完整镀膜回归${counts.passed}/${counts.total}，失败/跳过0；新增真实双腔、旧N控制物理余量、达标后继续及保存复算检查。原光学断言保持，资源断言随明确的共享额度及状态窗口契约更新。独立tmmcore ${p.rta.cases}点R/T/A最大差${totalError.toExponential(4)}，通过原2e-11阈值。Core/Coatings、材料数据和原Checks/Converged源段未改；三臂优化Core/Application字节相同，最终Engine与测试相同，另以最终测试三DLL冷复算，本轮光谱、指标和Checks与原新输出逐位一致。

默认构建初次被并行SpeosSpectralBsdf/MeasuredBsdf.Data接口修改阻塞，先以冻结共享Core定向验证；随后默认测试依赖图重建成功，完整回归运行冻结后的默认输出。失败构建不记为通过，记录保留在tests。全程后台，无用户桌面操作、DeepSeek、提交或推送。
`);
writeFileSync(`${root}/README.md`,`# 镀膜物理余量与连续状态验证（2026-10-31）

${p.cases}规格、三臂同一最终采样冷复算；本轮新优化${p.freshOptimizations}次，历史控制复用${p.historicalOptimizationsReused}份。完整镀膜${counts.passed}/${counts.total}通过。结论、代价和剩余未达标项见[报告](../../../../docs/COATING_MARGIN_REFINEMENT_2026-10-31.md)。

- inputs含原47份字节不变的输入和8份预冻结新规格；input-provenance核对n/k快照完全不变，初始相位/新目标/固定变量/匹配厚度界明确声明。
- baseline-source和baseline-runtime冻结本轮前Engine；control-source完全复用前阶段旧N控制；final-source保存本轮全部Engine源码。*-runtime和bin/obj忽略，SHA-256留在integrity/manifest。
- final-matrix/*-raw保留原优化输出，三个非raw目录统一冷复算，原Run工作量、原通过状态另存。before/budget-only各47份历史加8份新跑；after全部55份新跑。不能把历史复算称作新优化。
- probe-1定位恢复峰值锚点耦合并恢复旧N控制轨迹；probe-2记录余量目标；probe-3记录高反状态窗口。最终矩阵替代探针作为结论。
- PassbandQualityTrials逐点保存实际T、宽度误差、每带OD和原超限边界；ReflectanceMetricWindows保存实际损失、接受/收敛、起止阻尼与额度。拒绝点不推进正式保留点。
- paired-results保存所有精确分数和逐项差异，包含仍未达标或末位较差项。reference-*为26规格×3固定参考方法，点目标与本地带指标目标不同。
- tests保存初次非镀膜编译阻塞及3项聚焦回归；final-tests是冻结默认输出的完整TRX。不得把未完成构建或探针冒充完整通过。

仓库根目录复跑：

~~~powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run ${root}/inputs <new-raw-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck ${root}/inputs <raw-output> <cold-output>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-physics <cold-output> <physics.json>
node tools/diagnostics/CoatingAudit20261030/reference-continuation.mjs ${root}/reference-materials.json <reference-results>
node tools/diagnostics/CoatingAudit20261030/summarize-margin.mjs ${root} .tmp/coating-edge-audit-20261030/package
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj --no-restore
~~~

产品仍是共享Core的纯C#/.NET；Node仅离线参考/取证。最终manifest排除自身与忽略的运行时/构建目录，其余范围文件逐项SHA-256，不向产品运行路径注入验证候选。
`);
writeFileSync(`${root}/verification-counts.json`,JSON.stringify(counts,null,2)+'\n');
console.log(JSON.stringify({report:'docs/COATING_MARGIN_REFINEMENT_2026-10-31.md',tests:counts.passed,cases:p.cases,rawScoreWorse:p.vsBefore.worse}));
