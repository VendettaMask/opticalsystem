"""Update the maintained original icon standard; no application artwork changes."""
from pathlib import Path
import html,json,re

REPO=Path(__file__).resolve().parents[4]
WORK=REPO.parent
FIRST=WORK/'icon-redesign-20261103'
CURRENT=WORK/'redesigned-icons-20261103'
data=json.loads((FIRST/'_build/guide.json').read_text(encoding='utf-8-sig'))
data[0]['paragraphs']=[
    '全程序实际功能图标采用细线科学示意图，先按名称和实际功能确定对象、动作、自变量与方法，再绘制轮廓。基础图形使用结构、主信息和辅助三种角色；221款语义图形覆盖332处入口与状态，五主题共1105份普通状态SVG。',
    '普通、黑洞暗夜和像素保留已确认的基础图形。光域与异世界各有221款独立主题图稿，分别体现钢铁侠式科技HUD和剑与魔法世界。主题特色须服从功能识别，不能只换颜色或机械套相同徽章。首轮六个公差图标作为本规范的语义示例。'
]
for row in data[1]['table'][1:]:
    if row[0]=='显示尺寸': row[1]='18、24、32 px为主要验收尺寸；28、40、48 px可作补充检查'
data[2]['paragraphs'][0]='以对象轮廓为结构，将视觉注意力留给主要数据和操作。基础蓝青角色适用于普通主题；其他主题按自己的角色配色。一个图标可以只用结构与主信息，不机械凑齐三色。'
data[2]['paragraphs'][2]='主体以轮廓为主，点标记和极小数据单元可使用同角色实心填充。轻量曲面和椭圆基底用于专业对象；仅光域图标允许受控钢色渐变、信号边光及中心高亮，不扩展到文字、按钮或科学数据。'
data[5]={
    'title':'界面与五主题配色',
    'paragraphs':[
        '图标在32×32源网格内居中。工具栏保持左图右字，间距、分隔线、下拉箭头及按钮背景由现有控件负责。名称、命令、操作、布局、用户主题选择、品牌及物理光线和数据颜色保持。',
        '普通、黑洞暗夜和像素使用基础几何；光域与异世界解析各自的独立主题SVG。以下是功能图标的角色配色，与界面底色和主按钮颜色分别维护。'
    ],
    'table':[
        ['主题','功能图标角色'],
        ['普通 Light','结构 #8298A8；主信息 #398FF0；辅助 #28ADAF'],
        ['黑洞暗夜 Dark','结构 #A5978B；主信息 #F0AE79；辅助 #D5B89C'],
        ['光域 Lumen','钢色结构渐变，基准 #6C8D9D；主信息 #78DCFF；辅助 #C4F5FF，参考点金色 #D6AC6B'],
        ['异世界 Isekai','旧铜结构 #A18B5F；浅金主信息 #EACC8B；灰绿辅助 #A6B48B'],
        ['像素 Pixel','结构 #2F5169；主信息 #B8283A；辅助 #196937']
    ]
}
data[6]['paragraphs']=[
    '文件使用UTF-8和英文小写连字符命名。每个稳定功能ID对应可编辑矢量；保持viewBox="0 0 32 32"、1.65圆端点描边及structure、signal、secondary和对应fill角色。图形与描边等比缩放，不设置non-scaling-stroke。',
    'SVG背景透明，不嵌入位图、字体、外链、脚本、标签或按钮背景。普通、黑洞、异世界和像素保持平面轮廓；光域可使用本地钢色渐变与受控信号光晕，定义使用固定32网格的userSpaceOnUse坐标，不能遮住轮廓或使水平笔画消失。',
    '独立SVG为对应主题的普通状态。img显示的文件使用已导出的主题资源，不能假设外部页面类名会进入文件内部。程序主题切换会重新选择资源及画刷；禁用、强调区与对比度回退使用可读前景，并关闭光域材质。',
    '独立文件保留title，宿主提供可访问名称和必要提示。主题造型不能改变图形表达的对象、动作、自变量或方法，也不能覆盖品牌或物理数据的颜色。'
]
data[7]['steps']=[
    '逐项写明实际功能、对象、动作、输入或输出。功能不清楚时先核对界面或代码，不先画图。',
    '按稳定语义ID设计基础图形，复用有相同含义的部件。不同分析的响应、自变量与计算方法必须可区分，九项MTF不能共用通用折线或扫描框。',
    '为图元指定结构、主信息和辅助角色；保持32×32网格、1.65圆端点线条及透明背景。',
    '为光域和异世界分别设计有意义的主题造型。光域使用科技HUD；异世界在剑与魔法世界中选择魔导书、卷轴、法阵、符文或炼金器具。',
    '在18、24、32实际尺寸检查五主题、透明背景、功能差别、禁用与强调状态；主题切换和返回时轮廓与可读性应正确。',
    '同步功能对应表、源资源及主题清单、完整图库和SVG包；应用命令ID、文案、布局和物理数据保持。',
    '同步本Word及网页规范、仓库语义/本地图标/主题包/UI规范、变更记录和长期偏好。直接修正冲突的旧正文，历史资料标明适用版本。'
]
data[8]['checklist']=[
    '名称与图形可一一解释，不依赖随机装饰；编辑与查看、直方与曲线、良率与报告不会混淆。',
    '不同分析保留响应、自变量和方法的区别；MTF频率下降、穿焦峰值、随视场变化可区分。',
    '32×32网格、1.65圆端点描边、角色类名、透明背景及留白正确。',
    '18、24、32尺寸关键特征可辨，无裁断、粘连、过密网格或意外重叠。',
    '光域具有科技HUD的金属与冰蓝信号特征；异世界体现剑与魔法，炼金器具属于该世界。',
    '五主题与禁用、强调、背景对比度回退正确；光域材质只作用于图标。',
    '普通、黑洞及像素已确认图形与非图标资源保持，命令和物理数据语义保持。',
    'Word/网页/仓库规范、图库、SVG包、逐项对应表与记录一致。文档核对不计作产品测试通过。'
]
data[9]['paragraphs']=[
    '请按Optical Scientific Outline 1.3，以功能名称和真实操作确定对象、动作、自变量与方法，绘制32×32、1.65圆端点描边的透明SVG。普通、黑洞、像素沿用确认稿；光域使用钢色与冰蓝科技HUD；异世界保持剑与魔法，以魔导书、卷轴、法阵或炼金器具表达功能。18、24、32尺寸检查五主题及状态，同步规范、图库、SVG包和记录，保持命令与数据语义。'
]
data=data[:10]+[
    {'title':'光域与异世界独立造型','paragraphs':[
        '光域是钢铁侠式科技HUD。枪灰与钢色形成金属构件，冰蓝青光用于主信号，切角面板、分段仪表环和精密刻线用于对象结构。有限的高亮与边光加强仪器质感，不把光域解释成柔光玻璃风格。图标内部刻线用于识别，不在界面上增加假读数、假进度或装饰计数器。',
        '异世界的世界观固定为剑与魔法。指南采用魔导书，报告采用卷轴，编辑采用账册与羽笔，光瞳和波前采用法阵与符文刻线。古典炼金器具属于这个世界的专业语汇；不能把整套降为旧金配色的普通科学图。',
        '主题含义与功能含义同时成立。恢复工程仍表达历史恢复，保存仍表达保存，分析仍保留响应、自变量与方法。基础加减、导航保持简洁，不强制每款出现剑、魔杖或同一徽章。',
        '两套主题各有221款独立SVG，普通、黑洞与像素沿用基础图形。新增功能须在实际使用集和所有主题中补齐；当前完整清单以图库及功能对应表为准。'
    ]},
    {'title':'版本与维护记录','paragraphs':[
        '本规范当前为1.3。1.0建立六个公差图标的语义与细线规范；1.1统一功能图标角色和主题配色；1.2按实际功能重绘221款基础语义图形；1.3增加光域HUD与异世界剑与魔法的独立造型，并同步原有规范正文与维护记录。',
        '当前交付目录为redesigned-icons-20261103，包含完整图库index.html、逐项设计说明.html、功能与图标对应表.csv、五主题svg目录及442份theme-masters。图标风格规范.docx与图标风格规范.html是本规范的同版副本。',
        '最早的六图SVG及preview目录保留首轮设计对照，不代表当前五主题资源。公差图标与风格规范.zip及旧五主题1.2 ZIP属于历史交付；当前图标和规范使用按主题重绘图标_HUD与魔法_20261103.zip。产品原生与图库验证保留各自范围，文档同步不重新累计产品测试。'
    ]}
]
(FIRST/'_build/guide.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
intro='本规范用于后续批量改图：按实际名称与功能设计，并同时满足五主题造型。当前为Optical Scientific Outline 1.3，光域采用钢铁侠式科技HUD，异世界保持剑与魔法世界观；古典炼金器具属于异世界语汇。'
old=(FIRST/'style-guide.html').read_text(encoding='utf-8')
style=re.search(r'<style>(.*?)</style>',old,re.S).group(1)
sections=[]
for n,s in enumerate(data,1):
    body=''.join('<p>'+html.escape(p)+'</p>' for p in s.get('paragraphs',[]))
    if 'table' in s:
        body+='<table>'+''.join('<tr>'+''.join(f'<{"th" if i==0 else "td"}>'+html.escape(c)+f'</{"th" if i==0 else "td"}>' for c in row)+'</tr>' for i,row in enumerate(s['table']))+'</table>'
    for key,tag in [('steps','ol'),('checklist','ul')]:
        if key in s:body+=f'<{tag}>'+''.join('<li>'+html.escape(p)+'</li>' for p in s[key])+f'</{tag}>'
    sections.append(f'<section><h2>{n} {html.escape(s["title"])}</h2>{body}</section>')
guide='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>图标风格规范 1.3</title><style>'+style+'</style><main><h1>光学软件科学图示风格规范</h1><p class="meta">Optical Scientific Outline · v1.3 · 2026年11月3日</p><p>'+intro+'</p><p><a href="../redesigned-icons-20261103/index.html">当前全部图标</a> · <a href="../redesigned-icons-20261103/设计说明.html">逐项设计说明</a> · <a href="图标风格规范.docx">Word规范</a></p><img src="preview/icon-overview.png" alt="首轮六个公差图标的语义示例"><p class="meta">首轮示例保留设计起点，当前五主题造型使用上方的完整图库。</p>'+''.join(sections)+'</main></html>'
(FIRST/'style-guide.html').write_text(guide,encoding='utf-8')
current=guide.replace('../redesigned-icons-20261103/','').replace('preview/icon-overview.png','规范示例.png')
(CURRENT/'图标风格规范.html').write_text(current,encoding='utf-8')
tokens=json.loads((FIRST/'design-tokens.json').read_text(encoding='utf-8-sig'))
tokens['version']='1.3'
tokens['dark']={'structure':'#A5978B','signal':'#F0AE79','secondary':'#D5B89C'}
tokens['canvas']['recommendedSizes']=[18,24,28,32,40,48]
tokens['themes']=json.loads((CURRENT/'theme-colors.json').read_text(encoding='utf-8-sig'))
tokens['currentGallery']='../redesigned-icons-20261103/index.html'
tokens['exampleScope']='Six original tolerance designs. Original svg/ and preview/ are v1.0 references; current five-theme production SVGs are in currentGallery.'
tokens['independentProfiles']={
    'Lumen':{'identity':'Iron-Man-like technical HUD','features':['gunmetal and steel','ice-cyan signals','chamfered panels','segmented instruments'],'count':221,'materials':'Local fixed-coordinate steel gradient and bounded signal glow; readable monochrome on disabled/emphasis'},
    'Isekai':{'identity':'Sword and magic world','features':['grimoires','scrolls','magic circles','runic calibration','classical alchemical instruments'],'count':221,'materials':'Flat role colours; each feature explains the actual function'}
}
tokens['rules']=[
    '先确定功能对象、动作、自变量和方法，再选择图形；不同含义不能只靠颜色区分。',
    '基础语义与网格共享；光域与异世界分别设计主题造型，不能仅换色或机械套同一徽章。',
    '普通、黑洞及像素保持已确认基础图形；各主题角色色及禁用/强调状态由宿主适配。',
    '18、24、32尺寸检查五主题；只有光域允许本地图标材质，不作用于文字、布局或物理数据。',
    '同步原Word/网页规范、仓库规范、图库、SVG包、功能对应表、记录和长期偏好；直接修正冲突正文。'
]
(FIRST/'design-tokens.json').write_text(json.dumps(tokens,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(FIRST/'使用说明.txt').write_text('图标风格规范 1.3\n\n原有Word、style-guide.html、design-tokens.json已同步当前五主题要求。\n光域：钢铁侠式科技HUD。异世界：剑与魔法，含魔导书、卷轴、法阵、符文与古典炼金器具。\n\n当前全部221款图标、332处功能与状态及五主题1105份SVG：../redesigned-icons-20261103/index.html。\n本目录svg/及preview/保留首轮六图1.0设计对照；当前生产资源请使用完整图库svg目录。\n公差图标与风格规范.zip保留历史首轮交付；当前规范及完整图包见../按主题重绘图标_HUD与魔法_20261103.zip。\n后续改图同时更新规范、图库、SVG包、功能表、记录与长期偏好。\n',encoding='utf-8')
builder=FIRST/'_build/build-guide.py'
code=builder.read_text(encoding='utf-8-sig')
code=code.replace('v1.0   2026年11月3日','v1.3   2026年11月3日')
code=code.replace("doc.core_properties.subject='公差工具栏 SVG 功能语义与后续批量改图标准'","doc.core_properties.subject='功能语义 五主题造型与后续批量改图维护标准'")
code=code.replace('本规范以现有分析工具栏为视觉基准，将公差工具栏的六个功能重绘为一致的 SVG，并为后续批量改图提供可复用的功能语义、配色和检查标准。',intro)
code=code.replace('本批重绘 6 个公差功能图标。独立 SVG 位于 svg 文件夹，交互预览位于 preview/index.html。','首轮六个公差功能图标作为语义示例。当前全部221款、五主题图标见redesigned-icons-20261103中的完整图库。')
code=code.replace('for n in range(2,11):section(n,new_page=n in [2,4,6,8])','for n in range(2,len(sections)+1):section(n,new_page=n in [2,4,6,8])')
code=code.replace('new_page=n in [2,4,6,8,11]','new_page=n in [2,4,6,8]')
code=code.replace("doc.add_picture(str(ROOT/'preview/icon-overview.png'),width=Inches(7.06))","doc.add_picture(str(ROOT/'preview/icon-overview.png'),width=Inches(6.15))\ndoc.paragraphs[-1].alignment=WD_ALIGN_PARAGRAPH.CENTER")
code=code.replace("    for p in obj.get('paragraphs',[]):doc.add_paragraph(p)","    for p in obj.get('paragraphs',[]):\n        paragraph=doc.add_paragraph(p)\n        if n==10:paragraph.paragraph_format.keep_together=True")
builder.write_text(code,encoding='utf-8')

# Label superseded preferences rather than leaving two current contracts.
agents=REPO/'AGENTS.md'
text=agents.read_text(encoding='utf-8-sig')
text=text.replace('- User-approved unified icon direction (2026-11-03):','- Historical first unified icon direction (2026-11-03; the approved theme identities and semantic redraw above supersede its used-icon geometry and Lumen/Isekai palette clauses):')
agents.write_text(text,encoding='utf-8')
print('Updated original HTML, authoring source and tokens to 1.3; original SVGs unchanged.')
