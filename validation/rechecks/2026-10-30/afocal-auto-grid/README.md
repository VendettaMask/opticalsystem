# 无焦自动 PSF 网格后续复核（2026-10-30）

> 后续[瞄准镜修复](../riflescope-pupil/README.md)已关闭剩余两项。这里的18/2及归档字节保留原阶段身份；工作区`current-grid-expectations.json`继续维护当前20项通过的测试期望，本阶段原18/2期望仍可从本ZIP按entry-map还原。

**双筒四项自动网格关闭，瞄准镜两项仍不可比；原335点继续为0。** 本轮不代表无焦全部问题关闭或完整170项矩阵重跑。详见[修复与剩余项](../../../../docs/AFOCAL_AUTOMATIC_GRID_REPAIR_2026-10-30.md)。

| 权威文件 | 内容 |
| --- | --- |
| [assessment.json](assessment.json) | 原20请求前后指纹、模型哈希、坐标门槛比例及强度逐点结果、原五截面、测试身份、六份Core、原生失败/有效诊断与范围 |
| [verification.json](verification.json) | 455个逻辑路径/191个唯一内容的长度与SHA-256核对结果 |
| [current-grid-expectations.json](current-grid-expectations.json) | 绑定原清单哈希、原结论与请求指纹的当前测试期望；不是参考数据或修改后的门槛 |
| [evidence.zip](evidence.zip) | 本轮原始数组、前后结果、隔离试验、测试TRX、构建/失败日志、原生九光线两次尝试及源码 |

归档长 **17,078,802 bytes**，SHA-256 **`b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f`**。按内容去重，每个逻辑路径在ZIP中的`entry-map.json`指向实际成员；全部455条记录均重新读取核对长度和哈希，不需要解压出多个相同副本。不含原生专有程序集。

冻结依赖保持原位置，不重复装入本归档：

- [原生源归档](../../2026-10-27/afocal-fft-energy/README.md)：`evidence-current.zip` SHA-256 `235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1`。
- [原335点修复封存](../fft-seam-repair/README.md)：`evidence.zip` SHA-256 `8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1`，仍保持原字节与阶段Core身份。
- [原20请求清单](../../../zemax/2026-r1/afocal-psf-contract-2026-10-28/manifest.json)：SHA-256 `8b19e5b6e4094f5aa0ff0442e7e0de4a179d30fd296609f8bb1cd1df61da1d07`。

最新去重定向测试 **133/133**（108+25），零失败/跳过。早先106项、此前82项不累加。默认App Release及最终诊断构建零警告/错误；六份Core均为`03c251c875d01aab0eccfe72c6a9112c5c67464f2387ca3f51e6dd8ad04c3e52`。

20个原网格由14项逐点通过/6项不可比变为18/2。新增4项合计34,816值全部通过；18项总233,472值通过。原五截面继续0超限。原强度/坐标/NRMSE门槛、请求、原快照、采样和原生字节保持，拒绝网格隐式变形/峰值归一化；反推直径仅作诊断。

原生九光线控制首次因缺元数据`originalKeys`退出2，重试退出0；分别保存原始日志和数据，两次均不计光学Pass。有效重试9条光线×18面、无原生光线错误/渐晕、原ZMX未保存且哈希不变。最终面`CurrentAndChief`数据必须沿方向投影到共同像平面后再比较；早期`native-check.json`的未投影最终面位置误差不用于结论。整理工具时一次缺Regex导入的编译失败日志也保留，随后已修正。

剩余两项瞄准镜自动网格、三项质心能量、五项相对照度双边比较缺口及更广适用域见[问题页](../../../../docs/OPEN_ISSUES_2026-10-08.md)。本轮不控制用户桌面、不修改原生输入、不提交或推送。封存脚本为[Seal-AutomaticGridRepair.ps1](../../../../tools/diagnostics/FftProfileNative20261029/Seal-AutomaticGridRepair.ps1)，禁止覆盖既有归档。
