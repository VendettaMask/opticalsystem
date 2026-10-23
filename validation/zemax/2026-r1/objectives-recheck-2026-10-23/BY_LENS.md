# Objectives 全部 19 支镜头当前代码复算

日期：2026-10-23。软件源码 `52793e3cc0f11a1dfbf61e7671f57fde9635238d`，使用正式 C# Application/Core Debug 输出重新计算；对照 2026-10-21 冻结 Zemax OpticStudio 2026 R1 捕获。本轮没有新采 Zemax 数据，没有修改产品算法、原请求、采样、参考或容差。

分析计数按 **Pass / Close / Difference / 不可比 / Error**，光线按 **Pass / Difference / 不可比 / Error**。不可比不算通过。

638 项分析为 **568 Pass / 0 Close / 0 Difference / 69 不可比 / 1 Error**；74/74 组逐面光线通过。唯一计算错误为 210° 广角的场曲/畸变：光阑面 18 的瞄准在 1 次迭代后报告未收敛，残差 `1.02292e-11 mm`。不能因此声明目录全部通过。

200° 广角的场曲/畸变共同有效覆盖率为 89%，低于原门槛，因此保留不可比；核对 2026-10-20 原矩阵，该项原来也是不可比，并非此次新增回退。

| 镜头 | 分析 P/C/D/I/E | 光线 P/D/I/E |
|---|---:|---:|
| Apochromat.zmx | 46/0/0/4/0 | 8/0/0/0 |
| Apochromat2.zmx | 34/0/0/4/0 | 5/0/0/0 |
| Apochromat3.zmx | 26/0/0/4/0 | 3/0/0/0 |
| Apochromat4.zmx | 43/0/0/27/0 | 13/0/0/0 |
| Cooke 40 degree field.zmx | 30/0/0/0/0 | 3/0/0/0 |
| Double Gauss 28 degree field.zmx | 29/0/0/1/0 | 3/0/0/0 |
| Double Gauss 5 degree field.zmx | 35/0/0/3/0 | 5/0/0/0 |
| Doublet using MIL number glasses.zmx | 34/0/0/4/0 | 5/0/0/0 |
| Doublet.zmx | 27/0/0/3/0 | 3/0/0/0 |
| Even Asphere.zmx | 20/0/0/2/0 | 1/0/0/0 |
| Gaussian Quadrature over Annular Pupils.zmx | 18/0/0/4/0 | 1/0/0/0 |
| Gaussian Quadrature Over Wavelength.ZMX | 29/0/0/1/0 | 3/0/0/0 |
| Inverse telephoto lens.zmx | 30/0/0/0/0 | 3/0/0/0 |
| Petzval.zmx | 29/0/0/1/0 | 3/0/0/0 |
| Relay lens.zmx | 29/0/0/1/0 | 3/0/0/0 |
| Tessar lens using vignetting factors.zmx | 28/0/0/2/0 | 3/0/0/0 |
| Wide angle lens 100 degree field.zmx | 29/0/0/1/0 | 3/0/0/0 |
| Wide angle lens 200 degree field.zmx | 26/0/0/4/0 | 3/0/0/0 |
| Wide angle lens 210 degree field.zmx | 26/0/0/3/1 | 3/0/0/0 |

## 验证与边界

- 全部 638 项均实际执行，637 项生成新软件数据；其余异常保留为 Error。全部 74 组逐面光线重新追迹。19 个源文件与原捕获输入逐字节一致。
- 保持 2026-10-20 Objectives 矩阵的 564/564 个原通过项；新增通过来自后续既有修复，本轮不修改算法。
- 原诊断工具将所有读取/比较异常统一标为 Error。这里严格采用正式 Sequential 比较器的规则：InvalidDataException 及明确的 NativeInvalidResult 列为不可比，原始日志和异常仍保留；不会将它们变成 Pass。
- 不可比原因包括无足够有限曲线点、原生无效/低采样警告、原生 RMS 扫描方向范围不一致及空数据缓冲区；不是这些设置已达标的声明。
- 此次为同目录既有请求的 CPU 比较，不是 GPU、任意采样/所有视场组合或全 95 支镜头认证。补充采样控制保留其原记录，本轮未扩展。

[机器汇总](summary.json)、[逐项结果](analysis-matrix.csv)、[逐面光线](ray-results.json)、[请求及参考哈希](request-reference-manifest.json)、[源文件核对](source-integrity.json)。
