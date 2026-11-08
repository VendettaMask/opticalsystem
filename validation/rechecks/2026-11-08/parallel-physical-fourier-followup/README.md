# 95 支菜单镜头的物理傅里叶复验

独立台账由 **54 条降至 46 条**，本轮八条按原门槛通过。两条 Tessar 历史记录按既定范围单列，独立跟进还剩 44 条。 [关闭记录](resolved-in-this-round.csv) [剩余记录](independent-open.csv) [汇总](summary.json)

| 分析 | 台账剩余 |
| --- | ---: |
| FFT 衍射包围能量 | 26 |
| 二维 FFT PSF | 6 |
| FFT PSF 截线 | 10 |
| FFT MTF | 4 |

- **MTF**。Twyman Green #2159 及 Sample Spectrometer #445 在原 0–50 cycles/mm 上，通过两档 Core 相位与收敛、两档原生收敛，以及完整两轴交叉对照。Twyman Green 两轴的最大 NRMSE 为 0.000723，正式复算约 6.7 分钟。Sample Spectrometer 两轴最大交叉 NRMSE 为 0.0000864，600 个原频率点也通过逐点门槛。 [Twyman Green](../twyman-streamed-fourier/mtf-formal-receipt.json) [Sample Spectrometer](../streamed-mtf-frontier/445-receipt.json)
- **二维 PSF**。Singlet eyepiece #555 的自适应路径使用完整瞳孔和原物理傅里叶频点，正式结果与原生出瞳波前的独立重建在原 16,384 个像点上通过。Unobscured Gregorian #2499 的独立参考由 3,001 条原生实光线和 2,999 个波前点核对。Irregular Surface #1203 在显式自适应精度 0.003 下通过交叉验收，默认 0.001 尚未收敛。 [Singlet](../singlet-physical-fft/README.md) [Gregorian](../gregorian-fft-psf-reference/README.md) [Irregular](../irregular-fft-psf-reference/README.md)
- **包围能量**。#562 的虚像尺度按已选择的出瞳 F 数计算，在原 0–5 µm 上通过同档原生和独立连续参考。#592 和 #1664 也通过两条曲线的双边收敛及交叉对照。 [虚像尺度和回归](../singlet-exit-pupil-energy/README.md) [#592](../diffraction-energy-third-cohort/README.md) [#1664](../diffraction-energy-fourth-cohort/README.md)
- **新余项证据**。Grid GRIN #502 的同一步长 256→512 档出现明显周期和质心差异，更细步长采集已提升至 512 档。Sample Spectrometer #443 的三档原生 PSF 均报告欠采样。Toroid 的三项原 FFT 有有限像方尺度，最高可用采样下的实际相位跨步仍超过 0.5 波。 [Grid GRIN 密度对照](../grid-grin-pupil-size-check/README.md) [Spectrometer PSF](../spectrometer-psf-sampling/README.md) [Toroid](../toroid-fft-phase-domain/README.md)

全量重放重新导入 95 支镜头、115 个配置并重算 2,904 条可执行原请求。**3,024 条冻结判定逐项一致**，共 2,527 通过、19 接近、31 差异、427 不可比、20 求值错误，原通过项没有回退。30,700 份处方和参考载荷通过 SHA-256 验证。最后的虚像包围能量增量由 115 份快照识别出唯一受影响原请求 #562，按原档独立重放，冻结判定保持 Incomparable。 [全量回执](frozen-replay.json) [虚像增量原档回执](frozen-virtual-energy.json) [逐镜头冻结结果](../parallel-precision-followup/by-lens.csv)

流式 Fourier 和固定物理窗增量的 47 项定向回归通过。虚像包围能量的七项定向回归通过。164 项能量扩展测试的 A/B 对照确认两项虚像用例由失败转为通过，原有 38 项失败的用例及错误消息一致。 [Fourier 测试回执](physical-fourier-focused.trx) [能量 A/B 回执](../singlet-exit-pupil-energy/test-verification.json)
