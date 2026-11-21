# 离焦 MTF 零位参考线

2026-11-21：根据用户提供的惠更斯及几何离焦 MTF 对比截图，在横轴零位增加贯穿绘图区的实线。傅里叶、惠更斯和几何的公共离焦分析，以及采样离焦分析，均启用已有 `ShowVerticalZeroLine`；线型、0.5 DIP 线宽和颜色沿用公共绘图规则。Application 映射、原生界面和 PNG/报告共用这些绘图选项。

## 验证

- [默认 Release 构建](../through-focus-zero-line-build-serial.log)：主程序和内置依赖编译成功，0 警告、0 错误。
- [定向结果](checks.json)、[运行日志](../through-focus-zero-line-probe-final.log)：13 组直接调用断言检查通过；包含四组已有视场/FFT/离焦步数/Huygens 采样检查、一组采样离焦选项、三组运行时映射，以及五组实际离屏绘图检查。未增加仓库测试基线数量。
- 绘图检查使用真实 `AnalysisPanel`、图例及 `AnalysisPlotControl`，逐项确认竖线在零坐标并贯穿上下边界；隐藏所有曲线仍保留，改变可见范围后位置正确，零坐标移出范围则不绘制。关闭参考线选项作为负对照，确认识别到的确实是该参考线。
- 保存了三类离焦 MTF 的 Light 画面、Huygens Dark 画面和几何 Isekai 画面，以及各自的全隐藏状态。Isekai PNG 使用 2 倍栅格分辨率；本轮未声明完整显示器 DPI 交互验收。

画面：[傅里叶](Fourier-Light.png)、[惠更斯](Huygens-Light.png)、[几何](Geometric-Light.png)、[隐藏全部曲线](Huygens-Light-hidden.png)、[Dark](Huygens-Dark.png)、[Isekai](Geometric-Isekai.png)。为便于快速验证，采用内置 Cooke 模型和低密度采样，不把这些曲线当作用户原文件的数值对照。

首次并行构建未给出诊断且退出失败，随后串行重建成功；验证探针首次编译有一个枚举名称歧义，限定类型后重建零警告、零错误。相应日志保留在本目录上一级的 `through-focus-zero-line-*` 文件中。

本轮修改仅启用绘图选项。没有运行完整 95 支镜头精度矩阵、新原生参考捕获或四种报告格式专项导出；PNG 验证为真实绘图的离屏渲染保存。没有操作或重启用户的桌面程序，也没有替换旧图册中的历史捕获。
