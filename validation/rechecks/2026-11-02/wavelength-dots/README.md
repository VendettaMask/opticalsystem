# 波长列表色点：2026-11-02

系统选项的波长卡片标题行末尾显示直径 8 DIP 的圆点，波长输入沿用 nm，摘要沿用 μm。圆点按可见光近似色连续插值，修改波长即更新；380–780 nm 范围外显示灰色。圆点不接收命中测试，整行展开/折叠行为保留。

此提示使用 `SpectralColorMap.FromVisibleNanometers`；分析及光线原有 `FromNanometers` 工程辨识色保持。仅修改显示，不更改处方数值、权重或计算结果。

## 定向回归

执行：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CompactSidebarTests|FullyQualifiedName~SystemPropertiesLocalizationTests|FullyQualifiedName~SystemPropertiesPanelSectionThemeTests|FullyQualifiedName~WavelengthSeriesUseStableOpticalEngineeringColorsInsteadOfCategoryOrder" --logger "trx;LogFileName=wavelength-dots.trx" --results-directory validation/rechecks/2026-11-02/wavelength-dots
```

结果：**14 通过、0 失败、0 跳过**。默认 Release App、内置模块依赖及测试项目编译成功。范围包含窄侧栏布局、滚动与展开、主题切换、本地化以及原分析工程配色；未新增全仓测试结论。原始结果见 [TRX](wavelength-dots.trx)。

## 后台离屏预览

使用真实 `WorkbenchApplication`、`SystemPropertiesPanel` 与 Avalonia Headless/Skia；通过实际波长输入控件依次修改第一行，读取处方确认保存为 550、1064 nm。观测日志见 [preview-dark.log](preview-dark.log)。

| 输入 | 圆点 RGB | 观测 |
| --- | --- | --- |
| 588 nm | #FFE000 | 黄 |
| 632 nm | #FF3300 | 橙红 |
| 420 nm | #6A00FF | 紫 |
| 550 nm | #92FF00 | 黄绿，输入修改后立即更新 |
| 1064 nm | #7E8491 | 可见范围外的灰色 |

检查了 [亮主题](light.png)、[暗主题](dark.png)、[展开并改为 550 nm](edited-550nm.png)、[改为 1064 nm](infrared-1064nm.png) 与 [240 DIP 窄侧栏](narrow-240.png)。圆点实际布局为 8×8 DIP，明暗主题保持相同波长颜色，摘要省略时仍保留圆点。亮主题图以 192 DPI 输出，最终暗主题图以 96 DPI 输出。

未操作前台窗口、鼠标或键盘；未安装、重启或发布应用。本轮验证仅覆盖此显示改动。
