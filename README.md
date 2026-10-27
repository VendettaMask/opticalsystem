<p align="center">
  <img src="src/OpticalSystemDesign.App/Assets/Brand/AppIcon.png" alt="Optical System Design 应用图标" width="104" />
</p>

<p align="center">
  <strong>简体中文</strong> · <a href="README_EN.md">English</a>
</p>

<h1 align="center">Optical System Design</h1>

<p align="center">
  <strong>让光学设计，从一个想法走向可分析的系统。</strong><br />
  顺序与非序列建模、成像分析、优化、公差、初始结构与镀膜设计，汇聚在同一个桌面工作台。
</p>

<p align="center">
  <a href="https://github.com/VendettaMask/opticalsystem/actions/workflows/ci.yml"><img src="https://github.com/VendettaMask/opticalsystem/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI 实时状态" /></a>
  <a href="global.json"><img src="https://img.shields.io/badge/C%23%20%2F%20.NET-10-512BD4?style=flat-square" alt="C# / .NET 10" /></a>
  <a href="docs/ARCHITECTURE.md"><img src="https://img.shields.io/badge/Avalonia-Desktop-7C3AED?style=flat-square" alt="Avalonia 桌面界面" /></a>
  <a href="docs/CURRENT_STATUS.md"><img src="https://img.shields.io/badge/Status-In%20Development-D99A3D?style=flat-square" alt="持续开发中，提供测试版" /></a>
</p>

<p align="center">
  <a href="#功能一览"><strong>功能一览</strong></a> ·
  <a href="#安装与启动"><strong>下载与安装</strong></a> ·
  <a href="#开发者快速开始"><strong>快速开始</strong></a> ·
  <a href="docs/README.md"><strong>阅读文档</strong></a> ·
  <a href="https://github.com/VendettaMask/opticalsystem/issues"><strong>反馈问题</strong></a>
</p>

<br />

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/workbench-dark.png" />
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/readme/workbench-light.png" />
    <img src="docs/assets/readme/workbench-light.png" alt="Optical System Design 工作台：镜头数据编辑器、文档页签与系统选项侧栏" width="1120" />
  </picture>
</p>

<p align="center">
  <sub>明暗主题下的桌面工作台。预览来自 2026-10-15 实际 Avalonia 控件的离屏渲染，展示布局，不代表数值或跨平台验收。</sub>
</p>

---

**当前测试版：1.0.4 · 2026-10-27 · 构建 `a31fb444`**。Windows x64、Windows ARM64 和 Apple Silicon Mac 已统一更新。[选择安装包](#安装与启动) · [本次发布与验证记录](docs/validation/CLOUD_RELEASE_2026-10-27.json)

## 为什么是 Optical System Design？

这是 S.T.A.R. Labs 的非商业光学设计项目：用纯 C#/.NET 构建共享计算内核，用 Avalonia 提供桌面工作流，让镜头模型、分析结果和设计过程在一处衔接。

- **从处方到交付**：在镜头编辑、二维/三维布局、分析、优化、公差和设计报告之间衔接，无须反复转存工程。
- **三种界面语言**：默认简体中文，可即时切换 English 与日本語并记住选择。
- **一套计算内核**：主程序、初始结构和镀膜模块复用共享 Core，不依赖外部 Python 光学运行时。
- **一个完整产品**：两个模块均为同进程、必装内置功能，标准安装一次包含，无须另外启动或安装。
- **分类镜头示例**：从“文件 → 示例”浏览 Zemax Sequential 的 14 类、95 个镜头，用于打开、学习和分析；具体模型按[导入支持范围](docs/USER_GUIDE.md#镜头示例)执行。
- **可追溯的验证**：区分已实现能力、冻结参考、数值对照和待验证边界，不把界面展示当作精度证明。

> 项目持续开发，现提供三平台测试包。当前包身份、已验证范围与剩余工作统一见[当前状态](docs/CURRENT_STATUS.md)，各分析和模型的适用范围见对应指南。

## 功能一览

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🔭 镜头建模与布局</h3>
      <p>编辑曲率、厚度、材料、面型、孔径、视场和波长，设置变量、解与拾取。多配置可在镜头表头前后切换，并与系统选项、布局和撤销/重做联动。</p>
      <a href="docs/GUI_QUICKSTART_REFACTOR.md">桌面工作流 →</a>
    </td>
    <td width="50%" valign="top">
      <h3>📈 追迹与成像分析</h3>
      <p>点列、光线扇形、场曲/畸变、RMS、波前、真实干涉条纹、FFT/Huygens PSF 和 FFT/Huygens/几何 MTF，以及能量、照度和图像模拟；可按实际视场、波长与结构设置计算。</p>
      <a href="docs/PARITY_MATRIX.md">能力与支持边界 →</a>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🎯 优化与公差</h3>
      <p>设置变量、评价操作数、权重与约束，使用 Damped Least Squares、Nelder–Mead 等实际实现的算法；通过灵敏度、补偿和 Monte Carlo 评估制造偏差。</p>
      <a href="docs/OPTIMIZATION_ALGORITHMS.md">优化算法</a> · <a href="docs/TOLERANCING.md">公差分析 →</a>
    </td>
    <td width="50%" valign="top">
      <h3>🧩 初始结构设计</h3>
      <p>以 AutoLens 流程从规格与随机种子生成、优化和细化候选，支持运行记录、预算与续跑。设备可选自动优先 CUDA、GPU 或 CPU，候选使用共享 Core 独立验收，并可导出工程。</p>
      <a href="labs/InitialStructure/README.md">内置模块说明 →</a>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🌈 光学镀膜设计</h3>
      <p>生成减反、高反和窄带候选，编辑膜层厚度与 n/k 材料，计算 R/T/A、OD 光谱，进行厚度优化、有限结构搜索和制造公差，并保存独立实验。</p>
      <a href="labs/CoatingDesign/README.md">内置模块说明 →</a>
    </td>
    <td width="50%" valign="top">
      <h3>🛠️ 工程、材料与制造资料</h3>
      <p>使用材料与镜头库，以 STAROPT 保存完整工程；导出处方与分析 CSV、完整 PNG、Markdown 设计报告、制造图纸和分面 STEP。支持范围明确的 ZMX、SEQ、LEN 交换子集。</p>
      <a href="docs/FILE_FORMATS_AND_PLUGINS.md">文件与交换格式</a> · <a href="docs/MARKDOWN_REPORT_EXPORT.md">设计报告 →</a>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🔦 非序列与杂散光</h3>
      <p>在独立对象文档中编辑光源、光学对象、机械 STL 网格与探测器；通过分支追迹、光线数据库、路径筛选和探测器重建检查传播与能量。</p>
      <a href="docs/NON_SEQUENTIAL_TRACING.md">非序列工作流</a> · <a href="docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md">杂散光链路 →</a>
    </td>
    <td width="50%" valign="top">
      <h3>📷 相机成像仿真</h3>
      <p>结合当前镜头快照、传感器、QE、光源与反射谱，观察光学、噪声、ADC 和基础 ISP 的分阶段结果；支持连续光谱积分、A/B 对照及结果导出。</p>
      <a href="docs/CAMERA_IMAGING_LAB.md">成像实验室与模型范围 →</a>
    </td>
  </tr>
</table>

### 分析与显示

| 工作 | 当前可用内容 |
| --- | --- |
| 几何像质 | 点列、光线扇形、场曲/畸变、横向色差、RMS 随视场/波长/焦点变化、赛德尔系数及柱形图自动量程 |
| 衍射与能量 | FFT/Huygens PSF，FFT/Huygens/几何 MTF，累计能量、相对照度及各自支持的扫描设置 |
| 波前与干涉 | 波前图提供曲面、等高线、二维热图；干涉图提供灰度、伪彩色、等高线及两光束、倾斜、分析面和子孔径设置 |
| Zernike 系数 | Fringe、Standard、Annular 三种具名系数表与拟合统计，另有系数随视场分析；波前分布通过“波前图”查看 |
| 交互与导出 | 按命名曲线勾选图例、缩放/平移、切换绘图/数据/文本；小窗口保持可读字号，重排或滚动查看，PNG 包含完整分析内容 |
| 布局与实体 | 二维布局、三维布局与实体模型，支持视角预设、方向指示、光线显示及当前视口 PNG 导出 |

干涉图显示共享 Core 根据波前差计算的条纹强度，具体物理假设和设置见[干涉条纹说明](docs/INTERFEROGRAM_2026-10-26.md)。分析使用各自的采样、参考、量纲和适用范围，完整定义见[用户指南](docs/USER_GUIDE.md)与[能力矩阵](docs/PARITY_MATRIX.md)。

### 一条常用工作流

1. **打开或建立系统**：导入受支持的顺序文件，或从分类示例开始；正式工程保存为 `.staropt`。
2. **检查处方与布局**：调整系统选项、材料和表面，切换多配置，查看二维/三维传播。
3. **分析与改进**：配置实际视场、波长和采样，比较像质，再按需要设置变量、评价目标与公差。
4. **保存与交付**：保存工程，或在“数据库 → 设计报告”选择分析及所有配置，导出 Markdown 和配套图片；需要机械交换时显式导出 STEP。

AI 初始结构与镀膜设计从“实验室”打开，使用各自的实验和运行记录。初始结构候选需要独立达标验收，镀膜实验不会自动应用到当前镜头。[GPU 说明](docs/AUTOLENS_GPU.md)列出设备支持和回退范围；自主拓扑增删/搜索目前提供实验性 CPU 库接口，开发进度见[实施计划](docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)。

## 安装与启动

### 下载当前测试版

| 平台 | 安装包 |
| --- | --- |
| Windows x64 | [下载 EXE](https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.4-test.20261019/OpticalSystemDesign-1.0.4-win-x64-UnsignedTest-Setup-a31fb444.exe) |
| Windows ARM64 | [下载 EXE](https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.4-test.20261019/OpticalSystemDesign-1.0.4-win-arm64-UnsignedTest-Setup-a31fb444.exe) |
| Mac Apple Silicon | [下载 DMG](https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.4-test.20261019/OpticalSystemDesign-1.0.4-osx-arm64-AdHocTest.dmg) |

以上为 **1.0.4 测试预发布，构建 `a31fb444`，发布于 2026-10-27**。三个包均包含运行时、两个必装内置模块与字体，无须另装 .NET。[发布说明与 SHA-256](https://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.4-test.20261019) · [实际验证范围](docs/CURRENT_STATUS.md)。

本次包含真实干涉条纹与完整设置、Zernike 重复入口整理、分析排布与完整图片导出，以及功能搜索粘贴异常保护和 Mac 布局保存崩溃修复。三平台发布检查、公开下载校验和 Windows 更新签名已核对；完整数值认证及实机 GUI/OTA 验收范围仍以对应记录为准。

### 使用 Windows 安装包

选择与系统架构对应的 EXE，按向导完成安装后启动主程序。当前文件名以 `-Setup-a31fb444.exe` 结尾，末尾八位表示安装包实际源码。旧客户端需先从发布页手动覆盖安装一次，才能识别新的带码名称，无需先卸载。

软件版本与更新集中在 **帮助 → 关于**。“包含测试版”默认勾选；手动检查后，可下载并验证匹配架构的更新，再点击“安装更新并重启”。有未保存修改时可选择保存、不保存或取消；不会在后台强制安装或关闭程序。同版本的新测试构建也可通过签名构建编号识别，详见[更新规则](docs/BUILD_AND_RELEASE.md#软件版本与-github-分发)。

在主程序的 **实验室 → AI 初始结构 / 光学镀膜设计** 中进入两个内置模块。

完整使用方法可在主程序的 **帮助 → 用户指南** 中搜索查看；该图文指南包含真实界面截图和主题自适应说明图，覆盖当前注册的全部功能命令和分析，并随界面切换中文、英文或日文。仓库内的范围说明见[用户指南](docs/USER_GUIDE.md)。

Windows 安装包尚无 Authenticode 发布者签名，系统可能显示未知发布者提示；更新元数据的 RSA 验签不能替代该证书。安装、修复和恢复方式见 [Windows 安装说明](docs/WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)，当前包身份以[本次交付记录](docs/validation/CLOUD_RELEASE_2026-10-27.json)为准。

### 使用 Apple Silicon Mac 版

Mac 发布仅支持 M 系列芯片（`osx-arm64`）。打开发布页的 DMG，将 **Optical System Design.app** 拖入“应用程序”；包内包含 .NET、两个必装模块和字体。当前包为 ad-hoc 签名、尚未公证，首次打开可能需要系统批准；Mac 通过发布页手动更新。具体可下载包及验证范围见[当前状态](docs/CURRENT_STATUS.md)，打包步骤见[云端发布指南](docs/GITHUB_CLOUD_RELEASE.md#apple-silicon-mac-包)。

### 从源码启动

安装 [global.json](global.json) 指定兼容范围的 .NET SDK，在仓库根目录运行：

```powershell
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-build
```

也可使用 Windows 的 [Run-OpticalSystemDesign.cmd](Run-OpticalSystemDesign.cmd) 或 macOS 的 [Run-OpticalSystemDesign.command](Run-OpticalSystemDesign.command)。这两个开发入口会依次还原、清理、构建并启动，不删除用户工程、主题或工作区。

Windows ARM64 安装包由 Windows x64 构建机交叉打包；Mac 在 Apple Silicon 云端执行定向回归和包内无界面检查。Linux 的 CI 构建不代表提供了 Linux 安装包。实机安装和完整桌面验收范围见[构建与发布](docs/BUILD_AND_RELEASE.md)。

## 开发者快速开始

主技术栈：**C# / .NET · Avalonia · 共享光学 Core**。根解决方案统一包含主程序、内置模块与对应测试；普通构建和回归测试不需要安装 Zemax。

```powershell
# 在上面的 Release 构建后运行测试
dotnet test OpticalSystemDesign.slnx -c Release --no-build --no-restore /m:1 /nr:false

# 在 Windows 上构建包含全部必装模块的安装包
.\Build-Installer.cmd
```

首次打包需要联网准备依赖、目标运行时和安装编译器。详细要求、冻结证据门禁与发布边界见[构建指南](docs/BUILD_AND_RELEASE.md)。

<details>
<summary><strong>展开查看代码结构</strong></summary>

| 路径 | 职责 |
| --- | --- |
| `src/OpticalSystemDesign.Core` | 共享光学模型、材料、追迹、分析、优化、公差与文件内核 |
| `src/OpticalSystemDesign.Application` | 无 UI 的应用服务、事务、工作区与 DTO 映射 |
| `src/OpticalSystemDesign.App` | 桌面工作台、内置模块 UI 与恢复入口 |
| `src/OpticalSystemDesign.Modules` | 内置模块显示适配，复用共享计算与规范 DTO |
| `labs/InitialStructure`、`labs/CoatingDesign` | 内部领域库、设计流程与测试，不是独立产品 |
| `tests/`、`tools/` | 回归、离线验证与维护工具，不进入产品运行依赖 |
| `docs/` | 当前指南、功能契约与带日期的阶段报告 |
| `validation/`、受版本管理的 `artifacts/` | 保留文件、设置、版本及哈希身份的验证证据 |

继续阅读：[系统架构](docs/ARCHITECTURE.md) · [代码地图](docs/CODE_READING_MAP.md) · [协作约束](AGENTS.md)。

</details>

## 文档与进展

| 我想…… | 从这里开始 |
| --- | --- |
| 上手桌面工作流 | [使用指南](docs/GUI_QUICKSTART_REFACTOR.md) |
| 查看分析、报告与成像实验室 | [用户指南](docs/USER_GUIDE.md) · [设计报告](docs/MARKDOWN_REPORT_EXPORT.md) · [相机成像](docs/CAMERA_IMAGING_LAB.md) |
| 查阅全部文档 | [文档索引](docs/README.md) |
| 了解当前验证、安装包与剩余工作 | [当前状态](docs/CURRENT_STATUS.md) · [问题清单](docs/OPEN_ISSUES_2026-10-08.md) |
| 了解支持的模型和文件 | [能力矩阵](docs/PARITY_MATRIX.md) · [文件格式](docs/FILE_FORMATS_AND_PLUGINS.md) |
| 查看数值依据与验证边界 | [数值验证](docs/NUMERICAL_PARITY.md) · [操作数支持](docs/ZEMAX_OPERAND_SUPPORT.md) |

默认主要外部精度基准是已提交的 Zemax OpticStudio 2026 R1 `123456.ZMX` 捕获，结论仅覆盖所捕获的文件、设置与版本。导入、保留某个字段不代表对应计算已经实现，也不承诺与其它软件全面等价；未支持的能力应明确拒绝。

## 参与改进

欢迎通过 [Issues](https://github.com/VendettaMask/opticalsystem/issues) 提交问题与建议，或通过 [Pull Requests](https://github.com/VendettaMask/opticalsystem/pulls) 参与改进。

报告计算问题时，请尽量附上可分享的最小工程、软件版本、分析设置、复现步骤，以及期望结果与实际结果；不要上传保密处方或无权分享的数据。修改代码前请先阅读[协作约束](AGENTS.md)，并为行为变化补充测试与相关文档。

## 项目定位与致谢

本项目由 **S.T.A.R. Labs** 按非商业定位维护。非商业用途不等于自动取得全部第三方资源的使用或再分发权；代码、依赖、字体、图标和材料数据仍须分别遵守适用授权。

感谢以下开源项目及其贡献者。直接与传递依赖按上游项目归并列出，算法参考、资源和离线工具分别注明用途；参考项目不等同于程序运行依赖。

- **运行与桌面界面**：[.NET](https://github.com/dotnet/runtime)、[Avalonia](https://github.com/AvaloniaUI/Avalonia)、[Dock](https://github.com/wieslawsoltes/Dock)、[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)。
- **图形、文字与平台适配**：[SkiaSharp / HarfBuzzSharp](https://github.com/mono/SkiaSharp) 及其上游 [Skia](https://skia.org/)、[HarfBuzz](https://github.com/harfbuzz/harfbuzz)，以及 [Avalonia ANGLE](https://github.com/AvaloniaUI/angle)、[MicroCom](https://github.com/kekekeks/MicroCom)、[Tmds.DBus](https://github.com/tmds/Tmds.DBus)。
- **光学算法与交互参考**：[Optiland](https://github.com/optiland/optiland)、[AutoLens](https://github.com/AI4Optics/AutoLens)、[DeepLens](https://github.com/vccimaging/DeepLens)、[TFStudio](https://github.com/aai2k/TFStudio)、[tmmcore](https://github.com/aai2k/tmmcore)。感谢其在光学建模、初始结构与镀膜设计方面的工作。
- **开放材料数据**：[refractiveindex.info database](https://github.com/polyanskiy/refractiveindex.info-database)。
- **字体**：[DM Sans](https://github.com/google/fonts/tree/main/ofl/dmsans)、[思源黑体](https://github.com/adobe-fonts/source-han-sans)、[JetBrains Mono](https://github.com/JetBrains/JetBrainsMono)、[Noto Sans CJK](https://github.com/notofonts/noto-cjk)、[Inter](https://github.com/rsms/inter)、[Fusion Pixel Font](https://github.com/TakWolf/fusion-pixel-font)。当前桌面字体分工与许可见[字体说明](docs/TYPOGRAPHY.md)。
- **图标**：[Lucide](https://github.com/lucide-icons/lucide) 及其源项目 [Feather](https://github.com/feathericons/feather)、[Game-icons.net](https://game-icons.net/)（Delapouite、Lorc 等贡献者）、[FatCow / Farm-Fresh](https://github.com/gammasoft/fatcow)（FatCow Web Hosting / Marcis Gasuns）。
- **构建、测试与测试依赖**：[MSBuild](https://github.com/dotnet/msbuild)、[NuGet](https://github.com/NuGet/NuGet.Client)、[PowerShell](https://github.com/PowerShell/PowerShell)、[VSTest / .NET Test SDK](https://github.com/microsoft/vstest)、[xUnit.net](https://github.com/xunit/xunit)、[Coverlet](https://github.com/coverlet-coverage/coverlet)、[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)、[Inno Setup](https://github.com/jrsoftware/issrc)。
- **离线验证与报告工具**：[Python](https://github.com/python/cpython)、[NumPy](https://github.com/numpy/numpy)、[SciPy](https://github.com/scipy/scipy)、[Matplotlib](https://github.com/matplotlib/matplotlib)、[Pillow](https://github.com/python-pillow/Pillow)、[pythonnet](https://github.com/pythonnet/pythonnet)、[python-docx](https://github.com/python-openxml/python-docx)、[ReportLab](https://pypi.org/project/reportlab/)、[FreeCAD](https://github.com/FreeCAD/FreeCAD) / [Open CASCADE Technology](https://github.com/Open-Cascade-SAS/OCCT)、[Node.js](https://github.com/nodejs/node)。这些工具不参与产品运行时光学计算。

具体来源、署名与授权保留在[算法参考声明](third_party/autolens/AutoLens-NOTICE.txt)、[镀膜参考归档](third_party/coating-reference/README.md)、[材料目录说明](src/OpticalSystemDesign.Core/Materials/Data/README.md)、字体及图标目录的许可证中。致谢不能替代许可证；分发时须保留各项目要求的版权、许可和署名文件。

<p align="center">
  <sub>Optical System Design · S.T.A.R. Labs<br />让设计过程清晰，让计算依据可追溯。</sub>
</p>
