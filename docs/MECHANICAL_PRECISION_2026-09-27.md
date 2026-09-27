# 机械半直径显示精度修复

本文记录该阶段的实现和验证。后续主动作与紧凑侧栏已完成，跨阶段测试数不相加；当前整合行为及验证范围见 [状态页](CURRENT_STATUS.md)。


镜头数据的“机械半直径”原先直接绑定 `double`，未经过统一数字格式器；表面属性“绘图”摘要另外写死 `0.######`。现已统一使用 `SurfaceEditorRow.MechanicalSemiDiameterDisplay`，由 `NumericDisplayFormatter` 读取当前位数和科学计数法阈值。摘要中配对显示的净半径复用现有 `SemiDiameterDisplay`。

默认最多 3 位小数，不强制补零；用户设置 0、6 或其他位数时跟随该设置，设置刷新同时更新表格和摘要。机械半直径列保持只读，`SortMemberPath` 明确指向原始 `MechanicalSemiDiameter`，避免格式化文本改变数值排序。缺失独立机械半直径时沿用净半径回退语义。不修改模型值、计算、保存格式、原始精度或其他字段的编辑逻辑。

变更文件：

- `src/OptilandWorkbench.App/ViewModels/EditorRows.cs`
- `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs`
- `src/OptilandWorkbench.App/Panels/LensEditorPanel.SurfaceProperties.cs`
- `tests/OptilandWorkbench.Tests/SurfacePropertiesPanelTests.cs`

新增实际 Avalonia 控件回归验证：默认 3 位、6 位、0 位、科学计数法、不补零、净半径回退、表格真实文本、绘图摘要和设置刷新；另以长小数测试行确认 `12.3456789` 在真实单元格与摘要中分别显示为 `12.346` / `12.345679`，避免只有短小数的示例掩盖原始绑定错误。逐步确认原始值及文档修订号不变，并检查只读和数值排序绑定。证据目录为 `artifacts/validation/mechanical-precision-20260927/`。这属于界面显示回归，不表示重新完成全量光学计算或外部数值比较。

2026-09-27 验证：默认 Debug/Release 解决方案构建均零警告、零错误；表面属性、镜头列布局、机械尺寸导入/制造及数字格式相关回归在两种配置下分别 **18/18** 通过，零失败、零跳过，同一测试集不相加。独立 Avalonia Headless + Skia 截图回归 **1/1** 通过，已核对 `captures/mechanical-precision-3.png` 与 `captures/mechanical-precision-6.png`。这些是生产控件的运行渲染，不是原生桌面截图；本轮未重启用户现有窗口。

修改文件的 `dotnet format --verify-no-changes` 和 `git diff --check` 均通过。
