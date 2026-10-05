# 帮助页视觉复核 · 2026-10-03

合并 Debug 回归中的捕获为黑色空白，保留在 `blank-combined-captures`，不作为 UI 证据。独立运行 `OperandHelpPanelRendersDetailsAndAdaptsToNarrowWidth`，明确启用 Skia 后，两项测试通过，重新生成 `screenshots` 下 8 张真实 Avalonia 控件图像。

逐张查看 Light / Dark 各四张：概览、GRIN 功能组、I3GT 搜索详情、680 DIP 窄窗口。三级层级标签、18 项族概览、具体位置与“可计算 · 范围见说明”状态正确显示；详情中保留原生曲面点 Z 和数值尚未验证的说明。普通宽度仍左右分栏；窄窗口沿用原上下布局，长说明在详情区纵向滚动。原侧栏选中、键盘展开与支持状态筛选交互断言通过。

这些图像验证本次帮助内容和已有布局，不能证明材料编辑器、整个桌面工作区或 Zemax 数值等价。
