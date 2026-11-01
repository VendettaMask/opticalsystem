import {readFileSync,writeFileSync} from 'node:fs';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const s=read(`${root}/paired-results.json`),r=read(`${root}/reference-comparison.json`);
const test=readFileSync(`${root}/final-tests/coating-tradeoffs-final.trx`,'utf8').match(/<Counters\b[^>]*>/)?.[0];
if(!test)throw Error('Final tests not complete');
const c=Object.fromEntries([...test.matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],Number(m[2])]));
if(!s.integrityPassed||s.guardFailures.length||!s.fiveRepaired||c.failed||c.passed!==c.total)throw Error('Verification not green');
const find=n=>s.results.find(p=>p.name===n),f=(v,n=8)=>Number(v).toPrecision(n),pct=v=>(100*v).toFixed(6),width=v=>v.toFixed(6);
const names=s.regressionNames,reg=names.map(find),restored=reg.filter(p=>p.current.loss===p.legacy.loss).length;
const rows=reg.map(p=>`| ${p.name} | ${f(p.legacy.loss)} | ${f(p.previous.loss)} | ${f(p.current.loss)} | ${p.current.loss<p.legacy.loss?'进一步改善':'恢复旧水平'} |`);
const metrics=reg.map(p=>`| ${p.name} | ${pct(p.legacy.metrics.PeakTransmittance)} → ${pct(p.current.metrics.PeakTransmittance)} | ${width(Math.min(...p.legacy.od))} → ${width(Math.min(...p.current.od))} | ${width(p.legacy.metrics.FwhmNanometers)} → ${width(p.current.metrics.FwhmNanometers)} |`);
const hr=['continued-hr-96','real-hr-96','real-hr-original-target','holdout-real-hr-red','holdout-real-hr-nearir'].map(find);
const hrRows=hr.map(p=>`| ${p.name} | ${pct(p.legacy.metrics.MinimumReflectance)} | ${pct(p.current.metrics.MinimumReflectance)} | 98% |`);
const odRows=['continued-single-24','real-nbf2-96'].map(find).map(p=>`| ${p.name} | ${pct(p.current.metrics.PeakTransmittance)} | ${p.current.od.map(v=>width(v)).join(' / ')} | ${width(p.current.metrics.FwhmNanometers)} |`);
const referenceRows=['real-nbf1-96','real-nbf2-96'].flatMap(name=>{
 const p=find(name); return [{name:`${name} / 本轮`,metrics:p.current.metrics,od:p.current.od},...r.results.filter(x=>x.source===name)];
}).map(x=>`| ${x.name} | ${pct(x.metrics.PeakTransmittance)} | ${width(Math.min(...x.od))} | ${width(x.metrics.FwhmNanometers)} |`);
const resources=[...reg,find('continued-single-24'),find('real-hr-96')].map(p=>{
 const w=p.current.run.GeneralWork;
 return `| ${p.name} | ${p.current.run.Iterations} | ${w.TargetComparisonIterations??0} | ${w.TargetRepairIterations??0} | ${w.InequalitySubproblems??0} | ${w.DenseValidations} | ${p.legacy.seconds.toFixed(3)} / ${p.previous.seconds.toFixed(3)} / ${p.current.seconds.toFixed(3)} |`;
});
const max=s.comparison.maximumErrors;
const lines=[
'# 镀膜目标权衡、截止极值与高反数值步修复（2026-10-31）','',
`五组相对旧版的退步均已消除：${5-restored}组进一步改善，${restored}组恢复旧水平。${s.pairs}组、三版本${s.completedRuns}次复算完成；相对v1为${s.betterThanLegacy}组改善、${s.worseThanLegacy}组退步、${s.sameAsLegacy}组相同，相对v2为${s.betterThanPrevious}组改善、${s.worseThanPrevious}组退步、${s.sameAsPrevious}组相同。原${s.passed['legacy-v1']}组合格保持${s.passed.after}组；新增完整达标${s.passed.after-s.passed['legacy-v1']}，相对输入的逐项目标超限退步${s.inputTargetRegressions}。`,
'',
'真实 n/k、原 `Checks` / `Converged`、膜厚界、固定层、结构及默认协议保持。完整实验、参考结果、拒绝候选、工作计数和哈希见[冻结证据](../validation/rechecks/2026-10-31/coating-tradeoff-repair/README.md)，原版本2的五组代价保留[历史身份](COATING_TARGET_BALANCING_2026-10-31.md)。本轮没有按材料名、材料ID、固定层号或样例签名分支。','',
'## 查明的问题与通用修正','',
'1. 单一目标尺度不能覆盖所有峰值T、宽度和OD的权衡。DLS未完成幅度目标的路径现在比较原尺度与相对超限两种通用优化路径，后者保留其原停滞恢复策略。先执行原密集验收与起点逐项保护，再选基线。校正候选的每项超限不得高于这条基线；已完成项仍使用原门槛，允许合法裕量变化。',
'2. 中间候选的总分略低，曾导致已经完成OD的另一候选被丢弃。现在在逐项上界内优先保留新完成的当前目标，再重设下一项目标的保护起点。目标选择依据原归一化超限，不依赖膜层身份。',
'3. 截止带内部透射极大值会落在引导采样点之间。probe 15中，引导最差OD已到1，正式密集结果仍约0.999971；原验收正确拒绝了它。最终对通带及每个截止带均调用共享Core的有界实际峰值细化，在每次校正更新极值锚点。它只是局部采样补强，不是全谱极值证明；正式密集验收仍独立执行。',
'4. 新共享不等式步同时处理所有线性化约束及物理步长/厚度界。对只出现在约束中的零目标雅可比列也施加阻尼；活动集用白化法线的转置薄SVD，一次分解同时求投影和乘子，避免形成法线Gram矩阵及对宽矩阵零空间的重复分解。近相关约束、缩放、冗余、阻尼和取消均有独立数学回归。',
'',
'引导中保留粗细采样差的内向余量、峰值功率1e-5量级及中心原容差的十分之一余量；它们是搜索参数，并非误差上界或新验收门槛。停滞时可对所有自由变量的公共切空间列做正负实际光谱试探；最终候选仍须通过原密集逐项保护。没有复制薄膜公式、替换真实吸收或引入Python产品计算。','',
'当前组件为 `target-violation-envelope/3`、`pareto-target-correction/1`、`worst-reflectance/4`、共享 `active-set-inequality-step/1`。高反DLS未可行路径使用最差损失上界的不等式步；已可行高反/减反和其他优化器维持原质量流程。','',
'## 五组旧版退步','',
'下表M为原正式超限平方和，越低越好；v1为已同步的3b43bb7f，v2为上轮目标方向修正。五组相对v1的峰值、带宽、峰位及每个截止带超限均未增加。','',
'| 冻结输入 | M v1 | M v2 | M 本轮 | 结果 |','| --- | ---: | ---: | ---: | --- |',...rows,'',
'| 冻结输入 | 峰值T v1 → 本轮（%） | 最差截止OD v1 → 本轮 | FWHM v1 → 本轮（nm） |','| --- | ---: | ---: | ---: |',...metrics,'',
'`prior-double-96`的校正候选仍未同时保住旧版各项目标，因此明确返回旧版水平，不把内部候选或仅更低总分算作改善。该组原截止OD仍未完成。','',
'两项既有截止收益回归保持原严格断言：左右OD均≥1，M及宽度优于v1，不修改原门槛。','',
'| 起点 | 本轮峰值T（%） | 左 / 右OD | FWHM（nm） |','| --- | ---: | ---: | ---: |',...odRows,'',
'这些真实材料窄带仍须满足T≥75%、550±0.5 nm、FWHM15±1 nm和左右OD≥1；峰值和宽度未全部通过，不能因截止完成而宣布完整达标。','',
'## 真实材料高反及跨规格边界','',
'| 冻结起点 | v1最低R（%） | 本轮最低R（%） | 原门槛 |','| --- | ---: | ---: | ---: |',...hrRows,'',
`41份输入与上一阶段逐字节一致，覆盖405–2000 nm、单/双腔、0–45°、S/P/非偏振、固定层、吸收和低折射率差，以及24/48/96次主预算。${s.improvedFromInput}组相对自身输入改善。实际材料数据仍只有原SiO2/Ta2O5（Rodríguez-de Marcos 2016）；其他材料配对明确是合成控制，波长/角度变化不算新增实测材料认证。高反98%以及部分窄带规格仍开放；有限模板和局部优化不证明无解。`,'',
'## 计算量与重放','',
'本轮窄带增益使用了额外计算，不能称作相同总预算的算法优胜。主调用仍≤N；方向比较另≤N；随后校正另≤ceil(sqrt(N))，均单独记录。高反预处理与质量阶段仍合计≤N。每例诊断限时由2分钟改为5分钟，仅是资源时限，不改变任何物理门槛。','',
'| 起点 | 主迭代 | 附加方向迭代 | 校正次数 | 不等式子问题 | 密集调用 | v1 / v2 / 本轮秒 |','| --- | ---: | ---: | ---: | ---: | ---: | ---: |',...resources,'',
'耗时来自并行后台任务，负载不同，不是受控性能基准。光谱/灵敏度计数是指导查询，密集验收另计调用；不能相加冒充所有单波长求解总数。活动集限次不代表物理通过。新结果保存两方向实际膜厚/指标/停止原因、校正基线、每步膜厚/正式指标/是否保留，支持使用原快照保存重开与复算；旧文件缺字段不补造历史，输入指纹及schema 1保持。字段说明见[格式入口](FILE_FORMATS_AND_PLUGINS.md#镀膜实验文件)。','',
'## 固定参考与验证','',
`重新执行固定TFStudio提交8b942b3f956728a23cea2b35023c8b5d8de17428的原DLS/Newton：12份相同起点、24次实际运行，保持真实n/k、锁层、物理界及各自迭代上限。按最终Core和原门槛复验，${r.passed}份完整通过、${r.targetRegressions}份增加了某项输入超限。参考使用自身的平方点目标，本地使用实际带宽/峰位/最差OD及保护约束，两者不是同一优化问题，也不是同总计算量。`,
'',
'| 相同输入 / 路径 | 峰值T（%） | 最差截止OD | FWHM（nm） |','| --- | ---: | ---: | ---: |',...referenceRows,
'',
'参考源码对照了 `lsqEngine.js` 的尺度/阻尼、`boundedStep.js` 的锁层及物理边界，以及Newton路径对剩余曲率的处理；未把参考输出、拟合常数或不满足原目标的更深截止作为产品运行输入。[固定源与许可证](../third_party/coating-reference/README.md)及归档哈希保持，Node仅作离线参考。',
'',
`最终三臂使用字节相同的Core/Application，分别装载v1、v2、本轮Engine；本轮与完整镀膜测试的三份DLL哈希一致。${s.comparison.cases}组独立R/T/A对照最大绝对差：R ${max.R.toExponential(4)}、T ${max.T.toExponential(4)}、A ${max.A.toExponential(4)}，保持原2e-11阈值。Core薄膜、材料库、正式测量/导数及验收函数源代码未变。`,
'',
`最终完整镀膜范围 **${c.passed}/${c.total}通过**，零失败/跳过。包含7项共享数值步、5项冻结退步，以及原峰值/截止收益、固定层、三优化器、生成/搜索、保存重开/复算和1e-7尺度守卫；不累计重复试跑。没有全仓、原生桌面或额外材料实测认证。`,
'',
'早期独立替换优化轨迹、归一化和辅助残差试验未保住原OD收益；原失败TRX和候选保留。宽矩阵SVD的高反试跑触及5分钟限时，记录为未完成；最终薄SVD重新完成。新五组测试允许“恢复旧水平”，并增加旧版各项超限检查；两项原截止收益测试和原物理门槛没有放宽。并行相机/无焦中间代码曾造成构建中断，相关失败不计作通过；最终使用默认Release输出后台构建/测试，未操作用户桌面。',''];
writeFileSync('docs/COATING_TRADEOFF_REPAIR_2026-10-31.md',lines.join('\n'));
console.log(JSON.stringify({report:'docs/COATING_TRADEOFF_REPAIR_2026-10-31.md',tests:c.passed,restored,improved:5-restored}));
