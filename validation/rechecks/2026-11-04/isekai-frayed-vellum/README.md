# 中世纪磨毛羊皮图纸：真实程序渲染

用户否决网上素材与正式雕金画框，又指出整齐卷边仍不足，要求更像“中世纪磨毛边的图纸”。现行恢复此前生成的压纹棕皮，采用粗纤维、不规则透明毛边、磨损缺口与旧折痕的完整羊皮图纸；金色仅作少量小配饰。保持统一暖象牙/棕/旧金配色与原分析类型化配饰。

## 实现及边界

- 外沿用生成羊皮图纸的真实alpha分片，无规整矩形回填、连续金属框或圆角裁切；外部边距仍28 DIP。网络皮革、正式雕金框/铭牌和整齐卷边候选不再嵌入或加载。
- 纸面保留纤维和压痕，以UniformToFill保持纹理比例；图签不再将整页纹理压成横向条纹。原绘图/图签字体、1200×900源图与160 DIP图签保持。暂不做16:9。
- 真实设置/同步/复制与导出文本/PNG/线宽/适应窗口、设置弹层、结果和绘图/数据/文本仍在同一固定原生控件树里。页签/按钮用软纸或缝线皮革底板；浅纸文档页签明确用深褐文字，关闭叉继续是真实按钮。
- 图例/书册配饰只用真实留白，数据、曲线、科学颜色、相机和品牌不由生成图片提供。原2D/3D内部角饰组合保持。PNG不输出程序操作栏/页签；报告保持原源图比例与物理字高。

## 实际离屏截图

全部由真实C#主窗口/控件与共享Core计算的Cooke示例离屏绘制，生成素材只用于装饰。未控制或重启用户正在运行的程序。

| 画面 | 链接 |
| --- | --- |
| 主窗口，13号字体 | [完整界面](workbench/workbench-1600-13.png) |
| 主窗口，18号字体 | [较大字体](workbench/workbench-1120-18.png) |
| 原生设置展开 | [13号](workbench/settings-1600-13.png)、[18号](workbench/settings-1120-18.png) |
| 惠更斯MTF / 星象配饰 | [界面](previews/Huygens-MTF-live.png)、[源图](../isekai-paper-gaps/tabs/Huygens-MTF-source.png) |
| 惠更斯PSF / 炼金配饰 | [界面](previews/Huygens-PSF-live.png)、[源图](../isekai-paper-gaps/tabs/Huygens-PSF-source.png) |
| 波前 / 法阵配饰 | [界面](previews/Wavefront-Map-live.png)、[源图](../isekai-paper-gaps/tabs/Wavefront-Map-source.png) |
| 点列 / 书卷配饰 | [界面](previews/Spot-Diagram-live.png)、[源图](../isekai-paper-gaps/tabs/Spot-Diagram-source.png) |

## 验证与中间失败

默认Release App（含内置模块）及测试构建完成；没有把另一个输出目录当成默认构建。材质/原生书封/真实工作区/主题资源四类共 **30项全部通过**，包含设置实际点击、关闭文档、原生键盘页签、浅纸文字、窄窗、源图逐像素和其他主题往返。

较宽集合[final-scoped.trx](final-scoped.trx)记录147通过、3失败、6项导出占位按设计跳过；当时工作区正有并行标题栏修改，3个失败均为托管窗口按钮主题切换后零尺寸。该文件不伪称全通过。工作区标题栏修复更新后重新默认Release构建，[caption-recheck.trx](caption-recheck.trx)9/9通过，包含原3个失败及快速切换/全屏恢复。没有由本批材质代码冒领标题栏修复。

独立启用实际Skia导出的[exports-final.trx](exports-final.trx)为23通过、0失败、0跳过，覆盖常规集合中跳过的导出检查。上述文件按完整用例名去重、采用最新结果，共 **169个不同用例通过、零未解决失败**；不将跳过占位算作通过，也不重复累加重跑。准确合并结果见[verification.json](verification.json)。这不是全仓测试或光学精度认证。

四个其他主题的8项PNG/report历史像素逐项一致，见[核对结果](other-theme-source-pixels.json)；使用原`artifacts/validation/isekai-multi-concepts-20261104/unchanged-pixels.json`，没有重建基线来消除差异。实时主题往返也由原生控件夹具验证。

初始[frayed-integration.trx](frayed-integration.trx)29通过、1失败：旧测试要求金属边中点完全不透明，实际羊皮散纤维为251 alpha。后续仅将装饰边改验半透明且alpha有自然变化，原科学源图所有边角及1像素线条的逐字节断言完整保留。另修复了浅纸页签文字对比和图签纹理被压成木纹的问题。

当前[源代码指纹](source-fingerprints.json)、[素材规范/完整提示词](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)、[生成原字节/尺寸/透明数据](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/frayed-assets.json)共同记录本阶段。历史网络/正式金框在[上一阶段](../isekai-crafted-workbench/README.md)保留。未提交、推送、打包、发布或重启用户窗口。
