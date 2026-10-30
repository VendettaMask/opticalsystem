# 光学设计报告

**DOUBLE GAUSS**

当前工程的系统概览、布局与所选光学分析。各图表和数据均来自导出开始时的同一工程快照。

<a id="section-001"></a>

## 报告概览

| 报告信息 | 内容 |
| --- | --- |
| 导出时间 | 2026-10-30 14:58:58 +08:00 |
| 源文件 | D:\optical system design\opticalsystem\tmp\cross-format-tests\Examples\Sequential\Objectives\Double Gauss 28 degree field.zmx |
| 工程修订 | 1；包含未保存修改：否 |
| 配置范围 | 1 |
| 所选分析 | 离焦点列图、非相干照度 |
| 执行概况 | 2 项完成；0 项失败或不适用 |
| 软件版本 | Optical System Design 1.0.4.0 |

## 目录

- [配置 1](#section-002)
- [导出结果](#section-003)
- [附录 完整参数与计算记录](#section-004)

<a id="section-002"></a>

## 配置 1

### 系统参数

| 参数 | 值 |
| --- | --- |
| EFL / mm | 99.5006790492 |
| F/# | 2.98531890337 |
| TTL / mm | 132.988419345 |
| 入瞳直径 / mm | 33.33 |

### 离焦点列图

![离焦点列图](<Double%20Gauss%2028%20degree%20field_report_20261030_145858_598_assets/c001-analysis-001.png>)

*图 1　离焦点列图*

| 指标 | 数值 |
| --- | --- |
| 离焦步长 | 0.05 |
| FocusStepDiopters | 0 |
| 离焦范围 (µm) | 50 |
| DefocusStepDiopters | 0 |
| DefocusUnit | mm |
| 焦面数量 | 5 |
| 六角采样环数 | 6 |
| 光线密度 | 6 |

正文显示前 8 项，共 35 项。完整指标见附录。

### 非相干照度

![非相干照度](<Double%20Gauss%2028%20degree%20field_report_20261030_145858_598_assets/c001-analysis-002.png>)

*图 2　非相干照度*

| 指标 | 数值 |
| --- | --- |
| 探测器表面序号 | 12 |
| 探测器范围 | &#91;-24.57053308904182, 24.57053308904182&#93; x &#91;-24.57053308904182, 24.57053308904182&#93; mm |
| 探测器窗口来源 | 表面半口径采样窗口（未添加物理孔径） |
| 探测器分辨率 | 128 x 128 |
| 光线数 | 5 |
| 瞳孔采样分布 | random |
| 归一化显示 | True |
| 功率参考 | Incident surface power before detector interaction |

正文显示前 8 项，共 12 项。完整指标见附录。

<a id="section-003"></a>

## 导出结果

成功项目：2；失败或不适用项目：0。

| 配置 | 项目 | 执行状态 |
| --- | --- | --- |
| 1 | 离焦点列图 | 完成 |
| 1 | 非相干照度 | 完成 |

<a id="section-004"></a>

## 附录 完整参数与计算记录

<details>
<summary>展开完整参数与计算记录</summary>

### 配置 1 离焦点列图 计算记录

分析设置

| 参数 | 值 |
| --- | --- |
| 光线密度 (RayDensity) | 6 |
| 样式 (Pattern) | 六边 |
| 颜色显示 (ColorRaysBy) | 波长 |
| 参照 (Reference) | 主光线 |
| 离焦范围 (DefocusStepMicrometers) | 50 |
| 使用偏振 (UsePolarization) | false |
| 显示艾里斑 (ShowAiryDisk) | false |
| 波长 (WavelengthNumber) | 所有 |
| 视场 (FieldNumber) | 所有 |
| 表面 (SurfaceNumber) | 像面 |
| 显示缩放 (DisplayScale) | 比例尺 |
| 图形缩放 (PlotScaleMicrometers) | 0 |
| 散射光线 (ScatterRays) | false |
| 使用标注 (UseSymbols) | true |

计算来源：Application.WorkbenchRuntime.BuildAnalysisView/v1；请求指纹：A6EE69CCE28704DB99313557633A168F5A4AAED6AB3E143FF0FA04C73B3EF1DB

| 指标 | 数值 |
| --- | --- |
| 离焦步长 | 0.05 |
| FocusStepDiopters | 0 |
| 离焦范围 (µm) | 50 |
| DefocusStepDiopters | 0 |
| DefocusUnit | mm |
| 焦面数量 | 5 |
| 六角采样环数 | 6 |
| 光线密度 | 6 |
| 瞳孔采样分布 | hexapolar |
| 样式 | hexapolar |
| 波长序号 | 0 |
| 视场序号 | 0 |
| 表面序号 | -1 |
| 颜色显示 | 波长 |
| 参考 | 主光线 |
| 使用偏振 | False |
| 显示艾里斑 | False |
| 艾里斑半径 (mm) | 0 |
| ImageSpaceAfocal | False |
| ImageCoordinateUnit | mm |
| 显示缩放 | 比例尺 |
| 图形缩放 (µm) | 0 |
| PlotScaleMilliradians | 0 |
| 缩放标尺 (µm) | 114.971 |
| ScaleBarMilliradians | 0 |
| 散射光线 | False |
| 使用标注 | True |
| -2 步 RMS | 0.017 |
| -1 步 RMS | 0.013 |
| 名义焦点 RMS | 0.01 |
| +1 步 RMS | 0.011 |
| +2 步 RMS | 0.015 |
| 最佳焦移 | 0 |
| 最佳 RMS 点半径 | 0.01 |
| 最佳 80% 半径 | 0.011 |

```text
分析：离焦点列图
离焦步长：0.05
FocusStepDiopters：0
离焦范围 (µm)：50
DefocusStepDiopters：0
DefocusUnit：mm
焦面数量：5
六角采样环数：6
光线密度：6
瞳孔采样分布：hexapolar
样式：hexapolar
波长序号：0
视场序号：0
表面序号：-1
颜色显示：波长
参考：主光线
使用偏振：False
显示艾里斑：False
艾里斑半径 (mm)：0
ImageSpaceAfocal：False
ImageCoordinateUnit：mm
显示缩放：比例尺
图形缩放 (µm)：0
PlotScaleMilliradians：0
缩放标尺 (µm)：114.971
ScaleBarMilliradians：0
散射光线：False
使用标注：True
-2 步 RMS：0.017
-1 步 RMS：0.013
名义焦点 RMS：0.01
+1 步 RMS：0.011
+2 步 RMS：0.015
最佳焦移：0
最佳 RMS 点半径：0.01
最佳 80% 半径：0.011
```

### 配置 1 非相干照度 计算记录

分析设置

| 参数 | 值 |
| --- | --- |
| 光线数 (NumRays) | 5 |
| X 分辨率 (ResolutionX) | 128 |
| Y 分辨率 (ResolutionY) | 128 |
| 探测器表面序号 (DetectorSurfaceIndex) | -1 |
| 无物理孔径时按表面半口径采样 (UseSurfaceSamplingWindow) | true |
| 瞳孔采样分布 (Distribution) | random |
| 归一化显示 (Normalized) | true |

计算来源：Application.WorkbenchRuntime.BuildAnalysisView/v1；请求指纹：C1CEE685CEDBCE0EC742B8E56DC8D3DD7B7D9294DD48D145890B0D3D9542F484

| 指标 | 数值 |
| --- | --- |
| 探测器表面序号 | 12 |
| 探测器范围 | &#91;-24.57053308904182, 24.57053308904182&#93; x &#91;-24.57053308904182, 24.57053308904182&#93; mm |
| 探测器窗口来源 | 表面半口径采样窗口（未添加物理孔径） |
| 探测器分辨率 | 128 x 128 |
| 光线数 | 5 |
| 瞳孔采样分布 | random |
| 归一化显示 | True |
| 功率参考 | Incident surface power before detector interaction |
| 有效光线数 | 45 |
| 峰值照度 (W/mm²) | 33.209 |
| 视场数 | 3 |
| 波长数 | 3 |

```text
分析：非相干照度
探测器表面序号：12
探测器范围：[-24.57053308904182, 24.57053308904182] x [-24.57053308904182, 24.57053308904182] mm
探测器窗口来源：表面半口径采样窗口（未添加物理孔径）
探测器分辨率：128 x 128
光线数：5
瞳孔采样分布：random
归一化显示：True
功率参考：Incident surface power before detector interaction
有效光线数：45
峰值照度 (W/mm²)：33.209
视场数：3
波长数：3
```

</details>
