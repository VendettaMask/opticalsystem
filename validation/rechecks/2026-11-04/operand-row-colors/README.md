# 评价函数操作数行色映射

## 设计意图

用户在评价函数编辑器扫描类型、参数与贡献，需要在长列表中直接识别不同操作数。主体仍是数值表格；类型色作为低饱和整行底色，不增加表格高度、列或装饰。光线、波前、色差、口径、厚度、材料和数学控制沿用现有操作数帮助分类，按语义分色系，代码获得固定色阶。同一代码不随行号、插入、排序或进程改变；同类代码允许共享接近的色阶，不声称数百种颜色可逐一凭肉眼识别。

五个具体主题共用分类，底色从各自主题 Surface 混合，文字保留主题 TextPrimary，包括 Isekai 旧金与 Almendra；字号、密度、表头和科学数据保持。未知代码安静地回退中性色，保留代码与 Status；业务底色不表示可计算性、状态或制造验收。错误红色优先于分类，选中时加强原色并加主题强调轮廓，悬停略加强，焦点继续使用原生指示。

## 实现与核验入口

- `MeritOperandRowPalette` 统一映射，与 `OperandHelpTaxonomy` 共用代码目录；本地扩展 FNUM/RADI/THIC/RWFE 按实际含义归类。
- 原先只赋值 DataGridRow.Background，可能被 Fluent 的不透明 BackgroundRectangle 遮挡；本次绑定实际模板底板，覆盖冻结和滚动区域。离屏复核还发现 Isekai/Pixel 单元格的不透明背景覆盖了正确的 Rectangle.Fill，现局部设为透明。测试同时读取冻结序号、参数和注释格的最终合成像素，避免仅检查属性便误报可见。
- 每个已实现行容器单独观察 Type/Error、主题和交互状态；卸载、复用、面板 Dispose 都解除旧订阅。
- 配色测试导出 `operand-color-map.json` 和可筛选 `operand-color-map.html`。实际生产 OptimizationPanel 的 Headless/Skia 图为 `merit-rows-*.png`；使用禁用数值求值的代表性操作数夹具，不冒充光学性能数据。

## 映射与预览

全部387个代码、五主题的精确 HEX 背景/前景见[可筛选色表](operand-color-map.html)及[JSON映射](operand-color-map.json)。22个色系中的代表项如下；同色系代码可能共享或使用相近色阶，不能逐一只凭颜色识别。

| 操作数 | 语义 / 色系 | Isekai正常底色 | Light正常底色 |
| --- | --- | --- | --- |
| AXCL | 色差 / 玫瑰 | #624249 | #F1E1E9 |
| REAY | 光线 / 蓝 | #374555 | #DDE8F5 |
| EFFL | 一阶 / 青绿 | #2D433A | #E1F0EE |
| DIFF | 数学 / 灰蓝 | #494B49 | #E7EBEE |
| BLNK | 注释 / 中性 | #2C241B | #FBFBFA |
| DMFS | 分段 / 旧金 | #59492D | #F4EFE3 |
| MNCA | 口径 / 橄榄 | #504E2E | #EBEDDE |
| MXCA | 口径 / 橄榄 | #4A482A | #F0F2E6 |
| MNEA | 边缘厚度 / 赭橙 | #573F2B | #F6EEE7 |
| OPDX | 波前 / 紫 | #523F53 | #ECE5F3 |

实际生产面板离屏渲染：[Isekai](merit-rows-Isekai.png)、[150%](merit-rows-Isekai-150.png)、[200%](merit-rows-Isekai-200.png)、[选中及错误](merit-rows-Isekai-error-selected.png)、[Light](merit-rows-Light.png)、[Dark](merit-rows-Dark.png)、[Pixel](merit-rows-Pixel.png)、[Lumen](merit-rows-Lumen.png)、[窄窗](merit-rows-Light-narrow.png)。数值使用显示夹具，不作为光学性能证据。

## 验证结果与边界

- 默认Release App及测试构建0警告、0错误；18项定向用例通过，最终结果为 `operand-row-colors-verified.trx`。包括12个代表分类/大小写用例、全387目录覆盖与10个截图代表代码不同底色、所有388项（含未知）×5主题×8组合的独立WCAG对比度检查、1个真实原生交互/像素用例及3个既有参数编辑用例。
- 原生检查读取实际 BackgroundRectangle.Fill、文字颜色及冻结序号/参数/注释格的最终PNG像素，覆盖悬停、选中、错误、代码修改与类型下拉、主题往返、滚动复用、窄窗和Dispose后不再观察旧数据。色表不代表计算支持状态。
- 较早仅检查行属性/底板的批次虽通过，但预览发现 Isekai/Pixel 单元格遮挡；已修正并增加最终像素断言，未把较早通过数叠加计算。
- 额外全局 `OrdinaryAppUiColorsUseThemeResources` 审计报告其他文件违规，见 `operand-row-colors-final.trx` 及对应日志（18过/1失败）；本次Palette/Appearance/OptimizationPanel/EditorRows不在违规路径中。没有修改这些跨任务文件或放宽审计规则，不能称全仓审计通过。
- 沙箱内VSTest与testhost连接超时的中止记录保留；允许本机连接后正常完成。构建最初还遇到当前工作区缺少已声明的本地语言资源，使用仓库现有Localization工具执行本地sync生成，未改译文或联网。

未操作、关闭或重启用户主程序，未使用DeepSeek，未提交/推送。没有修改光学算法、数据、原口径、像质或制造验收门槛，也不作发布/安装或全仓认证声明。
