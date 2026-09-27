# 平铺子窗口交互与标题按钮修复

本文记录该阶段的实现和验证。后续主动作与紧凑侧栏已完成，跨阶段测试数不相加；当前整合行为及验证范围见 [状态页](CURRENT_STATUS.md)。


针对“平铺全部后关闭图标过大，点击页面变白”，在现有 Avalonia 12.1.0 / Dock 12.0.0.2 架构内修复，没有替换停靠库、业务控件或数据模型。

## 根因与实现

Dock 的 `DocumentDockControl` 同时附加标签和 MDI 两套控件，用 `IsVisible` 切换布局。点击 MDI 内容会更新 `ActiveDockable`，隐藏的标签容器也会创建内容宿主。原 `WorkspaceContentHost` 只判断是否在 `DeferredContentPresenter` 内，附加后立即把同一缓存面板从旧宿主移走，造成可见子窗口空白。新增真实控件回归在修复前重现了面板离开 MDI 视觉树的失败，见证据目录的 `repro.log`。

当前 `WorkspaceViewLocator.cs` 保持原面板实例；宿主根据完整视觉祖先链的可见性决定是否承载内容。祖先显示状态变化时重新判断，隐藏或分离时释放自己的内容，分离时取消全部祖先订阅。隐藏的标签容器不会再抢走 MDI 页面，重新切回标签/平铺或浮动时可重新承载页面。

`App.cs` 中原图标样式只覆盖文档标签和工具标题栏，遗漏了 `MdiDocumentWindow` 模板。现在局部覆盖该模板的最小化、最大化/还原、关闭及工具标题按钮：20 × 20 DIP、4 DIP 内边距、12 DIP 图标资源、无普通按钮边框。保留原主题悬停/按下/键盘焦点及 `CanClose` 权限。MDI 内容边框开启局部裁剪，避免被压窄的页面绘制或命中相邻子窗口；未调整全局控件尺寸、页面字段或光学默认值。

源码变更：`src/OptilandWorkbench.App/Services/WorkspaceViewLocator.cs`、`src/OptilandWorkbench.App/App.cs`。回归：`tests/OptilandWorkbench.Tests/WorkspaceMdiInteractionTests.cs`。原表格业务、未保存公差保护、非脏页关闭释放、再次打开重建的生命周期保持原样。

## 验证范围

新增真实 Avalonia 控件回归覆盖：三页反复点击激活；镜头标注编辑与 Escape 取消；二维“显示光线”切换；公差添加操作及未保存状态；合并标签、层叠、平铺多次往返；隐藏/重显；1050 DIP 窄窗口和 2 倍缩放；浮动后再平铺；亮/暗主题按钮尺寸；悬停/按下和移出释放；键盘焦点、Enter 关闭及重开；禁用最小化；最大化/还原/最小化；镜头页不可关闭。

另已在 macOS 原生验证窗口实际执行“窗口 → 平铺全部”，点击镜头输入框并用 Tab 切换焦点，切换二维“显示光线”，展开设置并操作下拉框 Down/Escape；页面保持显示，Escape 后焦点回到下拉框。原生截图通过本次会话的计算机工具返回。没有关闭用户另一个正在运行的工程进程。

可保存截图由同一生产控件在 Avalonia Headless + Skia 下运行并以 2 倍像素渲染，不是概念图，也不冒充原生桌面截图：

- `artifacts/validation/mdi-interaction-20260927/captures/mdi-three-interactive-panels.png`
- `artifacts/validation/mdi-interaction-20260927/captures/mdi-narrow-2x.png`
- `artifacts/validation/mdi-interaction-20260927/captures/mdi-compact-Light.png`
- `artifacts/validation/mdi-interaction-20260927/captures/mdi-compact-Dark.png`

没有逐一原生走查截图中的全部分析页，也没有重跑正式全量光学数值比较；局部交互回归不替代商业审计和各阶段历史基线。

## 构建与回归结果

2026-09-27，默认 Debug/Release 解决方案构建均成功，零警告、零错误，已更新默认可运行输出。相关测试在两种配置下分别 **53/53** 通过，零失败、零跳过；同一测试集不相加。范围为 `WorkspaceDockModelTests`、`WorkspaceSessionTests`、`WorkspaceMdiInteractionTests`、`DocumentTabCornerTests` 和 `BlueThemeInteractionTests`，覆盖本轮 3 项新增用例及原有工作区/主题交互。

独立 Skia 截图回归 **3/3** 通过；逐张检查了亮/暗紧凑按钮、三页面交互和窄窗口截图。本次三个源码文件的 `dotnet format --verify-no-changes` 与 `git diff --check` 均通过。

截图回归使用独立进程，仅筛选 `WorkspaceMdiInteractionTests` 并设置 `OPTILAND_MDI_CAPTURE_DIR`。首次把启用 Skia 的截图用例与默认 Headless 绘图用例放在同一进程，后续 14 项遇到 `HeadlessPlatformTypeface` 到 `SkiaTypeface` 的类型冲突；该诊断保留于 `mixed-renderer-debug.log/.trx`。统一渲染器后两种配置各 53 项全部通过，截图改为独立进程运行，避免测试框架的全局渲染器状态交叉污染。

证据目录为 `artifacts/validation/mdi-interaction-20260927/`，保留原始复现、默认输出构建日志、Debug/Release TRX、格式检查及截图证据。实际 macOS 操作记录见本次会话。
