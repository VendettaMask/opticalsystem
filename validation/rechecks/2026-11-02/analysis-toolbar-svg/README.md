# 分析工具栏 SVG 接入

用户确认稿中的九份独立SVG已逐字节接入[产品资源](../../../../src/OpticalSystemDesign.App/Assets/Icons/Analysis/README.md)，映射身份与SHA256见[确认稿核对](approved-artwork-hashes.json)。32×32网格、1.65源线宽、圆端点/连接、透明度、虚线及填充直接绘制；原始SVG仍可编辑，运行时不读取聊天预览目录。灰/蓝/青角色按主题明暗选用确认稿配色，22 DIP默认显示并跟随字号。

分类顺序、原生下拉菜单、单箭头和动作身份保持；报告、波前、圈入能量后加入三条细分隔线。非序列菜单及其他通用图标不由专用SVG替换。功能契约见[本地图标](../../../../docs/LOCAL_ICONS.md)，源文件不是第三方Lucide资源。

## 最终验证

- [完整依赖构建](build.log)：独立Release/win-x64输出，App、内置模块和测试依赖零警告/错误。[缓存还原](restore.log)和[独立锁文件配置](isolated-locks.props)保留；不改共享锁文件。
- [最终定向检查](final-tests.log)：**35/35通过**、零失败/跳过。8项新SVG检查、11项紧凑工具栏、13项主题资源、1项原主题运行时切换和2项分类下拉契约；早期或重复运行不累计。
- 五种主题Light/Dark/Lumen/Pixel/Isekai，中文默认13及英文/日文18字号布局。九份资源均产生实际非空矢量像素，1280→480→1280调整后可滚动访问最后分类；原有水平滚动条隐藏规则保留。
- 九类按钮逐一用离屏真实鼠标输入打开菜单并用Escape关闭，打开菜单不会执行主动作；65项菜单动作ID及全部分隔项保持。MTF首项通过原生Click事件验证仍触发`analysis-mtf`，未执行光学重算或假称原生桌面菜单验收。
- 同一控件Light→Dark→Lumen→Light重绘，图形边界及Alpha几何相同，深浅画刷改变，返回后像素恢复。不重建图标、不改其他主题包目录。

初次[编译失败](initial-compile-failure.log)是本批对Avalonia椭圆构造函数的错误假设，已改为属性初始化。初次[7项失败](initial-artwork-failures.log)暴露静态可变DashStyle跨离屏UI线程复用，现缓存不可变样式；大字号测试同时修正为检查精确Width及原生取整后的1 DIP边界。最终35项含全部原失败身份，不隐藏旧结果或降低物理验收。

参与[源码身份](source-hashes.json)、执行[程序集身份](runtime-hashes.json)、[范围摘要](verification-summary.json)、[局部文档目标](documentation-check.json)和[差异格式检查](diff-check.json)另存收据；不复制完整源码树、运行时或未变依赖。

## 实际控件截图

全部由最终产品控件在Avalonia/Skia安全离屏会话中生成，并非效果图。

- [光域Lumen](screenshots/analysis-toolbar-Lumen-zh-CN-13.png)、[普通Light](screenshots/analysis-toolbar-Light-zh-CN-13.png)、[暗夜Dark](screenshots/analysis-toolbar-Dark-zh-CN-13.png)
- [像素Pixel](screenshots/analysis-toolbar-Pixel-zh-CN-13.png)、[异世界Isekai](screenshots/analysis-toolbar-Isekai-zh-CN-13.png)
- [英文18字号](screenshots/analysis-toolbar-Light-en-US-18.png)、[日文18字号](screenshots/analysis-toolbar-Dark-ja-JP-18.png)

未控制、重启或安装用户当前软件；未完成默认输出构建、系统DPI/原生多屏验收、全仓测试、安装发布或远端同步。共享工作树中其他会话的光学/求解修改不归为本图标修正，也不通过本界面检查升级数值认证。
