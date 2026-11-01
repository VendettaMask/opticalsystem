# 双筒 Fourier 高频收敛复核

用户已确认以收敛后的 Zemax 结果为精度基准。两个配置在 3.2 cycles/mrad 的整条 T/S 曲线，共四条，按原曲线门槛通过：1,332 次对齐比较、最大 NRMSE 0.001726602、最大绝对差 0.000843226。仍有 782 个点超过点门槛，使用既有 NRMSE 判据，不称逐点零差异。

`assessment.json` 分开保存四组比较：原 32 对 32（两个 Close、两个 Difference）、原生 128→256 收敛、Core 32→128 收敛、Core 32 对原生 256。后三组各四曲线通过。`core-endpoints.json` 仅是 32–512 的边缘点补充，不能替代整曲线验证。其余五个频率、全部 Fourier 和 Huygens 均不外推。

`tests.trx` 为新增 3/3 定向回归，无失败/跳过：两配置曲线与 704 条主波长移位光线的 OPD/口径保护。光线 OPD 最大差 5.4349515235e-6 波，低于既有绝对门槛 1e-5。`runtime-identity.json` 记录最终 Core `14028ab2e857fb557ffb1dfbcaf0a5e49b157319469db1283ce5c9fd881f2d94`；前一专题 `fb6d…` 和早期 `84eaca…` 探针不冒充最终版本。

`evidence-current.zip` 保存研究、独立原生控制、失败与中断、最终结果和源码快照；SHA256 为 `debab561449f2fb3d0b4f0891ef47e466acc9af27b74d2b4edd30dcb00791ac6`，738 个成员，3,091,692 字节。`manifest.json` 逐成员记 SHA256，`verification.json` 为实际回读校验。原生曲线和可移植测试输入另见[参考包](../../../zemax/2026-r1/binocular-fourier-converged-2026-10-31/README.md)，其 SHA256 为 `d7d8ffa72f8820ed1e74bdb64789660dcbb31927de5235edebd625961836df07`。

`failure-events.json` 及归档中的原执行记录保留六频率高密度捕获/重算预算中断、空辅助控制、首次枚举名称错误和诊断编译重试。它们不计为数值通过。后续单频率原生请求有完整设置/模型回读和源 SHA 校验；C1 的 128 采样单频率曲线与原六频率的对应 300 个 X、600 个调制度值完全相同。

没有修改 Core 光学计算来模拟低采样高值，没有改容差。C# 原生宿主只开关自己的独立实例，全部禁止截图，无键鼠或桌面操作；未重跑全仓、执行原生 UI 验收、安装、发布或提交推送。详见[原因和边界](../../../../docs/BINOCULAR_FOURIER_CONVERGENCE_2026-10-31.md)与[C#诊断](../../../../tools/diagnostics/BinocularFourier20261031/README.md)。
