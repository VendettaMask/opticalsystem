# 修正采样后的匹配反馈与实测组合证据

见[报告](../../../../docs/COATING_RESAMPLED_FEEDBACK_2026-11-01.md)。全部优化使用正式采样/2，真实n/k与原验收门槛保留。

- all-inputs及materials：15个冻结起点，4套实测材料组合和一个合成红外控制；原表/提交/CC0/哈希、n-only排除原因及准备时的相位转移与界内夹取单列。
- formal-baseline-all、formal-final-all、formal-append-all：九组完整旧Search、两轮反馈研究流程和追加旧日程。formal-paired.json逐项比较；研究构建与最终显式API的启用边界见activation-boundary.json，未冒称重跑最终API的全部优化。
- primary-*、material-*、hr-*、continued-*、larger-old、reference-*：独立角色/重新匹配、不同实测组合、高反相位探针、550 nm继续、15组单次更大旧额度及15组固定参考。
- feedback-stage-prescriptions.json、stages：96个实际反馈基线/短程/完整处方重评；cold-index、cold-inputs、cold-output：101个磁盘原结果及元数据重放。物理比较见physics-comparison.json（6879点，含重复处方）。
- verification.json、execution-notes.json、superseded-*、one-generation-*：全部边界、失败和停止记录；logs/api-structure-feedback.trx为最终18项。额外匹配、恢复/质量、加密、打开复算另计，同日程主额度不等总查询成本。
- final-source、final-tests、final-tools、final-docs为最终源码/测试/工具/文档快照；final-manifest.json校验本轮，前轮421项保持。

普通Search/Generate及桌面未默认开启新反馈；原材料550/500 nm及98%高反仍未突破。未提交、推送、发布或原生GUI验收。
