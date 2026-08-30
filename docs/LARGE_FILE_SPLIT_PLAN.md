# 大文件拆分实施记录

## 当前状态

原计划中的主要机械拆分已经落地：

- `AnalysisFramework` 已按分析族拆分；
- Python Optiland JSON 读写已按模型责任拆分；
- `OptilandConnector` 的生产责任已迁入规范 `WorkbenchRuntime` 和独立应用服务；
- `WorkbenchApplication` 成为组合根，`WorkspaceCoordinator` 负责修订、写锁、取消和事件；
- `OpticalDrawingRenderer` 已拆成外观及制造绘制分部；
- `MainWindow` 已拆为生命周期、动作、Shell、文档、工作区和导入文件；
- `AnalysisPanel` 已拆为生命周期、参数、结果、绘图和导出文件。
- `ToleranceOperandEditorRow` 已从公差主面板拆为独立可测试编辑模型，`TolerancingPanel` 不再同时定义整套行模型与代码解析；后续报告与文件持久化仍可按同样边界继续拆分。
- 非序列探测器的归一化、平滑和剖面计算已从窗口文件抽为无 UI 状态的 `NonSequentialDetectorDisplay`，算法可独立测试，显示变换不会混入数据库重建或物理统计。

`OptilandConnector` 已迁入独立 `OptilandWorkbench.Compatibility` 程序集，仍作为旧调用者和兼容测试的薄外观存在，不应重新承载新生产逻辑。主 Application/App 不引用该程序集。

## 拆分约束

- 拆分不得改变公共数值语义、序列化格式或 GUI 文案；
- 每一步先做机械迁移，再做行为重构；
- Core 不得依赖 Application 或 App；
- Application 不得依赖 Avalonia 或 Dock；
- App 不得直接暴露 Core 类型；
- 测试、文档和基线必须与代码同步。

## 当前目标布局

```text
OptilandWorkbench.Core
  光学模型、材料、几何、追迹、分析、优化、公差、格式、插件

OptilandWorkbench.Application
  WorkbenchApplication
  WorkspaceCoordinator
  Runtime/WorkbenchRuntime.*
  独立文档/处方/分析/可视化/优化/公差/多配置服务

OptilandWorkbench.Compatibility
  OptilandConnector 兼容外观（无新增成员，单向依赖 Application/Core）

OptilandWorkbench.App
  MainWindow.*
  AnalysisPanel.*
  PanelManager / WorkspaceDockFactory
  OpticalDrawingRenderer 外观与 Rendering 分部
```

## 后续允许的拆分

- 当单个分析族再次增长时，可按计算、DTO 映射和演示拆分；
- 当格式适配器增长时，可按格式独立程序集，但不得复制光学构建规则；
- 当 App 面板过大时，可按“状态、命令、视图构建、渲染”拆分；
- 公共协调规则必须留在 `WorkspaceCoordinator`，不能散落到各服务。

## 验证门槛

每次结构调整至少执行：

```bash
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

普通局部修改可先运行定向测试；发布和跨层拆分必须重新建立全量基线。架构测试必须继续阻止 Core 泄漏到 App 公共 API，并验证 Application 不依赖 Avalonia/Dock。

## 非目标

- 不为减少行数而合并不同数值算法；
- 不在机械拆分中更改第三方兼容默认值；
- 不把本地化标题用作类型或缓存键；
- 不通过删除兼容测试来完成拆分；
- 不在没有验证的情况下移除 `OptilandConnector` 兼容外观。
