# 镀膜损耗与停滞根因证据

见[报告](../../../../docs/COATING_LOSS_AND_STAGNATION_2026-11-01.md)。用户要求先核查原因，本轮没有改产品优化器或原验收门槛。

- inputs/primary-inputs：15组冻结输入及550/500 nm重点；profiles-inputs额外含3组已优化实测组合，全部n/k保留。
- profiles.json、reference-points、reference-comparison：18个损耗剖面及4个修正探针，共246点实际R/T/A和全部膜厚导数，固定tmmcore原2e-11标准。
- periodic.json：6组真实材料、两种终止、原层数/厚度界内192个周期膜系；只说明该族，不是全局上界。
- directions.json：两档实际指标有限差分、局部线性可行步及48次原门槛复验；restored/summary和实际处方：同一Core修正器接入实际约束后的4个反例。
- fine-sampling、dense-restoration-comparison：30次相同门槛复核，3个稳定微步；550 nm半径0.001宽度有反转，不计稳定改善。restored-replay直接对照磁盘原值/元数据4/4。
- old-n：15组完整原N控制；larger-old为中止批次的9份已完成文件，不计整个批次完成；focus-larger-old为未完成的新550 nm起点N272，不计完成。budget-control-scope.json保留原因。
- reference-structural/reference-native：固定TFStudio从两个新重点起点运行，再以原指标验收，均未通过；点平方目标和实际约束不同。
- verification.json及final-manifest.json：源码未改、Core/Engine二进制和前轮737份证据完整性；final-source/tools/docs为本次快照。

微步是根因反例，不是75%或98%突破、生产修复或等总成本优势。未操作桌面、产品/全仓测试、提交、推送或发布。
