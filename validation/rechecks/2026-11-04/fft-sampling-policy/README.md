# FFT采样规则核查证据

范围和结论见[专题](../../../../docs/FFT_SAMPLING_RULE_AUDIT_2026-11-04.md)。原处方、原请求、参考及容差保持；此批是独立Core自加密，不是新增原生收敛捕获。31个产品结果仍不可用，未恢复Pass。

- [summary.json](summary.json)：Core/源码身份、29项测试、范围及保留边界。
- [request-audit.csv](request-audit.csv)：26项主矩阵拒绝、4项原生无效对照和独立无焦0009。
- [final-comparison.json](final-comparison.json)：1/2/4/8倍的原容差完整曲线/像面度量，包含每条曲线及原节点覆盖。
- [fft-sampling.trx](fft-sampling.trx)、[tests.log](tests.log)：最终29/29，零失败/跳过。编译失败日志属于早期诊断，不算验收。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)：455份逻辑文件，内容寻址去重；原始输入通过索引复用既有不可变归档。所有条目完整读回核验后才允许清理展开副本。
- [evidence.zip](evidence.zip)：最终31项产品结果、独立124份有限傅里叶数值、每档设置及前置30份相位局部诊断。相位诊断使用临时只读观测钩子；它已从最终Core移除，不能视为最终产品额外算法。

归档SHA256：`3544e4ad3b28c2348416256f3246c2ddd6293630e0ee8ac3634634d5a495d358`。最终Core：`a052ac75b497c5f1ecf254141fbef1cb61b915db5d8ef1898eeda672527f75a0`。旧低采样矩阵及原生失败保持原身份；不把自加密的2 Pass、3 Close、26 Difference混入原3024项统计。

复算入口见[工具说明](../../../../tools/diagnostics/FftSamplingPolicy20261104/README.md)。本轮未操作用户桌面、推送或发布。
