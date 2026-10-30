# MTF 标签简写与视场编号（2026-10-30）

按用户要求，所有 MTF 图例的 Tangential/Sagittal 简写为 T/S；视场只显示视场编辑器中的一基编号，轴上同样编号，不显示 On axis、自定义名称或坐标后缀。

后续用户已要求其他分析图同步采用编号，当前扩展实现与验证见[全部分析图视场编号记录](../analysis-field-numbers/README.md)；下述“其他分析保持”是本阶段原始范围。

- 普通与离焦图例：`Field 1, T`、`Field 1, S`。
- 随视场扫描图例：`50 cycles/mm, T`、`50 cycles/mm, S`，保留实际频率及有焦/无焦单位。
- 复用 MTF 共用命名入口，覆盖 Fourier、Huygens、Geometric、Sampled 和旧采样离焦入口。其他分析调用 FieldName 的默认行为保持原样。
- 编号按完整原视场表匹配，不按当前显示序号重新编号；单选第二视场仍显示 `Field 2`。
- 光学计算、坐标和结构身份、数据键、颜色/线型及独立过滤不变；自定义视场名称仍保留在原工程。

## 验证

复用现有测试，仅同步旧长标签断言及对应测试名称，无新增用例。

`mtf-short-labels.trx` / `test-existing-references.log`：27/27 通过，零失败/跳过。范围为 MtfMaximumFrequencyTests、EveryMtfFamilyCanToggleEachCurveAndKeepItsAxes 的十个 MTF 类别、SampledMtfLabelsUseZemaxPupilSpaceTangentialSagittalDirections、ContrastLossMapKeepsExistingZemaxPupilSpaceDirectionLabels。覆盖视场顺序、真实像高字段、自定义名称忽略、单选字段原编号、物理方向与原图例开关行为。

修改后的 Core 在首次完整依赖构建中编译成功。`test-build.log` 保留完整依赖构建失败：初始结构模块当时缺失 TopologyOperators.AirIntervals、StructureEvolutionSearch.Run 和 ValidateOptions；未修改这些无关代码。后续测试项目用 `BuildProjectReferences=false` 复用其他模块已有程序集，构建成功并实际执行上述 27 项。`app-existing-references.log` 为默认 Release App 同样复用依赖的构建，零警告、零错误；不视为完整依赖构建通过。

`mtf-legends-render.trx` / `render.log`：上述十个 MTF 图例用例离屏复跑 10/10，保存 20 张全显示/全隐藏截图于 `images/`，计入原 27 个不同用例，不重复累加。目检几何离焦视场编号与 T/S 显示，以及傅里叶随视场扫描；截图来自既有低采样测试夹具，不是附件镜头重算或导出交付。`core-hashes.json` 记录核对时程序集：App 与测试目录的实际 Core 哈希相同；Core 项目输出此时已有不同哈希，未将其他并行构建状态视作本次验证依据。差异空白检查通过。

受限环境的最初两次构建无输出退出，第三次无输出等待后主动中止；没有把这些尝试计为测试通过。后续以获准的后台进程执行验证，不启动或控制用户桌面应用。

未全仓重跑、重新标定 Zemax 精度、操作原生桌面或发布安装包。其他未提交工作保持。
