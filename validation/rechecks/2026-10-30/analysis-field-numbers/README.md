# 全部分析图视场编号统一（2026-10-30）

用户补充要求：其他分析图里的视场也统一用编号，不写 On axis。本轮继承此前 MTF 的 `Field 1, T/S`，将所有离散视场标题、图例与矩阵行标签统一为 `Field 1`、`Field 2`……。

## 实现

- 共用 FieldName 不再读取自定义名称或添加坐标后缀，覆盖点列、离焦点列、光线/光瞳/光程差扇形、全视场点列、几何能量、瞳图、入射角、照度及辐射强度等调用者。
- DefinedFieldSamples 的展示标签按原视场表编号生成，覆盖 RMS 曲线和离散点标签。保留 Index、实际 X/Y、归一化 Hx/Hy 与连续扫描样本的物理坐标、数量及单位。
- 光线足迹与衍射圈入能量的独立标签入口同样采用编号；保留原曲线颜色、波长/配置身份与独立过滤。
- 矩阵点列直接使用来源视场编号，移除从标题字符串提取并改写成毫米值的旧逻辑。配置矩阵继续保留结构编号。
- 原工程的自定义视场名称、原有物理定义和数据文件保持；设置下拉框不属于本次图面标签改动。单选视场保留完整原表编号。

操作说明：[用户指南](../../../../docs/USER_GUIDE.md)。

## 验证

默认 Release App 与测试项目连同全部引用依赖构建成功，未使用跳过依赖的构建选项。`test-build-final.log` / `analysis-field-numbers-final.trx`：**87/87 通过，0 失败、0 跳过**。范围：

- SpotDiagramFieldTests、FootprintDiagramLegendTests、RmsAnalysisTests、ActualFieldSamplingTests。
- 普通矩阵、配置矩阵、成对扇形和离焦点列界面契约。
- MtfMaximumFrequencyTests、EncircledEnergyVariantTests、ImageRadiometryRepairTests。
- 既有 SpotDiagramPreservesFrozenRelativeRayCoordinatesAcrossWavelengths，原冻结点列数字与边界不变，仅同步动态图面标题断言，未改动或重新生成冻结数据。

首轮 `test-build.log` / `analysis-field-numbers.trx`：35 通过、1 失败。ActualFieldSamplingTests 仍期待 Center/Actual 自定义点标签；保留自定义名称输入，将展示标签断言改为 Field 1/2/3 后，同一用例在最终范围内通过。其他数值断言和参数未放宽。无新增用例，已有测试仅同步标签断言。差异空白检查通过。

此前 MTF 任务的初始结构依赖编译阻断不代表本轮构建结果；本轮默认依赖构建已成功。不把本轮局部检查称为全仓或原生桌面验收；未发布、提交或推送。
