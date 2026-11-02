# 窗口适配与重复约束复核

行为及审计结论见[专题说明](../../../../docs/DIAGRAM_VIEWPORT_FIT_2026-11-02.md)。本批默认完整适应当前视口；原始1200×900图纸、160高图签和2404×1804 PNG保持。

源码验证使用独立 `artifacts/diagram-fit-20261102/build`，NuGet锁文件写入该工作目录的 `locks`；不改共享锁文件，不关闭或重启用户程序。还原从本机缓存完成，随后完整依赖构建成功才运行 `--no-build --no-restore` 测试。所有图像来自实际安全离屏Avalonia/Skia控件，不是效果图。

新检查覆盖分析、二维布局和三维布局：大/小窗口反复调整、完整纸面四边界与居中、原图/数据/相机保持、菜单切换100%及刷新保持，PNG全像素哈希不变。新菜单项使用离屏Click事件，原有图例/曲线/三维预设指针检查另列；不宣称系统原生窗口验收。

## 构建与验证

- [完整依赖构建](build.log)：Release、win-x64，App/内置模块/测试依赖零警告/错误；[最终测试编译](final-test-build.log)仅重编译修正后的测试，零警告/错误。
- [首轮定向范围](broad-122-of-126-tests.log)：126项中122通过、4失败。[最终复验](final-focused-tests.log)：10/10通过，覆盖全部四个原失败项。共126个不同用例最终有通过记录，六个重复通过不再累计；没有伪称单次126/126运行。
- 三个失败是旧“场景与工具条必须同父层”的断言，改为保持正常字号、屏幕边界与真实点击；一个是本批测试传入无效的波长枚举值，改用实际默认值。首轮至复验未改产品代码，[生产程序集哈希核对](runtime-comparison.json)全部相同，只有Tests程序集按预期改变。
- 覆盖 `DiagramViewportFitTests`、`AnalysisResponsivePlotTests`、`AnalysisLegendLayoutTests`、`UniformDiagramExportTests`、`ViewerImageExportTests` 及 `Viewer3DGeometryTests.ViewButtonsShowLabelsAndTrackTheActualCamera`。包含原生保存接口的离屏替身、取消/快照/过期状态和真实指针命中；不代表系统窗口/文件选择器验收。
- 实际共享Core的Cooke FFT PSF截面（64×64）与两种布局均检查大小窗口反复调整、完整居中边界、模式切换、原图/数据/相机保持及2404×1804 PNG全像素SHA256不变。布局操作控件的屏幕倍率为1，独立于纸面倍率。

[运行摘要](verification-summary.json)、[参与源码哈希](source-hashes.json)、[最终运行时哈希](runtime-hashes.json)、[首轮运行时身份](broad-runtime-hashes.json)和[独立锁文件配置](isolated-locks.props)保留实际范围。源码哈希登记当时共享工作树状态，不把其他会话的Core/报告修改归为本修正。保存[初次编译失败](chrome-compile-failure.log)、[已修复的测试API警告](initial-build-with-test-warning.log)及[缓存还原记录](restore.log)，不累计为通过。

## 实际离屏预览

| 页面 | 大窗口 | 小窗口 |
| --- | --- | --- |
| FFT PSF截面 | [1600×1000](screenshots/analysis-fit-1600x1000.png) | [520×360](screenshots/analysis-fit-520x360.png) |
| 二维布局 | [1500×1050](screenshots/layout-TwoDimensional-1500x1050.png) | [600×440](screenshots/layout-TwoDimensional-600x440.png) |
| 三维布局 | [1500×1050](screenshots/layout-ThreeDimensional-1500x1050.png) | [600×440](screenshots/layout-ThreeDimensional-600x440.png) |

小窗口的纸面文字随整纸等比显示，可切换100%滚动阅读；工具条保持正常大小。不同宽高比产生对称留白，不能拉伸源图。预览来自真实控件，不是设计效果图。

未执行默认输出构建、前台桌面操作、系统多屏/DPI验收、全仓测试、安装发布或远端同步。原图源规范和数值验收均保持；本记录不关闭其他光学精度或云打包问题。只保存本批新证据与哈希，构建缓存和未变运行时不重复封存。
