# 科学图示矢量图标

本目录为 Optical Scientific Outline 1.3 的矢量源。全部图标以 32×32、1.65 圆角描边绘制，使用 structure、signal、secondary 以及对应的 fill 角色；Avalonia 按当前主题映射角色，不执行 SVG CSS、脚本或外链。

当前 332 处实际入口和状态使用 221 款语义图形：`Designed` 中 196 款按功能名称新绘，`Approved` 中 25 款保留用户已确认的本项目原稿。九种 MTF、不同像差/波前/报告以及光学、膜层、材料和方向按钮均按实际含义设计；不能用通用扫描框或折线替代不同分析。

`Themes/Lumen`与`Themes/Isekai`各有221款独立主题SVG。光域是钢色/冰蓝的分段技术HUD；异世界属于剑与魔法，以魔导书、卷轴、法阵和炼金器具表现；其余三主题保持基础图形。禁用/强调时关闭图标材质。几何按绘制线程缓存。

`Glyphs` 中的 1,748 个图标为保留的备用目录，来自固定 `lucide-static 1.25.0` 节点规范化，保留原功能几何、ISC 许可及 Feather 署名，不能标成原创或冒称全部为本轮重画。备用和专用图形合计 1,969 个资源名。

保持 [Lucide 许可证](../LUCIDE-LICENSE.txt) 与软件一起分发。运行时不使用旧 Game-icons 或 Farm-Fresh 主题包，但其已有资源仍保留原许可证和来源记录。

`scripts/semantic_icon_designs.py` 保存新绘路径及每项含义，`build-semantic-icons.py` 默认重建专用 SVG 和清单，不再次改写已接入的命令；首次绑定计划及执行记录在本轮验证目录保留。`export-semantic-icons.py` 导出实际 221 款的五主题图包、图库和设计说明。运行 `build-scientific-icons.py` 只重建备用 `Glyphs` 及总目录，不修改确认稿或新绘路径。全部工具离线运行；产品运行时按需解析内嵌矢量并延迟建立原生几何。

完整语义、主题映射与验证见 [图标指南](../../../../../docs/LOCAL_ICONS.md)。

后续新增或修改图标时，按[语义与主题造型规范](../../../../../docs/ICON_SEMANTIC_DESIGN.md)同步五主题资源、图库与图包、原 Word/网页规范及当前记录。异世界的古典炼金器具属于剑与魔法世界；光域采用钢铁侠式科技 HUD，不能以同图换色代替独立造型。
