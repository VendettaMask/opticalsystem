# 无焦 MTF 随视场重检

本批独立于前阶段 170 请求，最终 Core `fb6d2b94cb461da4eef88ba6ead5e57bc7da4dc27419e6bedc005de70e11af22`。正式 15 请求：6 Pass、7 Difference、1 Close、1 缺原生数值；168 曲线、55,944 对齐值，逐点超限 18,696。原比较配置未修改。五配置几何曲线通过既有逐点或 NRMSE 判据，仍有 892 点超出点门槛。

`final-matrix.json` 为逐请求结果；`evidence-current.zip` 与 `manifest.json` 保存早期/最终 Core 输出、附加原生控制、候选失败、TRX/日志及源码快照。`verification.json` 为实际回读校验；`runtime-identity.json` 记录最终运行器、测试及默认 App 的 Core 身份。正式原生数据另见[参考包](../../../zemax/2026-r1/afocal-mtf-field-2026-10-31/README.md)。

主计算阶段：8f157171a35e（原实现）、3c06dd2dad17（失败的普通 FFT 复用）、c00d29df4a9e（几何修正）、52aa6ccc71c0（归一化修正）、fb6d2b94cb46（撤回实验代码后的最终重算）。离轴基向量候选 a1f60401b80b 的五个 Huygens 请求单列，未纳入产品。正式 15 个最终结果与 52aa 阶段逐字节相同。旧阶段身份不冒充最终运行器。

新定向测试 6/6，既有定向测试 8/8，无跳过。首版夹具缺模型回读导致五个失败的 TRX 保留；补齐后才通过。内部 API 可见性编译失败、缺 Skia 依赖的离屏绘图失败与重试也单列。未跑全仓、未操作桌面、未提交推送或发布。

复核流程见 [C# 诊断](../../../../tools/diagnostics/AfocalMtfField20261031/README.md)及[专题报告](../../../../docs/AFOCAL_MTF_FIELD_2026-10-31.md)。
