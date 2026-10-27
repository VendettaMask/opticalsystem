# 自主设计界面：工作量与原计划保持

2026-10-27。继续任务时，仓库已包含 M6 界面、后续阶段/轴向补偿和指定四片减片修复。本轮补齐现有界面的账目与输入保持，不重复实施 M6，不恢复旧 AutoLens 长训练或运行新的优化效果对照。

## 实际变化

- 原“梯度”预算明确为光线梯度，与恢复矩阵、矩阵 Jacobian、有限差分分开显示；同时列出已记录的瞳/几何查询和独立验收次数。未保存的查询计数显示“未记录”，未知中断行的矩阵/瞳计数也不填作已知零。已计费和在途预约各自包含函数、光线梯度、追迹及时间；这些量不等价，不相加为一个虚构总成本。
- 谱系新增逐阶段恢复矩阵/Jacobian/差分和瞳查询列，新增账目标签与列头跟随中英日切换。显示直接读取现有工作账，不改 Core、Engine、检查点格式或实际计费规则。矩阵计数仍仅覆盖显式恢复接口，不能解释为全软件所有近轴运算。
- 显式打开同一任务较低 revision 的备份会重置显示保护并使旧队列回调失效，确保预算、候选、谱系来自同一份已打开记录；后台进度仍拒绝过期回调。
- 只改预算不把旧 `Expansion=null` 升级成新扩展策略，不把只拆/只并的权限改成两者同时允许，不抹去显式 false 模式值。组合开关未改变时保留分别权限，固定片数通过原有派发开关生效，保留其分支偏好。
- 未编辑的视场、光阑净口径、轴向窗口、时间 ticks 与原目标保存原始数值，不用控件显示的小数重新生成处方；无效未提交数字必须报错，不能借“保留原值”绕过检查。

本轮源码修改限于自主窗口、其工作流/本地化及相关界面测试，现有 AutoLens 默认 GPU 选择及已有运行身份保持。收尾时工作区另有并行任务的 Core 分析修改，完整保留并在机器汇总单列，不归因于本轮；此处“Core/优化器未改”指本任务的变更范围。

## 七个不同定向用例

1. 实际有限差分恢复工作，界面显示的矩阵/Jacobian/差分与后端账目一致；没有记录的瞳查询显示“未记录”。
2. 实际自动矩阵求导/瞳倍率恢复工作，同样核对总览、逐阶段行、三语和窄屏；光线梯度仍有自己独立的预算。
3. 旧空扩展计划只改预算后，整份计划除该预算字段外指纹一致；未编辑物理数值、采样、时限和模式保持。
4. 不对称拆/并权限有相同保证，固定片数往返和显式材料权限修改保留其余输入。
5. 三份既有 M4 测试记录中的在途样本用于预约/未知辅助工作显示。只对已到试验上限的测试副本结算未知预约，不派发光线；显式打开该任务低版本副本后表格和预算一致。没有继续任何用户长跑。
6. 原有精度/轴向编辑回归：预算修改保留自定义采样，明确选择才启用高精度与轴向选项。
7. 原有计划读取/保存、三语与非法输入回归：载入和检查不自动开始搜索，相关界面操作零新增追迹。

所有窗口和测试点击都在 `SafeHeadlessUnitTestSession` 的 Avalonia Headless/Skia 中，未创建原生桌面窗口、抢焦点或注入系统输入。命令设置 `DOTNET_PROCESSOR_COUNT=2` 并降低父命令优先级；不将其描述为隔离测速。恢复夹具最多 30 次函数请求，只用于工作量显示检查，其宽松光学门槛不代表原设计任务达标。

## 结果与失败记录

| 执行 | 结果与范围 |
| --- | --- |
| [首次](desktop-first.trx) / [日志](desktop-first.log) | 7 项，3 通过/4 失败；发现旧备份行未刷新和列头语言问题，另有不可用的 FD+瞳恢复夹具组合，以及 ImmutableArray 记录按引用比较的断言问题 |
| [修正后](desktop-corrected.trx) / [日志](desktop-corrected.log) | 只复核受影响的工作量/权限/旧备份和一个相关三语回归，6/6 通过 |
| [账目渲染](accounting-render-final.trx) / [日志](accounting-render-final.log) | 格式整理及显示收尾后，仅两项恢复账目/离屏渲染，2/2 通过；保留三语宽屏和日语窄屏图 |
| [未知工作](unknown-recording-final.trx) / [日志](unknown-recording-final.log) | 最后只复核未知中断行不把辅助成本显示为已知零，1/1 通过 |

按最新对应结果去重，**7 个不同用例全部通过**；不累计重复执行。默认 Release App 与测试输出参与构建，无新增警告/错误。仅格式化本轮五个源码文件；最后格式、空白和严格文档检查见 [summary.json](summary.json)。本轮没有全仓测试、外部数值认证、提交/推送或发布安装包。

离屏截图用于新增账目字段、切换和可读性核对，不作为整个软件所有文案、原生交互或所有屏幕尺寸的认证；窄屏结果区仍通过页面/表格滚动访问。

- [中文宽屏](accounting-wide-zh-CN.png)
- [英文宽屏](accounting-wide-en-US.png)
- [日语宽屏](accounting-wide-ja-JP.png)
- [日语窄屏](accounting-narrow-ja-JP.png)

复现五项新增检查（保持后台/离屏）：

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M6_EVIDENCE = 'D:\Projects\opticalsystem\validation\rechecks\2026-10-27\autonomous-desktop-accounting'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~RecordedRecoveryCostsAndRayGradientBudget|FullyQualifiedName~BudgetEditsPreserveNullAndAsymmetric|FullyQualifiedName~ReservationUnitsAndUnknownAuxiliary' --logger 'trx;LogFileName=accounting-rerun.trx' --results-directory validation/rechecks/2026-10-27/autonomous-desktop-accounting
```
