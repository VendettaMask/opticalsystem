# 默认 Release 输出：SPC 完整构建与复验

2026-11-01。用户关闭原主程序后补齐默认 App 完整构建与后台验收。原口径、像质、制造验收及 F/4 规格冲突保持。最终范围为 **`final-verified/`**；本目录根级的较早运行不代表最终程序集验收。

## 权威结果

- `final-verified/default-app-build.log`：正常默认 Release App 完整构建，零警告/错误，包含两个内置模块。
- `final-verified/tests/spc-default-final.trx`：109 项定向测试通过，零失败/跳过；测试产品副本与实际默认八程序集相同。
- `final-verified/runtime-audits/`：47 个场景进程直接加载实际默认 App 目录的八个产品程序集，记录路径/MVID/SHA256；验证全程持有读取句柄，前后指纹一致，见 `verification-summary.json` 和 `default-runtime-before/after.json`。
- `final-verified/inputs/`、`formal/`：冻结原任务、预算与种子42–46，连续/六暂停位置新进程接续精确一致，恢复段无光线重放；每轮161函数/151梯度/503394条实际光线，56次差分与10次内部验收，均在600/600/2M/120秒原上限内。
- `final-verified/formal/*/seed*/independent-audit.json`：80次候选原验收、20次STAROPT读回，外部审核不回馈搜索。十份新进程重开零追迹。
- `final-verified/original-tasks/`：15个原F/8上限、F/4口径冲突与21°禁插片的零工作守卫；五份原F/8合格终点完整验收精确一致。
- `final-verified/product-session/`：真实会话跨进程暂停/恢复/自动交接，准备和主搜索费用分列，仅一份主记录；额外四次交接起点验收/读回通过，重复交接/加载/报告零追迹。
- `final-verified/default-runtime.zip`、`default-source.zip` 及对应清单：默认产品二进制、测试宿主、PDB编译文档与构建输入。外部依赖复用原包，不是独立安装包或完整第三方运行库。
- `delivery-source.zip`：本轮验证宿主/脚本和文档；`documentation-audit.json`：严格文档核查；`final-verification.json`：逐文件封存及读回结果，并核验之前五份封存包不变。

八份合格候选最终选出重复的单片/双片两种处方，不宣称五种独立设计、减片或完整制造Pareto收益。SPC正式续跑沿用原M3.1单片样例；原广角F/8是既有终点复核，不是新的SPC广角冷启动成功。自主胶合、递归增长、自动口径、前台GUI、GPU及跨平台验证不在本轮完成范围。

## 中间记录的身份

根级 `default-app-build.log`、`default-tests.log`、`formal/` 和相关审计为第一批，运行时匹配根级 `default-runtime-before.json`，但随后八个默认文件均被另一轮构建替换；不能用这批通过证明替换后的默认文件。根级归档是在替换后制作，其身份由自己的清单明确，不能当成第一批执行文件。重复归档仅在完整逐项校验后移除并留存引用。

`final/default-app-build.log` 保留后台并行编译占用中间 DLL 的失败。`final-verified/` 在其后串行完成正常构建，对默认 DLL/PDB 持有只读句柄，完整复验和归档后释放。没有终止用户进程或启动桌面程序。之前 [SPC自动续跑封存](../spc-automatic-resume/README.md) 的历史受阻结论保持，新增结果见[本轮报告](../../../../../docs/INITIAL_STRUCTURE_SPC_DEFAULT_OUTPUT_2026-11-01.md)。
