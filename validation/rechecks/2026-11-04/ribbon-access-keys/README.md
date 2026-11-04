# 三个菜单访问键补齐

2026-11-04。`MainWindow.RibbonTab`原映射遗漏加工与图纸、数据库和实验室，三项直接回退为普通标题，因此没有字母；现补为`加工与图纸(_M)`、`数据库(_D)`、`实验室(_L)`。沿用原生AccessText及现有本地化后缀处理，不更改主题字体、语言策略或命令。

- `default-build.log`：首次构建因并行占用`obj/Release/net10.0/OpticalSystemDesign.App.dll`失败。
- `default-build-retry.log`：正常默认Release App构建成功，零警告/错误。
- `tests.log`、`tests/ribbon-access-keys.trx`：31项现有CommandSearch、CompactRibbon、UiLocalization、IsekaiRibbonDetails回归通过，零失败/跳过。主测试项目有两条来自MaterialLibraryPanelTests的非本项警告（旧Bitmap.Save、xUnit2031），未改动该文件。

仅更新三项标题映射、搜索布局夹具和当前指南/规范/状态；没有新增逐行镜像测试、执行全仓或光学回归、启动/重启用户窗口、打包、提交或推送。用户正在运行的版本保持，下一次启动修正后的版本显示新字母。
