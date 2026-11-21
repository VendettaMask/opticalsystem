# 材料库公式核查（2026-11-21）

本次是实现核查和当前共享 C# Core 的定向计算，不是新的原生软件数值捕获或全目录精度认证。产品代码未修改。

用户截图中的 `APEL:APL5014CL` 来自内置 AGF 数据库，公式编号为 1，运行时选择 `CatalogGlassMaterial.ZemaxRefractiveIndex` 的 Schott 分支。按公开定义：

`n² = A0 + A1 λ² + A2 λ⁻² + A3 λ⁻⁴ + A4 λ⁻⁶ + A5 λ⁻⁸`，λ 以 μm 为单位。

独立幂次求和与实际 Core 调用在五个波长的差均不超过 `5e-15`；单独改变 A0 会改变计算结果。因此该字段对应真实计算模型。源系数、波长、实际计算值和出处保存在 [audit.json](audit.json)。

| 项目 | 值 |
| --- | --- |
| nF（486.1327 nm） | 1.5513392007635896 |
| nd（587.5618 nm） | 1.544501600284279 |
| nC（656.2725 nm） | 1.5416136986134366 |
| 由 F-d-C 计算的 Vd | 55.98699089030721 |
| 源目录及当前页面的 Nd / Vd | 1.5445 / 56 |

Zemax 2026 R1 官方说明中，目录 Nd/Vd 仅供参考，不参与逐波长折射率计算；模型选择需要对应的色散系数，系数名称随公式变化。这与截图的 Schott 名称及其真实计算含义一致。

核查时页面还有以下实现差距。本记录保留发现时的状态；后续[材料常数与主测试入口修复](../material-constants-and-test-entry/README.md)已接通实际计算按钮和模型对应标签，材料库仍为只读浏览界面：

- “计算 Nd/Vd”按钮仅调用 `ShowSelectedGlass()` 并更改提示文字。对于有 AGF 参考值的材料，`ToGlassMaterialDto()` 优先返回参考 Nd/Vd，按钮没有独立重算。
- 材料库是只读浏览界面；不像官方目录编辑器那样提供模型与系数的联合编辑。
- 当前 AGF 条目的系数标签统一为 A0…A9；Schott 的前六项对应正确，其他公式尚未根据其定义切换为 K/L、A/B/C 等名称。

构建命令：`dotnet build validation/rechecks/2026-11-21/glass-formula-audit/FormulaAudit.csproj -c Release -m:1`；执行生成的 `FormulaAudit.dll` 成功。构建有 NuGet 漏洞数据源不可达的 NU1900 警告，没有编译错误。本次没有启动、控制或重启桌面应用。

出处：

- [The Schott Formula — OpticStudio 2026 R1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Schott_Formula.html)
- [Description of Catalog Data — OpticStudio 2026 R1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Description_of_Catalog_Data.html)
