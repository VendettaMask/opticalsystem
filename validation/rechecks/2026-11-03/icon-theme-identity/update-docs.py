from pathlib import Path
ROOT=Path(__file__).resolve().parents[4]
def edit(name,pairs):
    path=ROOT/name;text=path.read_text(encoding='utf-8-sig')
    for old,new in pairs:
        assert old in text,(name,old[:70]);text=text.replace(old,new)
    path.write_text(text,encoding='utf-8')
edit('docs/ICON_SEMANTIC_DESIGN.md',[
 ('规范 1.2','规范 1.3'),
 ('[五主题 SVG 包](../../按功能重绘图标_五主题SVG_20261103.zip)','[HUD与魔法主题SVG包](../../按主题重绘图标_HUD与魔法_20261103.zip)'),
 ('文字标签、滤镜、外链','文字标签、外链'),
 ('| Lumen | #839D9E | #D6AC6B | #A1B4B3 |','| Lumen HUD | 钢色渐变 / #6C8D9D | #78DCFF | #C4F5FF，参考点金色 |'),
 ('| Isekai | #91774E | #E1AB44 | #BEA16D |','| Isekai 剑与魔法 | #A18B5F | #EACC8B | #A6B48B |'),
 ('## 源码与扩展','光域采用钢铁侠式技术HUD：钢色结构、冰蓝信号、切角面板及分段仪表。异世界的底层是剑与魔法：指南使用魔导书，报告使用卷轴，编辑采用账册羽笔，光瞳与波前采用法阵和符文刻线，辅以炼金器具。基础操作保持简洁，不为全部功能套统一徽章。\n\n仅光域允许局部钢色渐变、信号边光及中心高亮；不作用于文字或数据。独立SVG使用本地固定32网格的定义，原生绘制使用对应材质，强调/禁用单色覆盖关闭材质。两个主题各有221款资源；普通、黑洞、像素保持原图。\n\n## 源码与扩展'),
 ('新增图形时为功能指定稳定语义 ID，','`build-icon-theme-variants.py` 输出 `Scientific/Themes/Lumen`、`Scientific/Themes/Isekai` 各221款，主题manifest记录442份资源及材质。元数据共享，几何按绘制线程延迟缓存，避免跨Dispatcher复用椭圆/矩形对象。\n\n新增图形时为功能指定稳定语义 ID，')])
edit('docs/LOCAL_ICONS.md',[
 ('**Optical Scientific Outline 1.2**','**Optical Scientific Outline 1.3**'),
 ('当前按用户要求，依据功能名称重新设计实际使用图形。','普通、黑洞及像素保持基础图形；光域采用科技HUD，异世界属于剑与魔法，使用魔导书、卷轴、法阵与炼金器具，各有221款独立主题SVG。'),
 ('[五主题 SVG 包](../../按功能重绘图标_五主题SVG_20261103.zip)','[HUD与魔法主题SVG包](../../按主题重绘图标_HUD与魔法_20261103.zip)'),
 ('不缓存上一主题的像素或画刷。','主题图形按独立资源身份缓存，几何按绘制线程缓存。'),
 ('| 光域 Lumen | TextMuted | Accent | TextSecondary |','| 光域 Lumen HUD | 钢色渐变 / #6C8D9D | #78DCFF | #C4F5FF，参考点金色 |'),
 ('| 异世界 Isekai | TextMuted | TextAccent | TextSecondary |','| 异世界 Isekai | #A18B5F | #EACC8B | #A6B48B |'),
 ('- `Assets/Icons/Scientific/Glyphs/*.svg`','- `Assets/Icons/Scientific/Themes/{Lumen,Isekai}/*.svg`：各221款独立主题造型；\n- `Assets/Icons/Scientific/Themes/manifest.json`：批准试稿、语法及材质；\n- `Assets/Icons/Scientific/Glyphs/*.svg`'),
 ('## 制作和来源保留','原主题1989组图标/尺寸像素基线保持；独立主题及完整图库验证见[主题身份接入](../validation/rechecks/2026-11-03/icon-theme-identity/README.md)。旧1.2图包保留当时资源，持续更新的图库以当前版本为准。\n\n## 制作和来源保留')])
edit('src/OpticalSystemDesign.App/Assets/Icons/Scientific/README.md',[
 ('Outline 1.2','Outline 1.3'),
 ('`Glyphs` 中的','`Themes/Lumen`与`Themes/Isekai`各有221款独立主题SVG。光域是钢色/冰蓝的分段技术HUD；异世界属于剑与魔法，以魔导书、卷轴、法阵和炼金器具表现；其余三主题保持基础图形。禁用/强调时关闭图标材质。几何按绘制线程缓存。\n\n`Glyphs` 中的')])
edit('docs/THEME_PACKAGES.md',[
 ('各主题使用统一 Optical Scientific Outline 图形及独立角色色，','各主题共用Optical Scientific Outline的功能语义与网格；普通/黑洞/像素保持基础图形，光域与异世界各有221款独立主题SVG。光域使用冰蓝HUD及钢色边，异世界属于剑与魔法，使用魔导书、卷轴、法阵与炼金器具。'),
 ('异世界与像素主题也使用统一科学轮廓图标，各自保留旧金及海军蓝/红/绿角色色。','异世界采用旧铜/浅金/灰绿的魔导器具风格，像素保留原科学轮廓及海军蓝/红/绿角色色。')])
edit('docs/UI_DESIGN_SPEC.md',[
 ('Optical Scientific Outline 1.2','Optical Scientific Outline 1.3'),
 ('不同分析按对象、响应、自变量和方法区分；','光域和异世界各有221款独立SVG：科技HUD与剑魔世界的魔导器具；普通、黑洞、像素保持原图。材质只作用于图标，禁用/强调回退可读单色。不同分析按对象、响应、自变量和方法区分；')])
print('Updated documentation for the approved 1.3 theme identities.')
