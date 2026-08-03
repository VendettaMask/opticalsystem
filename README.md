# Optical System Design

**Optical System Design** 是 S.T.A.R. Labs 开发的纯 .NET/C# 光学设计工作台。桌面端采用 Avalonia，计算核心不调用 Python Optiland 后端；Windows 与 macOS 是主要目标平台，Linux 原则上可由 Avalonia 支持。

## 当前能力

- 以 `Optic` 为中心管理孔径、视场、波长、表面、材料、光线追迹、分析、优化、公差和多配置。
- 表面采用 `Geometry + MaterialBefore + MaterialAfter + Coating + Interaction + PhysicalAperture + Scattering + CoordinateSystem` 组合模型，同时保留镜头表格所需的兼容字段。
- 内置 Optiland 兼容玻璃数据以及由 63 个 Zemax AGF 目录转换的玻璃库，支持厂商消歧、13 种 Zemax 色散公式和热学、机械、透过率数据。
- 顺序实光线追迹支持局部坐标、孔径裁剪、折射、反射、衍射和全反射的显式交互类型；大批量追迹可选择仅末面、指定表面或完整历史保留模式。
- 支持平面、标准面、偶次/奇次非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q 等几何模型；尚未实现的自由曲面保持明确边界。
- 桌面分析目录覆盖点列图、光线像差、波前、Zernike、PSF、MTF、RMS、圈入能量、相对照度、辐照度、Jones 光瞳和图像模拟等工作流。
- 优化模块包括手动调整、评价函数、局部优化、差分进化和盆地跳跃；公差模块包括 TDE 风格编辑器、向导、灵敏度、补偿和确定性 Monte Carlo。
- 原生 `.staropt` 项目采用版本头、Brotli 压缩、SHA-256 校验、语义验证和原子保存；Python Optiland JSON、Zemax ZMX、CODE V SEQ、OSLO LEN 是显式交换格式。
- Dock.Avalonia 工作区支持标签拖放、分栏、合并、独立浮动、重新停靠、平铺、层叠、页面锁定和按文件保存工作区会话。
- 只有“全部独立浮动”会创建软件外的原生窗口；平铺和层叠会先把全部页面收回主文档区，再使用 Dock 的内部 MDI 布局。空浮动宿主会在操作、保存和旧会话恢复时清理。
- 明亮、暗夜和异世界主题统一使用主题资源；明亮主题设置卡片与按钮表面为纯白。
- 二维/三维查看器、分析图、制造审查、ISO 10110 系列制图、矢量 PDF 和实验性网格 STEP 导出均为 Avalonia/.NET 原生实现。
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
- 在“视图”中打开二维布局、三维布局或实体模型。
- 在“分析”中按像质类别选择方法；分析结果作为可关闭文档打开，底部提供绘图、数据和文本页。
- 设置面板默认折叠；修改参数后使用同步按钮重新计算重型分析。
- 在“窗口”中选择“保留分栏停靠”“合并单窗格”“全部独立浮动”“平铺全部”或“层叠全部”。平铺、层叠和合并都会自动回收软件外的浮动页面；平铺与层叠在中央文档区使用内部 MDI，合并切回单一标签窗格。
- “切换锁定”用于冻结或恢复当前页面更新；“关闭其他页”保留镜头数据页。
- “保存默认”保存用户布局，“载入默认”载入该布局；命令面板中的“重置为系统初始布局”是另一项操作。
- 在“数据库 > 镜头库”查看打包镜头，在“加工与图纸”执行可制造性检查和制图。
- STEP 输出目前是实验性的分面交换几何，进入制造流程前必须在目标 CAD 中复核。

## 构建与测试

```bash
dotnet restore OptilandWorkbench.slnx --locked-mode
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

截至 2026-08-03，仓库包含 `660` 项回归测试，当前全量基线为 `660/660`。基线覆盖 Core、Application、Avalonia 首帧与主题、Dock 空宿主/会话/锁定、内部 MDI 布局，以及本轮架构收敛与光学计算回归。

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
src/OptilandWorkbench.Application  无 UI 的应用服务、工作区协调和 DTO 映射
src/OptilandWorkbench.App          Avalonia 桌面端、Dock 工作区和会话持久化
tests/OptilandWorkbench.Tests      核心、兼容、GUI 契约和回归测试
docs                               架构、格式、兼容、验证和发布文档
```

## 文档索引

- [架构](docs/ARCHITECTURE.md)
- [旧、新架构收敛与单一结果链路修正计划](docs/ARCHITECTURE_CONVERGENCE_PLAN.md)
- [构建与发布](docs/BUILD_AND_RELEASE.md)
- [文件格式与插件](docs/FILE_FORMATS_AND_PLUGINS.md)
- [GUI 工作流与重构](docs/GUI_QUICKSTART_REFACTOR.md)
- [大规模光线追迹性能](docs/RAY_TRACING_PERFORMANCE.md)
- [Python Optiland 兼容审计](docs/PYTHON_PARITY_AUDIT.md)
- [数值兼容](docs/NUMERICAL_PARITY.md)
- [Python JSON 互操作](docs/PYTHON_JSON_INTEROP.md)
- [分析与绘图兼容](docs/PYTHON_ANALYSIS_PARITY.md)
- [Zemax 分析参考](docs/ZEMAX_ANALYSIS_REFERENCE.md)
- [Zemax 操作数支持](docs/ZEMAX_OPERAND_SUPPORT.md)
- [公差](docs/TOLERANCING.md)
- [镜头库](docs/LENS_LIBRARY.md)
- [制造与制图](docs/MANUFACTURING_DRAWINGS.md)
- [品牌资源](docs/BRANDING.md)
- [本地图标](docs/LOCAL_ICONS.md)

## 兼容声明

本仓库是依据公开资料完成的纯 .NET 独立实现。Optiland 0.5.8 与 Zemax 2026 R1 基准只约束已记录的镜头、版本和分析设置，不代表第三方软件的通用默认值。当前未完成项包括更广泛的自由曲面 JSON、完整薄膜 TMM、矢量衍射、非顺序追迹以及可选 GPU/自动微分后端。
