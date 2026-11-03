# 异世界主题与参考原稿集成核对 — 2026-11-03

用户要求更新并同步远端，同时保留设计参考原图。本次将主题代码、运行素材、阶段验证和参考原稿一并纳入 Git。

- 远端基线：`fbfe320b57be479a6c3aefde90523bdcbf8bf49f`。
- 合入后被验证的产品代码提交：`08aee6ff0917591eab348f79f44636ec5e82f33f`；最终测试另包含下述两项 UI 夹具采样修订，文件哈希见 `integration.json`。
- 将未推送的主题提交重放在远端最新修复之后。仅 `docs/CURRENT_STATUS.md` 的并行新增段落冲突，保留双方完整记录；远端 Core/Application、Light 材质及 Git 属性保持原样。
- 三张[参考原稿](../../../../docs/assets/themes/isekai/references/README.md)按原字节存储；已比较原始文件、工作树和 Git 已提交 blob 的 SHA-256，全部一致。来源/尺寸/哈希见该目录清单，原稿不嵌入运行程序。
- 沿用此前的 86 项绘图/主题/导出定向集合，并追加 12 项 `FirstOrderAndFftDefinitionTests`，在合入后的默认 Release 输出重新构建并执行。仅后台 Headless/Skia，不操作用户窗口；不把本次源码同步计作完整光学精度认证或云端发布验收。

此次集成不重复复制阶段截图。设计演进与实际渲染见[圆角旧金框阶段](../isekai-rounded-border/README.md)，各历史结果仍保留其原源码范围。

## 集成结果

默认 Release App、两个内置模块依赖及测试构建无警告/错误。最终 [integrated-final.trx](integrated-final.trx)：**98 执行、98 通过、0 失败、0 跳过**，约 26 秒。

首次 [integrated.trx](integrated.trx) 为 84/86：`MtfPlotTitleTests` 的真实 Double Gauss FFT MTF 使用 32 采样，`AnalysisLegendLayoutTests` 的 Cooke FFT MTF 使用 8 采样，均被远端新增的相位 Nyquist 检查返回 `Unavailable`。这两个用例验证的是标题/图例/导出，已仅将各自 FFT 分支的光瞳采样改为 256，保持原 Success、曲线身份、轴、标题、图例与 PNG/报告断言；Huygens/几何分支保持原输入。

没有关闭采样检查，没有改产品默认值、处方或任何数值阈值。追加的 12 项检查包含欠采样必须拒绝、真实 FFT 诊断、请求作用域隔离与一阶量约定，均通过。原光学修复版本和其原精度结论不改写。
