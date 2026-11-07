# Grid Sag 零光焦度下的分析适用性

Grid Sag Example 的两个配置沿用原镜头文件。物方无穷远、唯一视场在轴上，光阑与像面位于同一顶点平面。原处方、上轮的逐面实光线和本次原生模型读回共同确认各面位于同一环境介质且无镀膜、反射或会聚作用。Core 一阶光焦度为零。原分析仍声明有焦像空间。

## 95 支原矩阵复验

3,024 项原请求中复算 2,904 项。按原指纹、原阈值和冻结参考对比，结果为 **2,402 通过、38 接近、77 差异、443 不可比、64 求值错误**。仅六项 Grid Sag 请求由求值错误转为不可比，所有旧通过项保持。30,700 个原输入与参考文件通过哈希核验。独立的 251 组固定光线为 143 组通过、108 组缺有效参考。[逐镜头结果](BY_LENS.md) · [剩余 622 项逐项清单](remaining-requests.csv) · [重点镜头逐项清单](REMAINING_FOCUSED.md) · [固定光线缺口](remaining-fixed-rays.csv) · [判定变化](verdict-changes.json) · [摘要](summary.json)

在独立后台 OpticStudio 2026 R1 Enterprise（build 260127，ZOS-API Server）补采了 20 个请求。其中六个使用原设置，数值数组和轴与冻结捕获一致。其余只更改 FFT 瞳孔采样，单独作为参考诊断。归档内 140 个请求、结果和读回文件逐件核对 SHA-256。[原生控制账目](native-controls.json) · [原生文件归档](native-controls.zip)

| 原请求 | 原生结果及核验 | Workbench 结果 |
| --- | --- | --- |
| 1137、1159，FFT PSF | 64、128、256 三档均报告无效。固定 `0.25 µm` 像元间距的有焦 PSF 需要有限会聚光锥。 | 不适用。保留原生无效诊断。 |
| 1139、1161，FFT MTF | 原 `cycles/mm` 曲线随瞳孔采样显著改变。两个配置在 0.167224 cycles/mm 处从 64 档的 0.735347 降到 2048 档的 0.025546。64 对 2048 全轴 NRMSE 为 0.052284，原通过阈值为 0.01。 | 不适用。明确记录焦面频率标度缺失。 |
| 1148、1170，场曲与畸变 | 两配置都重复输出 101 个零场角和零位移，畸变焦距栏为 `1e9 mm`。缺少有限傍轴焦面和非零畸变基准。 | 不适用。保留原表并记录焦面、视场定义缺失。 |

OpticStudio 的有焦 FFT MTF 使用工作 F 数换算频率；无焦分析使用出瞳和角频率定义。对这条出射准直的光路，提高采样可以减小两次输出的数值差，但无法建立有焦 `cycles/mm` 标度。1024 对 2048 的 NRMSE 为 0.00074759，按原整体门槛达标，逐点最大差仍为 0.0129486。这项数值自比与参考适用性分别记账。[FFT PSF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html) · [FFT MTF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html) · [场曲和畸变](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)

固定采样和自适应分析均保留结构化的 `NotApplicable` 和原因。判断限于明确的轴上、同介质、无光学作用、光阑与像面共面的处方。无焦请求、有限物距以及实际有光焦度的处方沿用原计算入口。比较工具仅在同时获得 Core 适用性状态和独立原生处方证据时，拒绝看似有限的原生曲线。旧固定参考按原字节复算并保留历史判定。[Workbench 与原生对照状态](focal-domain-results.json)

Grid Sag Example 的 44 项原请求为 28 通过、16 不可比。除了上表的 6 项定义边界，Huygens PSF（1138、1160）、Huygens MTF（1140、1162）、Huygens PSF 截线（1142、1164）和通焦 Huygens MTF（1145、1167）仍缺可比网格或完整光扇。相对照度（1146、1168）的原生捕获失败。

首轮全量复算发现截线内部共用 PSF 中间网格，适用性检查误截断了 1141 和 1163。最终将直接二维 PSF 的适用性与截线、线/边缘扩散和能量分析的中间网格入口分开。两个 FFT 截线按原合同分别核验，最大差均为 4.9772×10⁻⁷。其历史截线判定保持独立于二维 PSF 的适用性。

最终版的 Core 与工作台测试 227 项、比较工具测试 23 项均通过。Release App 构建零警告、零错误。App、定向测试和全量复算的 Core SHA-256 均为 `af5adbe7ac5650a3f45b0566edd8bc155ba40872262c9b48ae21fd6aefeb6698`。[Core 与工作台回执](menu-precision-targeted.trx) · [比较工具回执](menu-precision-comparison.trx)
