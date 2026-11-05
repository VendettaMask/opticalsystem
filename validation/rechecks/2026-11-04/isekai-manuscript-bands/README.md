# 同纸页眉与手稿页脚：已确认版本

用户要求工具栏和底栏再换风格，随后确认本页实际画面，并要求按确认版本完成。两栏作为图纸上的页眉/页索引：去掉上一轮皮革带，复用原纸面底，采用深褐墨色命令/原造型图标、细笔分隔和原栏端淡印；底部原生页签以短墨线和点标出当前页。后续灰色/羽毛笔误解改版已被用户撤回，未接入程序。此处保留原确认画面和当时检查，[收尾验收](../isekai-approved-final/README.md)单独记录。

## 范围

- 仅实时分析工具栏与底部导航改变。纸张PNG、毛边/折痕、绘图/图签源绘制、源图比例、字体、真实数值/曲线/科学色标不变。主程序标题/菜单/Ribbon和文档页签也不在本轮范围。
- 设置、同步、复制/导出文本、PNG、线宽、适应窗口、状态提示及取消操作均继续使用真实控件。禁用文字保留淡墨反馈，原生键盘选择、焦点、关闭和设置弹层保持。
- 浅纸命令文字/图标和原生深色飞出菜单分别保持可读。命令墨色只匹配按钮自身的内容/呈现器，不跨入逻辑归属于按钮的菜单；状态绿/警告/失败仍有语义。
- 没有生成/下载新素材。原1200×900源图、160 DIP图签和28 DIP外部材质边保持，16:9仍暂缓。

## 实际程序渲染

真实C#控件及共享Core的Cooke示例结果离屏绘制。单独分析页由同一个实际AnalysisPanel直接渲染，不是生成概念图，也没有在截图上重画命令文字或曲线。

| 画面 | 链接 |
| --- | --- |
| 独立分析页，13号字 | [确认画面](workbench/analysis-workbench-1600-13.png) |
| 独立分析页，18号字 | [较大字体](workbench/analysis-workbench-1120-18.png) |
| 完整主窗口 | [13号字](workbench/workbench-1600-13.png)、[18号字](workbench/workbench-1120-18.png) |
| 深色原生下拉菜单 | [13号字](workbench/menu-1600-13.png)、[18号字](workbench/menu-1120-18.png) |
| 设置展开 | [13号字](workbench/settings-1600-13.png)、[18号字](workbench/settings-1120-18.png) |
| 其他分析配饰 | [PSF](../isekai-paper-gaps/tabs/Huygens-PSF-live.png)、[波前](../isekai-paper-gaps/tabs/Wavefront-Map-live.png)、[点列](../isekai-paper-gaps/tabs/Spot-Diagram-live.png) |

## 验证

默认Release App含内置模块与测试构建完成，[final-scoped-ink.trx](final-scoped-ink.trx) **10/10通过，零失败/跳过**。包括真实设置/关闭、原生页签与键盘、600×440窄窗、fit/100%、13/18号字和其他四主题往返；增强检查验证浅纸命令及图标对比度≥4.5、禁用文字与启用状态可区分、实际打开的原生深色菜单所有有字标签对比度≥4.5。

4类Isekai原源图与羊皮纸本体阶段逐像素相同，均为1256×956含框；其他四主题8项PNG/report历史哈希一致。[12项核对结果](../isekai-folio-rails/source-pixel-comparisons.json)沿用原历史基线，没有重新生成预期图来掩盖差异。

中间结果保留：manuscript-initial.trx与final-manuscript.trx各10项通过；增强实际菜单文本检查后final-ink-and-menu.trx有2项失败。随后收紧命令墨色选择器，并排除没有文字的菜单占位；所有真正有字标签的4.5对比度断言保留，最终10项全部通过。不同阶段不重复累加为通过率。本轮没有扩大到全仓或重新认证光学精度。

[源码指纹](source-fingerprints.json)、[现行素材/生成提示词](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)及[上一轮皮革带](../isekai-folio-rails/README.md)记录各自范围。没有重启/控制用户当前应用、提交、推送、打包或发布。
