# 95 支菜单镜头的最后八项焦面 FFT 复验

Axicon 和 Toroid 的原像面不满足有焦 FFT 的近焦前提。两支镜头各有四条原请求，经正式 Workbench 入口验证后报告 `NotApplicable/OutsideFftNearFocus`，不生成数值输出。八条冻结判定均为 `Incomparable`。两个台账分别记录数值比较和独立适用性结论。 [八条原请求和诊断](entry-point-replay.json) [正式入口原始载荷](formal-entry-points.zip)

| 原分析 | Axicon | Toroid |
| --- | ---: | ---: |
| 二维 PSF | #945 | #1365 |
| MTF | #947 | #1367 |
| PSF 中心截线 | #949 | #1369 |
| 衍射包围能量 | #952 | #1372 |

## 光学适用性

Ansys 要求像面足够接近几何焦点，并以数百波长横向像差描述 FFT 可能失准的量级。文档没有规定通用硬阈值。四类请求均选择相同的有焦标量 FFT。冻结原生数据中六项带欠采样诊断，两项截线返回空缓冲。近焦结论有独立实光线与传播对照支持。 [Ansys FFT PSF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html) [Ansys FFT MTF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html) [Axicon 原生回执](../axicon-continuum/fft-near-focus-guard-evidence.json) [Toroid 原生几何与相位](../toroid-fft-phase-domain/README.md)

- Axicon 的 131,072 条原生实光线显示，横向偏差第 5 百分位已达 77,210 波长。独立 FFT 和 Huygens 各自加密后，在原 128×128 像面上的相对 L2 差仍为 0.43017。该差异度量传播模型对原像面形态的影响，信号很弱，不能将它直接套到原绝对强度阈值。 [近焦与角度证据](../axicon-continuum/FFT-NEAR-FOCUS-DESIGN.md) [同窗传播对照](../axicon-continuum/fft-huygens-applicability.json)
- Toroid 的原 401 点理想包围能量对照使用同一瞳孔与相位约束，球面距离传播与 FFT 的 NRMSE 为 0.13365，高于原 0.003。MTF 的八个对照原频点有三个超过原逐点门槛。二维 PSF 和截线显示球面核改变了原窗口的强度形态，原绝对阈值仍覆盖低强度差值。商业 Huygens 数值同合同和全频 MTF 的 NRMSE 不在这组传播核见证的范围内。 [Toroid 四项独立传播核对照](../toroid-near-focus-kernel/README.md)

## 产品判定

正常入口仅对有限平像面、轴上、单波长标量 FFT 检查此条件，并要求主光线近似垂直于像面。追迹 17 个归一化瞳孔探针。四条有效透射光线必须位于同一条瞳孔直径的两侧，半径分别为 0.60 和 0.85。每条相对主光线的横向偏差至少为 10,000 个像方波长，从该光线到主光线像点的垂距导出的精确球面矢高修正至少为 100 个像方波长。这两个内部判定值用于识别严重偏离，和原比较阈值独立。无充分几何证据时保留现有相位采样检查。 [诊断和源代码](../../../../src/OpticalSystemDesign.Core/Analysis/Shared/FocalImageDomain.NearFocus.cs)

| 镜头 | 两个半径的最小横向偏差，波长 | 两个半径的最小球面修正，波 |
| --- | ---: | ---: |
| Axicon | 79,783 / 83,781 | 3,660 / 3,969 |
| Toroid | 16,977 / 24,044 | 433 / 870 |

## 冻结复验与独立台账

重新导入 95 支镜头和 115 个配置。115 份配置、原请求映射及冻结策略与前一轮一致，30,700 份原生与源文件载荷通过 SHA-256 核验。2,904 条可执行原请求均通过正式 Workbench 入口重算。2,844 条输出的逐条数据哈希一致，另外 60 条重新比较。其余 120 条保留原先无可执行规范请求的分类。 [冻结逐条回执](frozen-replay.csv) [95 支镜头](by-lens.csv) [汇总与哈希](summary.json)

| 冻结结果 | 通过 | 接近 | 差异 | 不可比 | 求值错误 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 原设置、原阈值 | 2,527 | 19 | 31 | 427 | 20 |

3,024 条冻结判定逐项相同。按独立台账从 10 条开放项关闭最后 8 条范围内请求，两条被排除的 Tessar 历史条目保留。逐项复核覆盖全部 497 条冻结非通过判定以及一条从冻结接近改为通过且有独立回执的请求。20 条冻结求值错误的独立自适应复验使用原比较门槛，八条截线对旧原生端点的覆盖限制在原回执中单列。 [逐项证据索引](independent-coverage.csv) [独立台账收口核对](closure-audit.json) [保留的两条历史记录](independent-open.csv) [20 条固定档和自适应证据](../../2026-11-07/fft-phase-refinement/README.md)

Twyman Green #2164 在显式 0.004 内部收敛目标下，两条原 401 点曲线均满足原 0.003 NRMSE 判据。默认 0.001 的求值返回未收敛，逐点判据也未通过。此项列为上一轮独立数值通过。 [Twyman Green 两次正式复跑](../twyman-physical-fft-energy/README.md)

默认 Release 产品构建通过，167 项定向测试全部通过，使用与重放一致的 Core。新增回归覆盖四类固定和自适应入口，并用 Twyman Green 的欠采样请求保护 `Unavailable` 与 `NotApplicable` 两类状态。 [后台定向回归](focal-fft-domain-final.trx)

