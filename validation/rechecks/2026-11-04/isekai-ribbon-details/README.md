# 异世界主Ribbon：确认版小细节与剑修正

按用户2026-11-04确认的“小细节”稿接入真实软件。保留深棕皮革、现有布局、配色、字体、功能图标及命令；整片羊皮纸顶栏和重排工具栏两种预览未采用。本轮只修改主Ribbon的绘制层，不修改分析纸面、二维/三维场景、光学计算或验收门槛。

## 实现及原因

- 确认稿的[原创SVG](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/ribbon-small-accents.svg)已嵌入产品；与原预览逐字节SHA256一致，见[tested-identities.json](tested-identities.json)。淡金枝叶和小星盘按真实命令右侧留白等比缩放、贴右定位，宽度上限130 DIP；空白小于66×18 DIP时省略。选中分类角线跟随真实TabItem，底部小刻痕只使用实际空隙。
- 原剑使用固定42 DIP菜单高度，斜护手伸入菜单并被选中项遮断，窗口加宽后又像平直金属条。现改为短握柄、对称弯护手、居中蓝宝石与收尖双面剑身；握柄/护手保持比例，剑身长度随栏宽延伸。整把剑裁切在实测菜单与命令间隙内，空间不足时省略，不挤动任何控件。
- 通用宿主只提供可选的`ILayoutAwareRibbonDecoration`布局信息，不识别具体主题。Isekai实现背景/前景两层绘制，两层都不接收输入或请求额外空间；其他主题继续原绘制路径，新增前景保持透明。绘制几何按UI线程缓存，选择、字体、滚动及窗口布局更新时重新定位。

## 实际画面

以下均为真实C#/Avalonia/Skia控件离屏渲染，没有生成图或截图后重绘。Ribbon夹具复用生产的原生框架、命令内容/布局工厂和真实语义图标，含文件/设置/视图三个分类；完整MainWindow另行验证。

| 范围 | 实际截图 |
| --- | --- |
| 宽Ribbon，13字号、200% | [1600 DIP](screenshots/ribbon-13-2-1600.png) |
| 窄Ribbon，18字号、200%，留白不足隐藏配饰 | [640 DIP](screenshots/ribbon-18-2-640.png) |
| 完整MainWindow，13字号、150% | [1600×1100 DIP](workbench/workbench-1600-13.png) |
| 完整MainWindow，18字号、200% | [1120×850 DIP](workbench/workbench-1120-18.png) |
| 真实分析设置与下拉菜单 | [设置](workbench/settings-1600-13.png)、[菜单](workbench/menu-1600-13.png) |

18字号完整工作台的分析图中仍可见源图溢出提示。该状态不是本轮Ribbon问题，也不在这20项通过结论内；不据此宣称所有分析排版或光学数值已达标。

## 验证

基于`aae931370c552265be6c6a4ab5cf26c8bf3d7761`的共享工作区，保留其他聊天正在修改的代码。使用原项目与锁文件完成[锁定restore](restore.log)，未关闭NuGet审计。首次隔离构建复制第三方原生依赖时磁盘不足，[唯一失败记录](disk-space-failure.log)保留；只清理本任务可再生输出，未关闭用户软件或清理其他任务数据。

最终[Release构建](build.log)含App、内置模块和测试，**0警告、0错误**。使用隔离`ArtifactsPath`、`UseAppHost=false`、`CopyLocalLockFileAssemblies=false`、`GenerateRuntimeConfigDevFile=true`及复制硬链接开关，共享既有NuGet缓存进行后台UI验证。没有改项目依赖、锁文件、运行时标识或审计设置；这不是默认输出目录、安装包或自包含发布认证。构建程序集与本轮源文件身份见[tested-identities.json](tested-identities.json)。

最终单次[final-ui.trx](final-ui.trx)及[控制台结果](final-ui-tests.log)：**20通过、0失败、0跳过，47.13秒**。前置6项同范围检查已被此次结果覆盖，不累计为26项。

- 新定向6项：13/18字号×100%/150%/200%实际显示缩放，每项1600→1120→640→1600 DIP；检查真实菜单—命令槽位、配饰边界与有/无前景时命令几何一致，实际组合像素包含配饰；模拟点击切换原生分类、打开语言菜单并Escape关闭，切换Light/Dark/Lumen/Pixel时新增前景完全透明。
- 既有紧凑Ribbon 11项：横排、分组、窄窗溢出、主题/页面/尺寸往返及撤销/重做状态。
- 既有真实工作台2项：150%/13字号与200%/18字号，实际MainWindow、停靠页、分析设置、下拉菜单、关闭按钮及工程状态。
- 既有装饰缓存/调整尺寸1项：复用绘制资源与几何，保留既有分配门槛。

全部后台离屏完成，没有操作桌面、重启用户程序、提交、推送或触发远端打包。本结论只覆盖上述UI范围，不等同于全仓数值、原生多显示器或发布认证。截图/日志摘要见[evidence-hashes.json](evidence-hashes.json)，不额外冻结整套源码、构建运行时或第三方包。
