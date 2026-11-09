# 异世界：花饰位于图纸内部 — 2026-11-03

**存储状态（2026-11-08）**：这是已被后续方案取代的阶段记录。原始用户参考、正式材质、已确认的手稿页眉/页脚版本及后续字体/DPI验收不在本次删除范围。这里保留代表画面、全部失败图、TRX与原校验记录，退役其他过程渲染；不再把整套过时画面作为当前参考。见[清理范围与逐文件记录](../../2026-11-08/process-artifact-cleanup/README.md)。

用户附图指出此前花边位置错误：花叶应在羊皮纸内侧伸入图中，外圈只留窄皮革金边。本轮依该位置关系修订，限于 Isekai 的分析、2D/3D 和 Solid Model。

## 当时实现

- 新增透明内角花饰与独立窄皮革框，沿用羊皮纸。上角为百合/藤蔓，右上天体星盘，下角炼金瓶与晶簇；四组花饰等比画入原源图，不再作为外圈宽衬边。
- 外框从 88 DIP 改为 28 DIP；1200×900 源图与 160 DIP 图签不变，1× 完整图为 1256×956。原适应窗口、100% 滚动、1920×1080 默认导出画布和报告原比例保持。
- 2D/3D 背景花饰不透明度 0.9；分析/图签 0.22，文字、坐标与曲线保持前景对比。装饰不接收指针、不随相机移动、不改变数值、几何、颜色及实际光线。报告共享内部花饰但保持原源图物理尺寸，不加入外部皮革边。
- 移除图例旁独立台座星盘，保留已有图例布局与交互。原宽花框和台座星盘资源保留作已交付历史来源，但不再嵌入 App。当前仅加载三张材质图；资源和角部画刷按 UI 线程缓存。
- 素材由内置 `image_gen` 生成，参考用户图片中的装饰位置；没有截取参考中的镜头/光线/数值。实际页面仍为产品 C#/Avalonia/Skia 与共享 Core 结果。[素材路径、完整提示词、原始尺寸与哈希](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)。

## 保留的阶段代表与失败画面

- [阶段代表：Huygens-MTF](previews/Huygens-MTF.png)
- [阶段代表：TwoDimensional-OpticalLayout](previews/TwoDimensional-OpticalLayout.png)

其他过程截图已在2026-11-08退役；原测量、测试计数、失败结论及其发生时间没有改写。后续当前绘图能力以独立的正式验证记录为准。

## 验证

默认 Release App、两个内置模块依赖及测试构建完成，无警告/错误。[inner-final.trx](inner-final.trx)：**86 执行、86 通过、0 失败、0 跳过**，约 28 秒。范围为 `IsekaiDrawingMaterialTests`（7 项）、`UniformDiagramExportTests`、`DiagramViewportFitTests`、`AnalysisLegendLayoutTests`、`MtfPlotTitleTests`、`AnalysisFrameDpiTests`、`ThemeResourceTests`、`ThemeRuntimeTests`。

除既有源图尺寸/几何/字体、相机/工程状态、曲线身份、图例/光线显隐、主题往返和源像素边界检查，本轮在三个**实际场景自身的截图**内核对上角墨色像素；该截图不含外框或浮动控件，旧版仅外框有花的实现无法通过这个断言。已目检实际 2D、3D 与分析图。

最初 [inner-first.trx](inner-first.trx) 的 7 项失败来自 Avalonia `DrawingGroup.Open` 不支持直接 `DrawBitmap`；已改用缓存的局部 `ImageBrush` 组合，无该运行时异常。[inner-render.trx](inner-render.trx) 为修正后的 7/7 阶段检查，最终以 86/86 记录为准，重复运行不相加。

所有验证仅后台 Headless/Skia；未控制/重启用户应用，未打包发布。未重跑完整光学精度矩阵，不宣称全仓或原生桌面验收。[上一阶段记录](../isekai-rich-frame/README.md)保留原范围，已不代表当前花饰位置。

`git diff --check` 通过。6 份相关 Markdown 的 933 个本地链接目标检查通过，详见 `document-links.json`；本机无 `pwsh`，未执行完整 PowerShell 文档审计。

历史阶段的原地截图生成命令不再作为当前回放入口。保留原源码身份与测试记录；今后的渲染使用独立临时目录，不回填已退役阶段或把新版本截图冒充当时结果。
