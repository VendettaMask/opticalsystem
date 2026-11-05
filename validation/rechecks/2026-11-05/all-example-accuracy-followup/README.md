# 95 支示例精度复算第二轮

再次核对全部 95 支菜单镜头、3,024 项原请求与 30,700 个源或原生参考文件。94 支成功导入，共 114 个配置。2,904 项原可执行请求全部重新计算。原请求指纹和原比较门槛逐项核对。当前运行使用的 Core 与默认 Release App 完全一致。见[运行身份](identity.json)和[程序集核对](../tolerance-precision-integration/followup-cohort.json)。

| 11 月 5 日原设置 | 通过 | 接近 | 数值差异 | 不可比 | 错误 |
| --- | ---: | ---: | ---: | ---: | ---: |
| [首轮](../all-example-accuracy/README.md) | 2350 | 42 | 100 | 454 | 78 |
| 第二轮 | 2367 | 41 | 93 | 453 | 70 |

17 项转为通过，原 2,350 项通过全部保留。示例光谱仪的 11 项原数值差异、1 项接近、1 项错误通过。另有 4 项惠更斯分析获得共同有效结果，判定为数值差异。一阶参数的 7 项字段类型错误中，4 项通过，3 项因参考无法确认零光焦度保留不可比。见[光谱仪专项](../spectrometer-surface-frames/README.md)、[一阶参数专项](../first-order-state/README.md)和[全部判定变化](verdict-changes.json)。

固定光线另核对 251 组，共 259,395 个有效逐面结果，143 组通过，108 组不可比。320° 椭圆反射器仍因物方锥角子类型受阻。其余超差和错误按镜头及请求列明。参考沿用冻结的 OpticStudio 2026 R1。显式自适应的七个 FFT 通过结果见[独立物理域复验](../fourier-physical-pupil/README.md)。

[逐镜头表](BY_LENS.md) · [全部原请求](request-results.csv) · [固定光线数据](ray-results.json) · [机器汇总](summary.json) · [最终集成测试](../tolerance-precision-integration/README.md)
