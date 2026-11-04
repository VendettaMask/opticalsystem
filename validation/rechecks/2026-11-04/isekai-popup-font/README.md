# 异世界弹出菜单补齐 Almendra

用户截图中的主Ribbon已有Almendra，但Samples类别和Objectives子菜单仍为无衬线字体。用生产代码`MainWindow.CreateSequentialExamplesButton`创建真实菜单，在独立离屏窗口展开两个层级，100%/150%/200%均复现：TextBlock实际字体链为`fonts:Inter#Inter, $Default`。见[修复前3项失败](before.trx)、[日志](before.log)和[150%截图](before/examples-menus-1.5.png)。不是字体未打包，而是Fluent弹层重置了主窗口字体继承。

`IsekaiThemeStyles`现在对MenuFlyoutPresenter、FlyoutPresenter、MenuItem、ContextMenu、ToolTip和ComboBoxItem显式设置`ProductFonts.Isekai`。只匹配实际Isekai主题；不设字号、字重或文字内容，不改菜单动作。普通窗口、锁定字体/语言策略、中文补字和独立工程图字体沿用原规则。切换主题后样式自动撤销，已打开的弹层恢复各主题原字体。

## 验证结果

默认Release App、内置双模块依赖及测试构建完成，日志无编译警告/错误。**15个不同定向用例最终通过**，按以下范围去重，不能把各批次数相加：

- [两级菜单修复后3/3](menus-after.trx)，[日志](menus-after.log)：100%/150%/200%，类别标题及Double Gauss菜单文字的真实GlyphTypeface均为Almendra；Light/Dark/Lumen/Pixel各自记录原字体后往返精确恢复，未触发打开示例回调。
- [较大范围批次14通过/1失败](font-regressions.trx)，[日志](font-regressions.log)：包含前述3项、既有Almendra字形/启动/锁定设置4项、普通字体与数值资源4项以及4种弹层。该批次不是15/15；唯一失败是新增ComboBox测试错误要求主题切换后保留同一个模板TextBlock实例。Fluent正常重建模板文字，产品字体规则不受此夹具问题影响。
- [四类弹层最终4/4](popup-surfaces-final.trx)，[日志](popup-surfaces-final.log)：修正上述模板假设后，检查实际字形、标签数量、原字体恢复、原选择值和下拉开合状态。与较大范围批次中3个已通过弹层用例重叠，因此最终为11项其他检查加4项弹层检查，共15个不同用例。

两次前置夹具问题也单独保留：[本地化前类别定位](fixture-category.trx)、[误将Headless覆盖弹层当作独立TopLevel](fixture-overlay.trx)。它们不属于字体修复前后的产品通过/失败统计。正式复现与最终检查使用真实生产菜单控件及Headless覆盖弹层宿主，所有窗口、输入和渲染均在后台离屏执行，没有控制或重启用户应用；未认证原生操作系统弹窗、全仓、光学精度、打包或发布。

## 修复后截图

- [100%两级菜单](after/examples-menus-1.png)
- [150%两级菜单](after/examples-menus-1.5.png)
- [200%两级菜单](after/examples-menus-2.png)
- [普通Flyout](after/popup-flyout.png)、[右键菜单](after/popup-context.png)、[提示](after/popup-tooltip.png)、[下拉选项](after/popup-combo.png)

Headless菜单共享顶层窗口。旧截图辅助逻辑曾以类别/子菜单/合成菜单三个文件名写出同一像素内容；逐文件SHA256确认后只保留合成菜单，核对收据见[capture-deduplication.json](capture-deduplication.json)。科学数据、源图纸和用户提供的原截图均未改写。
