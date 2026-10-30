# 镀膜设计优化与固定参考复核 · 2026-10-30

本地未提交工作树，仅修改 C# 镀膜候选编排、目标残差、版本记录和必要的界面保存/显示。共享 `CoherentThinFilmSolver`、固定源归档和原 120 组参考 SHA-256 全部保持，见[完整性核对](reference-integrity.json)。未操作用户桌面、运行 Python 产品计算或新增 Optiland 比较。

## 最终检查

按完整用例身份合并，**62 个不同用例的最新结果全部通过**，见[逐项账目](latest-test-ledger.json)。不是全仓、跨平台或原生桌面验收。

- [一次完整镀膜模块运行](coating-final.trx)：62 项，61 通过/1 失败。失败为新增常量光谱权重检查的十进制舍入断言：约 14779.89 的评价值相差约 7×10⁻¹¹，绝对小数位断言跨舍入边界。改为相对 10⁻¹² 的数值一致性检查，没有修改物理或设计验收阈值。
- [最终 35 项定向复验](verified/focused.trx)：全部通过，包含修正后的检查、新默认/试验策略、固定参考、历史文件、公差、约束、取消、真实示例和两项界面协议保存。
- [三个尺寸的控件贯通](verified/controls.trx)：全部通过，验证生成、复制/删除、复验、优化、无效输入及窄带切换。使用离屏真实控件，复制测试改为原层数加一，适应新版选择四层而非假定两层。
- [最终构建](verified/final-build.log)、[定向构建和测试](verified/build-and-tests.log)：默认 Debug App 与模块测试依赖图正常构建。定向格式和差异空白检查另外完成。

验证覆盖候选先短程优化再排序、未密集复验者不声明达标、固定层不被结构搜索改变、短程/完整工作量合计、相同种子复现、版本/指纹兼容、实际上游八套膜层构造、原稠密采样与独立更密网格一致性。新目标评价不代替最终逐项物理检查。

## 计算参考

- `ThinFilmPhysicsTests.FixedUpstreamReferenceMatchesAllCases` 复验原 **120 组** tmmcore 0.4.2 参考，包括四角度、S/P、裸界面、减反、高反、吸收及厚膜。它是一项测试内的 120 个物理案例，不再加进 62 项用例计数。
- [本轮默认四种实际膜系的 156 点 S/P/非偏振对照](defaults/tmmcore-comparison.json)：直接运行固定 tmmcore JavaScript，最大绝对误差 R **2.609×10⁻¹⁵**、T **2.998×10⁻¹⁵**、A **2.554×10⁻¹⁵**。使用 C# 冻结材料在各波长实际求得的 n/k 和膜厚，保留真实吸收。这个新增集合的角度为 0°，不扩大为任意角度/材料的认证。
- [八个镜层/腔阶参考](../../../../labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/tfstudio-prototypes-8b942b3.json)由实际固定 `prototypeLayers.js` 生成，C# 试验提议逐层核对材料顺序和厚度。这是结构构造核对，不是完整 TFStudio 优化器对比。

Node 仅离线执行原始参考，不参与产品构建或求解。原归档未改；导入原 TFStudio 源时 Node 曾提示未声明 ESM 类型，自动重解析后成功，未为消除提示而修改归档或参考源码。

## 改前与最终默认结果

[输入核对](comparison-input-checks.json)确认四组目标、材料和完整优化迭代上限相同；[机器对照摘要](comparison-summary.json)保留完整精度。每次完整优化上限为 24，正式 DLS、种子 1234、原 n/k/厚度界/目标保持。新协议新增短程阶段，因此不是同总算力比较。

| 规格 | 改前 | 最终默认 | 结论 |
|---|---|---|---|
| 减反，500–600 nm，最多 8 层 | 2 层，最大 R 1.098132% | 4 层，最大 R 0.084967% | 达标，层数与计算量增加 |
| 高反，520–580 nm，最多 32 层 | 最低 R 95.677295% | 最低 R 95.984480% | 改善，未达 98% |
| 单腔，550 nm / FWHM 15±1 nm / 峰值 T≥75% | 72.603178%，27.228174 nm | 相同 | 未达标，保持旧默认 |
| 双腔，同目标 | 68.946148%，20.297745 nm | 相同 | 未达标，保持旧默认 |

减反新增 123 次短程评价，完整阶段 26→73 次；高反新增 674 次，完整阶段 809→811 次。模板目标评价另计减反 9 次、高反 16 次；计数不是所有单波长求解。不能将两个正结果推广为一般成功率优势，也不能把高反或窄带未达标称作物理无解。

可在内置镀膜模块直接打开最终文件，读取后按材料快照复算：

- [减反](defaults/AR.coating.json)、[高反](defaults/HR.coating.json)
- [单腔窄带](defaults/NBF1.coating.json)、[双腔窄带](defaults/NBF2.coating.json)
- [最终默认汇总](defaults/summary.json)、[改前汇总](before/summary.json)

## 保留的失败与范围限制

`after/`、`refined/`、`balanced/`、`wing-guard/`、`measured-guidance-nbf2/`、`final/` 保留阶段试验。早期过弱的峰值权重让带宽更接近目标但峰值 T 显著下降；另有只约束两个半高点导致宽肩峰/FWHM 缺失的情况。后续加入直接 Core 指标指导后仍没有稳定窄带收益。

这些目录来自同一未发布 v2 的开发过程，目标评价在过程中调整过，不是最终评价函数的逐字节回放基线。扩展在阶段试验时曾自动加入，最终改为显式版本；重做相应结构搜索需开启 `PrototypeFamilyVersion = 2`。保存的实际膜层和 R/T/A 仍可用当前物理求解器复算，但不要跨阶段比较 merit。

[扩展双腔搜索](modern-search/summary.json)约 106 秒，峰值 T 66.93%、FWHM 20.75 nm；[旧搜索](legacy-search/summary.json)约 69.5 秒，68.95%、20.30 nm。两者都未达标，扩展没有获益。此为显式试验策略阶段，不能当作最终默认对照。最终不默认启用窄带设计协议 2，扩展还需显式 `PrototypeFamilyVersion = 2`。不会通过降低 k、调低目标或把未收敛/无 FWHM 结果标为通过来掩盖失败。

早期 `target-balanced-first` 批次曾使用错误的“短程生成就必须达标”测试假设，后来改为完整优化后按原标准检查，并采用共享安全离屏宿主；首批中止不计作通过。`target-balanced-second.trx` 为独立引用已构建依赖的 19 项通过，不能替代后来正常依赖图构建。工作区并行改动曾造成 `SurfaceTraceFailure` 缺失和相机 `SystemPolarization.Enabled` 编译失败；未修改这些其他任务代码，待其恢复后完成正常默认构建。NuGet 审计网络提示通过缓存依赖的锁定离线恢复处理，没有换包版本。

源码与重放工具位于 [CoatingAudit20261030](../../../../tools/diagnostics/CoatingAudit20261030/Program.cs)，相关来源哈希见[最终源码](source-hashes.json)。实现、版本字段与用法见[说明](../../../../docs/COATING_REFERENCE_REVIEW_2026-10-30.md)。
