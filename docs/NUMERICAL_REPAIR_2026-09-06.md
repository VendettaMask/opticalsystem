# Zemax 数值问题修复（2026-09-06）

本文是最初十项适配器修复阶段的历史记录，其计数、配置哈希和测试数量限定于当时运行。后续 54 项扩展的当前实现、完整验证和未解决问题见 [MS-L7 全分析扩展](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md)，不得把本文十项通过推广成所有分析已对齐。

此轮在独立比较工具发现差异后，按“解决问题”的要求修复产品数值路径。当时按用户要求仅保留本地；2026-09-06 后续已授权提交同步，见 [最新记录](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。此前 Python Optiland 移除工作保留；正式产品仍为纯 C#/.NET，计算和文件读写不依赖验证工具或历史资产。

## 根因和修复

| 问题 | 根因 | 修复 |
| --- | --- | --- |
| MS-L7 的 8 项分析抛出工作 F 数异常 | 部分调用没有转发镜头的 `RayAimingEnabled`；FFT MTF 即使已有有效方向 F 数仍无条件计算旧的备用 F 数 | 在圈入能量、衍射/图像分析及艾里斑尺度调用中显式转发瞄准设置；备用尺度按需计算；Huygens 光瞳、偏振光瞳及归一化参考使用一致瞄准方式 |
| 标准点列图 RMS 偏差 | 未瞄准时遗漏边缘光线；非偏振几何统计错误地按材料/表面透射损耗加权 | 标准点列遵循镜头瞄准设置和相同主光线参考；非偏振统计保留入射光瞳/apodization 权重及一次光谱权重；偏振和其它能量分析继续保留物理透射权重 |
| 渐晕计数漏报 | 到达目标面前失败的光线在计数前已被过滤 | 保持样本与入射光线索引对应，发射数计入全部光线，失败/渐晕数包含未到达目标面的光线 |
| 快照恢复改变玻璃折射率 | 目录玻璃只保存名称，恢复时重新解析到另一目录的同名玻璃 | 原生 `catalog_glass` 组件冻结实际色散公式、系数、表格、波长范围、消光系数、制造商与 AGF 元数据；不改变全局目录优先级 |
| FFT PSF 坐标不可比较、显式像面间隔计算不正确 | FFT 中心被标成半像素位置；显式间隔仅对已有 PSF 插值；光瞳中心和数组索引约定不一致 | Application 明确采用 Zemax FFT 采样模式；显式像面间隔先确定傅里叶共轭光瞳间隔再追迹；主光线位于物理零点；保留最后一行/列和正 Nyquist 边界对应的周期样本 |

Core 分析构造器的一般默认值保留。普通数值后端、光线与材料物理公式、优化算法、公差算法没有被替换。上述分析和材料序列化错误的修复会有意改变受影响的结果；不把此前移除 Python 功能时的“数值不变”结论套用到此轮。

## 输入、设置与权威边界

- 主基准：仓库 `artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX`，SHA-256 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。
- 用户已于 2026-09-06 确认本次指定镜头为 `C:\Users\19851\Desktop\[MS-L7](10X大NA大视场).ZMX`，SHA-256 `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`。重新核对后，与下述最终全分析报告的输入以及仓库同名副本字节一致；此前“候选输入”的身份疑问已解除，已有验证结果适用于本次指定文件。
- 本机 OpticStudio 2026 R1、API `260127`、SP0、EnterpriseEdition。所有新运行只使用镜头副本；已提交的旧 Zemax 捕获与 Optiland 0.5.8 冻结数据均未重写。
- 此轮比较配置为 `tools/OptilandWorkbench.ZemaxComparison/comparison-settings.json`，SHA-256 `5e648bf1f14ff2daeea7834089469648cd89b6d52bfca8fc54bc4e861bd315af`，容差没有放宽。
- 单色点列：配置 1、视场 1、波长 1、主光线参考、hexapolar 密度 20（1261 条发射光线）、非偏振。镜头长度单位 mm，RMS/GEO 输出单位 µm。回归断言绝对容差 `2e-8 µm`；工具的公开比较容差另保留其原配置。
- FFT PSF：光瞳 64×64、像面 128×128、像面间隔 0.25 µm、旋转 0、非偏振、Normalize=false。输出强度是相对理想 PSF 的无量纲量，不是绝对辐照度。坐标为 `[-15.75, 16] µm`，主光线对应零起始索引 `(63, 63)`。比较使用原始标量/网格，无拟合平移、翻转、尺度校准或重新归一化，NRMSE 门槛保留 `0.01`。
- Huygens 项采用工具明确记录的 32×32 光瞳/像面；其余设置、单位和容差逐项记录在每个报告的 request/captured-settings 中。这些都是捕获设置，**不是通用 Zemax 默认值**。

原理依据：[OpticStudio 2026 R1 Standard Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Standard_Spot_Diagram.html) 和 [FFT PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)。其中 FFT PSF 明确描述显式 Image Delta 对光瞳采样的影响。

## 数值证据

| 镜头 / 项目 | 修复前 Workbench | 修复后 Workbench | Zemax |
| --- | ---: | ---: | ---: |
| MS-L7 RMS，µm | 0.7977275282012398 | 0.7471048949276571 | 0.7471048941977483 |
| MS-L7 GEO，µm | 1.47236240340286 | 1.4722738652308895 | 1.4722738652562612 |
| 123456 RMS，µm（快照路径） | 0.10590945944287443 | 0.09409428003812383 | 0.0940942801067534 |
| 123456 GEO，µm（快照路径） | 0.16001473887256698 | 0.16650679448933117 | 0.1665067944948616 |

MS-L7 未瞄准时实际有 1141 条有效光线、120 条失败/渐晕；修复后的标准点列按镜头配置瞄准，1261 条全部有效。此前计数把这 120 条隐藏了。

`123456.ZMX` 第一波长的两个材料在旧快照恢复时折射率分别从 `1.7274298300195519` 变为 `1.7273769983817193`、从 `1.6395914016732425` 变为 `1.639561904144352`。新快照保留原始材料参数，两个镜头的直接导入、JSON 快照恢复和配置导入结果一致。

高 NA FFT PSF 原生网格及其设置/环境另保留在 [validation/zemax](../validation/zemax/2026-r1/numerical-repair-2026-09-06/README.md)，供离线数值回归。新增测试也覆盖材料表格、消光、NaN 热学缺项、旧目录名称快照、瞄准异常、光线计数和 FFT 边界插值。

最终新报告：

- [MS-L7 全分析报告](../artifacts/zemax-comparisons/ms-l7-final-2026-09-06/COMPARISON_REPORT.md)：177 个矩阵项，Workbench 有效捕获 69，Zemax 捕获 10；10 Pass、0 Close、0 Difference、59 Incomparable、108 Skipped、0 Error，退出码 0。原先 8 项异常全部消除。另有 1 项不可用输出保留原始结构。9 项原生 JPEG；First Order 文本窗口截图单独记录不可用。
- [123456 十适配器报告](../artifacts/zemax-comparisons/123456-fixed-2026-09-06/COMPARISON_REPORT.md)：10/10 Pass、0 Difference、0 Error，退出码 0。此轮明确只选择十项，其他 167 项跳过，不称作该镜头的全分析运行。原生 JPEG 为 9 项。

| 分析 | MS-L7 最大 NRMSE | 123456 最大 NRMSE |
| --- | ---: | ---: |
| First Order | 1.16276e-16 | 1.66019e-16 |
| Spot Diagram | 9.76983e-10 | 7.29370e-10 |
| Ray Fan | 2.93560e-9 | 8.25207e-9 |
| Pupil Aberration | 0.00124957 | 0.00192796 |
| FFT PSF | 4.24946e-5 | 1.36067e-5 |
| Huygens PSF | 0.00172150 | 0.00295684 |
| MTF | 0.000456201 | 0.00111926 |
| Huygens MTF | 0.00278138 | 0.00324709 |
| Optical Path Difference | 8.51159e-7 | 4.43323e-8 |
| Wavefront | 7.26962e-7 | 4.61868e-8 |

两支源文件及工作副本哈希核验均通过。报告保存两侧原始数据、实际设置、版本、单位、采样、逐物理量容差、规范化记录和程序集指纹。以上“通过”是原容差内通过，不代表逐位相等、任意镜头/视场均等价，或全部 72 个规范分析均已有数值适配器。逐运行摘要与哈希见 [机器可读验证记录](validation/NUMERICAL_REPAIR_2026-09-06.json)。

## 最终验证

| 检查 | 结果 |
| --- | --- |
| 锁定依赖还原 | 正式解决方案 `--locked-mode --force`，使用本地缓存，通过 |
| 完整默认 Debug 构建 | 0 警告、0 错误；占用输出的指定桌面进程已关闭，默认 App 文件已更新 |
| Release 比较工具构建 | 0 警告、0 错误 |
| 完整正式解决方案测试 | 主测试 1212/1212，工具 45/45；合计 1257，0 失败、0 跳过；本轮新增 15 个主测试 |
| 格式 | `dotnet format OptilandWorkbench.slnx --verify-no-changes --no-restore` 通过 |
| 差异空白检查 | `git diff --check` 通过 |

最终日志位于 `artifacts/numerical-fixes`，包括 `restore.log`、`final-build.log`、`final-release-build.log`、`final-full-tests.log`、`final-format.log` 和 `final-capture-all.log`。中途一次回归发现输出键拼写问题，修正后重新运行了上述完整测试；未把此前定向结果作为最终全量验证。

本轮未执行在线 NuGet 漏洞审计（还原使用 `NuGetAudit=false`）、其它操作系统测试、独立初始结构实验室解决方案或旧外部报告工具测试。其旧记录不计入本轮通过数。

## 兼容性和保留项

- 当前读入仍支持旧 `catalog` 名称快照；旧文件此前未保存的系数无法凭空恢复。需要准确重建时应重新导入原始 ZMX/AGF 并保存。
- 新保存的目录玻璃使用 `catalog_glass`；旧版程序不认识该组件，可能拒绝读取新保存的 STAROPT/原生 JSON。容器版本和其它文件格式不变，属于新增组件带来的向后读取限制。
- 非均匀传播的目录玻璃缺少无损快照编码时明确拒绝保存，避免静默改成均匀传播。现有导入目录玻璃使用均匀传播。
- STAROPT、ZMX、CODE V SEQ、OSLO LEN 入口保留；Python Optiland 文件格式和运行功能没有恢复。
- `OptilandWorkbench` 的程序集、命名空间、解决方案和目录历史名称保留，后续品牌重命名单独处理；通用数值后端以及仅供历史回归的固定资料继续保留。
- 生产依赖方向仍是 `App → Application → Core`；独立验证是 `tools → Application/Core`，没有 `src → tools/validation` 反向依赖。
