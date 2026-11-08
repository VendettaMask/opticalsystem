# 完整物理瞳孔包围能量与高密度截线复验

95 支菜单镜头的独立开放项由 **17 条降至 10 条**。其中 8 条在后续独立复验范围内，2 条为保留的 Tessar 历史条目。3,024 条冻结判定引用当时的全量重放。本轮使用原设置重新比较二极管镜头全部 22 条请求，18 条通过、4 条不可比，与对应冻结判定逐项一致。校验 65 个该镜头的原生载荷。8 条另外抽查的固定请求输出逐字节一致。 [逐镜头清单](by-lens.csv) [本轮计数](summary.json) [关闭条目](resolved-in-this-round.csv) [开放台账](independent-open.csv) [二极管固定重放](diode-frozen-replay.json) [固定抽查](fixed-sampling-controls.json)

## 完整像面功率与局部半径窗口

显式选择自适应包围能量时，支持单波长、单视场、有限焦面、显式正半径和主光线或质心参考。分批追迹覆盖完整的未拉伸光瞳。按两个完整傅里叶周期求实际质心，用 Parseval 全功率归一化，再在原半径周围用有界复变换读取实际与理想像场。每一档都检查实际相邻相位以及射线、变换和内存预算。默认固定设置继续使用原累积积分路径。 [光谱仪与 Irregular 正式复验](resolved-in-this-round.csv)

| 请求 | 正式有效档 | 对独立实际曲线 NRMSE | 对独立衍射极限 NRMSE |
| --- | --- | ---: | ---: |
| #450 Sample Spectrometer | 3072→4096 | 0.00214436 | 0.0000969205 |
| #1210 Irregular | 3072→4096 | 0.0000623373 | 0.0000525762 |

两条请求都用默认 0.001 自适应精度完成，各比较原 401 点的实际和理想曲线。既有 262144 名义档的相位失败记录保留。倾斜镜头 #2572 保持原处方和质心，二维原生有限格与独立连续解分别有验证。其正式 65536→131072 收敛，最细档对独立参考最坏 NRMSE 为 0.00199731。原生直接档间差异单列。 [光谱仪](../spectrometer-physical-fft-energy/README.md) [Irregular](../irregular-physical-fft-energy/README.md) [倾斜镜头](../pivoted-lens-physical-fft-energy/README.md)

Twyman Green #2164 的高密度档利用经处方校验的弧矢反射对称性。求子午边际功率的追迹同时保留弧矢 Nyquist 项，每档两次追迹即可得到完整有限周期质心。正式 5120 和 5888 档的最大相邻相位差分别为 0.490905 和 0.427512 波，总分批追迹 95,564,948 个点。实际和理想曲线的档间相对 RMS 最坏为 0.003174，默认 0.001 返回未收敛。显式 0.004 复跑完成，两条曲线各覆盖原 401 点。两条曲线的逐点判据均未通过，分别以 0.002131588 和 0.000088756 通过原 0.003 NRMSE 判据。 [独立原半径曲线与两次正式复跑](../twyman-physical-fft-energy/README.md)

## 原中心截线

- #1097 的居中点源直达首面平面光阑时，入射与出射斜率沿主光线到瞳点成线性比例，光程相位积分使用闭式原函数。二极管正式 3072→4096 的原 201 点自适应通过，独立 NRMSE 为 0.000431038。22 条冻结请求中有 11 个 Workbench 数值载荷变化，原 OPD 和波前图的最大数值变化分别约为 9.22×10⁻⁹ 和 1.21×10⁻⁸ 波。逐条判定均一致。 [截线回执](../diode-fft-axis-reference/verification.json) [冻结重放](diode-frozen-replay.json)
- #1991 保持原瞄准和自动物理范围，固定 8192→16384 的截线 NRMSE 为 0.000803262，最细档对独立曲线为 0.00111144。整个 Adaptive 证书和其辅助 Strehl 仍有局限，按原截线强度门槛记录补充通过。 [广角镜头回执](../wide-angle-physical-fft-profile/README.md)
- #2161 的 4608 试档超出相位门槛，5376→6144 两档相位有效并在默认 0.001 内收敛。原自动 201 点对独立参考的 NRMSE 为 0.000116559。 [Twyman Green 回执](../twyman-streamed-fourier/2161-formal-receipt.json)

最新 Core 的 162 项定向回归通过。测试包括完整周期的独立二维质心对照、弧矢对称的 Nyquist 边界项、对称资格的几何反例及近轴相位的原生控制。 [定向测试](streamed-energy-phase-focused.trx)

Axicon 和 Toroid 的独立模型已量化近焦限制，八条原请求均有收敛的条件数学参考。本阶段结束时，八项仍在开放台账中。 [Axicon 适用域](../axicon-continuum/fft-huygens-applicability.json) [Axicon 原频率](../axicon-continuum/fft-947-mtf-continuum.json) [Axicon 原截线和能量积分](../axicon-continuum/fft-949-952-continuum.json) [Toroid 原窗口](../toroid-fft-phase-domain/toroid-segmented-window.json) [Toroid 原频率](../toroid-physical-fft-mtf/results.json) [Toroid 原半径能量](../toroid-physical-fft-energy/results.json)
