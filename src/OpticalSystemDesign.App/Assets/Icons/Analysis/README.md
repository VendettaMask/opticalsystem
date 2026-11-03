# 分析分类图标

本项目原创，2026-11-02按用户确认的SVG预览接入。九份SVG是运行时唯一几何来源；不属于旁边的第三方通用图标包，不改写其来源和许可。

| 文件 | 分类 |
| --- | --- |
| [ray-spots.svg](ray-spots.svg) | 光线迹点 |
| [engineering-report.svg](engineering-report.svg) | 报告 |
| [aberration.svg](aberration.svg) | 像差分析 |
| [wavefront.svg](wavefront.svg) | 波前 |
| [point-spread.svg](point-spread.svg) | 点扩散函数 |
| [mtf-transfer.svg](mtf-transfer.svg) | MTF曲线 |
| [rms-curve.svg](rms-curve.svg) | RMS |
| [encircled-energy.svg](encircled-energy.svg) | 圈入能量 |
| [image-analysis.svg](image-analysis.svg) | 扩展图像分析 |

源网格32×32、线宽1.65，圆端点和圆连接；无位图/字体依赖，可独立编辑和缩放。`structure`、`signal`、`secondary`为描边，后两者的`-fill`类为填充。[AnalysisCategoryIcon](../../../Controls/AnalysisCategoryIcon.cs)使用 ThemeRegistry 中各主题的 IconPalette；2026-11-03 起，Dark 按确认的黑洞暖灰/橙金/浅沙金，其他主题分别沿用自身色板，不再将全部暗色主题统一为蓝青色。活动科学图标目录中保留同源的 analysis 前缀副本。新增图元或样式语义时同步原生解析和渲染验证，不静默忽略不支持的元素。

操作范围和实际验证见[图标指南](../../../../../docs/LOCAL_ICONS.md)。
