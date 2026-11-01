# F/8 本次补完证据

最终`final-cold-v6`候选五种子全部新冷启动、5/5通过；历史控制1/5。完整说明见[报告](../../../../docs/INITIAL_STRUCTURE_F8_COMPLETION_2026-10-31.md)。

- `protocol.json`在本批执行前声明；`source.zip`、`source-sha256.json`、`runtime-sha256.json`冻结策略，运行中未修改。
- `candidate/seed42`至`seed46`包含独立输入、完整结果、摘要、进度；无终点或检查点导入。
- `control`为`../paired-v3/control`完整字节副本；`historical-control-provenance`保留原协议、源/运行时哈希和复制清单。不同运行时，未冒称同二进制重跑。
- `audit-v6-final-frozen`和`audit-v6-current-product`各自核对完整十次运行、原预算/边界/系数、全部已完成原验收及合格工程读回。
- `actual-product-pipeline-v6`为额外真实App自动交接/保存/导出/读回检查，不计入五个冷启动样本。主搜索费用单列。
- `completion-summary.json`含逐项结果、成本、去重140个通过测试及范围。`tests/INCOMPLETE_RUNS.md`保留零匹配、中断和早期旧UI超时范围。
- 所有`development-*`、`endpoint-diagnostics`及失败/中断批次单独保留；Gate5未采用，Cold5保留为不开放原型编号。最终Cold6仍使用Gate4，阶段更新≤原有400，总预算不增。
- F4原规格冲突、以前21°二片结果均保持；未进行原生桌面、GPU或更广搜索认证。
