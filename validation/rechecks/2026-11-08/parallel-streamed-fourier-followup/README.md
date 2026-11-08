# 95 支菜单镜头的物理傅里叶精度续查

本轮独立复验新增 **29 条补充通过**，开放项由 **46 条降至 17 条**。其中 15 条继续独立复验，2 条是依照本轮范围保留在历史台账里的 Tessar 条目。冻结判定引用上一次覆盖 95 支镜头、115 个配置的全量重放。此次核查了全部 2,904 条可执行原请求的自适应采样开关，并对新 Core 抽查 10 条固定请求，快照、请求身份及数值文件的哈希均匹配各自的固定回执。 [本轮计数](summary.json) [本轮通过](resolved-in-this-round.csv) [开放台账](independent-open.csv) [固定抽查](fixed-sampling-controls.json)

| 冻结口径 | 通过 | 接近 | 差异 | 不可比 | 求值错误 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 3,024 条原请求 | 2,527 | 19 | 31 | 427 | 20 |

全量回执对应前一轮 Core，核验 30,700 个原参考文件和 115 份导入快照。其后虚像包围能量修正按唯一受影响的固定请求另行核验。本轮的 57 项定向回归使用 `a0e1ccb7…ddafc` Core。 [全量回执](../parallel-physical-fourier-followup/frozen-replay.json) [虚像固定回执](../parallel-physical-fourier-followup/frozen-virtual-energy.json) [逐镜头冻结结果](../parallel-precision-followup/by-lens.csv) [本轮定向测试](streamed-final-focused.trx)

## 正式自适应 FFT

- 计算固定像窗时可在同一射线预算内切换到完整、未拉伸瞳孔。每档保留相邻实际相位检查，在原像点计算复傅里叶强度。扫描镜配置 2、3 的默认 0.001 自适应证书通过 2048→4096，原 16,384 个像点对独立折叠光路标量 FFT 的 NRMSE 均约为 0.000147。原生 FFT 图仍不可用。 [扫描镜回执](../scanning-mirror-folded-fft/formal-receipt.json)
- 横向或纵向中心截线可先求完整瞳孔的一维复边缘和，再直接求原 201 个物理坐标。自动轴的范围和坐标间距均保持。扫描镜的两条中心截线对独立模型的 NRMSE 约为 0.0000813。 [光谱仪和 Irregular 证据](../fft-central-axis-reference/README.md) [扫描镜截线](../scanning-mirror-folded-fft/sections-formal-receipt.json)
- #443 和 #1205 分别在显式自适应精度 0.003 通过，#447 在 0.005 通过。三项的默认自适应精度 0.001 仍报告未收敛。独立对照使用原门槛。#1205 子午曲线以原 NRMSE 分支通过，最低的非零频率点超过逐点门槛。 [正式数值与采样回执](../streamed-physical-fourier-windows/README.md) [MTF 逐方向结果](../irregular-fft-mtf-reference/verification.json)
- #1095 使用原 600 个频率坐标，两个方向的原生 1024→2048、正式产品 1024→2048 及交叉对照全部通过。Fabry-Perot 的 201 点原截线对独立参考的 NRMSE 为 0.000307。 [二极管 MTF 回执](../streamed-mtf-frontier/1095-receipt.json) [Fabry-Perot 回执](../fabry-perot-physical-fft/README.md)

## 衍射包围能量

保留原 0–5 µm 半径、实际和理想两条曲线、质心定义及完整瞳孔总功率。独立连续解使用原生实光线、波前和孔径几何。有限原生 FFT 格点按实证采样约定复算，连续曲线另经独立加密，并与高密度 Core 曲线比较。直接原生档间结果仍保存在各自回执。 [反射变焦](../mirror-zoom-physical-fft-energy/README.md) [扫描镜](../scanning-mirror-folded-fft/README.md) [逐项证据](resolved-in-this-round.csv)

- Grid GRIN #502 保持 512 瞳孔和完整像面周期，原生 0.001→0.0005 mm 的原 401 个半径最大绝对差为 6.56×10⁻⁸。两个既有高密度 Core 档对新原生实际曲线均通过，理想分量沿用独立复场重建。 [Grid GRIN 回执](../grid-grin-fine-step-energy/README.md)
- 扫描镜配置 2、3 的实际能量对独立模型 NRMSE 约为 0.003873，超过原 0.003 NRMSE 门槛。最大绝对差约为 3.45×10⁻⁶，两项均通过原逐点分支。 [配置 2](../scanning-mirror-folded-fft/2904-formal-receipt.json) [配置 3](../scanning-mirror-folded-fft/2926-formal-receipt.json)

## 仍开放的相位和适用域

#450 的完整独立曲线已完成，最高正式档的相邻实际相位差仍为 1.12591 波。Irregular、Toroid 和 Twyman Green 的三条能量请求也在最高名义档超过 0.5 波。#945 的径向标量积分收敛，原像面超出近焦适用范围。 [光谱仪相位证据](../spectrometer-physical-fft-energy/README.md) [最高档相位回执](highest-energy-phase-controls.json) [Irregular 独立相位复算](../irregular-fft-energy-cap/README.md) [Axicon 条件模型](../axicon-continuum/fft-945-radial-model.json) [开放台账](independent-open.csv)
