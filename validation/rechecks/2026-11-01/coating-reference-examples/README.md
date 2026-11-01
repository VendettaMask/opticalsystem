# 五套参考镀膜示例

实现与物理范围见[专题说明](../../../../docs/COATING_REFERENCE_EXAMPLES_2026-11-01.md)。来源为已存档TFStudio `8b942b3f956728a23cea2b35023c8b5d8de17428`；源归档、许可证和既有材料/目标模板未替换。生产只加载静态数据并调用共享C# Core，不运行Node。

## 独立参考

产品唯一处方/材料数据为[reference-examples.json](../../../../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/reference-examples.json)。[离线脚本](../../../../tools/diagnostics/CoatingDemoExamples20261101/generate-reference.mjs)实际执行源示例构造、原材料采样器和固定`tmmcore 0.4.2`，输出[完整对照夹具](../../../../labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/tfstudio-demo-examples-8b942b3.json)，不从本地Core反向生成期望值。

- 五例完整原层序/膜厚/锁定值；高反15层，边缘滤光20层，宽带减反4层，银镜2层，单层MgF₂ 1层。
- 400–800 nm，0.5 nm；0°/35°/57°，s/p共24,030点，每点R/T/A沿用2e-11绝对误差，另核对同角度平均功率。
- 七种材料共488个原节点/中点/边界样本；保留TiO₂原k=0、Ta₂O₅和Ag吸收、BK7内部透射率k与PCHIP。
- Ta₂O₅原表从500 nm开始；参考在400–500 nm延用端点，明示非实测。未声明新模型的旧材料继续严格拒绝域外计算，原98%门槛不改。
- 原始参考未定义优化验收；本地默认比较目标与来源元数据分开说明。1 mm BK7和Air出射保存在元数据；所比曲线为前面单独评价，忽略另一面。

## 复现

在仓库根目录展开存档中的以下文件至 `.tmp/coating-demo-reference-20261101/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/`：`web/demo-examples.js`、`src/utils/materials/materialDatabase.js`、`src/utils/materials/pchip.js`、`src/utils/misc/numberParsing.js`、`package.json`；将`tmmcore-0.4.2.tgz`展开至同父目录的`package/`。运行 `node tools/diagnostics/CoatingDemoExamples20261101/generate-reference.mjs .`。脚本记录实际三个上游文件的SHA-256；原归档保持不变。正常Node的ES模块自动检测提示不影响结果。

后台测试使用现有隔离输出缓存 `artifacts/coating-spectrum-20261101/build` 和其 `isolated-locks.props`，防止写共享NuGet锁文件或关闭用户正在使用的App。构建先成功完成，再运行 `dotnet test --no-build --no-restore`，Release、Win-x64、`UseAppHost=false`、单MSBuild工作进程；离屏截图使用 `COATING_UI_EVIDENCE` 指向本批工作目录。此目录名称只标识可再生构建缓存，不是上一批测试结论的沿用。

定向测试筛选包含 `ReferenceExampleTests`、`DesktopTests`、`ThinFilmPhysicsTests`、`DisplayPolarizationTests`、`AnalysisDisplaySettingsTests`、`DailyDesignTests.MaterialImport`、`DailyDesignTests.Legacy`、`WorkflowTests.InvalidInputsAndOutOfRangeMaterials`。截图与菜单操作使用安全Avalonia离屏宿主，不操作用户桌面；系统原生文件对话框/剪贴板仍不在验收范围。

## 完成结果

[桌面及邻接范围62/62](base-62-tests.log)通过后，收紧解析n模型的有效域守卫并增加检查；[最终完整构建](build.log)零警告/错误，[最后定向范围38/38](final-tests.log)通过。后者覆盖全部16项ReferenceExampleTests、五例菜单、显示/导出、原物理、偏振、采样、旧材料域和旧文件。共63个不同用例身份（17个新增），重跑不累计。本批未替换默认输出、重启用户窗口、安装或发布；不计原生桌面验收。

首轮[3个域规则接线失败](initial-domain-failures.log)完整保留；后续显式表格规则贯通Core并全部复验通过，不将首轮58项通过叠加为新增用例。[上游与数据身份](reference-identity.json)、[源码身份](source-hashes.json)和[最终运行时身份](runtime-hashes.json)绑定本批范围；不重复封存整个源码树、运行时或第三方依赖。

## 实际预览

- [15层高反](demo-hr-qw-1500.png)、[高反结果表](demo-hr-qw-results-1500.png)：首行400 nm，T=92.2373%、R=7.7627%，与用户截图相符。
- [四层宽带减反](demo-bbar-4-1500.png)、[20层边缘滤光](demo-edge-1500.png)：原Ta₂O₅端点规则提示可见。
- [Ag银镜](demo-metal-ag-1500.png)、[单层MgF₂减反](demo-mgf2-ar-1500.png)：按原层序和物理厚度实算。

图像来自实际离屏控件，不是效果图或从参考截图描出的曲线。PNG及日志在本目录为证据，`artifacts`只作可再生工作输出。
