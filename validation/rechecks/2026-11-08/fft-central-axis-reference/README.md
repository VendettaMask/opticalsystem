# 两条原自动范围 FFT PSF 截线

按原处方、配置 1、轴上视场 1、0.55 µm、原像面、X 方向中央截线和自动显示范围复算。各案例的物理横轴都由原 32 档请求与独立追迹得到的 F 数确定。每条截线在原 201 个 X 坐标直接积分，Y 坐标固定为零，辐照度以同一通光瞳孔的零相位峰值归一化。

| 请求 | 原截线范围 | 对独立参考 NRMS | 最大绝对差 |
| --- | --- | ---: | ---: |
| #447 Sample Spectrometer | ±44.1937487731 µm | 0.001074889 | 1.52×10⁻⁷ |
| #1207 Irregular Surface | ±20.9759689593 µm | 0.000520244 | 3.21×10⁻⁸ |

两例物理轴对独立计算的最大差分别为 7.18×10⁻¹³ µm 和 1.57×10⁻¹² µm。截线积分加密变化分别为 1.74×10⁻¹¹ 和 8.47×10⁻¹¹ NRMS。原 NRMS 0.003 与逐点门槛均通过，覆盖率 100%。冻结原生截线仍记为 Incomparable。 [#447 参考与逐项指标](spectrometer-0447.json) [#1207 参考与逐项指标](irregular-1207.json)

Workbench 两例都从原 32 档启用自适应，在流式 3072→4096 档通过相位检查。#1207 用默认相对精度 0.001 返回完整曲线，档间 NRMS 为 0.00065366。#447 声明相对精度 0.005 时返回完整曲线，档间 NRMS 为 0.00498121；默认精度的自收敛仍报告不可用。 [正式入口的请求和原始数值](../streamed-physical-fourier-windows/README.md)

复算使用各原 PSF 证据包的 `SpectrometerReference.py` 或 `IrregularReference.py`，参数依次为仓库目录、输出路径、对应 Workbench 数据和请求文件，再附加 `--axis`。 [Spectrometer 光线、相位和积分](../spectrometer-fft-psf-reference/README.md) [Irregular 光线、相位和积分](../irregular-fft-psf-reference/README.md)
