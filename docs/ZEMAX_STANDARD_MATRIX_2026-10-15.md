# Zemax 官方标准镜头精度复验：2026-10-15

本页保留修复前的 `full-matrix-v1` 数量和证据身份。随后发现 Spot/Huygens 原生分析配置选择器与 MCE 编号不同，部分 Zoom 配置 2/3 捕获实际选择了前一配置；本页这些分析记录不能证明对应配置的数值残差。该工具缺陷、产品通光修复及正确配置补采后的当前结果见[后续修复复验](ZEMAX_ZOOM_WIDE_THROUGHPUT_REPAIR_2026-10-15.md)，本页原始失败数据和证据包保持不变。

本轮已覆盖原 27 文件计划中仍在范围内的 25 份官方镜头，并另验主基准 `123456.ZMX`；独立 Cooke/Tessar 按用户决定排除。26 份文件全部完成导入、原生模型读回与适用项目执行尝试，Zoom 三配置分别登记，共 28 个配置。**没有全部通过，不能据此宣称商业软件级完整数值认证。** 本轮未修改产品计算代码或放宽容差。

## 本轮数值结果

868 项分析请求：**381 Pass、19 Close、32 Difference、300 Incomparable、136 Error**。请求数不是镜头通过数；同一文件的主波长/边场控制可能包含重叠条件，Close 不算 Pass，失败或不可比项目均保留在分母。明细见[误差矩阵](../validation/zemax/2026-r1/standard-matrix-2026-10-15/analysis-matrix.csv)及[机器账本](validation/ZEMAX_STANDARD_MATRIX_2026-10-15.json)。

| 文件（Zoom 合计三配置） | Pass | Close | Difference | Incomparable | Error |
| --- | ---: | ---: | ---: | ---: | ---: |
| Apochromat.zmx | 45 | 1 | 0 | 4 | 0 |
| Apochromat2.zmx | 34 | 0 | 0 | 4 | 0 |
| Apochromat3.zmx | 26 | 0 | 0 | 4 | 0 |
| Apochromat4.zmx | 0 | 0 | 0 | 70 | 0 |
| Double Gauss 28 degree field.zmx | 28 | 0 | 1 | 1 | 0 |
| Double Gauss 5 degree field.zmx | 0 | 0 | 0 | 38 | 0 |
| Doublet using MIL number glasses.zmx | 0 | 0 | 0 | 0 | 38 |
| Doublet.zmx | 26 | 1 | 0 | 3 | 0 |
| Even Asphere.zmx | 18 | 1 | 1 | 2 | 0 |
| Gaussian Quadrature over Annular Pupils.zmx | 15 | 0 | 3 | 4 | 0 |
| Gaussian Quadrature Over Wavelength.ZMX | 29 | 0 | 0 | 1 | 0 |
| Inverse telephoto lens.zmx | 23 | 3 | 1 | 3 | 0 |
| Petzval.zmx | 26 | 2 | 0 | 1 | 1 |
| Relay lens.zmx | 28 | 0 | 1 | 1 | 0 |
| Wide angle lens 100 degree field.zmx | 0 | 0 | 0 | 30 | 0 |
| Wide angle lens 200 degree field.zmx | 0 | 0 | 0 | 30 | 0 |
| Wide angle lens 210 degree field.zmx | 1 | 1 | 6 | 8 | 14 |
| Offner relay.zmx | 18 | 1 | 1 | 2 | 0 |
| Tilted mirror.zmx | 0 | 0 | 0 | 0 | 22 |
| Prism using total internal reflection.zmx | 11 | 4 | 2 | 4 | 1 |
| Zoom lens.zmx | 6 | 2 | 16 | 36 | 30 |
| Schmidt-Cassegrain.zmx | 0 | 0 | 0 | 30 | 0 |
| Beam Expander.ZMX | 0 | 0 | 0 | 22 | 0 |
| Doubly telecentric system.zmx | 19 | 2 | 0 | 1 | 0 |
| Example 2, Double Gauss Experimental Arrangement.ZMX | 0 | 0 | 0 | 0 | 30 |
| 123456.ZMX | 28 | 1 | 0 | 1 | 0 |

91 组配置/波长逐面光线检查：**55 Pass、25 Difference、2 Incomparable、9 Error**。取得 15768 条归一化输入、192672 条原生逐面记录，112265 个共同有效物理表面样本的 673590 个坐标/方向值进入比较。部分坐标断点样本因面号折叠缺少明确对应，单列不可比；不存在有效共同交点时，不以零误差宣称追迹正确。反射的原始局部方向约定尚待对齐，不能把方向符号残差直接解释成物理追迹错误。

## 实际暴露的问题

- **Zoom 多配置与超广角通光。** Zoom 三配置仅 6 项 Pass，16 项 Difference、30 项 Error、36 项 Incomparable、2 项 Close；9 组逐面光线控制都有通光/方向差异。100°/200°/210° 控制也存在通光差异；210° 的部分衍射计算没有有效入瞳。应先核对源处方的配置、解、光阑和大角度发射，不能仅调 MTF/PSF 的显示或采样。
- **材料与面型支持缺口。** MIL 玻璃 `517642`、`648338` 未匹配；Tilted mirror 与有限共轭 Double Gauss arrangement 的 `PARAXIAL` 未映射为可计算面，按能力检查阻断。文件能导入不代表可计算。
- **照度与衍射残差。** Double Gauss、Inverse telephoto 的相对照度仍 Difference；Petzval 在当前密度下孔径边界未解决。Even Asphere MTF NRMSE 约 0.0317763，Relay DEE 约 0.0117417，Annular Pupils DEE 约 0.0124978，均指本批设置。19 项 Close 及原生警告仍需处理。
- **比较契约缺口。** 5 份非 MM 文件及 1 份无焦文件的分析共 220 项因现有适配器边界不可比；另有退化单点曲线、原生诊断警告、场坐标映射、面号对应和反射方向约定问题。棱镜 FFT 截线有一次原生 `Host.Capture` 空引用，是采集适配器错误，不能当成产品光学误差。

## 之前精度问题的进度

以当前 Core 重算四文件旧原生 MFE 控制：FCGS/FCGT **2160/2160** 通过原 `1e-5 mm` 门槛，全组最大绝对差 **3.054520025802532e-8 mm**；没有回退。EFNO/RELI 的 256 个控制仍记录 Difference，EFNO 全档位最大相对差约 **12.537%**，未关闭。此前单分析设置的约 0.309% 不能代表扩展矩阵。此复验使用既有明确设置的原生控制；本轮全文件矩阵则另有新捕获，不混淆二者的捕获时间。

## 设置、独立性与证据

实际原生环境为 OpticStudio **2026 R1 / 26.1 / build 260127 / EnterpriseEdition，有效 API 许可证**。27 份选样原文件和 `123456` 的 SHA-256 均与登记身份相同。每配置运行原 18 项主波长请求、每活动波长 4 项最后定义场控制；采样为保存策略中的 64×64（适用分析），连续场分析保留自身契约。逐面控制为共享 Core 产生的 GQ6 节点、原始渐晕和径向归一化定义场。不覆盖所有场×波长的全部分析，也未完成 32/64/128/256 收敛矩阵。

Zemax 串行、逐请求重新加载只读副本，Workbench 两个隔离进程调用正式 Application/Core。倒序选取 Zoom 配置 3/1、Double Gauss、Apochromat，分别以全新 Zemax 进程补采，4 项 Spot 规范化结果与持续进程结果逐字节一致；这排除了这四个控制上的应用/配置污染，不扩展成全部状态认证。

比较工具完整 Release **201/201**，零失败、零跳过；诊断工具及其依赖构建零警告、零错误。本轮未重跑完整主测试或模块全量，不沿用历史全量计数冒充当前验收。工具说明见[批处理入口](../tools/diagnostics/StandardMatrix20261015/README.md)。

旧 2668 份冻结清单按原路径核验为 **2664/2668**：上游产品更名迁移了四个路径，三份夹具在新路径字节完全一致；维护中的工具配置只改变 JSON 格式，值和容差完全相同，但字节哈希不同。原配置已[按原字节另存](../validation/zemax/2026-r1/standard-matrix-2026-10-15/original-comparison-settings.json)。[路径迁移审计](../validation/zemax/2026-r1/standard-matrix-2026-10-15/frozen-relocation-audit.json)保留失败的旧路径门禁，不把它写成通过，也不恢复退役的产品目录。

原始两侧数组、掩码、CFG、环境/请求、模型、源副本、失败日志、独立控制及 TRX 已保存于[完整压缩证据](../validation/zemax/2026-r1/standard-matrix-2026-10-15/native-and-core-evidence.zip)，附逐文件 SHA-256 且解压内容全部复核。没有专有 API 二进制、第二套光学引擎、拟合增益或修改原生数值。
