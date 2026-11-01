import {readFileSync,writeFileSync} from 'node:fs';
function edit(path,fn){const s=readFileSync(path,'utf8');writeFileSync(path,fn(s));}
function replace(s,a,b){if(!s.includes(a))throw Error('Missing guide anchor: '+a);return s.replace(a,b);}
edit('docs/COATING_DESIGN_LAB.md',s=>{
 s=s.replaceAll('worst-reflectance/7','worst-reflectance/8');
 s=replace(s,'LS及余量阶段各≤ceil(sqrt(N))且使用前面阶段剩余额度','LS≤ceil(sqrt(N))；余量阶段分为原质量窗口和非线性修正窗口，各≤ceil(sqrt(N))，合计≤2ceil(sqrt(N))且使用前面阶段剩余额度');
 s=s.replace('当前对照见[余量优化报告](COATING_MARGIN_REFINEMENT_2026-10-31.md)','当前对照见[非线性约束修正](COATING_NONLINEAR_CORRECTION_2026-10-31.md)；上阶段见[余量优化报告](COATING_MARGIN_REFINEMENT_2026-10-31.md)');
 s=replace(s,'完整协议4的DLS窄带优化另有`passband-margin-quality/1`，正式主结果与输入保护完成后，从未消耗的额外N额度中最多取ceil(sqrt(N))次。','完整协议4的DLS窄带优化使用`passband-margin-quality/2`。正式主结果与输入保护完成后，先保持原质量窗口，再从未消耗的额外N额度中取一个最多ceil(sqrt(N))次的非线性修正窗口；两窗口各≤ceil(sqrt(N))，与恢复及LS共享额外N，未扩大该共享上限。');
 const marker='共享 Core 的 `coherent-scattering/2` 已提供';
 s=replace(s,marker,'共享Core的`trial-constraint-correction/1`只生成试探点：在目标已有下降但非线性约束仍超界时，最多做两次带辅助上界的约束修正，以第一次修正的实测余项给第二次提供向内缓冲。高反额外度量阶段在首次可接受回溯步及前一拒绝步附近比较原试探与修正试探，始终保留更小的实际最差损失；窄带修正窗口每个阻尼尝试最多修正一次已有目标下降的试探。指导比较只容纳sqrt(machine epsilon)量级的归一化舍入误差，正式峰值、OD、带宽、超限和通过保护仍逐值严格执行。这不是完整光学Hessian或全局最优证明。\n\n共享薄膜求解器的`WithThicknesses`可在同一私有材料快照内复用n/q准备工作，缓存键为精确波长与入射角；厚度、反射透射计算和导数仍重新计算。缓存按复数元素总数有界，并保留材料/界面/调整参数；不能跨材料快照复用。相关API逐值一致性及R/T/A独立复验见本阶段报告。\n\n'+marker);
 return s;
});
edit('docs/FILE_FORMATS_AND_PLUGINS.md',s=>{
 s=s.replaceAll('worst-reflectance/7','worst-reflectance/8').replaceAll('GeneralWork.PassbandQualityProtocol="passband-margin-quality/1"','GeneralWork.PassbandQualityProtocol="passband-margin-quality/2"');
 s=replace(s,'它与恢复及LS合计≤N，自身≤ceil(sqrt(N))，不另增N。','它与恢复及LS合计≤N；原余量窗口与新增修正窗口各≤ceil(sqrt(N))，合计≤2ceil(sqrt(N))，不另增共享N。');
 const marker='`GeneralWork.PassbandQualityProtocol="passband-margin-quality/2"`';
 const position=s.indexOf(marker); if(position<0)throw Error('Missing format section'); const end=s.indexOf('\n\n',position);
 s=s.slice(0,end)+'\n\n新增可选`GeneralWork.NonlinearConstraintCorrectionProtocol="trial-constraint-correction/1"`与`NonlinearConstraintCorrections`记录共享Core实际修正迭代，不与主/窗口迭代相加。`PassbandCorrectionIterations`为`PassbandQualityIterations`的子集；`Run.PassbandCorrectionStartLayers`、`PassbandCorrectionIterationLimit`、`PassbandCorrectionStopReason`记录第二窗口。`PassbandCorrectionAttempts`含累计步骤、阻尼尝试、回溯比例、最大指导超界、指导目标及是否采用修正；非有限指导值写null，不能用于达标判断。正式保留试探仍在`PassbandQualityTrials`中，原实际Bounds逐点保持，schema与输入指纹定义不变。新增高反修正工作仍在原额外Q窗口中计数；它不引入新的Q额度。'+s.slice(end);
 return s;
});
edit('labs/CoatingDesign/README.md',s=>replace(s,'# 光学镀膜设计（产品内置模块）','# 光学镀膜设计（产品内置模块）\n\n2026-10-31 当前：共享Core新增试探点非线性约束修正及同材料快照的有界n/q复用。协议4继续用真实n/k和原门槛，窄带保留旧质量结果后在共享额度内进一步改善峰值、各截止OD和带宽；高反在原额外窗口内修正回溯试探。实现、跨规格对照与剩余98%未达标项见[本阶段报告](../../docs/COATING_NONLINEAR_CORRECTION_2026-10-31.md)。下列日期记录保留各阶段范围。'));
edit('docs/README.md',s=>replace(s,'## 从这里开始','最新镀膜优化验证：[非线性约束修正、真实材料高反与窄带对照](COATING_NONLINEAR_CORRECTION_2026-10-31.md)。\n\n## 从这里开始'));
