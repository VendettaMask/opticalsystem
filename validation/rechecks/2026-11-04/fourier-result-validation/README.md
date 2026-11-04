# FFT结果验证与参考收敛

见[实现、使用、结果和限制](../../../../docs/FOURIER_RESULT_VALIDATION_REPAIR_2026-11-04.md)。

- [最终摘要](summary.json)、[优先六项](priority-results.json)、[其他Core检查](other-core-results.json)。
- [13项逐项对照](reference-and-core-audit.csv)、[相邻原生档位度量](native-convergence.json)、[采用的13组参考](native-reference-controls.json)、[33次捕获读回](native-capture-results.json)。
- [原固定采样身份](original-fixed-results.json)、[四份默认数组核对](default-controls.json)。

`source-verification.json`记录最终PDB源文件绑定。`archive-index.json`映射逻辑文件到新/复用的内容载荷，`archive-verification.json`记录完整读回；仅在全部验证后退役本任务展开目录，最终运行环境保留。依赖继续复用包缓存。前轮稳定参考继续使用其原归档，不复制为新的参考权威。

[并行材料源码版本](concurrent-source-versions.json)区分三个在封存时已变化的工作区文件与其经哈希核验的编译版本；[默认App核验](default-app-build-receipt.json)记录并行重建前的已测版本。当前混合工作区未计作本轮整体认证。

新显式协议12 Pass/7 Unavailable与原固定请求19 Unavailable分别维护。13项原生曲线/窗口收敛不等于全部Core结果、报告标量或全平面能量认证。
