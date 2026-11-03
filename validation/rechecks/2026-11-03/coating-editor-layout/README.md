# 膜系编辑器TFStudio布局修正

用户进一步给出TFStudio的具体截图后，界面已按其结构重做：正/背面镀膜标签，表面与忽略另一面，五项短工具栏，行内材料选择，行尾锁定/移动/删除，表头下的基底边界和表格下的外侧介质。撤销、批量操作、膜层剪贴板、公式和高级评价设置收进工具/右键；底部保留折叠介质设置。以下预览均为重排后的实际C#离屏窗口。

- [22字号浮窗，参考TiO₂/SiO₂高反膜系](reference-floating-22.png)
- [亮主题22字号完整工作区](reference-Light-1440-22.png)
- [暗主题22字号完整工作区](reference-Dark-1440-22.png)
- [820宽、18字号工作区](reference-Light-820-18.png)
- [1200宽、13字号工作区](reference-Light-1200-13.png)

参考为仓库固定TFStudio归档，提交 `8b942b3f956728a23cea2b35023c8b5d8de17428`，SHA-256 `055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6`。对照`DesignEditor.js`、`LayerList.js`和`StackGeometryPanel.js`，未另存源树或运行时副本。[实现与参考说明](../../../../docs/COATING_EDITOR_LAYOUT_2026-11-03.md)记录具体映射。

## 当前验证

最终默认Release App和内置模块依赖构建零警告/错误，见[构建日志](build-release-final.log)。最终同一测试版本 **87/87 个定向用例通过，零失败/跳过**，历时8分42秒；以[最终TRX](final/editor-final.trx)、[测试日志](test-release-final.log)和[身份收据](receipt.json)为准，不累计中间批次。

测试目录App/Core/Engine在本轮运行前后哈希一致。共用目录的普通App于测试中途被图标工作重建，整份App DLL与测试版本不同，不标为全字节同版；PDB逐文件核对显示仅`ScientificIconLibrary.cs`、`ScientificIconPalette.cs`两个非镀膜源文件不同。**29个镀膜UI编译源校验和在测试版本、当前普通App及磁盘源码三方全部一致**；Core与镀膜Engine DLL仍完全相同。没有为追赶无关图标构建而重跑已通过的镀膜批次。

本轮定向范围：

- 明暗主题、13/18/22字号、820–1440宽工作区及1280×900浮窗；五项工具栏、表格主要高度、水平滚动与底部折叠媒体。
- 浮窗内实际指针/键盘改材、行锁、行移动、删除及撤销；原生菜单打开、批量设厚/换材、非连续多选上移/下移及整步撤销。
- 反向显示投影与原物理数组逐项一致；打开/保存、富剪贴板、公式、独立λ₀、原始精度及材料快照。
- 正背面切换、复制到另一面、整片评价、对称链接、停靠/浮动与布局恢复、异常退出恢复；同物理输入的双面传播/优化保护。

Core镀膜和Engine源文件没有改动，原始n/k、计算物理顺序与正式验收保持。界面从基底侧编号，两面的文件/剪贴板/Core数组仍各自为外侧→基板；行箭头和批量移动映射屏幕方向。λ₀变化保持物理膜厚，未移植参考程序按QW缩放膜厚的规则。

## 中间记录与边界

早期compact阶段只压缩按钮，已被用户这次截图要求替代；first/refined/typography记录不计入当前通过数。screenshot-shape阶段曾有重复Padding setter导致界面装载失败，修复后继续测试；screenshot-fixed剩余一项因测试找到了DataGrid隐藏复用的材料控件而失败，改为定位实际可见控件，保留原失败记录。主动中断的integration/test-final批次均记为未完成。

`reference-final/reference-editor-final.trx`曾为81通过/6失败：三个旧测试把实时行集合改成快照后仍期待增删更新，一个测试仍按旧的物理末尾插入检查材料，两个双面测试仍寻找已移入菜单的撤销按钮。仅修订这些测试入口与预期后，`adapted/reference-adapted.trx`的6项通过，前后App/Core/Engine哈希相同。随后补齐工具菜单的多选移动并重建，最终相同范围另起完整批次；不把前述81与6项或早期重复批次累加成新总数。

只做后台/离屏验证；输入事件发给测试内的Avalonia headless窗口，膜层剪贴板使用测试端口，不控制用户桌面或系统剪贴板。不据此宣称原生多屏、macOS GUI、全仓测试或安装发布通过。保留必要的不同视图预览、日志、TRX和元数据身份，已清理被替代的重复截图；没有冻结另一份源代码或构建运行时。

身份可用[汇总脚本](../../../../tools/diagnostics/CoatingEditorLayout20261103/Summarize-Editor.ps1)重查；参考归档、测试、源码、正常App与测试输出程序集的SHA-256均记录在收据中。

全仓严格文档检查发现1处本轮范围外的缺失链接：`docs/LOCAL_ICONS.md:63`指向尚未生成的`theme-scientific-icons/README.md`；未修改另一项进行中工作的文档，也不记为全仓文档检查通过。本轮镀膜文档没有断链，代码围栏、仅本机路径和生命周期问题均为0。见[检查日志](documentation.log)与[摘要](documentation-summary.json)。
