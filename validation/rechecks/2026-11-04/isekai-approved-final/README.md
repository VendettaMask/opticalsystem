# 异世界同纸页眉与手稿页脚：确认版收尾

用户确认了[上一轮实际分析页](../isekai-manuscript-bands/workbench/analysis-workbench-1600-13.png)，随后撤回灰色/羽毛笔误解改版，并要求“按照确认的版本你去搞定”。现以该版本完成本地接入和复验。

## 最终范围

- 分析工具栏与底栏沿用同张羊皮纸、深褐命令墨色、细笔分隔；原生绘图/数据/文本以短墨线和点标出当前页，原栏端淡印保留。
- 原纸张、毛边、折痕、框体、配饰和色系保留。1200×900源图、160 DIP图签、28 DIP材质边、科学内容/颜色/字体及报告不变；16:9继续暂缓。
- 设置、同步、复制/导出文本、PNG、线宽、适应窗口、状态提示和原生导航均为真实控件。浅纸命令与深色原生飞出菜单各自保留可读墨色、焦点、悬停及禁用反馈。
- 五个确认版实现/测试文件指纹完全相同，收尾没有再次改画。灰色两张候选曾生成但从未嵌入/加载，已移出产品资源至[未采用候选归档](../../../../artifacts/design-candidates/2026-11-04/isekai-gray-not-applied/README.md)。

## 实际渲染

以下均为当前C#控件离屏渲染，使用共享Core的真实Cooke示例分析。独立分析页直接渲染实际AnalysisPanel，没有在截图上重画文字或科学曲线。

| 内容 | 截图 |
| --- | --- |
| 完整分析页 | [13号字](workbench/analysis-workbench-1600-13.png)、[18号字](workbench/analysis-workbench-1120-18.png) |
| 主窗口 | [13号字](workbench/workbench-1600-13.png)、[18号字](workbench/workbench-1120-18.png) |
| 设置展开 | [13号字](workbench/settings-1600-13.png)、[18号字](workbench/settings-1120-18.png) |
| 实际深色下拉菜单 | [13号字](workbench/menu-1600-13.png)、[18号字](workbench/menu-1120-18.png) |
| 其他分析页 | [PSF](previews/Huygens-PSF-live.png)、[波前](previews/Wavefront-Map-live.png)、[点列](previews/Spot-Diagram-live.png) |

## 验证结果

默认Release构建包含App、内置模块及测试程序集。最终单次[approved-final.trx](approved-final.trx) **17/17通过，零失败/跳过**：AnalysisResultBookTabsTests 8项、IsekaiCraftedWorkbenchTests 2项、IsekaiDrawingMaterialTests 7项。包含实际设置/关闭/菜单、原生键盘页签、600×440窄窗、fit/100%、13/18号字、浅纸命令/图标及深色菜单文字对比度、禁用反馈、毛边透明边界、源图几何和主题往返。

[12项源图核对](source-pixel-comparisons.json)全部一致：4类Isekai含框源图仍为1256×956，解码RGBA逐像素与羊皮纸本体阶段相同；其他四主题8项PNG/report哈希与原不可变基线一致。没有重新生成预期图以掩盖差异。

[确认与资源核对](approval-record.json)记录原确认截图身份、五个文件的指纹一致性、最终测试结果和实际程序集Isekai资源名单；[本次源码指纹](source-fingerprints.json)标明范围。程序集仍为9项自有生成材质，没有灰色候选、网络皮革或已否决的正式金属画框。

同步了AGENTS、主题规范、UI规范/复核、素材说明和当前状态。未重启或控制用户正在使用的应用；没有提交、推送、打包或发布。这是本地Release及相关离屏检查，未扩展为全仓回归或新的光学精度认证。前期检查、失败和修复保持各自历史身份，不与本次17项重复累计。
