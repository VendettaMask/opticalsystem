# 协议 4 可行区反射率质量修正

2026-10-30，范围见[通用算法和结果](../../../../docs/COATING_REFLECTANCE_QUALITY_2026-10-30.md)。本轮只改变协议 4 的 AR/HR 可行区质量优化和必要的复验/工作记录；材料、前向求解、解析膜厚导数、原验收和默认协议保持。

## 最终测试

| 文件 | 结果 | 身份 |
| --- | --- | --- |
| [before/quality-before.trx](before/quality-before.trx) | 0/6 | 六组可行 AR/HR 的最差损失均无法继续下降 |
| [guards/quality-guards.trx](guards/quality-guards.trx) | 12/12 | 增加固定层、改名、尺度、优化器、预算、取消、保存和阶段衔接 |
| [focused/quality-focused.trx](focused/quality-focused.trx) | 37/37 | 原通用协议/导数与新质量目标的影响面；包括加密后的乘子/罚项缩放 |
| [final-tests/coating-quality-final.trx](final-tests/coating-quality-final.trx) | 127/127 | 最终完整模块，包含新增精确物理极限/单次迭代保护；无失败/跳过 |

上述组有重叠，不能相加。最终产品源码在完整轮后保持；不作主仓全量或原生桌面声明。

`attempt-1/quality-attempt-1.trx` 中六组其实都已通过“严格改善、仍达标、预算”断言，随后错误地按含字典快照的对象引用比较材料而失败；改成完整材料 JSON 内容比较，物理和验收断言未降低。`attempt-2/quality-attempt-2.trx` 为随后 9 项成功复验。全部记录保留。

## 17 份冻结输入、34 次新旧配对运行

- [inputs/](inputs/) 在本轮扩展对照运行前一次冻结；六组初始控制、六组扩展控制、固定膜层控制和四份真实材料处方。运行后没有调整目标或 n/k。
- 最终只使用 [final-matrix/before/summary.json](final-matrix/before/summary.json) 和 [final-matrix/after/summary.json](final-matrix/after/summary.json)。所有 34 次正常完成，逐例结果为相应目录的 `.coating.json`，可以重开复算。
- [paired-results.json](paired-results.json) 核对整份输入指纹、目标/设置、材料和固定层：**15/15 个已可行例严格改善最差反射指标，15/17 的原通过状态保持**。两个原失败控制仍失败，未省略。
- 真实减反最大 R：`1.999999971% → 1.032146859%`；原已精化真实减反：`0.084967491% → 0.059040810%`。真实高反仍为 `95.956285% < 98%`。窄带控制两臂的处方相同，FWHM、峰值和左截止 OD 保留失败。
- [918 点实际 Core 输入/输出](final-matrix/core-cases-all.json)及[固定 tmmcore 独立结果](final-matrix/tmmcore-comparison.json)：最大绝对差 R `2.1094237467877974e-15`、T/A `1.9984014443252818e-15`；原阈值 `2e-11` 不变。

`matrix-before/`、`matrix-after/` 保留第一次扩展试验，不能混入最终结果。随后按数学定义补齐指导点数变化时的乘子/罚项单位缩放，所有 17 份输入不变，重跑得到最终矩阵。`heldout-*` 表示未用于实现参数调整的扩展组，不宣称统计泛化认证。

两臂都设最多 24 次连续迭代，实际计算量不同：旧流程多数可行例早停，新流程继续消耗可用预算。完整 `Run` 中包含实际迭代/评价以及谱点/灵敏度/解析批次/加密和复验计数，不将异质计数或其子集相加。没有相同总成本或全局最优声明。

## 完整性和运行时

[integrity.json](integrity.json) 确认：

- 原 `Checks` 和 `Converged` 函数源码与本轮开始时的 `baseline-source/DesignService.cs` 相同；前向求解、膜厚导数、默认示例协议和原薄膜数据文件保持。
- 新旧对照使用完全相同的 Core/Application DLL；新 Engine 与模块测试 DLL 字节相同。旧 Engine 来自 `baseline-runtime/`，本轮开始前保存。
- tmmcore 原归档 SHA-256 保持，实际执行的 JS 与归档条目字节一致。

`baseline-source/` 保存本轮前的三个受影响领域文件；这包含上一轮已有的半高宽修复身份，不回退该修复。独立运行目录位于 `.tmp/coating-quality-compare-20261030/{before-final,after-final}/`。两臂共用正式 Core，分别加载旧/新 Engine；Node 和旧引擎快照仅为离线对照，不是产品运行依赖。

正常默认 Release App/模块/测试依赖图与诊断构建通过，零新增警告/错误；只做后台计算和原有离屏检查。其他并行工作区修改未在本轮认证，没有操作用户桌面、提交、推送或发布。

## 重放入口

```powershell
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll quality-run <冻结输入目录> <输出目录>
node tools/diagnostics/CoatingAudit20261030/summarize-quality.mjs validation/rechecks/2026-10-30/coating-feasible-quality <固定tmmcore的package目录>
```

新建独立矩阵可用 `quality-freeze <新目录>`；复现本轮配对时必须使用已有 `inputs/`，不能重新生成 GUID 或改写来源处方。旧臂在同一个正式 Core 下加载基线 Engine，其他运行时和诊断工具保持同一份。程序的输出包含真实材料、声明目标、计算工作和正式验收，比较脚本会对退化/材料改变/固定层或预算违规报错。

## Git同步范围

仓库保存源码/基线源码快照、冻结输入、完整结果、测试记录和哈希；本机编译比较运行时 `*-runtime/` 保留本地，由本目录 `.gitignore` 排除。上述旧/新二进制配对命令需要已封存的本地运行时；普通当前引擎重算可从工程重新构建。哈希用于核对已封存二进制，不承诺从部分基线源码逐字节重建旧运行时。
