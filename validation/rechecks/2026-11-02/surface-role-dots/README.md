# 表面角色圆点：2026-11-02

镜头数据表的“表面类型”单元格在左侧加入 8 DIP 圆点，普通面灰蓝、物面蓝、光阑橙、像面青绿。暗色主题使用同色系较亮版本。角色文字位于圆点右侧，具体面型继续靠右；普通行仅省略角色文字。角色按行号、末面和光阑状态判断，优先级与已有角色文字相同，不使用本地化名称或面型名称选色。

`SurfaceRoleIndicator` 每次绘制读取当前行 DataContext，行复用及主题切换均触发重绘；`SurfaceRolePalette` 集中维护角色色，与波长光谱映射分开。圆点不接收命中测试；材料蓝底、选择状态与处方计算保持原路径。

## 验证命令

```powershell
$env:OPTICAL_SYSTEM_DESIGN_LENS_HEADER_CAPTURE_DIR = 'D:\Projects\opticalsystem\validation\rechecks\2026-11-02\surface-role-dots\previews'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~LensHeaderTypographyTests|FullyQualifiedName~LensMaterialRowThemeTests|FullyQualifiedName~OrdinaryAppUiColorsUseThemeResources" --logger "trx;LogFileName=surface-role-dots-final.trx" --results-directory validation/rechecks/2026-11-02/surface-role-dots -m:1 -nodeReuse:false
```

沿用镜头表头定向用例，更新左侧角色文字的预期起点为 22 DIP（8 DIP 内边距 + 8 DIP 圆点 + 6 DIP 间距），覆盖五种主题、13/18 字号、中英日文及 140/240 DIP 列宽。材料行用例覆盖编辑、行复用、文件切换和选择；配色架构检查约束颜色集中在主题层。

## 实际结果

默认 Release App、内置模块依赖及测试项目构建成功。最终 [TRX](surface-role-dots-final.trx) 为 **11 通过、1 失败、0 跳过**，用例运行约 17 秒：

- 10 项 `LensHeaderTypographyTests` 全部通过，含角色与面型对齐、窄列及主题/语言/字号组合。
- 1 项 `LensMaterialRowThemeTests` 通过，含实际编辑、材料蓝底、行复用及文件切换。
- `OrdinaryAppUiColorsUseThemeResources` 失败，报告 `UserGuideMath.cs`、`ExportImageFrame.cs`、`CoatingStackGeometryPlot.cs`、`MainWindow.Geometry.cs`、`SpectrumPlot.cs` 和 `TopologyStructurePlot.cs` 中的既有硬编码颜色。对这六个文件执行 `git diff --name-only HEAD -- ...` 无输出，确认它们均未由本轮修改；未放宽审计或修改无关代码。

沙箱内启动测试时，测试运行器与本机 testhost 连接超时，未执行用例；中止记录单列保留。随后在沙箱外运行相同已编译程序集的后台离屏测试，得到以上最终结果。测试与截图仅使用 Avalonia Headless/Skia，不操作前台窗口，也不进行安装或发布。

实际预览：[亮主题](previews/lens-headers-Light-zh-CN-13.png)、[暗主题](previews/lens-headers-Dark-en-US-13.png)、[亮主题窄列](previews/lens-type-narrow-Light-zh-CN-13.png)。其余预览覆盖另外三种主题与较大字号。
