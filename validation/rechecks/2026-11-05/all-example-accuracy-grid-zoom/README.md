# Grid GRIN、Grid Sag 和反射变焦精度复验

复验范围为 95 支菜单镜头和原矩阵的 3,024 项请求。实际运行 2,904 项，以 2026-10-21 的 OpticStudio 2026 R1 冻结参考比较。核对 95 支处方和 30,700 个原始输入与参考文件，并逐项核对请求指纹。沿用原视场、波长、配置、采样、输出定义和容差。比较策略 SHA-256 为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。场曲的 Workbench 适配修正在下文单独说明。

| 结论 | 上轮 | 本轮 |
| --- | ---: | ---: |
| 通过 | 2,367 | 2,396 |
| 接近但未通过 | 41 | 38 |
| 数值差异 | 93 | 77 |
| 不可比 | 453 | 443 |
| 计算或求值错误 | 70 | 70 |

29 项转为通过，原通过项全部保持。成功导入 94 支，共 114 个配置。单独的固定光线复核为 143 组通过、108 组不可比，共 259,395 次逐面比较。

## 修复及定向证据

- **Grid Sag Example**。导入器按配置应用 `IGNR`，被忽略面保留原编号，用无间距、无口径、无折射的参考面表示。这个示例中，同介质、零后间距、无口径和无相互作用的 Grid Sag 展示面，在网格外使用顶点平面通过，保持与原生报告一致。有折射、反射或遮挡的网格依然受测量范围限制。两配置的 12 项数值差异均通过，另外 10 项光扇、光程差和波前从不可比转为通过。两条原单光线在 `Py=0.8` 时到达像面，原生实际列出的有效面与 Core 对应行逐列相同。原矩阵仍因报告缺少被忽略行判为不可比。[IGNR 原生定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Multi_Configuration_Operands.html) · [Grid Sag 有效范围](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Grid_Sag.html) · [单光线对照](numeric-controls.json)
- **反射变焦**。冻结的有焦场曲报告使用主光线和归一化瞳孔偏移 `0.001` 的正向傍轴光线。旧 Workbench 适配值为 `0.00001`，三个配置的子午差约为 3.94–4.28 µm。修正适配后，五个配置均通过，三个原差异的最大子午残差为 1.27×10⁻⁹ mm。复验保留原生设置、原请求指纹和阈值，执行记录另存翻译后的适配值。用户直接指定的 Workbench 傍轴步长和无焦定义沿用各自入口。[适配执行账目](field-curvature-setting-translations.csv)
- **Grid GRIN**。单光线报告改用体积分后的实际入射方向。请求 490 的第 2 面入射角从 6.15621° 改为 1.14745°，原生为 1.13612°。Core 最大步长从 1 mm 缩至 0.005 mm 后，出口位置变化小于 10⁻¹⁰ mm。冻结参考的第 2 面 Y 仍差约 0.939 µm，且报告段长为 0，Core 几何段长为 3.80399 mm。焦距分别为 3,018.733 mm 与 25,293.100 mm，一阶定义仍待对齐。请求 494 的同一 64×64 波前对冻结参考 NRMSE 为 0.31588，对原 0.005/0.001 mm 原生收敛控制为 1.81249×10⁻⁵，原阈值为 0.003。两个参考各自保留结论。[GRIN 步长定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_1.html) · [收敛和报告控制](numeric-controls.json)

## 阅读逐项结果

- [每支镜头与固定光线的统计](BY_LENS.md)
- [四支相关菜单镜头的逐请求未通过清单](REMAINING_FOCUSED.md)
- [全 95 支剩余 628 项请求及误差、原因](remaining-requests.csv)
- [剩余 108 组固定光线及参考缺口](remaining-fixed-rays.csv)
- [本轮判定变化](verdict-changes.json)和[验收摘要](summary.json)

反射变焦的 64/128 瞳孔试算相对冻结 32 档仍有差异，固定矩阵保留各自原档位。前轮已通过的自适应 FFT 专项与固定采样的相位欠采样结论分别保留。[反射变焦采样试算](numeric-controls.json) · [前轮自适应 FFT 专项](../fourier-physical-pupil/README.md)

定向测试共 227 项通过，其中 Core 与工作台 208 项、比较工具 19 项。默认 Release App 零警告、零错误。App、定向测试与冻结复算的 Core SHA-256 一致，为 `5cefdbd2cdf2ceda44c2d69587401b25b2c572c2e0a1f0f582cd8e8829db329d`。[定向测试回执](menu-precision-targeted.trx) · [比较工具回执](menu-precision-comparison.trx)

首次较宽的测试过滤误纳入了已排除镜头的旧测试，也遇到缺失的旧夹具和较早的原生比较断言失败。定向测试和 95 支验收统计使用正式限定范围的独立回执。
