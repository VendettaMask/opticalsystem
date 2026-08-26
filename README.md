# Optical System Design

**Optical System Design** 是 S.T.A.R. Labs 开发的纯 .NET/C# 光学设计工作台。桌面端采用 Avalonia，计算核心不调用 Python Optiland 后端；Windows 与 macOS 是主要目标平台，Linux 原则上可由 Avalonia 支持。

## 当前能力

- 以 `Optic` 为中心管理孔径、视场、波长、表面、材料、光线追迹、分析、优化、公差和多配置。
- 表面采用 `Geometry + MaterialBefore + MaterialAfter + Coating + Interaction + PhysicalAperture + Scattering + CoordinateSystem` 组合模型，同时保留镜头表格所需的兼容字段。
- 内置 Optiland 兼容玻璃数据以及由 63 个 Zemax AGF 目录转换的玻璃库，支持厂商消歧、13 种 Zemax 色散公式和热学、机械、透过率数据。
- 顺序实光线追迹支持局部坐标、孔径裁剪、折射、反射、衍射和全反射的显式交互类型；大批量追迹可选择仅末面、指定表面或完整历史保留模式。
- 桌面端提供相互隔离的顺序与非序列工作模式。同一STAROPT工程并列保存顺序处方和独立非序列文档；非序列对象编辑器支持类型化光源、原生几何、像素探测器和内嵌ASCII/Binary STL机械对象。追迹内核使用对象/三角形两级BVH、实体介质传播和Fresnel反射/透射分支光线树，并可流式保存 `.starrdb`，通过统一路径筛选联动路径分析、3D布局和探测器重建。
- 支持平面、标准面、偶次/奇次非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q 等几何模型；尚未实现的自由曲面保持明确边界。
- Core 当前注册 `72` 个规范分析；桌面端按模式暴露其中顺序 `70` 项或非序列 `2` 项，禁止跨模式运行。顺序模式覆盖报告、点列图、光线像差、波前、Zernike、PSF、MTF、RMS、圈入能量、相对照度、辐照度、Jones 光瞳和图像模拟等工作流。
- 优化模块包括手动调整、评价函数、局部优化、差分进化和盆地跳跃；公差模块包括 TDE 风格编辑器、向导、灵敏度、补偿和确定性 Monte Carlo。
- Zemax ZMX 评价函数按源顺序导入；当前参考 `[MS-L7]` 文件的 103 行均可见，其中已实现的操作数参与计算，尚未实现的类型以禁用只读行保留原参数，不伪装为 Zemax 数值等价。
- 原生 `.staropt` 项目采用版本头、Brotli 压缩、SHA-256 校验、语义验证和原子保存，并保留多配置属性继承断开关系；Python Optiland JSON、Zemax ZMX、CODE V SEQ、OSLO LEN 是显式交换格式。
- Dock.Avalonia 工作区支持标签拖放、分栏、合并、独立浮动、重新停靠、平铺、层叠、页面锁定和按文件保存工作区会话。
- 只有“全部独立浮动”会创建软件外的原生窗口；平铺和层叠会先把全部页面收回主文档区，再使用 Dock 的内部 MDI 布局。空浮动宿主会在操作、保存和旧会话恢复时清理。
- 明亮、暗夜和异世界主题统一使用主题资源；设置按钮使用主题设置表面，覆盖在图形区上的设置卡片使用半透明主题浮层。
- 二维/三维查看器、分析图、制造审查、ISO 10110 系列制图、矢量 PDF 和容差控制的分面 STEP 装配导出均为 Avalonia/.NET 原生实现。
- 打包镜头库只在桌面端读取已审核的原生项目；下载、转换和索引重建由离线维护工具完成。

## 环境要求

- .NET SDK 10 或更高版本。
- Windows 10/11 或受支持的 macOS。

## 启动

仓库根目录提供一键脚本：

- Windows：双击 `Run-Optiland.cmd`。
- macOS：双击 `Run-Optiland.command`。

两个脚本都会依次执行：

1. `dotnet clean` 清理 App 项目的旧构建输出。
2. `dotnet build` 重新构建。
3. `dotnet run --no-build` 启动刚生成的程序。

清理不会删除用户工程、主题、默认布局或按文件保存的工作区会话。

终端启动：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## 桌面工作流

- 在顶部 Ribbon 切换文件、设置、视图、分析、优化、公差、加工与图纸、数据库和窗口命令。
- 在顺序模式“设置 > 工作模式”进入非序列模式；此时主页面切换为可编辑的“非序列对象数据”，功能区仅保留非序列对象、三维视图和专用分析。模式切换不转换或修改数据；如需从顺序处方生成对象，必须在编辑器中显式执行转换命令。
- 在“视图”中打开二维布局、三维布局或实体模型。
- 在“分析”中按像质类别选择方法；分析结果作为可关闭文档打开，底部提供绘图、数据和文本页。
- “分析 > 报告”依次提供表面数据报告、系统数据报告、分类数据报告、系统数据摘要和基面数据；旧“一阶量、处方报告”只作为兼容别名保留。
- 设置面板默认折叠；修改参数后使用同步按钮重新计算重型分析。
- 在“窗口”中选择“保留分栏停靠”“合并单窗格”“全部独立浮动”“平铺全部”或“层叠全部”。平铺、层叠和合并都会自动回收软件外的浮动页面；平铺与层叠在中央文档区使用内部 MDI，合并切回单一标签窗格。
- “切换锁定”用于冻结或恢复当前页面更新；“关闭其他页”保留镜头数据页。
- “保存默认”保存用户布局，“载入默认”载入该布局；命令面板中的“重置为系统初始布局”是另一项操作。
- 在“数据库 > 镜头库”查看打包镜头，在“加工与图纸”执行可制造性检查和制图。
- 新建、打开、镜头库载入和退出共用未保存确认；公差文件独立于 STAROPT 保存，但与项目修改一起受保护，重置或载入布局也会在销毁公差编辑器前确认。撤销/重做保存完整多配置文档和断开链接；配置添加、激活、厚度编辑及表面结构增删均进入同一事务和撤销历史，结构增删同步到全部配置并重映射断开链接。优化取消同样恢复完整文档，成功结果仅在自动半口径刷新完成后发布。
- STEP 输出从真实 Sag 和表面坐标系构建自适应分面镜片装配，并在写出前验证闭合、方向、体积和自相交；它仍不保留解析曲面、材料、镀膜或公差语义，进入制造流程前必须在目标 CAD 中复核。

## 构建与测试

```bash
dotnet restore OptilandWorkbench.slnx --locked-mode
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

截至2026-08-26，正式产品严格构建为 `0` 警告、`0` 错误，全量回归测试为 `786/786`；其中全部非序列测试 `38/38`，新增杂散光专项 `11/11`，覆盖STAROPT v1/v2迁移与v3网格往返、ASCII/Binary STL、单位、网格约束和求交、路径语法、STARRDB、场景过期、损坏输入、取消原子性及数据库筛选联动。独立智能初始结构实验室构建为 `0` 警告、`0` 错误，其定向测试为 `7/7`，不并入正式产品基线。全量基线包含多配置链接持久化/旧文件推断、完整文档撤销/重做、材料传播、公差未保存保护、镜头库安全打开与事务发布、全部处方写入和优化自动半口径事务回滚；STEP CAD 导出专项为 `17/17`，Linux CI 继续承担 OpenCascade 实际导入验收。

受限沙箱中，VSTest 可能需要本地套接字权限，Avalonia 构建任务也可能需要写入用户目录中的构建日志。

## 发布

框架依赖发布：

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
```

自包含发布把最后一个参数改为 `true`。可按目标机器选择 `osx-x64`、`osx-arm64`、`win-x64` 或 `win-arm64`。

## 目录结构

```text
src/OptilandWorkbench.Core         计算模型、追迹、分析、优化、公差、文件和插件
src/OptilandWorkbench.Application  无 UI 的应用服务、规范 WorkbenchRuntime、工作区协调和 DTO 映射
src/OptilandWorkbench.App          Avalonia 桌面端、Dock 工作区和会话持久化
tests/OptilandWorkbench.Tests      核心、兼容、GUI 契约和回归测试
docs                               架构、格式、兼容、验证和发布文档
```

## 文档索引

- 架构与工程：[系统架构](docs/ARCHITECTURE.md)、[架构收敛计划](docs/ARCHITECTURE_CONVERGENCE_PLAN.md)、[智能初始结构实验室计划](docs/INITIAL_STRUCTURE_LAB_PLAN.md)、[大文件拆分记录](docs/LARGE_FILE_SPLIT_PLAN.md)、[构建与发布](docs/BUILD_AND_RELEASE.md)。
- 桌面产品：[GUI 工作流](docs/GUI_QUICKSTART_REFACTOR.md)、[UI 设计规范](docs/UI_DESIGN_SPEC.md)、[UI 符合性审计](docs/UI_CONFORMANCE_AUDIT_2026-08-04.md)、[UI 设计走查](docs/UI_DESIGN_REVIEW.md)、[品牌资源](docs/BRANDING.md)、[本地图标](docs/LOCAL_ICONS.md)。
- 数据与互操作：[文件格式与插件](docs/FILE_FORMATS_AND_PLUGINS.md)、[STAROPT 工程格式](docs/STAROPT_FILE_FORMAT.md)、[Python JSON 互操作](docs/PYTHON_JSON_INTEROP.md)、[镜头库](docs/LENS_LIBRARY.md)。
- 数值与兼容：[兼容矩阵](docs/PARITY_MATRIX.md)、[数值兼容](docs/NUMERICAL_PARITY.md)、[Python 分析兼容](docs/PYTHON_ANALYSIS_PARITY.md)、[Python 兼容审计](docs/PYTHON_PARITY_AUDIT.md)、[精度验证](docs/ACCURACY_VALIDATION_2026-07-31.md)、[追迹性能](docs/RAY_TRACING_PERFORMANCE.md)。
- Zemax 边界：[分析参考](docs/ZEMAX_ANALYSIS_REFERENCE.md)、[基准配置边界](docs/ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md)、[操作数支持规范](docs/ZEMAX_OPERAND_SUPPORT.md)。
- 工程工作流：[公差分析](docs/TOLERANCING.md)、[可制造性与制图](docs/MANUFACTURING_DRAWINGS.md)。

## 兼容声明

本仓库是依据公开资料完成的纯 .NET 独立实现。Optiland 0.5.8 与 Zemax 2026 R1 基准只约束已记录的镜头、版本和分析设置，不代表第三方软件的通用默认值。当前未完成项包括更广泛的自由曲面 JSON、完整薄膜 TMM、矢量衍射、完整 NSC 对象/光源/探测器环境以及可选 GPU/自动微分后端。
