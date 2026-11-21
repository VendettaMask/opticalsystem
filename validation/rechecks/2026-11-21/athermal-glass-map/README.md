# 无热化玻璃图修复

日期：2026-11-21。范围：用户提供的无热化玻璃图并排截图；材料分析通用行为，不属于单个镜头的光学精度认证。

## 实现

- 色光焦使用当前工程的最短、主波长和最长波长，不再固定为 F/d/C；横轴显示 `10⁻³`，使用明确的 `PartsPerThousand` 轴单位。热光焦显示 `10⁻⁶/K`。
- 共享 Core 的 `CatalogThermalOptics` 在每种玻璃目录参考温度、1 atm 下将 TD 的绝对折射率温度导数转换为相对空气的导数，再扣除线膨胀项。保留正/负共振参数、TCE 与忽略热膨胀标志，拒绝缺失、非有限或奇异输入；不改变玻璃色散公式或追迹行为。
- 默认显示色光焦 0–60、热光焦 −30–30，原生“显示全部范围”选择框可恢复全部有限数据的自动范围。数据中保留默认范围外的有效点，同时报告范围内、范围外和缺少所需数据的数量。
- 名称按目录标准/首选/废弃/特殊/熔融状态分类，所选玻璃与参考线继续可见。名称避让只作用于本图；悬停、缩放和平移仍可访问所有点，不删除数据来消除重叠。
- 启用环境折射率匹配且系统压强不是 1 atm 时，明确显示当前分析不可用。

## 源数据边界

初次全库检查确认：部分源 TD 记录本身给出极大的系数，例如 `OHARA-A:TPH55` 的 D0 为 `2.957`，`HIKARI:7054` 的 D0 为 `1.19`，`ANGSTROMLINK:AL-6265-(OKP-850)` 的 D0 为 `-2.299`。这些原值足以产生截图中百万量级的热光焦，不能在没有厂家证据时擅自猜测其应为 ppm。

运行时将 TD 导出的 `|dn/dT| ≥ 0.01/K` 明确列为需核对源目录的记录，并保留原值、名称和全范围显示。此阈值只提供检查提示，不用于剔除、重标度或修改目录。默认窗口让普通光学玻璃可读，未将源目录异常伪装为已经校正。

## 界面意图

光学设计人员需要比较不同材料的色光焦/热光焦并选择消色差、无热化组合。任务主体是散点分布与材料身份；复用原生材料选择、分析图、共享字体、主题输入和次级说明。状态色表达目录分类，坐标单位表达真实比例；保留 Light、Dark、Isekai 原有材质和密度。范围选择采用已有原生复选框，数值/来源提示置于现有信息栏。

## 验证方法

最终默认 Release 主程序增量构建通过，零警告、零错误；复用此前本轮已通过的依赖构建，见[构建日志](build-app-final.log)。直接调用9项新增检查与3项既有材料分析回归，共12项全部通过，见[逐项结果](results.json)、[控制台记录](direct-tests-final.log)与[数值/程序集摘要](summary.json)。涵盖独立有限差分验证相对温度导数、正/负共振、缺失/非有限/奇异热数据、工程波长和比例、范围计数、保留极大源值、压强适用性，以及三主题范围切换往返。

真实离屏控件使用 Headless `SetRenderScaling` 改变窗口 DPI、`CaptureRenderedFrame` 取得实际组合像素，并检查输出宽度：

| 场景 | 默认范围 | 全部范围 |
| --- | --- | --- |
| Light，1500 DIP，100% | [画面](captures/athermal-Light-1500.png) | [画面](captures/athermal-Light-all-range.png) |
| Dark，1100 DIP，150% | [画面](captures/athermal-Dark-1100.png) | [画面](captures/athermal-Dark-all-range.png) |
| Isekai，900 DIP，200% | [画面](captures/athermal-Isekai-900.png) | [画面](captures/athermal-Isekai-all-range.png) |

逐图检查确认窄图的底部图例自动换行、名称不相互遮盖、全部/默认范围往返保留所有点，Isekai 的新标签、图例和说明使用 English/Almendra。本轮 Cooke 工作区的工程波长为0.48/0.55/0.65 μm，因此不能将其散点数值冒充用户截图另一处方的逐点原生比对。

早期独立输出目录构建在复制跨架构 Skia 调试符号时因磁盘空间不足失败，见[失败记录](attempted-isolated-build.log)；仅清理本子任务刚生成、未运行的副本，随后改用现有默认 Release 与轻量测试探针。该失败不计为通过，未删除用户资料或终止用户进程。

新增测试保存在 `tests/OpticalSystemDesign.Tests/AthermalGlassMapTests.cs`。`focused/AthermalMap.csproj` 可只运行本次与既有材料分析测试；磁盘空间紧张时，`probe/AthermalProbe.csproj` 以 `Private=false` 引用已构建的默认 Release 程序和缓存依赖，直接逐项调用相同断言方法，不复制另一份产品/原生调试符号。

直接调用不是完整 VSTest/xUnit 项目运行。没有重启或控制用户窗口，没有运行全仓测试或95支镜头的数值矩阵。

## 一手定义来源

- [Athermal Glass Map，OpticStudio 2026 R1.03](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Athermal_Glass_Map.html)：工程波长、相对折射率温度导数、玻璃参考温度、1 atm 与参考线定义。
- [Index of Refraction Computation](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Index_of_Refraction_Computation.html)：空气模型、绝对/相对折射率转换及有符号共振参数。
- [AGF/BGF File Formats](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v242/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_AGF_BGF_File_Formats.html)：TD 七项、ED 热膨胀字段与五种状态编码。
- [SCHOTT TIE-19](https://media.schott.com/api/public/content/aaa572afd854434fb7b3faa4bc46103f?v=c0f4fa52)：绝对折射率的温度变化和适用范围说明。目录系数保留自己的来源与有效波长范围，不把此资料的典型范围当作所有厂家的通用有效域。
