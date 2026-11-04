# 中间分析面与波前参考证据

结果见[专题](../../../../docs/ANALYSIS_SURFACE_REFERENCE_REPAIR_2026-11-04.md)与[summary.json](summary.json)。

- [surfaces.json](surfaces.json)：16份原设置控制，999全拒绝，瞄准的光阑前请求明确不支持；面12的OPD/波前实际计算，FFT保留采样拒绝。
- [reference-modes.json](reference-modes.json)：Absolute/Absolute2按像面含相位光程计算，原诊断点定义误差清零；Infinity明确Unsupported。不是新增原生数值捕获。
- [primary-reference.json](primary-reference.json)：六份完整主波长参考控制；主波长自身不变，其他波长改变参考球。
- [default-controls.json](default-controls.json)：四份完整默认物理数组与已核验的旧归档完全相同。
- [image-pair-results.json](image-pair-results.json)：60项原请求52/4/2/2/0，逐项结论保持。[cross-analysis-results.json](cross-analysis-results.json)：156项旧Pass保持。范围不累加，不重标原95支矩阵。

最终679个不同定向用例通过，含27个新控制；默认双模块Release App零警告/错误。主测试输出被其他testhost锁住的额外尝试未完成、未计通过。最终Core为`8821d3a2e315641238275f0343832f19e1dd337bcdf36b4a0cad136801136920`。

`source-verification.json`按最终portable PDB核对实际源码；`archive-index.json`按哈希索引完整数组、原始失败、原生参考复用、测试和拥有的二进制；`archive-verification.json`记录新旧载荷完整读回后才清理展开目录。只保留一份权威载荷，沿用已有归档；第三方依赖复用本机包缓存。严格文档与最终核对另见`documentation-check.json`、`final-review.json`。
