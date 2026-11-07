# 427 项不可比的证据台账

台账覆盖 95 支菜单镜头、3,024 条原请求中原判定为不可比的全部 427 条。逐项按请求编号、镜头 SHA-256、配置、视场、波长及已有请求指纹关联证据。一条原请求只计一次。原判定和比较容差全部保留。 [逐项台账](by-request.csv) · [计数、策略与证据哈希](summary.json) · [原矩阵](../parallel-parity/remaining-requests.csv)

| 最充分的独立证据 | 原请求数 |
| --- | ---: |
| 数值比较已独立通过 | 287 |
| 依原定义不适用 | 20 |
| 已复查的原生设置未提供有效数值 | 53 |
| 独立数值比较接近 | 2 |
| 已有原生候选，尚未建立收敛比较 | 63 |
| 缺有效同合同原生参考 | 2 |

22 条补齐的全瞳 OPD 按原曲线判据通过。#458 和 #466 在另行逐点检查时各有一个补齐点超单点阈值。弧矢瞳坐标 −0.30 的残差分别为 0.01094499 和 0.00630381 波，阈值分别为 0.00562727 和 0.00324667 波。 [全节点逐项对照](../opd-exact-pupil-reference/results.json) · [两条残差诊断](../opd-exact-pupil-reference/spectrometer-controls.json)

20 条定义不适用。14 条有限像面分析没有有限像面比例，2 条混合模式 NSC 镜组缺少通用近轴等效，3 条无焦相对照度缺有限像面。#1241 MEMS 的轴线落在两镜面法线的边界，三档对称扰动的出射方向均跳变 17.29833°，跨轴双侧一阶导数不存在。另有两条米制独立包围能量达到接近档。 [米制和无焦证据](../incomparable-meter/README.md) · [无焦相对照度](../parallel-refinement/afocal-ri.json) · [MEMS 逐光线证据](../parallel-refinement/mems-first-order.json)

## 129 次专项原生采集

19 条冻结 FFT PSF 截线按原字节请求重采，OpticStudio 2026 R1 均报告结果无效。各条都没有 API 数值或文本数值行。原 25 项空缓冲中，另 5 项已有相同设置复查，#499 有独立的完整物理域截线通过。 [本轮 19 条原请求回读](frozen-profile-recaptures.json) · [前轮相对照度重采](../parallel-parity/relative-illumination-native-diagnostics.json) · [RMS 随视场重采](../incomparable-meter/live-cli-smoke.json) · [#499 完整截线](../../2026-11-06/grid-grin-bounded-fft-reference/README.md)

另外 110 次为高档补采。PSF 保留 128×128、0.25 µm 像元间距，MTF 保留 0–50 cycles/mm，衍射包围能量保留 5 µm 半径、质心参考和 FFT 方法。采集时回读相同处方、原设置键、场点和波长。本专项下列 9 条通过相邻原生档、最终 Core 收敛和跨软件原容差，所有交叉度量覆盖率均为 100%。 [PSF 与 MTF 逐档比较](higher-pair-comparisons.json) · [能量逐档比较](energy-pair-comparisons.json) · [追加 256/512](extended-energy-pair-comparisons.json) · [追加 512/1024](higher-energy-1024-pair-comparisons.json) · [前七条最终 Core 复验](earlier-passes-final-core-replay.json)

| 编号 | 分析 | 原生档位 | Core 档位 | 最大交叉 NRMSE |
| --- | --- | --- | ---: | ---: |
| 1041 | FFT PSF | 512、1024 | 1024 | 0.00294147 |
| 2249 | FFT PSF | 256、512 | 2048 | 0.00132040 |
| 2521 | FFT PSF | 256、512 | 2048 | 0.00096370 |
| 2899 | FFT MTF | 256、512 | 1024 | 0.00055819 |
| 2120 | 衍射包围能量 | 256、512 | 512 | 0.00049336 |
| 2386 | 衍射包围能量 | 256、512 | 512 | 3.18×10⁻¹¹ |
| 2476 | 衍射包围能量 | 256、512 | 512 | 0.00132164 |
| 2098 | 衍射包围能量 | 256、512 | 512 | 2.55×10⁻⁷ |
| 838 | 衍射包围能量 | 512、1024 | 1024 | 9.74×10⁻⁵ |

39 条上一轮缺同合同高档参考的衍射包围能量全部重采 256、512 两档。其中 17 条两档仍欠采样，2 条仅 512 档有效。4 条追加 1024 档均取得有效原生曲线。#316 和 #2860 的细档跨软件比较通过，两侧档间仍超原门槛。#562 仍有数值差异。#2787 的两档原生 PSF 已通过，产品 2048 自适应预算内未建立收敛。#1095 的两档原生 MTF 超原门槛，产品专项在本轮计算预算内未形成完整回执。 [初批能量回执](energy-native-captures.json) · [余下 31 条两档回执](extended-energy-native-captures.json) · [四条 1024 档回执](higher-energy-1024-native-captures.json) · [PSF 和 MTF 回执](higher-native-captures.json)

#1295、#2231、#2879 的 256 和 512 档自动窗口有有效文本数值，较窄档只覆盖较宽档约 70.4% 的横轴长度。按 Ansys 的 `Plot Scale` 定义，进一步用 512 和 1024 两档分别重采固定窗口 ±175、±145、±250 µm，原生数值覆盖完全一致。#2879 的原生两档通过，#1295 接近，#2231 差异。三个产品结果在 1024、2048 档均可覆盖固定窗口，档间尚未收敛。#1273 的 256、512 档仍无有效数字。 [自动窗口回执](higher-profile-diagnostics.json) · [固定窗口原生回执](fixed-profile-native-captures.json) · [固定窗口产品回执](fixed-profile-core-receipts.json) · [固定窗口对照](fixed-profile-pair-comparisons.json) · [Ansys FFT 截线设置](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_Cross_Section.html)

并行专项继续覆盖其余 FFT MTF 和 FFT PSF。各条独立范围和收敛状态在台账中分别链接。 [FFT MTF](../residual-reference-followup/fft-mtf-results.json) · [FFT PSF](../fft-psf-native-window/results.json)

额外检索 D 盘已有的 13,447 份原生设置记录，对 87 条当时缺同合同参考的请求和 20 条空截线核对处方 SHA-256、选择器、固定物理设置及原生模型读回，无额外可复用的同合同数值。新采集的原生数据、设置、消息和 Core 数值按成员 SHA-256 去重并复核归档。PSF 数值证据采用原生 API 的 128×128 声明窗口，并保留更大文本矩阵的文件哈希和报告头。专项新增通过均已在最终 Core `8493d6a0…` 复验。 [早期 Core 结果回执](core-control-receipts.json) · [最终 Core 能量回执](extended-energy-core-receipts.json) · [最终 Core 补采与复验回执](higher-energy-and-final-core-replay-receipts.json) · [原始数值证据](new-numeric-evidence.zip)
