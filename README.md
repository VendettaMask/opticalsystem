# Optical System Design

S.T.A.R. Labs 的非商业光学设计项目。产品计算采用纯 C#/.NET，桌面界面采用 Avalonia，不依赖外部 Python 光学运行时。非商业定位不等于自动取得全部第三方素材的再分发权，使用与分发仍须遵守各资源许可证。

初始结构与镀膜设计是主程序的**必装内置模块**：同一进程、同一个标准安装包，一次安装全部功能；没有独立程序、可选组件或额外安装步骤。`labs/` 只是内部领域代码与测试目录，不代表独立产品。

## 当前状态

已有顺序与非序列镜头工程、追迹与分析、优化、公差、多配置、材料/镜头库、制造制图，以及初始结构和镀膜工作流。工程支持原子保存、撤销、定时恢复副本与崩溃恢复；Windows 安装支持事务恢复、损坏载荷修复与安全旧载荷清理。

项目尚未通过完整发布验收。最新测试范围、已知失败、包身份与待办集中在 [当前状态](docs/CURRENT_STATUS.md)、[问题清单](docs/OPEN_ISSUES_2026-10-08.md) 和 [内置模块交付记录](docs/BUILT_IN_MODULES_2026-10-13.md)。本页不复制易过期的测试通过数。

## 安装与启动

普通用户使用标准 `OpticalSystemDesign-*-Setup.exe`，无需另外安装 .NET 或两个模块。当前交付为 Windows x64 未签名自包含安装包；未知发布者/系统安全提示不因哈希校验而消失。已有安装不会因为源码更新而自动替换。见 [Windows 安装说明](docs/WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)。

开发者需安装 [global.json](global.json) 指定兼容范围的 .NET SDK，在仓库根目录执行：

```powershell
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-build
```

也可使用 Windows 的 `Run-OpticalSystemDesign.cmd` 或 macOS 的 `Run-OpticalSystemDesign.command`。开发入口依次还原、清理、构建并启动，不删除用户工程、主题或工作区数据。

进入主程序的“实验室 → AI 初始结构 / 光学镀膜设计”即可使用两个模块。顺序工作区自动打开镜头数据与二维视图；非序列工作区和显式保存布局有各自规则。详见 [桌面工作流](docs/GUI_QUICKSTART_REFACTOR.md)。

## 构建、测试与发布

```powershell
dotnet test OpticalSystemDesign.slnx -c Release --no-build --no-restore /m:1 /nr:false
.\Build-Installer.cmd
```

根解决方案包含主程序、两个模块及其测试，普通测试不需要安装 Zemax。首次打包需要联网准备 NuGet、目标运行时和安装编译器。标准脚本强制发布全部模块，缺失模块或必要许可证会失败。Windows ARM64、macOS 发布入口和 Linux CI 构建存在，但不能据此宣称实机安装与完整桌面验收通过。详见 [构建与发布](docs/BUILD_AND_RELEASE.md)。

## 代码与文档

| 路径 | 职责 |
| --- | --- |
| `src/OpticalSystemDesign.Core` | 唯一共享光学模型、材料、追迹、分析、优化、公差和文件内核 |
| `src/OpticalSystemDesign.Application` | 无 UI 的应用服务、事务、工作区与 DTO 映射 |
| `src/OpticalSystemDesign.App` | 桌面端、工作区、内置模块 UI 与恢复入口 |
| `src/OpticalSystemDesign.Modules` | 内置模块显示适配，复用共享计算和规范 DTO |
| `labs/InitialStructure`、`labs/CoatingDesign` | 内部领域库、搜索/设计流程和测试 |
| `tests/`、`tools/` | 回归、离线验证和维护工具，不是产品运行依赖 |
| `docs/` | 当前指南、专题契约和明确标注的历史报告 |
| `validation/`、受版本管理的 `artifacts/` | 有限定文件、设置、版本及哈希的验证证据 |

阅读入口：[文档索引](docs/README.md) · [架构](docs/ARCHITECTURE.md) · [代码地图](docs/CODE_READING_MAP.md) · [文件格式](docs/FILE_FORMATS_AND_PLUGINS.md) · [初始结构](labs/InitialStructure/README.md) · [镀膜设计](labs/CoatingDesign/README.md)。

## 精度与兼容边界

默认主要外部精度基准是已提交的 Zemax OpticStudio 2026 R1 `123456.ZMX` 捕获；结论仅适用于所捕获文件和设置。内部回归通过、捕获字节完整、当前重新计算和数值等价是不同结论。Optiland 0.5.8 资料仅为冻结历史辅助证据，禁止重新生成或进入产品依赖链。

导入兼容行不等于对应功能已经实现；不支持的模型和算法须明确拒绝，不能替换为另一算法或成功零值。见 [数值验证](docs/NUMERICAL_PARITY.md)、[兼容矩阵](docs/PARITY_MATRIX.md) 与 [操作数支持](docs/ZEMAX_OPERAND_SUPPORT.md)。
