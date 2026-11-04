# 2026-11-04 主题切换后窗口按钮消失修复

## 结果与范围

修复后的原复现流程：六种主题设置之间全部 **30/30** 个有序切换正常，按钮消失 **0 次**，无需尺寸变化或手动恢复。修复前相同固定尺寸离屏流程为 **30/30** 次消失。原始定位记录位于工作目录 `D:\optical system design\output\titlebar-theme-repro-20261104\README.md`，本目录是本次产品修复的维护记录。

最终相关测试 **30/30** 通过，零失败/跳过：标题栏9、命令搜索9、本地化搜索3、主题运行时9，见 [最终 TRX](regression-final.trx)。标题栏新增用例内部覆盖普通/最大化各30个主题组合、40批共240次快速主题切换、全屏后切换主题并恢复，以及刷新尚在排队时关闭窗口。每次检查真实三个按钮的可见性、非零尺寸、实际图形、22 DIP 圆形和圆外点击区；不在切换后手动调用框架创建按钮层，也不调整窗口尺寸来让断言通过。

默认 Release App 及必需内置模块构建 **0 警告、0 错误**，见 [构建日志](app-build-after.log)。默认测试输出被此前已运行的 testhost PID 63336 锁定，见 [保留的锁定记录](default-test-build-locked.log)；未停止该进程。测试项目已单独完成 MSBuild `Compile` 目标（Release、退出码0），将实际编译产物和上述默认 App 构建放入独立临时测试目录完成最终执行，不把它称作默认测试输出完整构建通过。

全部验证使用 Avalonia.Headless 和真实 Skia 绘制，不创建或控制原生桌面窗口，不修改用户设置或重启用户软件。Win32 装饰协商/状态回调由测试代理镜像，其余布局、模板、输入命中和生命周期由实际框架控件处理。此结果不替代原生 Windows 多显示器、DPI、Snap 或 macOS/Linux 验收，也不代表全仓测试或安装包发布。

## 原因与实现

Avalonia 12.1 的窗口装饰模板内容变化后，其可视层包装仍可能保留“测量/排列已有效”的缓存。三个按钮在树中且可见标记为 true，但新模板内容没有被测量，按钮 Bounds 为0×0且没有图形子节点。修复前额外60轮布局/渲染和仅重新测量总宿主都不能恢复；重新测量装饰层，或窗口宽度临时变化1 DIP，均可恢复。

[`MainWindowTitleBar`](../../../../src/OpticalSystemDesign.App/Theming/MainWindowTitleBar.cs) 现在在主题/资源变化、打开和窗口状态更新时合并一次延迟布局刷新。待主题事务结束，对平台宿主中与 Window 并列的装饰层标记重新测量，让框架完成正常测量、排列和按钮模板生成。实现使用公开可视树/布局API，不解析私有类型名，不调用框架私有更新方法，不改变窗口尺寸、按钮命令或命中角色。关闭时撤销事件订阅，已排队任务检查关闭状态后退出。

资源变化也单独监听，覆盖资源更新但实际主题变体可能相同的切换；连续切换只保留一个待处理刷新。原交通灯配色、圆形效果、最大化/还原图形和点击区域继续使用原框架控件。此前正在进行的异世界标题材质改动完整保留。

新增测试位于 [`MainWindowTitleBarTests`](../../../../tests/OpticalSystemDesign.Tests/MainWindowTitleBarTests.cs)，测试专用平台代理位于 [`ManagedDecorationHeadlessWindow`](../../../../tests/OpticalSystemDesign.Tests/ManagedDecorationHeadlessWindow.cs)。该代理只在测试项目编译，不进入产品。

## 中间记录

- [修复前回归](regression-before.trx)：新增切换用例3项全部实际失败，普通/最大化均在 Light→Dark 后得到0×0按钮，快速切换首批也失败。
- [首次修复回归](regression-after.trx)：8通过、1失败；普通/最大化30组合及240次快速切换已通过，残留失败是全屏恢复时测试代理仍沿用 Headless 的旧缓存状态路径，没有镜像Win32有效状态Getter与WindowStateChanged回调。补齐测试代理的原生状态协商后，窗口正常自行更新装饰部件；没有在断言前手动修复产品层或放宽断言。
- [最终回归](regression-final.trx)：30通过、0失败/跳过。
- [复现前汇总](before-summary.json)、[修复后汇总](after-summary.json)、[当前综合汇总与程序集SHA-256](summary.json)。

## 实际离屏截图

修复前，深色切回浅色：

![修复前](before-dark-to-light.png)

修复后，相同切换：

![修复后](after-dark-to-light.png)

修复后，浅色切到深色：

![深色切换后](after-light-to-dark.png)
