# Yolo 单光线、Huygens PSF 与波前覆盖复验

目标 8 项按原处方、配置、波长、视场、采样和容差全部通过。与四项 Huygens PSF 共用计算路径的四条截线也通过。[逐项原设置结果](focused-results.json)

| 原请求 | 镜头与分析 | 结果 |
| --- | --- | --- |
| 2516、2533 | Yolo 单光线逐面报告 | 两条的最大 X 坐标差分别为 `3.5135e-12 mm` 和 `3.5953e-12 mm`，最大几何段长差 `4.8825e-8 mm`。 |
| 1138、1160 | Grid Sag 两个配置的 Huygens PSF | 两个完整 `32×32` 网格最大强度差均为 `6.1871e-8`。 |
| 1344 | Telecentric system 的 Huygens PSF | 最大强度差 `1.4232e-5`，NRMSE `6.7660e-6`。 |
| 2202 | Etalon 的 Huygens PSF | 最大强度差 `1.8720e-8`。 |
| 2430 | 施密特–卡塞格林支架遮挡波前 | 2,282 点逐点配对，共同覆盖 `100%`，最大光程差 `4.0071e-9` 波。 |
| 2498 | 无遮挡 Gregorian 波前 | 2,999 点逐点配对，共同覆盖 `100%`，最大光程差 `1.8484e-6` 波。 |

## 修复与独立检查

- **组合坐标断点**。`PARM 6=0` 按内禀 X、Y、Z 旋转，非零时按内禀 Z、Y、X 旋转。依同一顺序计算反序断点的主光线偏心求解。Yolo 原生第 5、9 面的方向余弦可以独立核对相邻坐标系。两个顺序分别有合成回归。[OpticStudio 坐标断点约定](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Coordinate_Break.html)
- **零参考球半径**。远心系统的出瞳位于无穷远，采用实际像方光线方向和有限锥角的平面极限，仍做逐点相干叠加。同介质、轴上准直、瞳面与像面重合的样例单列为瞳面读出，窗口须完整位于已验证的无中心遮挡圆形通光域。此读出只在 Huygens PSF 及其截线路径启用。原 Huygens 图的非有限强度保护保留，FFT 适用条件仍由原规则判定。
- **施密特–卡塞格林**。原生同一 64 档网格内的 3,001 个圆瞳节点中，421 个被圆形遮挡，298 个被第 5 面支架挡住，剩余 2,282 个有效。增加 `SPID` 三臂等分遮挡的导入、单位换算、快照和 ZMX 往返。每条臂从局部中心向外延伸，首臂沿局部 +X。 [OpticStudio 孔径说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Aperture_surface_properties.html)
- **Gregorian**。后台原生真实光线的 3,001 个节点全部通光。旧批量路径在次镜遇到两个均满足弧矢面的正向交点时选择较近的根，落到了实际通光区外。批量与标量统一使用顺序面顶点附近的已验证解。波前显示网格按原约定保留 2,999 个节点，修复后与原生波前掩码完全一致。

两组后台原生固定节点和捕获时的实际孔径数据在[原始证据包](native-mask-controls.zip)。每个载荷的 SHA-256 经压缩包读回校验，输入处方哈希保持。[掩码差异与归档回执](native-mask-summary.json)

## 95 支菜单镜头复验

95 支菜单镜头完整列入统计，94 支导入成功，共 114 个配置。原矩阵实际请求来自 93 支，3,024 项中执行 2,904 项。对照策略 SHA-256 仍为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。

| 原策略结论 | 上轮 | 本轮 |
| --- | ---: | ---: |
| 通过 | 2,419 | 2,461 |
| 接近但未通过 | 38 | 25 |
| 数值差异 | 80 | 67 |
| 不可比 | 423 | 415 |
| 求值错误 | 64 | 56 |

新增 42 项通过，旧通过项全部保持。15 项无焦角度／屈光度补充验收继续独立通过。独立固定光线维持 143 组通过、108 组不可比，共 259,395 次逐面比较。30,700 个冻结输入和原生参考载荷经原权威归档 SHA-256 核验。

Core、App 和数值核查使用同一个 Core SHA-256 `9c73db9d1a606e1f5fa1eafba6a063c1a52b7d9343d2395e9a85c2f22dc64e03`。81 项 Core／App 定向测试和 18 项对照工具测试通过。[Core／App 回执](core-verified.trx) · [对照工具回执](comparison-final.trx)

全量核对沿用原请求指纹及阈值，并逐条保留非通过结果的请求、配置、分析、阻断原因和误差指标。[汇总](summary.json) · [逐镜头](by-lens.csv) · [判定变动](verdict-changes.csv) · [逐项剩余请求](remaining-requests.csv) · [固定光线余项](remaining-fixed-rays.csv)

剩余 563 项分别为 25 项接近、67 项差异、415 项不可比和 56 项求值错误。差异较集中的镜头包括 Grid GRIN 原冻结低采样请求 15 项、MEMS 6 项、分光立方体 6 项、反射变焦 5 项。Grid GRIN 的后续收敛参考及独立通过结论继续按各自设置记账。[既有 Grid GRIN 收敛验证](../../2026-11-06/grid-grin-bounded-fft-reference/README.md)
