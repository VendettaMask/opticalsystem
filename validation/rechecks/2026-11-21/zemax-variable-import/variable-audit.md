# 顺序 ZMX 变量全面核对

本页保留“198个同步、22个未支持”的前轮范围。用户后续要求已继续补齐其中16个，当前支持与22项明细见[最新记录](remaining-variables.md)。

2026-11-21。当前记录接续[半径/厚度](README.md)和[圆锥/非球面](shape-variables.md)两个阶段。目标是同步当前模型和优化器支持的变量、保留配置身份，并在原生工程保存重开后保持一致。

## 支持范围

| 源字段 / 面型 | 导入后的变量身份 | 范围 |
| --- | --- | --- |
| `CURV ... 1`、`VDSZ`、`VCON` | 半径、厚度、圆锥系数 | 可编辑物理面；变量厚度的坐标参考面保留，不折叠丢失 |
| `EVENASPH`、`ODDASPHE`、`BINARY_2` 的 `VPAR 1..8` | `c0..c7` | 各面型已有形状系数；不混入相位系数 |
| `XASPHERE`、`XOSPHERE`、`XPOLYNOM` 的 `XDAT 3+` | `c0+` | 扩展偶次、奇次、笛卡尔多项式 |
| `FZERNSAG` 的 `VPAR 1..8` / `XDAT 3+` | `a0..a7` / `z0+` | 基面和 Fringe 系数分别绑定 |
| `GRID_SAG` 的 `VPAR 1..8` | `a0..a7` | 已有基面系数；现有编码器的网格单位限制保留 |
| `TOROIDAL` 的 `VPAR 2..8` | `a0..a6` | 已有可编辑系数；不把另一方向半径当作系数 |
| `QED_TYPE` 的 `XDAT 4+` | `q0+` | 已有 Forbes normal-slope 模型 |
| `CHEBYSHV` 的 `XDAT 1+` | `c0+` | 已有 Chebyshev 系数 |
| `BINARY_3` 的交错 `XDAT` 形状槽 | `i0+` / `o0+` | 内/外区形状系数，保持另一分区及边界值 |
| `GRINSUR1` 的 `VPAR 2/3/4` | `n0` / `nr2` / `nr1` | 保留材料变量及范围；`nr1` 因现有标量近轴限制明确禁用 |
| 多配置 `THIC` / `CRVT` / `CONN` / `SDIA` | 厚度/曲率/圆锥/半口径单元格 | 每配置独立变量掩码，固定单元格覆盖基础标记 |
| 多配置 `PRAM` / `EDVA`、旧 `PAR1..8` | 按面型映射的形状/GRIN 单元格 | 保留具体系数身份，兼容普通与扩展参数列 |
| 原生工程内已有镀膜层变量 | 倍率/折射率偏移/消光偏移 | 三种现有变量均验证保存重开；不新增 ZMX 物理膜层导入 |

扩展数据同时接受已有普通参数编号别名；只绑定实际模型中存在的系数。项数、归一化半径、坐标变换、玻璃选择、模型玻璃优化、未实现的相位/分区参数等不会被猜测为可优化变量。物面、像面及不可编辑坐标参考属性也有具体诊断。

## 修复的遗漏和一致性问题

- 读取 `XDAT` 行内变量标记，补齐扩展面、Chebyshev、Fringe、Forbes 和双分区形状系数。重导出时将标记放回本表面的正确 `XDAT` 行。
- 将 `PRAM` / `EDVA` 绑定扩展为带稳定参数身份的操作数；配置切换、参数行重排/删除、变量列表及拾取图使用同一身份。修复固定配置值恰好等于基础值时仍随基础变量改变的问题。
- Gradient 1 的材料变量不再被介质安装覆盖；传播同时更新相邻表面的入射介质，各系数独立维护配置继承关系。
- Fringe 基面的半径/圆锥、Chebyshev 球面基底的半径现在真正更新已有不可变几何，并保留全部系数、归一化和偏心设置。未实现这些基面参数的几何不登记无效变量，普通标记和多配置标记都给出诊断。
- 支持有明确映射的原生参数拾取，按源/目标量纲转换比例和偏移，源先于目标求值。显式配置单元格覆盖对应原生拾取。文件未请求的默认后焦规则不参与该依赖图。
- 对目前无法持续更新的跨列/自动口径依赖记录诊断，并阻止会破坏该依赖的变量优化。没有把“半径拾取”错误改为线性“曲率拾取”。
- 未支持标记保留原面号、面型、字段、编号及原因；模型玻璃还保留源 `GLAS` 记录。诊断和受阻参数随快照、克隆、撤销及 STAROPT 保存，文件打开显示提示。带这些诊断的有损 ZMX 导出明确拒绝。
- 文件打开、镜头库安装、模型包转换均传递变量行、配置链接和拾取。最高阶有效小范围继续使用前阶段修正后的非零优化尺度。

形状变量最低要求快照 schema 7 / STAROPT 23；新增参数绑定、GRIN 配置链接和导入诊断最低要求 schema 8 / STAROPT 24。当前程序按[格式契约](../../../../docs/STAROPT_FILE_FORMAT.md)选择实际版本；版本校验拒绝低版本夹带新语义。先前已丢失标记的旧导入工程必须从原始 ZMX 重导入。

## 验证结果

[最终专项 TRX](audit-complete.trx) / [日志](audit-complete-tests.log)：**347 通过，0 失败，0 跳过**，由标准主测试入口筛选14个相关测试类运行，其中本轮全面核对58项。此前[独立定向工程](focused/VariableImport.csproj)的[344项结果](audit-final.trx)及主测试入口的[55项核对](audit-standard.trx)也均通过，属于重叠范围，不累计。独立工程直接链接维护中的测试源，两种入口都引用默认 Release 产品程序集与实际镜头库转换工具，没有复制源码或建立另一套光学实现。

覆盖12类系数面型及 mm/cm/in（网格面沿用现有单位限制）、全零和最高阶系数、原生 JSON / STAROPT / 支持的 ZMX 往返、真实 DLS 联合优化、固定配置单元格、非活动配置变量、形状/GRIN 拾取、应用服务参数行重排/清除/撤销/重开，以及已有半径/厚度求解、梯度材料编辑、跨列拾取和未解析玻璃回归。三种已有镀膜变量单独验证原生工程重开。

[逐文件核对](audit-final-corpus.json)直接读取随附95个顺序 ZMX 原文件，每个文件都比较保存前后所有配置变量的身份/值及诊断，另独立扫描源文件变量标记：

| 结果 | 标记数 |
| --- | ---: |
| 已同步为现有变量 | 198 |
| 明确记录为未支持 | 22 |
| 未处理或未记录 | 0 |
| 合计 | 220 |

95/95文件通过。22个未支持标记包含坐标断点、倾斜面、双区相位/未映射参数、模型玻璃和物/像面属性；不计入同步成功数。全库没有使用“其它配置覆盖”兜底状态。该核对验证变量元数据与保存重开，不是完整95镜头光学精度矩阵或新增原生数值认证。

首轮[结果](audit-first.trx)为288项中282通过、6失败：新增测试删除标记时使用了可跨行的空白正则，误删下一条扩展数据行。改用横向空白后[第二轮](audit-second.trx)289项全通过；随后加入2项应用服务生命周期和53项已有相关回归得到最终344项。失败原件保留，未降低产品断言或数值容差。最终测试构建有一次并发文件锁重试并成功，不中断用户程序。

默认 App 构建的[首次日志](audit-app-build-first.log)记录了另一项并行本地化工作尚未写完时的缺失方法错误；本任务未改动该功能。[后续构建](audit-app-build-before-geometry.log)零警告/错误。末轮347项由标准主测试工程重新编译默认App、全部内置模块和两个镜头转换工具后执行；此前主测试工程的引用阻断是当时状态，后续其他工作已修复，不据此宣称当前仍阻断。补充几何写入检查后通过的347项取代此前344项作为本轮最终范围。

未操作、关闭或重启用户程序，未提交、推送或发布。

## 格式依据与复验

多配置命令的语义和旧 `PAR1..8` 别名核对[官方操作数说明](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Multi_Configuration_Operands.html)；参数/扩展数据列区分见[官方 SURP 说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/SETSURFACEPROPERTY_SURP.html)。具体磁盘标记位置由本地源文件和独立测试输入验证，不把 API 枚举值推定为文件求解代码。玻璃优化的未支持范围参照[官方模型玻璃说明](https://optics.ansys.com/hc/en-us/articles/42661781831571-How-to-use-the-Model-Glass)。

```powershell
dotnet restore validation/rechecks/2026-11-21/zemax-variable-import/focused/VariableImport.csproj --ignore-failed-sources -m:1
dotnet test validation/rechecks/2026-11-21/zemax-variable-import/focused/VariableImport.csproj -c Release --no-restore -m:1
```

受限沙箱内 testhost 的本地连接会超时；正式通过结果使用获准的沙箱外 testhost，仍执行同一产品程序集。

最终标准入口使用 `dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1`，用完整类名筛选上述14个相关类；精确用例身份保存在最终TRX。补充几何写入后，默认App的[最终Release构建](audit-app-build.log)仍为0警告/错误。文档[严格检查](audit-docs.json)覆盖2194份文档，坏链、围栏、仅本机链接、当前指南重复段落均为0；[差异检查](audit-diff-check.log)无空白错误，日志中的CRLF提示不影响通过。最终计数、局部链接复核和验证文件指纹见[机器回执](audit-receipt.json)。
