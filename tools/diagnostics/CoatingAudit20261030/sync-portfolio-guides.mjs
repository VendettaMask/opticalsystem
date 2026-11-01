import {readFileSync,writeFileSync} from 'node:fs';
function change(file,pairs){let s=readFileSync(file,'utf8');for(const [a,b] of pairs){if(!s.includes(a))throw Error('Missing anchor '+a);s=s.replace(a,b);}writeFileSync(file,s);}
change('docs/COATING_DESIGN_LAB.md',[
 ['余量阶段分为原质量窗口和非线性修正窗口，各≤ceil(sqrt(N))，合计≤2ceil(sqrt(N))','余量流程最多四个窗口（原质量、旧方向、修正方向、联合抛光），各≤ceil(sqrt(N))，合计≤4ceil(sqrt(N))'],
 ['正式主结果与输入保护完成后，先保持原质量窗口，再从未消耗的额外N额度中取一个最多ceil(sqrt(N))次的非线性修正窗口；两窗口各≤ceil(sqrt(N))，与恢复及LS共享额外N，未扩大该共享上限。','正式主结果与输入保护完成后先保持原质量窗口，再从同一正式点比较旧余量方向和修正方向。只有修正方向在实际峰值、各截止OD、带宽误差、原逐项超限和已有采样/通过状态上支配旧方向才选用，否则保留旧方向；随后按剩余额度共同抛光。各窗口≤ceil(sqrt(N))、最多四窗口，与恢复及LS共享额外N，未扩大该共享上限。两个比较方向都计费，总成本增加，不称等成本胜出。']
]);
change('docs/FILE_FORMATS_AND_PLUGINS.md',[
 ['原余量窗口与新增修正窗口各≤ceil(sqrt(N))，合计≤2ceil(sqrt(N))','原质量、旧方向、修正方向及联合抛光窗口各≤ceil(sqrt(N))，合计≤4ceil(sqrt(N))'],
 ['`PassbandCorrectionIterations`为`PassbandQualityIterations`的子集；','`PassbandCorrectionIterations`为`PassbandQualityIterations`的子集，含修正方向与最终抛光、合计≤2ceil(sqrt(N))；'],
 ['记录第二窗口。','记录最后修正窗口。未选分支的试探保存在方向记录中。'],
 ['新增高反修正工作仍在原额外Q窗口中计数；它不引入新的Q额度。','`Run.PassbandDirectionTrials`保存同起点两方向的膜厚、指标、各OD、收敛/通过、额度、实际迭代/评估、选择标记及各自Trials/CorrectionAttempts；`PassbandDirectionSelectionBounds`为旧方向结果的实际余量及原超限。修正方向只有逐项支配该基线才选用，最终抛光继续保护所选点。`PassbandQualityTrials`保留所选路径，工作计数包含两方向（包括未选用者），不能将分支试探拼成一条物理迭代链。新增高反修正工作仍在原额外Q窗口中计数；它不引入新的Q额度。']
]);
