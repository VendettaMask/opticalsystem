# 原四文件无焦 MTF 随视场原生参考

`reference.zip` 保存 15 个请求的原生数据/文本、设置和模型回读、环境，以及源 ZMX 和 Core 快照。`manifest.json` 固定归档和每个成员的 SHA256；归档 SHA256 为 `7941bdca3dcdd0ea492d3fbb8b295d93e8148fc421c32806bb8808d62edb0549`。Core 快照为原有共享导入器生成的输入，不能冒充原生模型回读。

OpticStudio 2026 R1，独立 `CreateNewApplication`，CaptureScreenshots=false，仅关闭任务自己的实例。请求中保留绝对历史路径；ZIP 中 `cases.json` 的 source/snapshot 已改为相对路径以供复核，源内容及 SHA 不变。模型中的原生角单位与设置必须通过校验后才能比较。

四文件/五配置 × Fourier/Huygens/Geometric，32 采样、密度 10、+Y、全部波长、共同频率 0.1/0.2/0.4/0.8/1.6/3.2 cycles/mrad。角分/角秒按输入单位换算，不对输出拟合。扩束镜 Fourier 的空数值结果原样保留，不作通过依据。

双筒使用既有 PORRO_PRISM-LH/RH.POB 依赖及原 SHA 校验；本包未另改三角片或边界规则。附加 13 个原生控制及候选计算封存在[重检包](../../../rechecks/2026-10-31/afocal-mtf-field/README.md)。[专题说明](../../../../docs/AFOCAL_MTF_FIELD_2026-10-31.md)区分实际修复、通过与未关闭项。
