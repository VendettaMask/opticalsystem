# M3.1 胶合与 SPC 过渡基础验证

日期：2026-10-30。实现边界见[交付说明](../../../../docs/INITIAL_STRUCTURE_M3_1_2026-10-30.md)，后续见[计划](../../../../docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md)。这是共享界面、过渡和实体化验证，不是完整 M3、鞍点搜索或性能验收。

- [预先冻结的正式协议](experiment-protocol.json)：种子 42–46；原八片两组和原 21°输入不变。
- [正式总结果](formal/verification.json)：历史完整重验 15/15 与 M2 内容哈希一致；零厚度状态 15/15 矩阵/导数核对通过；普通实体分支 10/10 完成原规格验收、0 合格。
- `formal/` 内逐项保留旧/新完整验收、实验过渡文件、实体处方、材料来源及失败；实际总追迹 3,017,500，零厚度状态没有物理追迹。
- [最终测试](tests-final/foundation-final.trx)：88 通过、0 失败/跳过。34 个新增用例与 54 个直接相关旧回归；不代表全仓或原生桌面通过。
- [默认 App 构建](default-app-build.log)：Release 0 警告/错误，包含内置模块。
- [源码核对](source-integrity.json)：`baseline-source.zip` 539 文件、`executed-source.zip` 546 文件，归档条目与相应 manifest 全部一致；执行源码与最终源码一致。[运行时身份](runtime-manifest.json)冻结正式 DLL 与资源。

保留 `tests-initial/` 首次编译中断、`tests-round2/` 32 通过/1 失败、`tests-regression/` 86 通过/1 失败和修正后的 `tests-regression-v2/` 88 通过。首次缺少 PupilSample 命名空间引用；第二轮负例实际是边缘增厚而非变薄；第三轮旧断言仍要求拒绝已支持的玻璃链。未删除这些记录，也未把中断或失败算作通过。

复现入口为仓库 `tools/diagnostics/InitialSearchM31`，参数依次为仓库绝对路径与不存在的输出目录；正式运行冻结了源码和独立运行时。使用当前共享 Core CPU double、原 21 场点/每波长 320 积分点及中心+32 边缘检查，原中心/边缘厚度、空气和后焦规则不变。没有使用外部 Python 光学引擎，没有改变原口径、目标或权限。

M3.2–M3.6 及真正的 SPC 分支优化尚待完成；SPC 实验文件只是参数保存，不是可恢复准备检查点。联网 NuGet 审计在验证命令中逐次关闭，未宣称审计完成。未控制用户桌面、运行独立 Cooke/Tessar 认证或发布产品。

[文档全仓扫描](documentation-final.json)检查 1,432 份文档，本轮 8 份相关文档无坏链，Markdown 围栏、仅本机链接和当前指南重复均为 0。全仓仍有另一并行镀膜任务文档指向其尚未生成的 `coating-competition/README.md`，因此严格全仓文档检查退出 1；本轮未改动该未完成任务，不宣称全仓文档全绿。

采集 `dotnet --info` 时，SDK 的 Windows 工作负载查询因无权读取 Service Control Manager 而中断；这只是完整环境查询未完成。实际 SDK 版本及已安装运行时另行通过只读命令取得，见[执行环境](execution-environment.json)和[运行时列表](installed-runtimes.log)。构建、测试和正式计算已实际完成，不借此宣称工作负载检查通过。
