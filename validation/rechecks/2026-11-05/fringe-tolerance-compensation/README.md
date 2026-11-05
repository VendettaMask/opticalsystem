# 面形／条纹公差与镜组装调补偿

范围为标准折射或反射曲面和平面、同面叠加 TFRN/TIRR、有界镜组 X/Y 偏心和倾斜。实际入口、物理约定、单位与 v4 文件见[公差说明](../../../../docs/TOLERANCING.md)。

## 验证内容

- 检查 50 mm、−50 mm 和平面三种名义半径。以 6 mm 净口径半径和 0.6328 µm 波长验证正端点和缩放后的负端点，以及原几何恢复。
- 两种行顺序都验证 TFRN 与 TIRR 能叠加。检查边缘矢高、真实法线、种子对应的像散方位和恢复。
- Cooke 三片镜的两个条纹操作数改变正式几何 MTF 的灵敏度结果，四次 Monte Carlo 的串行和双线程结果一致。
- 三次模拟装配同时包含偏心与倾斜。镜组姿态和末段空气间隔补偿共同参与，补偿后各次 RMS 点列半径均下降，调整值在行程内。系统修订和活动处方保留。
- 验证坏检测波长、净口径越界、非实体边界、同面 TRAD/TFRN 重复、组区间非法和补偿冲突。
- 离屏验证向导生成、操作数属性应用、报告、v4 文件往返与 v1–v3 读取。实际窗口含[条纹编辑器](fringe-editor-light-840.png)、[640 宽度向导](fringe-wizard-light-640.png)和[480 宽度补偿页](mtf-tolerance-light-480-compensation.png)。

首轮定向运行 [89/89](fringe-tolerancing.trx) 通过。公差与回归 [91/91](fringe-tolerancing-final.trx) 通过。窄窗首轮发现[曲率类型选项被截断](fringe-wizard-light-640-before.png)，改为独立行后在 150% 缩放复核，[18/18](fringe-tolerancing-ui.trx) 通过。中英日选项切换和窄窗复验 [7/7](fringe-tolerancing-ui-localized.trx) 通过，三语词库检查 0 漏译、0 错误。

未进行原生 OpticStudio 同处方数值对照。本轮未覆盖非标准名义几何、Zernike／ISO 面形或自定义机械支点与多步装调脚本。
