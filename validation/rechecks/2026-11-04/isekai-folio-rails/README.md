# 异世界分析工具栏与底栏协调

用户明确本轮要改工具栏和底栏。保留上一轮中世纪磨毛羊皮图纸、折痕、毛边、图例及分析小配饰，未重新生成或下载素材。

## 改动

- 两栏共用带窄旧纸衬边的软边皮革条；13号字常规单行工具栏约48 DIP、底部56 DIP，去掉原工具栏中央独占金饰层。实际换行继续按控件测量，不裁掉命令。
- 命令空闲状态共享底色，悬停仍有反馈；底部仅当前原生页签用浅纸标记，其他页签为统一浅色文字，原生焦点和键盘保留。
- 顶部小分隔饰与底部天体饰放到实测空闲栏端；底饰缩小并降低浓度，空间不足仍省略。
- 原皮纹按520 DIP平铺，复用原图集的软边分片；不把小按钮的中心皮纹横向拉成木纹。

本轮只画实时栏部。主程序标题/菜单/Ribbon、文档页签、源图绘制、字体、物理数据、色标、相机、原1200×900图纸和28 DIP边距不变；16:9仍暂缓。

## 实际离屏渲染

由真实C#程序与Cooke示例结果绘制，生成素材只作既有装饰，不用效果图代替科学数据。

| 画面 | 链接 |
| --- | --- |
| 完整主窗口 | [13号字](workbench/workbench-1600-13.png)、[18号字](workbench/workbench-1120-18.png) |
| 原生设置展开 | [13号字](workbench/settings-1600-13.png)、[18号字](workbench/settings-1120-18.png) |
| 惠更斯MTF | [分析界面](previews/Huygens-MTF-live.png)、[原源图](previews/Huygens-MTF-source.png) |
| 惠更斯PSF | [分析界面](previews/Huygens-PSF-live.png) |
| 波前 | [分析界面](previews/Wavefront-Map-live.png) |
| 点列 | [分析界面](previews/Spot-Diagram-live.png) |

## 验证

默认Release App含内置模块及测试构建完成，[final-rails.trx](final-rails.trx) **10/10通过，零失败/跳过**。覆盖原生设置实际点击/收起、文档关闭、绘图/数据/文本与键盘切换、13/18号字、600×440窄窗、fit/100%、状态保留以及其他四主题往返。首轮[rails-integration.trx](rails-integration.trx)也通过，后将皮纹拉伸改为原比例铺陈，最终重验不重复累加。

额外逐像素核对4类Isekai原源图与上一轮完全相同，均为1256×956含框；其他四主题8项PNG/report历史哈希也一致，共12项核对。见[source-pixel-comparisons.json](source-pixel-comparisons.json)。这是源图像素核对，不把实时窗体缩放后的大小当作源图改动，也没有重建基线来消除差异。

当前改动/测试源码身份见[source-fingerprints.json](source-fingerprints.json)，材质来源与提示词仍见[素材规范](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)。本轮没有重新运行全仓或全部光学精度矩阵，不将上一轮169项历史检查和本轮10项相加。未重启/控制用户正在运行的应用，未提交、推送、打包或发布。
