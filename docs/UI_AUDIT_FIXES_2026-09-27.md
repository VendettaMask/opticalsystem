# UI 审查问题修复（2026-09-27—28）

任务于 09-27 开始，最终验证于 09-28 完成；产物目录保留任务开始日期。

本次修复审查中确认的四项问题，保留原布局、列宽、页签顺序、侧栏与材料行蓝底规则。未修改 Core 光学计算，也未处理本次未选择的中文命令搜索问题。

## 已实现行为

| 问题 | 修复后行为 | 实现位置 |
| --- | --- | --- |
| P1：锁定后将 F 数 4.5 改为 5，解锁仍把 4.5 结果标为“已同步” | 锁定期间文档变化即标过期；解锁比较结果来源修订与当前修订，旧结果提示“结果已过期，请同步”；重新计算成功后才显示已同步。切换文件仍立即清空锁定页内容 | `AnalysisPanel.cs` |
| P1：展开属性、滚动后物面/光阑文字错行 | 可复用单元格绑定当前 `SurfaceEditorRow.SurfaceRole/SurfaceType`；角色和面型随 DataContext 更新 | `LensEditorPanel.cs` |
| P2：输入 `abc` 被静默撤回且提示已更新 | 曲率、厚度、净口径先验证再提交；失败保留输入并提供错误边框、悬停说明与无障碍帮助，不产生工作区更新。修正后失焦提交，Escape 恢复原显示值 | `EditorRows.cs`、`LensEditorPanel.cs` |
| P2：报告默认打开空白绘图 | 通用结果按实际内容选择默认页：有点数据选绘图，否则有表格/摘要行选数据，仅有正文选文本。页签顺序不变，专用控件继续按类型化 PresentationKind 分派 | `AnalysisPanel.Results.cs` |

数字输入保留当前文化与 invariant 解析、完整存储精度和曲率平面别名。拒绝 NaN/溢出；正无限厚度只允许物面。净口径保留既有 `0.1` 下限，改为明确拒绝低于下限的输入。拾取值及自动净口径的只读行为保持不变。紧凑单元格以红色边框和悬停说明反馈错误，关闭模板额外的错误文字行，保证输入内容可见且不改变表格行高。

## 验证范围

回归覆盖真实 Avalonia 键盘输入与失焦、Escape、修正提交、撤销、错误边框；Tessar 锁定后 F 数变更、解锁、重算与文件切换；展开属性后反复滚动、行复用与材料蓝底；四种报告、点列图、矩阵点列图、波前图及本地化标题不影响默认页选择。

最终默认输出验证：

| 范围 | 结果 | 证据 |
| --- | --- | --- |
| 正式解决方案 Debug / Release 构建 | 各 0 警告、0 错误 | [Debug 日志](../artifacts/validation/ui-audit-fixes-20260927/build-debug-final.log)、[Release 日志](../artifacts/validation/ui-audit-fixes-20260927/build-release-final.log) |
| UI 与相邻分析回归 | Debug 124/124，Release 124/124，均零失败、零跳过 | [Debug TRX](../artifacts/validation/ui-audit-fixes-20260927/ui-audit-Debug-final.trx)、[Release TRX](../artifacts/validation/ui-audit-fixes-20260927/ui-audit-Release-final.trx) |
| 实际 Skia 控件渲染 | 12/12，零失败、零跳过，生成 9 张 PNG | [渲染 TRX](../artifacts/validation/ui-audit-fixes-20260927/ui-audit-skia-final.trx) |
| 源码格式与差异 | 修改的 C# 文件 whitespace 校验、git diff --check 通过 | 仅校验本次修改文件 |

本次新增 16 个回归用例，扩充既有材料行测试的实际滚动角色断言。上述测试集重叠，不能相加为全仓通过数。[机器可读验证记录](../artifacts/validation/ui-audit-fixes-20260927/verification.json)保存最终计数、默认输出哈希和源码哈希。正式全量、实验室、Zemax 基线完整性及外部数值比较未运行；本次 Workbench 重算仅服务于上述 UI 回归，不能作为 Zemax 数值精度结论。

初次运行受沙箱本机通信权限限制；获得运行权限后，新增异步测试因离开 Headless 调度生命周期而停滞，已调整为单个异步 Dispatch。随后首次完整定向运行的两项撤销断言发现样例自动口径在撤销重算时更新；测试现先通过正常处方事务建立重算后的基准，再严格比较整行恢复值，未修改 Core 或放宽精度。Skia 检查另发现默认验证文字挤压输入框，已补充输入文字可见与行高不变断言并修正模板。

## 复现命令

```sh
dotnet build OptilandWorkbench.slnx -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build OptilandWorkbench.slnx -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
ui_audit_filter='FullyQualifiedName~UiAuditRegressionTests|FullyQualifiedName~LensMaterialRowThemeTests|FullyQualifiedName~SurfaceEditorRowTests|FullyQualifiedName~LensEditorLayoutTests|FullyQualifiedName~RadiusSolveUiTests|FullyQualifiedName~SurfaceSolveUiTests|FullyQualifiedName~SurfacePropertiesPanelTests|FullyQualifiedName~BlueThemeInteractionTests|FullyQualifiedName~ViewerInteractionTests|FullyQualifiedName~AnalysisGuiContractTests|FullyQualifiedName~SeidelReportPresentationTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-build --no-restore --filter "$ui_audit_filter"
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-build --no-restore --filter "$ui_audit_filter"
```

Skia 渲染在独立测试进程中运行，不能与使用假绘制的主题用例混用字体缓存：

```sh
OPTILAND_UI_AUDIT_CAPTURE_DIR="$PWD/artifacts/validation/ui-audit-fixes-20260927/rendered" \
OPTILAND_MATERIAL_ROWS_CAPTURE_DIR="$PWD/artifacts/validation/ui-audit-fixes-20260927/rendered" \
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~UiAuditRegressionTests|FullyQualifiedName~LensMaterialRowThemeTests'
```

## 修复后的控件渲染

以下为实际 Avalonia/Skia 控件渲染，非概念图、非原生桌面截图；保持原面板结构。没有进行本轮 Windows/Linux 或原生 macOS 全流程验收。

- [系统摘要默认显示数据](../artifacts/validation/ui-audit-fixes-20260927/rendered/first-order-default-data.png)
- [解锁后旧结果明确提示过期](../artifacts/validation/ui-audit-fixes-20260927/rendered/unlocked-stale-result.png)
- [非法曲率保留 abc 并显示错误边框](../artifacts/validation/ui-audit-fixes-20260927/rendered/RadiusSolveCell-invalid.png)
- [非法厚度](../artifacts/validation/ui-audit-fixes-20260927/rendered/ThicknessSolveCell-invalid.png)、[非法净口径](../artifacts/validation/ui-audit-fixes-20260927/rendered/SemiDiameterSolveCell-invalid.png)
- [展开属性、滚动返回后的角色](../artifacts/validation/ui-audit-fixes-20260927/rendered/material-rows-recycled-roles.png)
- [滚动到光阑附近](../artifacts/validation/ui-audit-fixes-20260927/rendered/material-rows-recycled-stop.png)、[滚动到像面](../artifacts/validation/ui-audit-fixes-20260927/rendered/material-rows-recycled-image.png)
