# Isekai locked font / language controls

2026-11-04 用户明确要求：只改异世界主题，让固定的字体家族选择框及Language按钮呈现灰色不可选状态。

## 实现

这两个已被策略禁止操作的入口增加 `theme-locked-setting` 标记，仅Isekai策略生效时添加。专属样式将实际原生背景、文字、图标和下拉箭头绘为中性灰；其他输入、字形/字号设置及其他主题保持原状态，退出主题清除标记并恢复可用。`LocalIcon.DisabledColor` 为可选的单个图标禁用色，默认空值沿用原路径，只有上述锁定按钮设置覆盖。共享灰色背景/边框使用不可变画刷，支持离屏宿主和多UI线程。

## 最终验证

默认Release App和Tests增量构建成功，零警告/错误；复用已有Core/内置模块产物，见[App构建](build-app.log)与[测试构建](build-tests.log)。

最终单次 **10/10通过，零失败/跳过，26秒**，见[TRX](locked-controls-final.trx)：4项字体/语言策略检查（含两组真实工作台13/18 DIP、150%/200%）；2项实际下拉框跨主题行为；4项图标身份检查。新增断言检查实际模板灰色背景、文字、箭头、图标覆盖和退出主题恢复，保留原设置和工程恢复状态。

Light、Dark、Pixel共 **1989** 个实际使用图标/尺寸组合与[已有基线](../../2026-11-03/icon-theme-identity/unchanged-theme-baseline.json)逐项像素哈希一致。测试使用现有基线的临时硬链接作为只读输入，不重新生成基线；比较收据见[icons/comparison.json](icons/comparison.json)，相同内容不另留重复载荷。

较早[locked-controls.trx](locked-controls.trx)保留4项失败：3项由新增静态可变画刷跨离屏UI线程造成，已改为不可变画刷；1项是图标基线环境变量缺失，已接入既有基线。最终通过记录覆盖这些失败，不累计早期通过数。

## 实际离屏效果

- [字体家族框灰显，字形/字号保持可用](../isekai-popup-font/after/almendra-locked-settings.png)
- [实际工作台Language按钮灰显](../isekai-popup-font/after/almendra-workbench-13.png)
- [18 DIP / 200%工作台](../isekai-popup-font/after/almendra-workbench-18.png)

全程Headless/Skia后台验证，没有重启或控制用户程序，没有修改光学计算、提交、推送或重新打包。不作为全仓或数值认证。
