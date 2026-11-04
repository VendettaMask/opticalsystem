# 界面译文生成输入

`optical-terms.tsv` 为光学术语；`ui-translations.tsv` 和 `ui-translations.json` 为补充译文；`invariant-text.tsv` 为保持原样的数据/符号声明。TSV 的前三列为原文、英文、日文，第四列可指定非中文源词条的中文；列之间使用 Tab。

使用 `tools/OpticalSystemDesign.Localization` 提取、同步和检查。应用只读取生成后内置的本地资源。新句子无法复用时会列为缺失并阻止同步，不自动发布未核对的译文。

完整流程、数据保护及静态提取边界见[本地化说明](../docs/LOCALIZATION.md)。
