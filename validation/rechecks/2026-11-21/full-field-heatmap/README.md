# 全视场像差热图样式

用户提供当前圆形图标截图与对比度损失热图作为样式参考。应用默认「显示为：颜色图」，沿用显式图标选项；Core通用构造函数保留原来的图标默认值，应用预设独立选择颜色图。

颜色图发布 typed Heatmap 序列，使用与对比度损失图相同的 AnalysisPlotControl 采样热图样式（内部名称由 PupilHeatmap 泛化为 SampledHeatmap）。每个有限采样值对应一个真实位置上的色块，不填补遗漏/无效点，不平滑或插值生成假数据；坐标保持实际视场中心、范围、比例与单位。色条显示波前差、waves 和实际取值范围。全局科学色图修改只重绘，不重新计算或改动数据。

## 验证

[FullFieldHeatmapTests.cs](../../../../tests/OpticalSystemDesign.Tests/FullFieldHeatmapTests.cs) 对比图标/颜色图的全部坐标、带符号数值和边界，并检查应用默认、实际图纸/色条、全局色图往返以及Light100%、Dark150%、Isekai200%的原生离屏组合画面。另复用5项现有FullFieldAberrationAnalysisTests，9项案例最终全部通过，见[逐项结果](results.json)。与39项表面颜色/属性检查共用[直接测试方法运行器](../surface-colors/probe/Program.cs)，48项按案例去重；不是完整VSTest项目运行。

实际画面：[Light100%](captures/full-field-heatmap-Light-1.0.png)、[Dark150%](captures/full-field-heatmap-Dark-1.5.png)、[Isekai200%](captures/full-field-heatmap-Isekai-2.0.png)。Isekai的坐标、色条和动态图注均检查英文，图纸没有溢出；图标模式与颜色图的所有原始数值完全一致。

默认Release完整构建及最终词库补齐后的[App增量构建](build-final.log)均为0警告、0错误。先前UI用例误填不在产品下拉框内的8×8光瞳采样，修正为允许的16×16后通过；没有修改产品采样限制来迁就测试。原始失败及后续复验保存在[共享验证目录](../surface-colors/README.md)。

这是绘图与应用预设调整，未改变Zernike拟合、波前计算、视场/光瞳采样或数值验收条件；未做全套光学精度矩阵或控制用户桌面。
