# FFT PSF：高 NA 镜头工作 F 数追迹失败修正

2026-09-06 后续修复已覆盖其它分析的瞄准传递、FFT PSF 像面/光瞳采样和物理零坐标，并完成两支镜头的十项数值复验；见 [数值修复记录](NUMERICAL_REPAIR_2026-09-06.md)。下文保留首轮历史边界。

本次核验日期：2026-09-04。范围仅为 FFT PSF 的光瞳瞄准与尺度计算，不是整套衍射分析的 Zemax 精度验收。

## 复现与原因

使用仓库 `zemax-ms-l7-high-na.ZMX` 测试夹具，通过 `WorkbenchRuntime.BuildAnalysisView("PSF", ...)` 的产品默认参数（64 × 64 光瞳、128 × 128 显示、视场 1、全部波长）可复现：

```text
Working-F-number ray did not reach the image surface.
```

未瞄准光阑的归一化边缘光线在第 6 面被截断，工作 F 数所需的最终光线方向缺失。对该夹具的三个视场、三个波长启用真实光阑瞄准后，工作 F 数探针能够到达像面。这是该输入的追迹复现结论，不以截图推断数值正确性。

## 已实现

- FFT PSF 在生成光瞳前检查工作 F 数探针；默认发射方式缺少有效像面样本时，改用光阑瞄准重新计算工作 F 数。
- 发生重试时，波前采样和 Jones 偏振采样一并采用光阑瞄准，不能只改 F 数而保留原来的未瞄准光瞳。FFT 网格尺寸、归一化和采样间距公式不变。
- 新增可选 `ComputeFftPsf(..., aimAtStop: true)`，允许调用方显式使用瞄准光瞳；Jones 采样新增同名选项，现有 cell-centered 调用仍自动瞄准光阑。
- 已准备的 cell-centered 瞄准波前和偏振数据会保留，包括调用方施加的相位/离焦；未瞄准的预计算光瞳不能静默切换尺度，需调用方重新生成并显式传入 `aimAtStop: true`。
- 不修改镜头口径、光阑、表面、材料或共享追迹缓存配置；不忽略物理遮挡，不使用任意 F 数兜底，不吞掉取消或瞄准异常。瞄准后仍无有效像面探针时继续报错。
- 默认探针能正常到达像面的路径保留原有数值；公共 `WorkingFNumber(s)` 不会偷偷切换瞄准方式，重试仅由 FFT PSF 协调。

## 定向验证

本次定向测试共 **20 项通过，0 失败、0 跳过**，未运行全量测试或打包程序：

- `PsfWorkingFNumberRegressionTests`：12 项。涵盖产品默认参数复现、三个视场 × 偏振开关（每项覆盖全部波长）、自动重试与显式瞄准逐像素/尺度一致性、保留预计算相位、防止预计算光瞳混用、物理遮挡、取消，以及 `123456.ZMX` 的有限非空输出与原尺度保留。
- `FrozenAnalysisRegressionTests.FftPsfRetainsFrozenReferenceGridWithPowerAmplitude`、`FftMtfRetainsFrozenReferenceFrequencyGridWithPowerAmplitude`、`JonesPupilMatchesFrozenReferencePointForPoint`：6 项 Cooke/Tessar 辅助数值回归。
- `PolarizationPsfLabelTests`：2 项，继续明确标注标量偏振近似的限制。

上述定向验证属于 2026-09-04 历史记录。2026-09-05 已完成默认 App 和正式解决方案重建，`0` 警告、`0` 错误，并补跑完整主测试；桌面输出锁定已解除。复色参考中心和 OTF 相位的后续修复及最终验证见 [项目修复报告](PROJECT_REPAIR_2026-09-05.md)。

未新增 Zemax 实机捕获或 GUI 对比截图；`123456.ZMX` 用例是内部输出/尺度回归，不是与 Zemax 捕获像素逐点比对。固定 Zemax 基线未改动，也未据此宣称高 NA 标量 FFT PSF 已与 Zemax 完全一致。
