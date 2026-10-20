# Objectives：19 个镜头逐项结果

日期：2026-10-20。全部原文件保持，使用本机 OpticStudio 2026 R1 与正式 C# Core，同设置、原容差。分析计数按 **通过 / Close / 超差 / 不可比 / 错误**；光线计数按 **通过 / 超差 / 不可比 / 错误**。不可比和 Close 均不计为通过。

共 638 项分析；74 组逐面光线。详见 [逐分析数据](analysis-matrix.csv)、[逐面光线原始比较](ray-results.json)及[机器汇总](summary.json)。

| 镜头（Objectives/） | 分析 P/C/D/I/E | 光线 P/D/I/E | 主要结论 |
|---|---:|---:|---|
| Apochromat.zmx | 46/0/0/4/0 | 8/0/0/0 | 已比较项通过，存在不可比项 |
| Apochromat2.zmx | 33/0/1/4/0 | 5/0/0/0 | 衍射包围能量 超差 |
| Apochromat3.zmx | 26/0/0/4/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Apochromat4.zmx | 42/0/1/27/0 | 13/0/0/0 | 衍射包围能量 超差 |
| Cooke 40 degree field.zmx | 30/0/0/0/0 | 3/0/0/0 | 本次请求全部通过 |
| Double Gauss 28 degree field.zmx | 29/0/0/1/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Double Gauss 5 degree field.zmx | 34/1/0/3/0 | 5/0/0/0 | FFT PSF 截面 Close |
| Doublet using MIL number glasses.zmx | 33/0/1/4/0 | 5/0/0/0 | 衍射包围能量 超差 |
| Doublet.zmx | 27/0/0/3/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Even Asphere.zmx | 20/0/0/2/0 | 1/0/0/0 | 已比较项通过，存在不可比项 |
| Gaussian Quadrature over Annular Pupils.zmx | 18/0/0/4/0 | 1/0/0/0 | 已比较项通过，存在不可比项 |
| Gaussian Quadrature Over Wavelength.zmx | 29/0/0/1/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Inverse telephoto lens.zmx | 30/0/0/0/0 | 3/0/0/0 | 本次请求全部通过 |
| Petzval.zmx | 29/0/0/1/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Relay lens.zmx | 29/0/0/1/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Tessar lens using vignetting factors.zmx | 28/0/0/2/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Wide angle lens 100 degree field.zmx | 29/0/0/1/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Wide angle lens 200 degree field.zmx | 26/0/0/4/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |
| Wide angle lens 210 degree field.zmx | 26/0/0/4/0 | 3/0/0/0 | 已比较项通过，存在不可比项 |

本次覆盖固定主分析和正权波长的边缘视场控制，不代表任意采样、全部场点及所有设置的通用认证。原生无效数据、零跨度曲线及其他缺少共同物理输出的请求保留为不可比。

修正前通过的 391 项中，保持通过 391 项；退化 0 项。
