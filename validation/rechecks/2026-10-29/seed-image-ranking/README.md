# 初始结构真实像差排序：范围与账目

本批实现自主拓扑“生成一阶起点”的显式真实像差排序。旧空配置、历史身份和完整验收规则保持；没有调用外部审阅或修改用户桌面。产品代码为 C#，物理指标全部来自共享 Core。

## 最终冻结对照

最终配置仅使用 **1 环 × 4 方位高斯积分、中心加 8 条边缘检查** 做便宜排序，每候选最多 2,500 条实际追迹，准备总额仍为 10,000。四个一阶/制造可行候选均完成原 3 个已定义视场和 3 个波长，共 5,516 条实际追迹、56 次准备函数评价；原排序准备为 52 次函数、零追迹。完整验收仍是 35 个去重视场、10 环 × 32 方位积分、中心加 32 条边缘检查、64 点制造几何及原畸变门槛。

最终协议在检查新起点的密集像质前冻结：[coarse-protocol.json](coarse-protocol.json)，SHA256 `7be7f028beeba7516f2586ad14cbab0776ed88739ff69d1a910594fec24670d4`。原任务 SHA256 `57f7f14bea9c3a19ef801f6850f583938f02b6692dc91d3fc95722b981e2376d` 保持。每组总上限同为 600 函数、100 梯度、2,000,000 实际追迹、60 秒、6 次结构试验；准备实际消耗先扣除。两组从相同 60 秒中预留 10 秒给持久化，CPU 2 个处理器、Normal 后台优先级、无界面。

| 指标 | 原一阶残差排序 | 真实像差排序 |
|---|---:|---:|
| 新保留起点完整验收最差 RMS | 180.745961 µm | 127.541907 µm |
| 新保留起点完整验收最大光斑半径 | 427.242016 µm | 310.984333 µm |
| 新起点最大绝对畸变 | 0.359419% | 2.770767% |
| 全部函数 / 梯度 | 77 / 21 | 81 / 21 |
| 全部实际追迹 | 1,637,216 | 1,637,216 |
| 计算及阶段保存耗时 | 15.300518 s | 12.493494 s |
| 最终合格归档数量 | 1 | 1 |
| 最佳合格 RMS / 最大半径 | 49.292572 / 136.488028 µm | 49.292572 / 136.488028 µm |

**结论：本单例中新起点 RMS 改善约 29%，最大半径改善约 27%；新起点仍超过原 50/150 µm 门槛。最终最佳合格方案没有改善。** 新起点畸变上升但仍低于原 3% 上限；粗筛没有使用畸变排序。两组仅完成一次后续结构试验，均因剩余额度不足完整补偿与终验而停止，未加额、放宽门槛或要求删到指定片数。运行时间差不能作为普遍提速结论，本批也不是广泛成功率基准。

完整报告、带校验检查点、起点与工程导出在 [comparison-coarse](comparison-coarse/)，汇总见 [summary.json](comparison-coarse/summary.json)。只读审计 [verification.json](verification.json) 校验原目标/权限/逐面口径/材料/光阑像面、准备与事件账目、总额、完整筛选和最新测试结果。复核命令：`python tools/diagnostics/HappyLensWide20261029/verify_image_ranking.py <repository-root>`；脚本只读证据，不提供产品光学计算。

## 开发失败与中断保留

没有把以下记录算作成功对照，也没有覆盖后重跑同名目录：

- [comparison-v1](comparison-v1/)：Debug、BelowNormal，两组在完成初始验收前超时，实际墙钟还超过 60 秒。
- [comparison-release](comparison-release/)：Release、BelowNormal 进程长时间就绪却只获得 2.359375 秒 CPU，约三分钟后仅结束本任务进程；[中断说明](comparison-release/interrupted.json) 保留，检查点运行状态不是完成状态。
- [comparison-normal](comparison-normal/)：改为两组 Normal、CPU 2。32 条边缘粗筛使四个 2,000 配额均用尽，全部只完成 1/3 视场，没有有效排名。控制组持久化后实际 64.646772 秒，不能声明严格墙钟内成功。
- [comparison-sparse](comparison-sparse/)：边缘粗筛降为 8，仍用 2 环 × 8 方位积分、每候选 2,000；四个候选均只完成 2/3 视场，原起点回退，未提供候选质量改善证据。
- [development-preparation.seed-search.json](development-preparation.seed-search.json)：仅把每候选分配提高到 2,500、准备总额不变，2 环 × 8 方位仍全部不完整；新增配额测试失败。
- [development-coarse](development-coarse/)：最终 1 环 × 4 方位粗筛的完整视场/总额测试通过后，才冻结最终双臂协议。开发排序分数不冒充完整验收结果。

## 定向测试与构建

| 记录 | 通过 / 总计 | 范围 |
|---|---:|---|
| [screening-focused.trx](tests/screening-focused.trx) | 13 / 13 | 首轮计算、历史准备与原离屏按钮流程 |
| [screening-ui.trx](tests/screening-ui.trx) | 1 / 1 | 新选项三语与无追迹保存读取 |
| [screening-final.trx](tests/screening-final.trx) | 17 / 18 | 新广角配额测试暴露 2 环 × 8 方位仍太贵 |
| [screening-coarse-development.trx](tests/screening-coarse-development.trx) | 1 / 1 | 最终粗筛配置完成原广角全部候选 |
| [screening-verified.trx](tests/screening-verified.trx) | 18 / 18 | 最终相关用例全部通过 |

**18 个不同用例的最新结果全部通过。** 覆盖直接 Core 多波长/最差视场一致性、独立裁光拒绝、候选内外配额及后续候选不受耗尽子配额污染、取消、不完整结果不排名、像质窗口与多样性、准备总函数/追迹账目、旧空配置零追迹和身份、JSON 往返、三语无追迹保存读取与原准备/搜索按钮流程。

默认 Debug App/模块测试项目构建成功，Release 诊断及其 Core/引擎依赖构建零警告、零错误。首次诊断 `--no-restore` 因缺少 `project.assets.json` 失败，随后按锁文件恢复并构建通过。未重跑全仓、产品 Release GUI 验收或跨平台原生验收；没有关闭用户产品、提交或发布。
