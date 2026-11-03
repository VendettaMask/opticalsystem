# 全程序科学图示图标与主题适配

2026年11月3日，按用户确认的 Optical Scientific Outline 1.1 接入全程序功能图标。活动图标共 **1,773** 个：固定 Lucide 1.25.0 几何规范化得到的 1,748 个通用 SVG，以及 25 个确认的原创稿。原创稿包括公差 6 个、帮助 4 个、窗口管理 6 个和分析分类 9 个。通用几何保留 ISC 来源与 Feather 署名。

工具栏、菜单、页面操作、命令搜索及内置实验室共用原生角色矢量；少量使用字符作为按钮图标的添加、删除、帮助和更多操作入口已改为同一控件。三维视角立方体保留实际视角与选中面语义，按当前主题取轮廓和强调色。

## 主题与状态

五个主题分别注册 ScientificOutline 图标包。普通模式用蓝青，暗夜用黑洞暖灰/橙金/浅沙金，光域用灰青/暖金，异世界用旧金色系，像素主题用海军蓝/红/绿。跟随系统解析到实际 Light 或 Dark。图标几何和原有按钮尺寸、命令顺序、标签、下拉菜单与键盘入口保持一致。

禁用状态取主题禁用色；带色按钮和强调区优先使用可读的宿主前景，必要时回退到主题的可读前景。结构、主要数据和辅助角色不会泄漏上一主题的画刷。实际波长、数据系列、材料和表面角色色保持原有语义。

## 最终验证

默认 Release App 和内置模块依赖构建完成。最终 **53/53** 项定向回归通过，结果见 [theme-icon-final.trx](theme-icon-final.trx)。本次没有运行完整光学数值矩阵。

- 全部 1,773 个 SVG 经原生路径解析，确认不使用位图回退；纯目录查询不依赖图形平台，原生几何在绘制环境建立后按需缓存。
- 原有 1,748 个通用语义及全部确认稿均可解析，未知名称使用同风格问号。
- 同一个图标实例在五种主题之间切换，实际像素中分别出现该主题的三个角色色，禁用状态转为主题禁用色。
- 五主题的带色按钮检查覆盖实际前景和背景切换；像素主题蓝底标题栏使用可读的浅色前景。
- 实际 Double Gauss 工作台在五个主题、13/18 两种字号下逐一访问公差、帮助和窗口菜单，确认 16 个专用图标均实际挂接。切换前后镜头恢复状态一致。
- 其余回归覆盖主题资源、运行时切换、光域与黑洞主题、主操作按钮和系统选项标题。

验证使用后台离屏绘制，没有控制或重启用户的应用窗口。完整截图、18/24/32 DIP 原生图标图集及字体切换截图位于 [previews](previews)。[交互查看](index.html) 可以切换主题和字号；[五主题工具栏总览](theme-overview.png) 仅从原始工作台截图裁出工具栏，不重画界面。

## 来源与复现

制作脚本为 [build-scientific-icons.py](../../../../scripts/build-scientific-icons.py)，规范见 [图标指南](../../../../docs/LOCAL_ICONS.md)。几何、许可、图标目录、最终程序集和测试结果的摘要见 [verification.json](verification.json)。

复现定向检查：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_ICON_CAPTURE_DIR = '<项目根目录>/validation/rechecks/2026-11-03/theme-scientific-icons/previews'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ScientificIconTests|FullyQualifiedName~ThemeResourceTests|FullyQualifiedName~ThemeRuntimeTests|FullyQualifiedName~LumenThemeTests|FullyQualifiedName~BlackHoleDarkThemeTests|FullyQualifiedName~StandardActionButtonThemeTests|FullyQualifiedName~SystemPropertiesPanelSectionThemeTests'
```

早一轮的 theme-icon-regression.trx 记录了旧测试仍假设 Dark 不提供输入框背景的失效断言；最终测试改为核对目的主题实际背景，相关用例已通过。scientific-icons.trx 为首轮 11 项检查，不能与最终 53 项累加为独立总数。
