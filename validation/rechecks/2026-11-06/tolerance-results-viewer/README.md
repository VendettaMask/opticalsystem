# 公差数据查看器验收

离屏测试从工作区创建独立的「公差数据查看器」，运行双配置 Cooke 算例。固定种子 431，抽样 3 次，配置 2 的像侧间隔相对配置 1 增加 0.4 mm。使用两段装调、配置专属的补偿器和验收限值。

| 验收对象 | 合格数／试验数 | 良率 |
| --- | --- | --- |
| 整机 | 0／3 | 0.0% |
| 配置 1 | 3／3 | 100.0% |
| 配置 2 | 0／3 | 0.0% |

测试核对独立文档入口、运行前空页、关闭编辑器后的结果留存、灵敏度表、最差试验定位、两段装调及配置行程、计算失效筛选、处方变更后表格和直方图的失效状态。另以零次 Monte Carlo 运行反求，核对正负端点和图表入口状态。

| 150% 缩放 | 总览 | 逐次试验与第二阶段 |
| --- | --- | --- |
| Light，1080 DIP | [整机与配置良率](tolerance-results-overview-light-1080.png) | [左右并排](tolerance-results-trial-light-1080.png) |
| Dark，560 DIP | [整机与配置良率](tolerance-results-overview-dark-560.png) | [上下排列](tolerance-results-trial-dark-560.png) |

[运行前空页](tolerance-results-empty-light-1080.png) · [首次离屏测试](viewer-initial.trx) · [公差定向回归 123 项](viewer-final.trx) · [最终界面复验 17 项](viewer-layout.trx)

[扩展回归](viewer-acceptance.trx)记录 123 项通过和 1 项失败。关页保存测试在加载 Isekai 图像资源时缺少 Avalonia 渲染接口，尚未执行保存断言。[先前的单独复验](../joint-iso-surface-figure/workspace-isolated.trx)

## 关页保存复验

在 Light 和 Isekai 两个主题下使用带 Skia 的离屏会话，从实际运行对话框启动 Cooke 公差分析。取消运行时没有打开结果页，完成运行后结果页自动激活。关闭编辑器后结果仍在，取消保存时保留未保存状态。通过工作区保存入口写出 `.startol.json` 后，重新解析文件核对公差备注，重新打开编辑器时核对结果引用和编辑器实例。[首轮复验](viewer-dock-first.trx)

复验将自动保存的工作区布局指向独立临时目录。离屏复验前后核对应用默认布局文件的 SHA-256。

平铺复验分别打开镜头数据、二维视图、公差编辑器和公差结果页。首次复现中，二维光线开关坐标为 x=1884，超出 1500 DIP 的窗口。二维图工具条改用绘图与视口的可见交集，光线开关可点击。平铺截图仍可见二维图纸内容的裁切。[失效坐标和控件尺寸](viewer-dock-isolated.trx) · [四窗截图](docking-captures/mdi-three-interactive-panels.png) · [窄窗 200% 截图](docking-captures/mdi-narrow-2x.png)

最终扩展回归 22 项通过、1 项失败。公差与停靠相关 7 项均通过。三维实体模型在 Light、日文、400 DIP 和 18 DIP 字号组合下，自由旋转后仍保留 Bottom 预设。修正限于二维工具条后，该三维断言仍失败。最终离屏运行前后默认布局文件校验值一致。[初次扩展复验](viewer-dock-final.trx) · [最终 TRX](viewer-dock-regression.trx)
