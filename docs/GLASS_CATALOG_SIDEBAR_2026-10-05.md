# 玻璃库列表右侧遮挡修复

2026-10-05。已修复系统选项“材料库”下的“当前玻璃库”和“可用玻璃库”右侧遮挡。侧栏继续使用 256 DIP 默认宽度、240–280 DIP 范围；字号、条目高度、顺序和业务操作保持原有设置。

## 原因与实现

内部 ListBox 原先禁用了横向滚动，但仍使用自动隐藏的覆盖式纵向滚动条。条目按整个视口宽度排列，滚动条覆盖了右侧边框和选中背景。实际回归在 240 和 280 DIP 均复现：条目右缘比可见区域右缘多出 16 DIP。

两处列表统一使用纵向 `ScrollBarVisibility.Hidden`，不显示滚动条；横向保持 `Disabled`。鼠标滚轮、键盘选择及程序滚动仍然可用。条目按完整可见宽度排列，保留原有选中、悬停、边框和圆角主题语义。

## 验证

- 默认 Debug、Release 桌面输出均已更新，构建各 0 警告、0 错误。
- Debug、Release 相关回归各 **21/21** 通过，零失败、零跳过。覆盖紧凑侧栏、分组主题切换、玻璃库解析、加入当前库和撤销。
- 扩展已有侧栏用例，检查 240/256/280 DIP、1×/2× 缩放、Light/Dark 主题下两处列表的条目及模板右边界；滚动到中间、末尾、开头后仍不越过视口，横向偏移保持零；验证滚动条不可见、鼠标滚轮确实改变内部纵向偏移，End 键可选择末项并滚动到末尾。分组主题测试的旧六分组断言同步为实际七分组（含偏振）。
- 独立 Skia 实际控件渲染 **2/2** 通过，共 18 张 PNG。列表截图包含两个主题和两处列表；另有两张展开侧栏图。它们是生产 Avalonia 控件离屏渲染，未代表原生 Windows/Linux 桌面验证。
- 修改仅涉及 UI 滚动配置及已有用例断言；没有新增测试身份或修改光学计算。本次没有重新运行累计数值测试。最新累计 **3500/3500** 仍指 [2026-10-04 MTF 阶段](MTF_TOLERANCING_2026-10-04.md) 的 Debug/Release 记录，不能将本次 21 项与之相加。

证据目录为 [`artifacts/validation/glass-catalog-sidebar-hidden-20261005`](../artifacts/validation/glass-catalog-sidebar-hidden-20261005/verification.json)。原始覆盖问题的失败回归保留在前一轮 `glass-catalog-sidebar-20261005/diagnostics/regression.trx`；最终双配置 TRX、构建日志、过滤器和 PNG 在本次证据目录。

可直接查看：[默认宽度可用库](../artifacts/validation/glass-catalog-sidebar-hidden-20261005/screenshots/sidebar-catalog-available-light-256-2x.png)、[最窄侧栏](../artifacts/validation/glass-catalog-sidebar-hidden-20261005/screenshots/sidebar-catalog-available-light-240-1x.png)、[暗夜主题](../artifacts/validation/glass-catalog-sidebar-hidden-20261005/screenshots/sidebar-catalog-available-dark-256-1x.png)、[当前库](../artifacts/validation/glass-catalog-sidebar-hidden-20261005/screenshots/sidebar-catalog-current-light-256-1x.png)。
