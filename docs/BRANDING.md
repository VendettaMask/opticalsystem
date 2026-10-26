# 应用品牌资源

桌面图标采用黑洞与暖色吸积盘视觉，启动画面沿用同一品牌语言。产品名称和加载进度由 Avalonia 在运行时绘制，避免把版本文字固化进图片。

## 统一产品名称

界面名称为 **Optical System Design**；解决方案、项目、程序集、命名空间、主题资源键、开发启动器与发布载荷统一使用 `OpticalSystemDesign`。开发入口为 `OpticalSystemDesign.slnx`、`Run-OpticalSystemDesign.cmd` / `.command` 和 `OpticalSystemDesign.App.exe`，不再使用退役品牌作为产品代码或新输出标识。插件接口同步为 `IOpticalSystemDesignPlugin`，旧插件必须针对新程序集重新编译。

用户明确决定不兼容旧名称：已删除旧身份兼容表及读取层，不查找或迁移旧用户目录、旧载荷文件名、旧环境变量、旧工程后缀或旧阻断元数据别名。设置、工作区、目录与恢复副本仅使用 `OpticalSystemDesign` 目录；磁盘上的旧数据不自动复制、修改或删除。标准工程仍为 `.staropt`，显式原生数据快照仅使用 `.optic.json`，普通 `.json` 不再进入工程分派。冻结验证原件、材料真实来源及测试专用的退役名称拒绝样本保留原始身份，不进入产品命名或兼容运行逻辑。

## 关于窗口与版本号

“关于 Optical System Design”在产品名称下显示“版本 …”；启动页使用同一个 `ProductVersionInfo` 来源。优先读取主程序程序集的 `AssemblyInformationalVersion`，保留预发布后缀，隐藏构建元数据（如 Git 长哈希）；缺失时使用实际程序集的三段版本，两种来源都缺失则显示“未知”，不编造版本。

Windows 标准发布脚本通过 `-p:Version=$AppVersion` 注入发布版本，因此关于页随发布版本自动变化；普通开发构建显示自身元数据，不以安装包文档里的版本号替代。版本文字复用字号和主题资源，原画像、出品方、软件说明和关闭操作保留。版本显示本身不会自动更新安装包；实际重建身份只在 [当前状态](CURRENT_STATUS.md) 汇总，已有正式安装不会自动替换。

## 资源

### 公司字标

`Assets/Brand/CompanyLogo.svg` 是统一的 S.T.A.R.Labs 标准矢量字标，由 Nasalization Regular 的真实字形轮廓制作，保留原标识的宽幅比例。文件只包含完整组合的路径和说明，没有嵌入 PNG、实时文字、字体数据或外部依赖。[资源制作来源](../src/OpticalSystemDesign.App/Assets/Brand/CompanyLogo.source.md)记录作者、字体哈希、离线生成命令及使用范围。

启动页、关于窗口、普通分析结果页、工程图设置区与 FUI 预览统一使用 `CompanyLogoControl`。主题资源提供 `DrawingImage` 路径图形，浅色/深色主题切换时更新颜色；启动页与 FUI 保留各自背景需要的品牌配色。工程图元件/总体布局标题栏直接用同一 SVG 路径绘制，默认 Logo 在 PDF 中保持矢量。用户临时导入的自定义 PNG 仍作为该张图纸的图片使用，恢复默认后重新使用标准矢量字标。

图形标识和普通文字分别处理：“S.T.A.R. Labs 出品”保留为关于页的出品说明，安装器出版商、安装路径、文件元数据、命令行说明、工程组织与示例来源继续保留普通文本。黑洞应用图标和启动背景属于产品图标资源，不替换成公司字标。旧 `CompanyLogo.png` 仅保留为历史制作参照，运行时不再解码、去底、裁切或放大它，也不再用普通字体拼出备用 Logo。

Nasalization 字体只用于本机离线制作完整字标，不随软件分发；SVG 已转为轮廓，运行时及矢量导出不依赖该字体。字体作者的桌面制作授权与字体嵌入授权有不同范围，维护时须使用自己取得的适用版本；不要把制作字体自动加入产品字体菜单或发布包。

主窗口标题行也使用 `AppIcon.png` 的真实品牌位图，左侧与文件标题排列；不以示例中的占位图标替代品牌。紧凑搜索置于标题行右侧，普通 Light 标题/菜单共享蓝色渐变，其余主题沿用各自资源。窗口标题、修改标记和版本来源仍按实际运行状态生成；当前交互与验收边界见 [UI 规范](UI_DESIGN_SPEC.md) 和 [当前状态](CURRENT_STATUS.md)。

资源位于 `src/OpticalSystemDesign.App/Assets/Brand`：

- `AppIconArtwork.png`：1024×1024 RGBA 主图；
- `AppIcon.png`：带透明圆角和平台安全留白的运行时图标；
- `AppIcon.ico`：Windows 多尺寸图标；
- `AppIcon.icns`：macOS 应用图标；
- `Splash.png`：1280×720 无文字启动背景。
- `CompanyLogo.svg`：可缩放的公司字标，Avalonia 与工程图共用。

ICO 通过项目的 `ApplicationIcon` 使用，PNG 作为 Avalonia 资源用于窗口和启动页。macOS 在首个窗口出现前通过平台桥接设置 Dock 图标，避免短暂显示开发默认图标。

## 重新生成

下述 Python 脚本仅供维护者离线处理已批准的素材，不参与 .NET 构建、产品启动或发布包运行，不依赖外部光学引擎。

从已批准主图重新处理圆角和平台资源：

```bash
python tools/round_brand_icon.py src/OpticalSystemDesign.App/Assets/Brand
```

修改资源后应构建解决方案、运行 `BrandAssetTests`，并人工检查小尺寸图标和完整启动页。

字标修改还需运行 `CompanyLogoUiTests`、关于窗口和工程图定向回归，检查真实 Skia 多倍率渲染、深浅主题/放大字号、主题实时切换及默认 Logo 的 PDF 无图片对象断言。`tools/branding/build-company-logo.py` 是维护工具，不加入产品构建步骤。实际本轮结果见 [当前状态](CURRENT_STATUS.md)。

## 启动生命周期

启动窗口先于主工作台显示。主窗口在不可见状态完成工作区初始化并发出就绪信号（顺序模式只创建镜头数据与二维视图；非序列模式沿用对应会话恢复），随后关闭启动页、显示工作台并转移桌面生命周期。最短显示时间用于避免快速机器上的闪烁。
