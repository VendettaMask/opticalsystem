# 可加工性评估界面整理（2026-10-29）

本次只调整桌面评估页的布局、展示文本和本地化。`OpticalManufacturingModel`、共享几何计算、65 点厚度采样以及五个阈值的默认值和取值范围均未修改。

## 页面

- 顶部显示总体状态及玻璃元件、通过项、需评审项、不可加工项。后三项仍是规则结论数量，不改算为元件数量。
- 规则标签与数字输入框成组换行，重新评估使用现有主题的主操作样式。
- 宽页面左右显示完整指标表和规则结论卡片；不足 1120 DIP 时改为上下排列，整页和两个内容区均可滚动。
- 卡片优先展示错误和警告，保留元件、表面、检查项、实测值和可选择的工艺建议文本。无元件、文件切换、语言切换和评估失败会清除旧内容或更新状态。
- 指标表取消纵向网格线，数值与说明分配合理宽度，文本可换行，行高按实际内容增长。
- 继续使用全局主题语义颜色；新增文字支持中文、英文与日文。展示层翻译不解析或重新计算数值。

## 验证

默认 Debug 输出构建通过。相关制造及界面回归 **29/29** 通过，零失败、零跳过，见 [合并回归结果](manufacturability-layout.trx)。最终布局的四项界面回归另行重跑，**4/4** 通过，结果见 [最终界面结果](manufacturability-ui-final.trx)。

界面用例使用 Cooke 示例的真实评估结果，核对所有指标和结论数量、宽窄布局、主操作可见性、阈值重新评估、语言切换及新建空白工程后无旧数据。截图采用 Avalonia Headless + Skia，不打开或操控实际桌面窗口；不据此宣称完成原生交互验收。

- [浅色中文，1440 × 960](images/manufacturability-Light-zh-CN-1440.png)
- [深色英文，1440 × 960](images/manufacturability-Dark-en-US-1440.png)
- [大字体日文，680 × 760，上部](images/manufacturability-Light-ja-JP-680.png)
- [大字体日文，680 × 760，下部结论](images/manufacturability-Light-ja-JP-680-findings.png)

运行范围：`ManufacturabilityPanelLayoutTests`、`ManufacturingDrawingTests`、`ManufacturingInvalidThicknessTests`；最终细节只重跑 `ManufacturabilityPanelLayoutTests`。没有重跑全仓测试，没有修改处方或发布安装包。
