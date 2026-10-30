# M3.2 已有镜头检索验证

日期：2026-10-30。实现及使用见[M3.2](../../../../docs/INITIAL_STRUCTURE_M3_2_2026-10-30.md)，范围见[计划](../../../../docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md)。本轮保持原验收、口径、材料/面型/轴向和保护权限；没有新广角合格、减片或一般提速结论。

- [正式协议](experiment-protocol.json)、[输入 manifest](input-manifest.json)、[镜头库](inputs/sources.lens-library.json)、[来源/费用索引](inputs/library-index.json)：正式执行前冻结，28 条镜头、21 个源文件，含一个原设计费用未知的用户来源。
- [40 轮逐项费用](formal-runs.json)、[八组汇总](formal-summary.json)、[全部检索淘汰类别](formal-retrieval-outcomes.json)、[实际终验候选](retrieval-finalists.json)。`formal/` 内保留每轮源计划、完整处方、验收、来源及预算。
- [独立回读](independent-verification/verification.json)：40 份结果全部验证；68 次实际密集验收按每轮相同处方去重后重算 59 次，内容哈希一致，额外 6,435,680 实际追迹单列，不进入在线对照费用。
- [取得/建库/在线费用](offline-online-costs.json)：已知取得费用按源文档哈希去重，未知保持未知。在线正式总追迹 8,912,026；这不是冷启动总费用。
- [额外尺寸协议](scale-check-protocol.json)与[实际结果](scale-gates/verification.json)：主对照成功迁移均为 1 倍，故另作 0.5/2 倍新尺寸查询各五次。十对直接/检索的完整验收哈希一致；0.5 倍 5/5 合格，2 倍 0/5（58.807541 µm RMS 超过原 50 µm），未放宽原绝对像质或制造最小值。额外 20 次验收、890,400 实际追迹单列；源码/运行时见 `scale-source.zip`、`scale-source-manifest.json`、`scale-runtime-manifest.json`。
- [最终定向测试](tests-final/retrieval-final.trx)：122 通过、0 失败/跳过；本轮 26 个新增用例，含两项真实离屏工作流。并非全仓或原生桌面认证。
- [默认 Release App 构建](default-app-build.log)：实际默认输出，0 警告/错误，含内置模块。
- `baseline-source.zip` / `baseline-manifest.json` 保存任务前 559 个文件；`executed-source.zip` / `executed-source-manifest.json` 保存正式执行 576 文件；[运行时 manifest](runtime-manifest.json)绑定正式程序与依赖。

两臂每任务各五个种子：受控三片两臂均 5/5 有合格结果，检索每轮复用两条已有镜头；八片 F/8、F/4 两臂均 0/5；原 21°两臂均保留已有三片，检索均为原输入复用。三片查询刻意由库内已知镜头扰动形成，用于检索闭环，不能视为独立泛化或新设计证据。原 21°和受控查询跨种子不变，检索本身确定性，不作五个独立镜头样本的统计解释。

`pilot-retrieval-seed42/`、`pilot-fixed-seed42/` 保留冻结前开发预试。`tests-initial/` 19、`tests-round2/` 22、`tests-ui-isolated-v2/` 24、`tests-regression/` 121 的阶段通过均保持原范围，最终以 122 项为准。首轮开发编译误用字段 API、一次自动审批超时、默认构建文件占用及并行镀膜源码缺类型的中断见[验证过程](validation-process.json)；这些未执行或未完成的测试不算通过。`formal-summary-draft-hashtable.json` 是汇总脚本误用字典属性分组的草稿，已从原始结果重读为正式汇总，未改变数值记录或正式运行。

复现工具为 `tools/diagnostics/InitialSearchM32`：`prepare` 冻结输入，`run` 分别执行 fixed/retrieval，`verify` 在新目录独立重算。工具拒绝覆盖既有目录；正式运行使用独立冻结 DLL。后台 CPU double、原视场/波长与原制造检查；没有 Python 光学计算、桌面控制或 GPU 执行声明。逐次验证命令关闭 NuGet 网络审计，不表示漏洞审计通过。

[源码核对](source-integrity.json)确认三个归档逐文件校验通过，本轮 14 个代码文件在主正式冻结后未改变。当前计划的完成标记是执行后的文档更新；并行的两个优化器和一个镀膜灵敏度文件更新也如实保留。尺寸补充源码与当前对应计算源一致；主正式数值绑定其原归档运行时。

[文档扫描](documentation-final.json)覆盖 1,446 份文档，本轮相关指南无坏链，围栏、仅本机链接和当前指南重复均为 0。全仓严格检查退出 1：另一并行镀膜任务的 `COATING_GENERAL_FIXES_2026-10-30.md` 指向其尚未生成的证据 README；本轮未改写它，也不宣称全仓文档全绿。
