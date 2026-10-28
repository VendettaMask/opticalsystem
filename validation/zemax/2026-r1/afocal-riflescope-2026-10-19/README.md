# Afocal Riflescope / OpticStudio 2026 R1

2026-10-19，单镜头，源 SHA-256：
`36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b`。
本轮修复比较工具，未改 Core 光学算法。

| 项目 | 结果 |
|---|---|
| 3 波长逐面光线 | 3/3 Pass，648 输入、11664 逐面结果 |
| 轴上 0.486 µm Ray Fan | Pass；最大差 2.660821e-9 度 |
| 3 视场复色 FFT MTF，64 档 | 轴上 T/S Pass，轴外四曲线 Difference；最大调制度差 0.852976837 |
| 场曲 | Core 毫米与原生屈光度不兼容，保留物理缺口 |
| 百分比畸变 | Difference；最大差 547.560654678 个百分点 |

完整说明与范围见[专项报告](../../../../docs/AFOCAL_COMPARISON_REPAIR_2026-10-19.md)。

- [三个正式请求及全部指标](formal/run-summary.json)、[配置](formal/comparison-settings.json)、[容差来源](tolerance-provenance.json)。
- [Ray Fan 对比](formal/comparisons/ray-fan-c1/comparison.json)、[FFT MTF 对比](formal/comparisons/mtf-c1/comparison.json)、[场曲/畸变对比](formal/comparisons/field-curvature-and-distortion-c1/comparison.json)。
- [逐面光线结果](ray-results.json)。原坐标、方向余弦、接收及首截光门槛保持。
- `formal/normalized`保留两侧单位化数据，`formal/raw`的历史数据、设置和模型角色保留；相同内容通过[路径映射](../../../evidence-aliases.json)共享一份原件，历史布局可由[还原工具](../../../../tools/validation/README.md#无焦证据副本收敛)生成。
- `captures`是最初四组控制及独立原生数据的逻辑集合；[原清单](capture-manifest.json)字节不变，比较测试构建自动还原共享内容后逐项验证。旧绝对路径仍是捕获身份，未以新采集覆盖。
- 最终正式请求已经明确无焦标志、角单位及全波长身份；此前诊断捕获保留其原字节。旧 95 镜头的无焦统一跳过统计没有被覆盖。

角分与毫弧度、角频率与线频率、屈光度与长度都分别核对。MTF 横坐标统一 cycles/mrad，Ray Fan 统一度，场曲不强行换单位；默认有焦容差文件保持。退出码 0 不等于精度全部通过。
