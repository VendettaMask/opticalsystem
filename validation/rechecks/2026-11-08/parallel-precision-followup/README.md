# 95 支菜单镜头并行精度复验

原范围包含 95 支镜头、115 个配置。2,904 条可执行请求以重新导入的处方重算，3,024 条原判定逐项保持。30,700 个原处方及冻结参考载荷通过 SHA-256 验证，115 份配置快照和原请求映射逐字节一致。 [冻结结果与程序集](summary.json) [逐镜头结果](by-lens.csv)

| 冻结结果 | 通过 | 接近 | 差异 | 不可比 | 求值错误 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 原设置、原容差 | 2,527 | 19 | 31 | 427 | 20 |

独立开放记录从 **117 条降到 81 条**。本轮 25 条按原门槛通过，11 条经实光线验证为原分析定义不适用。81 条余项均为 FFT 衍射，其中 44 条尚未完成收敛比较，37 条仍缺有效原生参考。 [关闭的 36 条](resolved-in-this-round.csv) [剩余 81 条](independent-open.csv)

- **FFT 衍射**。修复自适应截线粗档覆盖不足时提前退出。#2879 在 ±250 µm 全窗口通过双边收敛及交叉比较。二维 PSF、截线和高档衍射包围能量累计 16 条补充通过。二维 PSF 在粗档缺足够瞳内节点时也可继续加密，#2499 通过 Core 收敛，仍缺有效原生网格。 [截线与首批通过](../diffraction-refinement/README.md) [二维 PSF 补采](../diffraction-psf-followup/README.md) [追加三条二维 PSF](../diffraction-psf-three/README.md) [包围能量](../diffraction-energy-followup/README.md) [二维自适应](../fft-adaptive-coarse-retry/README.md)
- **Axicon**。正式 Huygens PSF、截线和通焦 MTF 可显式启用有焦轴对称瞳孔积分。#950 和 #953 在原 32×32、0.25 µm 像面上与独立收敛的原生重建通过。截线最大差约为原生峰值的 2.78×10⁻⁷，通焦 MTF 最大差为 7.15×10⁻⁸。 [产品入口、实光线及采样证明](../axicon-continuum/README.md)
- **方向工作 F 数、波前及 OPD**。MEMS #1246 的投影比例与原生比值差为 1.49×10⁻¹⁸。Cylinder #1070 的 63 个原投影节点通过。Cylinder 与 MEMS 的弧矢角域为零，独立判定八条原 FFT 请求不适用，覆盖双方向 MTF、二维 PSF、X 截线及圆形包围能量。Etalon 两条原 Difference 的独立稳定重建误差低于原绝对门槛。 [方向 F 数与 Etalon](../wavefront-directional-f-number/README.md) [FFT 有限焦面坐标](../fft-focal-axis-applicability/README.md) [原波前节点](../wavefront-axis-reference/README.md)
- **一阶量、相对照度及 RMS**。GRIN interconnect #825 的轴上一阶焦距和 F 数以原生实光线的近轴外推独立通过。三条同像面准直光瞳的相对照度确认为不适用。Twyman Green Interferometer 的唯一物理视场相对值为 1，Chebyshev OAP 的唯一轴上 RMS 通过。 [GRIN 一阶量](../grin-first-order-reference/README.md) [照度角域](../illumination-angular-support/README.md) [轴上 RMS](../wavefront-axis-reference/README.md)

最后一处二维 FFT 自适应增量另行验证。原 2,904 条可执行请求中没有启用自适应选项。增量程序集通过 **206 项产品回归、82 项比较回归**。产品回归包含同步过期断言后单独通过的完整参数约定测试。早期定向筛选误带旧 `123456.ZMX` 夹具，最终筛选和验收计数已排除。 [回归、范围与程序集](../fft-adaptive-coarse-retry/README.md)
