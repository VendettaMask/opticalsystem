# RMS离焦零位参考线

2026-11-21：截图中的“RMS vs. 离焦”遗漏了零位竖线。在`RmsVsFocusAnalysis`的已有绘图选项中启用`ShowVerticalZeroLine`，点列半径与波前两种数据共用。使用既有主题参考线色、实线及0.5 DIP线宽；不添加采样点，不改动计算和坐标范围。横轴0在可见范围时贯穿绘图区，隐藏全部曲线后仍保留。

## 实际文件与验证

采用截图中的内置`Objectives/Double Gauss 28 degree field.zmx`，源文件位于仓库`src/OpticalSystemDesign.App/Assets/SequentialExamples`。运行时设置为16个离焦点、−0.01至+0.01 mm、GQ六环，其余沿用本分析默认值。`wavefront-data.json`和`spot-data.json`保存实际设置、映射后的绘图选项及曲线。

`probe.log`、`checks.json`记录10组直接调用断言检查全部通过：2组已有RMS回归、2组运行时设置及DTO映射，以及2种数据×Light/Dark/Isekai的6组真实界面检查。后者逐项确认零坐标位置、完整高度、全部隐藏、关闭选项负对照、非对称范围及零位移出范围。

每种数据/主题保存真实`AnalysisPanel`离屏画面，以及正常和全隐藏的实际`CapturePlotImage`→`SavePlotImage` PNG导出，共18张PNG。已目视检查[波前Light导出](wavefront-Light-export.png)和[Dark全部隐藏](wavefront-Dark-hidden-export.png)，中间参考线清楚且与图例筛选独立。

默认Release主程序构建成功，0警告、0错误，见`default-release-build.log`。这是定向断言运行，未执行完整VSTest或95镜头矩阵；未专项执行报告格式导出，也未替换旧图册的冻结图片。全程后台运行，没有操作、关闭或重启用户程序。

## 复现

先构建默认App输出，再运行：

```powershell
$env:DOTNET_PROCESSOR_COUNT='2'
dotnet run --project validation/rechecks/2026-11-21/rms-focus-zero-line/probe/Probe.csproj -c Release -p:BuildProjectReferences=false -- validation/rechecks/2026-11-21/rms-focus-zero-line 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx'
```
