# Zemax 操作数名称与支持口径复核

> 历史阶段报告：保留当时的实现、命令、测试与失败结果，不代表当前产品验收。最新状态见 [当前状态](CURRENT_STATUS.md)，可执行构建入口见 [构建与发布](BUILD_AND_RELEASE.md)；旧独立实验室架构和命令已被统一内置模块取代。

本页计数和 2057 项验证保留名称审计阶段范围。当前 340 项受限执行、43 项兼容保留（36 已知功能、2 定义待核实、5 未用），执行项仍有模式与原生验证缺口。最新实现与验证见[多配置行表](MCE_ROW_OPERANDS_2026-10-03.md)。

复核日期：2026-10-01。用户要求确认已经列出和实现的操作数是否确实存在于 Zemax。本次核对名称及定义来源，不宣称所有参数和数值已等价。

## 结论

本项目注册表的 **383 个代码全部存在于官方 OpticStudio ZOS-API 2026 R1 的 448 名称枚举**。没有发现这 383 个名称由本程序杜撰。但界面另外包含 **FNUM、RADI、THIC、RWFE 四个本程序自定义操作数**，不属于该官方 MFE 枚举。此前把它们与 Zemax 项混在一起而没有显著标识，容易误导；当前名称、分类和计算说明已明确标成“本程序”。代码和已有项目保存保持兼容。

| 383 个注册名称的证据类型 | 数量 | 处理 |
| --- | ---: | --- |
| 官方字母表有计算或控制定义 | 357 | 按具体参数和模式实施、验证 |
| 官方 InGT/InLT/InVA 族展开为 I1–I6 | 18 | 真实编号代码，不能把族名当四字符代码 |
| HYLD 在官方实际光线分类页有定义 | 1 | 字母表未列出不等于不存在 |
| BIPF、COSA、HACG、QOAC、TRAN 明确标为 Unused | 5 | 仅兼容保存，不列入待实现计算功能 |
| OGSS、SPHD 仅核实官方 API 名称 | 2 | 公开计算定义待核实，不能猜测实现 |

376 项有公开功能定义，另有 5 个未用名称和 2 个定义待核实项。审计时 **291 项有受限可执行路径**；92 项兼容保留应分为 **85 项已知功能待接通、2 项定义待核实、5 项 Unused**。已执行项仍有参数模式和原生数值验收缺口，因此“291 已接通”不是“291 完整兼容”。四个本程序扩展不计入这 291 项。

## 最近新增名称抽查

以下名称均同时存在于官方 API 枚举和手册定义中；功能概括只用于说明名称真实性。

| 组 | 确认名称 | 定义范围 |
| --- | --- | --- |
| 相位 | SPHS、PSLP、DPHS、QSLP | 指定点相位/斜率，孔径相位/斜率统计 |
| 指定点面形 | SSAG、SSLP、SCRV | 矢高、斜率、曲率 |
| 面形统计和制造 | DSAG、DSLP、DCRV、VOLU、TMAS | 面形统计、体积、质量 |
| 能量 | GENC、GENF、ERFP、DENC、DENF | 几何/衍射能量及边缘响应 |
| 惠更斯 PSF | STRH、CEHX、CEHY | Strehl 和质心 |
| 临时系统状态 | PRIM、CVIG、IMSF | 主波长、渐晕因子、评价像面 |

这些名称真实，不代表本程序已经支持所有模式。例如，相位移除倾斜/光焦度、面形最佳拟合球面、部分偏振/无焦/多配置模式和新增原生 ZMX 列映射仍未完成，详见[实现范围](ZEMAX_OPERAND_SUPPORT.md)。原生列未核对的导入行仍禁用只读，不应被当作完整兼容功能。

## 本次发现并修正的口径问题

1. **本程序扩展未显著区分。** FNUM、RADI、THIC、RWFE 的显示名增加“本程序”，帮助归类为“本程序扩展”，明确不是该版本的 Zemax MFE 操作数。ZMX 文件中的同名系统记录与 MFE 操作数不是同一概念。
2. **未用名称混入待实现数量。** 五项官方 Unused 不执行，也不为它们编造数值公式；保留旧文件往返用途。
3. **仅有 API 名称与有完整定义混为一类。** OGSS/SPHD 标为“定义待核实”；MNAI/MXAI/REQS/RRET/TSAG 等已有定义的条目单独说明。
4. **实机证据表述过强。** 旧文档记载的 448 枚举/442 顺序可选项没有找到完整可重放原始清单；本次独立核对了官方发布的 448 名称，不声称重新运行 Zemax 或重验 `AvailableOperandTypes()`。已提交的 MS-L7 数值捕获和 123456 分析基线继续保留其各自证据范围。
5. **383 不是所有版本的通用总数。** 官方 v261 手册还列有 GOPT，而所查官方 2026 R1 API 枚举不含 GOPT；这是文档/API 版本范围差异，需对应实机进一步确认。不能据一个固定枚举宣称覆盖当前所有 Zemax 功能。

## 可复查证据与验证

- [383 行逐项核对 CSV](../artifacts/validation/zemax-operand-authenticity-20261001/registered-codes.csv)：每项列出名称、官方来源和本程序连接状态。
- [机器审计摘要](../artifacts/validation/zemax-operand-authenticity-20261001/audit.json)：数量、例外、网页 SHA-256 和证据边界。
- [独立官方枚举夹具](../tests/OptilandWorkbench.Tests/Fixtures/zemax-2026-r1-official-merit-operand-catalog.json)：仅保存名称、来源和哈希，不依赖产品注册表生成期望值。
- [构建与测试记录](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)：名称、标识及既有相关回归；不是新 Zemax 数值认证。

官方来源：[2026 R1 API 枚举](https://developer.synopsys.com/docs/zemax-opticstudio-zos-api-2026-r1/reference/namespace_z_o_s_a_p_i_1_1_editors_1_1_m_f_e.md)、[v261 操作数字母表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[v261 实际光线分类（HYLD）](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)。
