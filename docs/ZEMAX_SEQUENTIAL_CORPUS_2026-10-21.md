# 全部 95 支镜头重新对比

2026-10-21 已完成一次全部 Sequential 95 支镜头的新对比：当前正式 Application/Core 重算，本机有许可证的 Zemax OpticStudio 2026 R1 新捕获，原比较策略与容差保持。**主矩阵只有 2 支全部请求通过，另 93 支尚未完整通过。** 不能把能力缺口、不可比、Close 或失败算为通过，也不能将 93 支都归为算法错误。

## 按镜头计数

| 本轮首要状态（互斥） | 支数 |
| --- | ---: |
| 本次请求全部通过 | 2 |
| 可比项通过，仍有不可比 | 18 |
| 存在分析 Difference | 32 |
| 无分析 Difference，但存在逐面光线差异 | 2 |
| 存在计算/采集错误 | 38 |
| 导入/准备阻断 | 3 |
| 合计 | 95 |

两支全通过仍为 Cooke 40 degree field、Inverse telephoto lens，各 30 项分析与 3 组光线通过。分类按首要问题互斥计数；同一镜头可能同时有超差、Close、错误和不可比。完整清单见[95 支逐镜头结果](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/BY_LENS.md)和[逐镜头 CSV](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/lens-results.csv)。

## 实际执行与数值结论

| 范围 | 请求/控制组 | Pass | Close | Difference | 不可比 | Error |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 95 支 ZMX 主分析 | 3024 | 1102 | 25 | 181 | 449 | 1267 |
| 逐面光线尝试 | 251 | 119 | — | 21 | 23 | 88 |
| 两项补充采样控制 | 2 | 0 | 2 | 0 | 0 | 0 |

主矩阵 2904 个可执行软件任务全部完成，1622 个正常返回、1282 个返回计算失败，无超时；其余 120 个分析行保留契约或准备阻断。正常返回不等于比较通过。3279 个原生任务分 66 批执行，每批最多 50 个，包含 2904 个分析、115 个模型、95 个配对格式模型和 165 个逐面光线捕获。25 个原生失败均保留，未发现已识别的 Remoting、内存或启动类基础设施失败，因此没有重试真实无效结果。

1267 个分析 Error 按原日志互斥分类：**1110 未支持模型能力、98 主光线失败、26 瞄准失败、32 其他软件计算失败、1 准备/比较阻断**。不是 1267 个独立算法缺陷。具体日志和[错误分类 CSV](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/failure-matrix.csv)保留原始原因。三个准备阻断涉及 Beam Expander 的未验证 AS 角单位、Apochromat_prism 的坐标断点顺序、Elliptical reflector 的物方锥角孔径；Beam Expander 已有部分准备结果，不能全称为文件导入失败。

请求由旧全量的 2526 增至 3024，增加 498 项，主要因为后续导入修复和无焦契约使更多配置能生成请求。不能直接用总通过数 770→1102 声称旧通过全部保持。

## 原通过项回退

本轮发现 **3 支镜头、29 项互不重复的原通过分析请求未保持**，原请求设置与容差相同：

| 镜头 | 相对已有通过记录的回退数 | 本轮观察 |
| --- | ---: | --- |
| Wide angle lens 200 degree field | 15 | 13 项 Error，另有不可比；3 组逐面光线存在差异 |
| Wide angle lens 210 degree field | 13 | 13 项 Error；3 组逐面光线存在差异 |
| Flat-field Schmidt | 1 | 原 32 档衍射包围能量变为 Close，实际曲线 NRMSE 约 0.00623606 |

两支广角的几何能量出现 `EnergyPlotSampling.Geometric` 的 Range/count 异常，另有追迹未到达像面的失败。这些是观测到的调用位置，尚未定位根因，本轮没有修改光学算法来掩盖失败。

旧 95 支基线的 770 个 Pass 中 **758 保持，12 未保持**；后续 Objectives 基线的 564 个 Pass 中 **536 保持，28 未保持**。两个集合重叠，不能相加；并集为上述 29 项。逐项见[旧全量通过保持审计](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/prior-pass-preservation.json)和[Objectives 通过保持审计](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/objectives-prior-pass-preservation.json)。

此前修复的三项原 32 档及七个可比能量对照在本轮新捕获下仍为 Pass；Double Gauss 5° PSF 截面、Petzval 相对照度/RMS 亦保持。本轮同时暴露的广角回退意味着当前不能把 Objectives 整组表述为全保持。

## 两项补充采样

Apochromat4 的 64/128 瞳/像采样仍为 Close，最大 NRMSE 约 **0.00579340**；Cooke 40° 的 128/256 采样仍为 Close，最大 NRMSE 约 **0.00565586**。这两项与主矩阵单独计数，未改变原采样或容差，见[补充结果](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/additional-sampling-controls.json)。补充任务首次封装误用了路径键名，两个失败尝试已归档；纠正键名后完成正式重算，未改数值请求。

## 完整性与范围

295 个 Documents 原文件全部保持，模型、请求、原生、软件及比较数据均保留；2668 份旧冻结资产在工作区与 HEAD 均保持原聚合身份。计算期间四份实际程序集哈希稳定；光学源代码仍为此前修复提交，身份见[执行记录](../validation/zemax/2026-r1/sequential-corpus-2026-10-21/run-identity.json)。新报告生成器按实际请求数校验，不套用旧 2526 项的固定分母。

此次主矩阵使用原 ZMX；ZOS 仅重新捕获第一配置模型读回，不把旧 13 组 ZOS 数值结果混入本轮。分析覆盖既有 18 类主分析、各正权波长末视场四项控制与 GQ6 逐面光线，不是所有视场、波长、采样或模式的穷举。没有运行无关单元测试、改变容差或重建安装包。

新[原始证据档案](../validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip)含 65871 个载荷，382060824 字节，SHA-256 为 `1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad`，全部读回核对；不含 SDK 二进制。完整执行不等于全部通过，当前剩余项与证据见[机器账目](validation/ZEMAX_SEQUENTIAL_CORPUS_2026-10-21.json)。
