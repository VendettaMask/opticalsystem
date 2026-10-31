# 双筒 Fourier 随视场直接原生参考

`reference.zip` SHA256 为 `556398d0ce599d02f1343b9bb8df785e552c8c668609aea1ffe1818827db7089`，71 个成员，487,486 字节。成员清单与实际回读结果见 `manifest.json`、`verification.json`。

`direct/c1-n128`、`c1-n256`、`c2-n128`、`c2-n256` 保存完整原生 `data.json`、文本、实际设置、源/依赖检查、模型和阶段时间线；`direct/execution.json` 为四次成功退出、源 SHA 不变及原 120 秒预算记录。每份原件含 0.1、0.2、0.4、0.8、1.6、3.2 cycles/mrad 的完整 T/S 及 300 个横坐标。

`contract.json` 绑定两个原始快照、ZMX SHA、+Y 十个计算场点、全波长、无偏振、去渐晕、Milliradians 与不变比较设置。参考直接使用原生缓冲区，没有重建或插值。`previous-derived/` 只供独立复核此前派生参考；48 条曲线、14,400 值与直接原件最大差 3.33e-16。

`audit/late` 保留上一轮实际成功但被汇总遗漏的后期控制及中途空结果记录，`audit/timeout` 保留真实 600 秒超时与未开始请求的最终执行记录。它们分别用于成功/超时/未开始状态保护，不混作数值验收。

直接参考与结论已补证；早期超时的底层触发条件尚未复现、未查实。详见[调查和数值结果](../../../../docs/BINOCULAR_FOURIER_DIRECT_REFERENCE_2026-10-31.md)与[本轮复核](../../../rechecks/2026-10-31/binocular-fourier-direct/README.md)。历史归档及低采样分类保持。
