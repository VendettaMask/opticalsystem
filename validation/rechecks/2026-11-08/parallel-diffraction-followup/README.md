# 95 支菜单镜头 FFT 衍射续查

独立开放项由 **81 条降至 54 条**。其中 **23 条通过补充对照，4 条确认为原分析不适用**。关闭记录保留原请求身份、配置及冻结判定。环形光瞳两条原记录的 Workbench 指纹为空，独立证据保留原请求 SHA-256。 [关闭记录](resolved-in-this-round.csv) [剩余记录](independent-open.csv) [汇总](summary.json)

| 分析 | 剩余 |
| --- | ---: |
| FFT 衍射包围能量 | 29 |
| 二维 PSF | 9 |
| FFT PSF 截线 | 10 |
| FFT MTF | 6 |

剩余 26 条仍需完成收敛或交叉验收，28 条缺有效原生参考。台账保留 #1927、#1934 两条 Tessar 历史开放记录，按既定范围单列，独立跟进范围还有 52 条。

- **二维 PSF**。八条物镜请求保留原 128×128 物理网格及 0.25 µm 像素间距，双边收敛和交叉对照通过。#1011、#1093 和 #1291 的原网格也全部通过。 [八条物镜及重合网格控制](../diffraction-objective-psf/README.md) [#1011 及同批请求](../diffraction-original-profiles/README.md) [#1093 与 #1291](../diffraction-psf-late-window/README.md)
- **环形光瞳**。#653 和 #2335 的参考使用独立锥面反射和径向 Hankel 积分，并核对各自原生实光线的瞳孔掩码、角度和相位。两条原网格通过双档收敛和交叉对照，2048 档最大 NRMSE 为 0.0001901。随后接入原 64 档缺相邻受光节点时的自适应重试，两条正式入口均在原网格返回收敛结果，50 项定向回归通过。全量冻结请求的自适应模式启用数为零。 [两套处方、求积收敛及原网格比较](../annular-fft-psf-reference/README.md) [自适应入口回执](../annular-fft-psf-reference/annulus-adaptive-verification.json) [定向测试](../annular-fft-psf-reference/annulus-retry-focused.trx)
- **Twyman Green**。#2157 的自适应分批 PSF 在 6656、8192 档通过相位检查，正式入口全图与收敛原生参考的 NRMSE 为 0.000915。正式复算约 14.8 分钟，20 项定向回归通过。#2159 的原生 MTF 与 5120 档 Core 相位检查已通过，产品 MTF 仍待完整双档对照。 [产品入口、用时及原生参考](../twyman-streamed-fourier/README.md)
- **FFT 截线**。#1295 在 ±175 µm 全域通过 2048→4096 档双边收敛。#971、#993 和 #2681 在各自原 201 个坐标上通过。 [#1295 逐点结果](../diffraction-profile-1295/README.md) [三条追加截线](../diffraction-original-profiles/README.md)
- **包围能量**。#2054、#532、#1814、#1874 和 #2024 的实际光束与衍射极限曲线均在原 0–5 µm 半径通过。两批共保留十二条的原设置及分量指标。 [高采样六条](../diffraction-energy-high-sampling/README.md) [追加六条](../diffraction-energy-second-cohort/README.md)
- **平面光路适用性**。原截线和包围能量入口现可识别两套纯平面准直处方，四条原请求返回 `NotApplicable`。原生实光线证实了几何判据。Etalon 的原截线和能量输出逐字节一致，23 项定向回归通过。 [原生实光线与原入口复验](../fft-collimated-plane-applicability/README.md)

平面光路和流式 PSF 增量接入后，以重新导入的 95 支镜头、115 个配置重算了 2,904 条可执行原请求。3,024 条冻结判定逐项一致，共 2,527 通过、19 接近、31 差异、427 不可比、20 求值错误，原通过项无回退。30,700 份原处方和参考载荷通过 SHA-256 验证。与上一次重放相比，四条平面光路结果变为明确的不适用，另有 20 条输出只出现数值差，按各字段原单位的最大绝对差为 9.95×10⁻¹⁰。 [冻结全量回执](frozen-replay.json) [95 支逐镜头冻结结果](../parallel-precision-followup/by-lens.csv)
