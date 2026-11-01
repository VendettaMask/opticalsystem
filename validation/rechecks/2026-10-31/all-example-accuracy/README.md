# 全部 95 支菜单示例：本轮结果与证据

范围已由用户确认：软件“文件 → 示例”的 14 类、95 支 ZMX。865 份历史工程夹具不纳入本轮。

本轮同一冻结 Core 重新导入全部文件，94 支/114 配置可用，1 支物方锥角孔径阻断。原 3,024 行主账目中 2,904 个可执行请求完成新计算；对原 OpticStudio 2026 R1 数值参考得到 **1,204 Pass、22 Close、119 Difference、352 Incomparable、1,327 Error**。其中 1,098 个 Error 为未支持模型能力，2 个为 F-Tan ≥90° 定义拒绝，不能全称为精度算法错误。

独立逐面光线 251 组为 **130 Pass、12 Difference、109 Incomparable**，257,019 次有效表面比较。不可比来自 86 组缺有效旧原生参考、23 组折叠面号对应未建立；旧 119 个逐面 Pass 全部保持。原分析 1,102 个 Pass 保持 1,089，**13 项未保持，涉及 7 支镜头**；另有 115 项新 Pass，净增不能掩盖未保持项。

另以完全相同 Core 复核已建立的无焦 170 项：**163 Pass、7 不适用或不可比**，495 分量、314,478 对齐值，逐点超限 0。该集合与旧主矩阵分别统计，不能直接相加。MTF 随视场的同日专项仍按各自冻结 Core/收敛范围记录，本轮不冒充其全部重跑。

查看：

- [结论与改善优先级](../../../../docs/ALL_EXAMPLE_ACCURACY_2026-10-31.md)、[95 支逐镜头表](BY_LENS.md)、[逐镜头 CSV](lens-results.csv)。
- [全部请求与原因](request-results.csv)、[逐分析汇总](analysis-summary.csv)、[类别汇总](categories.json)、[错误分类](failure-taxonomy.json)。
- [原通过保持](prior-pass-audit.json)、[光阑/主光线归因](regression-probe.json)、[逐面结果](ray-results.json)、[无焦独立汇总](afocal-summary.json)、[完整摘要](summary.json)。

6 支在本轮既定请求及逐面光线全部通过；仍不等于所有分析、全部视场/波长/采样或收敛认证。24 支至少一项 Difference/Close，58 支至少一项原始 Error，两组可以重叠。逐镜头“首要状态”是互斥分类，不能把其中单列的“有数值差异”支数误当全部差异镜头数。

数值 Core SHA-256：`cf9d428fc6ae02b4629e0f747ad9de78a206c94bd67decad3d8168c6814c44c6`。原策略 SHA-256：`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。本轮复用冻结原生数组，原文件、数据、请求共 **15,783** 个载荷按不可变原生清单核验；不是新采集全部 Zemax 参考。

[本轮证据 ZIP](evidence-current.zip)：SHA-256 **`8ee6068cb8e0516a657dbc41c5a822a155e17fdd3b5d73b22a122ed7f9a172c6`**，27,728 项、222,503,772 字节，全部按 SHA-256/长度读回核验。见 [成员清单](manifest.json)及[核验结果](verification.json)。包内包含当前主矩阵与无焦实际输出、固定光线输入/输出、比较指标、源文件、诊断器、冻结运行二进制和失败阶段；派生图不作为数值依据。

主要依赖是[原 95 支完整原生证据](../../../repairs/2026-10-21/sequential-corpus-full-evidence.zip)，SHA-256 `1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad`；无焦原参考、基线档案及各成员身份列于本包 `afocal-study/input-identity.json`、原请求账目和相关恢复记录。实际计算身份以 `study/identity.json`、每请求 provenance/execution 及 `afocal-study/runtime-identity.json` 为准；准备记录中的历史身份不冒充当前组件。

早期诊断编译、JSON 数字表示身份误判、无焦离屏绘图库缺失和封存器自身日志占用均保留为失败阶段，未混入最终统计。补齐绘图库后只重做结果评估，没有重新计算或改写 170 项光学数组。13 项诊断中的取消光阑只用于归因，未供给任何正式通过结论。

全部使用纯 C#/.NET 与共享 Core，原容差保持，全程后台，没有桌面控制、原生 UI 验收、全量单元测试、发布、提交或推送。后续先处理原通过未保持、逐面残差与无焦 Huygens 收敛，再补高影响能力和参考覆盖。
