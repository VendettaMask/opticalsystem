"""Deliver the actual semantic drawings with five theme exports and an offline review gallery."""
from pathlib import Path
from copy import deepcopy
from collections import defaultdict
import csv,hashlib,html,json,re,shutil,textwrap,xml.etree.ElementTree as ET

REPO=Path(__file__).resolve().parents[1];WORK=REPO.parent
EVIDENCE=REPO/'validation/rechecks/2026-11-03/semantic-icon-redraw'
OUT=WORK/'redesigned-icons-20261103';OUT.mkdir(exist_ok=True)
ASSETS=REPO/'src/OpticalSystemDesign.App/Assets/Icons/Scientific'
N='{http://www.w3.org/2000/svg}';ET.register_namespace('',N[1:-1])
def read(p):return re.sub(r'\r+\n','\n',p.read_bytes().decode('utf-8-sig')).replace('\r','\n')
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def sha(b):return hashlib.sha256(b).hexdigest()
manifest=json.loads(read(EVIDENCE/'semantic-manifest.json'))
INPUTS=REPO/'scripts/icon-gallery-inputs'
themes=json.loads(read(INPUTS/'theme-colors.json'))['themes']
theme_profiles=json.loads(read(ASSETS/'Themes/manifest.json')) if (ASSETS/'Themes/manifest.json').exists() else None
if theme_profiles:
    for profile in theme_profiles['profiles']:
        t=next(t for t in themes if t['key']==profile['theme'])
        t.update({k:profile['palette'][k] for k in ('structure','signal','secondary')})
        t['name']='光域 · 科技HUD' if t['key']=='Lumen' else '异世界 · 剑与魔法'
uses=deepcopy(manifest['uses'])
items=[];new_ids=set(manifest['newlyAuthored']);by_def={d['id']:d for d in manifest['usedDrawings']}
viewer=(REPO/'src/OpticalSystemDesign.App/Panels/ViewerPanel.cs').read_text(encoding='utf-8-sig').splitlines()
for u in uses:
    if u.get('native'):u['line']=next(n for n,line in enumerate(viewer,1) if '(ViewCubeFace.'+u['designedId'][5:].capitalize()+',' in line)
for id_ in sorted(by_def,key=lambda n:(n in new_ids,not n.startswith('mtf-'),by_def[n]['category'],n)):
    file=ASSETS/('Designed' if id_ in new_ids else 'Approved')/(id_+'.svg')
    root=ET.fromstring(file.read_bytes());body=''.join(ET.tostring(node,encoding='unicode').replace(' xmlns="http://www.w3.org/2000/svg"','') for node in root if node.tag==N+'g')
    definition=by_def[id_]
    item=dict(id=id_,label=definition['label'],meaning=definition['meaning'],group=definition['category'],approved=id_ not in new_ids,native=False,
              function='；'.join(dict.fromkeys(u['label'] for u in uses if u['designedId']==id_)),
              uses=[dict(label=u['label'],group=u['group'],source=u['file'],line=u['line'],method=u['method']) for u in uses if u['designedId']==id_],
              source=file.relative_to(REPO).as_posix(),sourceSha256=sha(file.read_bytes()),body=body)
    if theme_profiles:
        item['themeSvgs']={};item['themeSources']={}
        for theme in ('Lumen','Isekai'):
            variant=ASSETS/'Themes'/theme/(id_+'.svg')
            item['themeSvgs'][theme]=read(variant)
            item['themeSources'][theme]=dict(source=variant.relative_to(REPO).as_posix(),sha256=sha(variant.read_bytes()))
            destination=OUT/'theme-masters'/theme;destination.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(variant,destination/variant.name)
    items.append(item);(OUT/'masters').mkdir(exist_ok=True);shutil.copyfile(file,OUT/'masters'/file.name)
assert len(items)==221 and sum(i['approved'] for i in items)==25
def css(t):return f'.structure{{fill:none;stroke:{t["structure"]}}}.signal{{fill:none;stroke:{t["signal"]}}}.secondary{{fill:none;stroke:{t["secondary"]}}}.signal-fill{{fill:{t["signal"]};stroke:none}}.secondary-fill{{fill:{t["secondary"]};stroke:none}}.glyph{{stroke-width:1.65;stroke-linecap:round;stroke-linejoin:round}}'
def svg(i,t):return f'<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32" role="img" aria-label="{html.escape(i["label"],quote=True)}"><title>{html.escape(i["label"])}</title><style>{css(t)}</style>{i["body"]}</svg>\n'
for t in themes:
    folder=OUT/'svg'/t['key'];folder.mkdir(parents=True,exist_ok=True)
    for i in items:(folder/(i['id']+'.svg')).write_text(i.get('themeSvgs',{}).get(t['key'],svg(i,t)),encoding='utf-8',newline='\n')

mtf=[]
for u in uses:
    if u.get('commandId') and u['group']=='分析 · MTF 曲线':mtf.append(dict(commandId=u['commandId'],label=u['label'],icon=u['designedId'],group='MTF 曲线'))
assert len(mtf)==9 and len({m['icon'] for m in mtf})==9
groups=sorted({u['group'] for u in uses})
data=dict(themes=themes,icons=items,mtf=mtf,groups=groups,uses=uses,counts=dict(total=221,sites=332,approved=25,variants=1105))
template=read(INPUTS/'gallery.template')
template=template.replace('软件实际使用的全部图标','按功能语义重绘 · 全部实际图标').replace('OPTICAL SCIENTIFIC OUTLINE / 1.1','OPTICAL SCIENTIFIC OUTLINE / 1.2')
template=template.replace('按实际界面绑定筛选，包含下拉菜单、页面工具、弹窗及内置模块的全部功能图标。','依照功能名称设计，221 款语义图形覆盖 332 处入口和状态；196 款新绘，保留 25 款先前确认稿。')
template=template.replace('种实际用图','款语义图形').replace('按图形去重','按语义图形').replace('所有界面','所有界面 / 功能').replace('当前界面用图','专用重绘')
template=template.replace('overview/used-icons-Dark.svg','overview/semantic-icons-Dark.svg').replace('overview/used-icons-${key}.svg','overview/semantic-icons-${key}.svg')
template=template.replace('<details style="margin-bottom:20px">','<details open style="margin-bottom:20px">')
template=template.replace('截图中的 MTF 菜单 · 9 项实际映射（展开）','MTF 九项已分别重绘 · 按计算方法与自变量区分')
template=template.replace('这 9 项已统一线宽和主题配色，当前共用 5 种通用图形。“离焦”三项共用扫描线，“随视场”三项共用折线图；尚未分别重画成九个专用图标。以下与当前源代码映射一致。','频率响应采用下降曲线；离焦采用穿焦峰值和焦面参考；随视场采用场点扇与场边变化。采样频谱、次波圆弧、几何光线分别区分傅里叶、惠更斯和几何方法。')
template=template.replace('统一风格','专用重绘').replace('当前使用通用图形；统一了风格与主题色，尚未逐项专用重画。','这是一项单独设计的 MTF 图形；计算方法和扫描自变量都有明确图示。')
template=template.replace('Lucide 1.25.0 / ISC · 保留功能几何，统一网格、描边及三种角色色','根据本功能语义新绘的 32 × 32 SVG · 1.65 圆端点描边')
template=template.replace("$('total-icons').textContent=data.counts.total;", "$('mode').value='shapes';$('total-icons').textContent=data.counts.total;")
start=template.index('<footer class="foot">');end=template.index('</footer>',start)+len('</footer>')
template=template[:start]+'''<footer class="foot"><p>本套包含 196 款按功能新绘的 SVG 和 25 款保留的已确认专用稿。物理对象、动作、自变量与方法负责表达含义；三种色彩角色由主题提供。不同分析不再套用同一张通用折线图或扫描框。</p><p>按语义图形查看 221 款完整集合，或按功能展开 332 处引用和切换状态。7 个方向按钮也已改用同一规范的 SVG。程序中的实际命令绑定已同步，文字、操作和数值计算保持。</p><p><a href="设计说明.html">规范与逐项设计说明</a><a href="功能与图标对应表.csv" download>完整功能对应表</a><a href="theme-colors.json" download>主题配色</a><a href="使用说明.txt">使用说明</a></p><p>单个 SVG 为透明背景普通状态；程序保留禁用、强调和宿主背景对比度适配。备用 Lucide 图标库继续保留原来源，但未混入这套实际使用的新绘图。</p></footer>'''+template[end:]
if theme_profiles:
    template=template.replace('OPTICAL SCIENTIFIC OUTLINE / 1.2','OPTICAL SCIENTIFIC OUTLINE / 1.3')
    template=template.replace('依照功能名称设计，221 款语义图形覆盖 332 处入口和状态；196 款新绘，保留 25 款先前确认稿。','221 款功能图标：光域采用科技 HUD，异世界采用剑与魔法的魔导仪器；普通、黑洞及像素保留原版本。')
    template=template.replace('function iconMarkup(item,size=32){return', 'function iconMarkup(item,size=32){if(item.themeSvgs?.[currentTheme])return `<img class="icon" width="${size}" height="${size}" alt="${esc(item.label)}" src="data:image/svg+xml;charset=utf-8,${encodeURIComponent(item.themeSvgs[currentTheme])}">`;return')
    template=template.replace('function standalone(item){const', 'function standalone(item){if(item.themeSvgs?.[currentTheme])return item.themeSvgs[currentTheme];const')
    a=template.index('for(const c of data.mtf){');b=template.index('\nfunction search()',a)
    template=template[:a]+"function renderMtf(){$('mtf-grid').replaceChildren();"+template[a:b]+'}\n'+template[b:]
    template=template.replace("$('download').textContent=`下载${t.name.split(' · ')[0]} SVG`;render()}","$('download').textContent=`下载${t.name.split(' · ')[0]} SVG`;renderMtf();render()}")
    template=template.replace('物理对象、动作、自变量与方法负责表达含义；三种色彩角色由主题提供。','普通/黑洞/像素保持原图；光域和异世界各有完整 221 款专用造型与材质。')
    template=template.replace('单个 SVG 为透明背景普通状态；','光域使用分段仪表、金属切角和冰蓝青光；异世界以剑与魔法为背景，使用魔导书、卷轴、法阵及炼金器具。单个 SVG 为透明背景普通状态；')
encoded=json.dumps(data,ensure_ascii=False,separators=(',',':')).replace('<','\\u003c').replace('>','\\u003e').replace('&','\\u0026')
(OUT/'index.html').write_text(template.replace('/*__CATALOG__*/',encoded),encoding='utf-8')

catalog=dict(date='2026-11-03',style='Optical Scientific Outline',version='1.3' if theme_profiles else '1.2',uniqueIcons=221,newlyDrawn=196,preservedApproved=25,uiReferencesAndStates=332,themeCount=5,themedSvgFiles=1105,
 themes=themes,mtfMenu=mtf,icons=[{k:v for k,v in i.items() if k not in ('body','themeSvgs')} for i in items],uses=uses,sourceManifestSha256=sha((EVIDENCE/'semantic-manifest.json').read_bytes()))
if theme_profiles:catalog['themeProfiles']=theme_profiles
dump(OUT/'catalog.json',catalog);dump(OUT/'theme-colors.json',dict(grid=[32,32],strokeWidth=1.65,themes=themes))
with (OUT/'功能与图标对应表.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.writer(f);writer.writerow(['所在界面','功能名称','重绘图标ID','源绑定','源行','设计含义'])
    for u in sorted(uses,key=lambda u:(u['group'],u['file'],u['line'])):writer.writerow([u['group'],u['label'],u['designedId'],u['file'],u['line'],by_def[u['designedId']]['meaning']])

(OUT/'overview').mkdir(exist_ok=True)
cols=8;tw=180;th=126;width=cols*tw+48;height=150+((len(items)+cols-1)//cols)*th
for t in themes:
    themed=theme_profiles and t['key'] in ('Lumen','Isekai')
    extra_defs='';style=css(t)
    if themed:
        reference=ET.fromstring(items[0]['themeSvgs'][t['key']]);defs_node=reference.find(N+'defs')
        if defs_node is not None:extra_defs=ET.tostring(defs_node,encoding='unicode').replace(' xmlns="http://www.w3.org/2000/svg"','')
        style=reference.find(N+'style').text
    out=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}"><title>全部 221 款语义图标 · {t["name"]}</title>{extra_defs}<style>{style}text{{font-family:"Microsoft YaHei UI",sans-serif;fill:{t["text"]}}}.muted{{fill:{t["muted"]}}}</style><rect width="100%" height="100%" fill="{t["surface"]}"/><text x="24" y="43" font-size="25">按名称与功能设计 · 全部 221 款图标</text><text x="24" y="77" font-size="14">{t["name"]} / 覆盖 332 处入口及状态 / 规范 {"1.3" if theme_profiles else "1.2"}</text><text x="24" y="105" font-size="12" class="muted">独立主题造型与材质 · 32 × 32 · 1.65 圆端点描边 · 2026-11-03</text>']
    for n,i in enumerate(items):
        x=24+n%cols*tw;y=132+n//cols*th
        label=i['label'];label=label[:14]+'…' if len(label)>15 else label
        graphic=i['body']
        if themed:graphic=ET.tostring(ET.fromstring(i['themeSvgs'][t['key']]).find(N+'g'),encoding='unicode').replace(' xmlns="http://www.w3.org/2000/svg"','')
        out.append(f'<g transform="translate({x},{y})"><title>{html.escape(i["label"]+"："+i["meaning"])}</title><g transform="translate(65,0) scale(1.5)">{graphic}</g><text x="89" y="67" text-anchor="middle" font-size="12">{html.escape(label)}</text>')
        for k,line in enumerate(textwrap.wrap(i['id'],width=27,break_on_hyphens=True)):out.append(f'<text x="89" y="{86+12*k}" text-anchor="middle" font-size="9" class="muted">{html.escape(line)}</text>')
        out.append('</g>')
    out.append('</svg>');(OUT/'overview'/f'semantic-icons-{t["key"]}.svg').write_text(''.join(out),encoding='utf-8',newline='\n')

rows=''.join(f'<tr><td>{html.escape(i["label"])}</td><td><code>{i["id"]}</code></td><td>{html.escape(i["meaning"])}</td></tr>' for i in items)
guide=f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>功能图标设计规范 1.2</title><style>body{{max-width:1100px;margin:35px auto;padding:0 24px;font:15px/1.8 "Microsoft YaHei UI",sans-serif;color:#24374b}}h1{{font-size:27px}}h2{{font-size:20px;margin-top:28px}}table{{border-collapse:collapse;width:100%;font-size:13px}}td,th{{border:1px solid #d7e3ee;padding:9px;text-align:left;vertical-align:top}}code{{font-size:11px;overflow-wrap:anywhere}}a{{color:#287cd3}}</style><h1>功能图标设计规范 · 1.2</h1><p>本套以实际功能名称和用户操作对象为设计依据。221 款图形覆盖 332 处界面引用与状态，其中 196 款新绘，25 款沿用已确认的专用原稿。</p><h2>视觉语法</h2><p>源网格为 32 × 32，普通线宽 1.65，端点及连接均为圆形。结构色用于对象边界、坐标、容器；主信息色用于动作或分析特征；辅助色用于参考线、方法标识、次要节点。保持透明背景，不把文字、按钮背景或下拉箭头画入 SVG。以 18、24、32 的真实界面尺寸检查。</p><h2>先确定含义，再选择图形</h2><p>先写清对象与动作，例如“对膜层锁定”“按波长观察 RMS”“导出图像”。相同对象的不同动作保留对象轮廓，以功能标记区分；不同分析则用该分析的数据结构、横向自变量和计算方法区分。常用添加、删除、保存、导航允许共享同语义图形。</p><p>MTF 的频率、穿焦、随视场不能共用折线：分别使用下降响应、穿焦峰值、视场点及场边变化。RMS 的最佳位置是最小值，穿焦 MTF 的最佳位置是最大值。傅里叶、惠更斯、几何采用采样频谱、次波圆弧与几何光线标识。PSF 用点峰与衍射环，圈入能量用包围圈与累积上升；光程差、波前图、干涉条纹、刀口检验分别采用对应结构。</p><h2>主题与状态</h2><p>Light 使用蓝青，Dark 使用黑洞暖灰/橙/砂金，Lumen 使用灰青/金，Isekai 使用旧金，Pixel 使用深蓝/红/绿。几何共享，角色按主题取色。程序保留禁用与强调状态适配；物理光线、曲线、材料和标志色不作为功能图标色更改。</p><h2>逐项设计说明</h2><table><thead><tr><th>功能名称</th><th>图标 ID</th><th>图形含义</th></tr></thead><tbody>{rows}</tbody></table><p><a href="index.html">返回完整图库</a></p></html>'''
if theme_profiles:
    guide=guide.replace('1.2','1.3').replace('Lumen 使用灰青/金，Isekai 使用旧金','Lumen 使用枪灰钢、冰蓝青光和分段 HUD，Isekai 以剑与魔法为背景，使用魔导书、卷轴、法阵及炼金器具')
    guide=guide.replace('几何共享，角色按主题取色。','普通、黑洞和像素保留原图；Lumen 与 Isekai 各有 221 款独立主题 SVG，保持功能含义。HUD 金属渐变与光晕只作用于图标，强调/禁用状态回退可读单色。')
    guide=guide.replace('<h2>逐项设计说明</h2>','<h2>长期主题方向与维护</h2><p>光域固定为钢铁侠式科技 HUD：钢色金属结构、冰蓝青光、切角面板与分段仪表。异世界固定为剑与魔法世界：魔导书、卷轴、法阵、符文与古典炼金器具服务于实际功能；炼金属于该世界的器具语汇。不能仅换色或机械套同一徽章。</p><p>后续改图须同步五主题资源、功能对应表、原 Word/网页规范、仓库语义/主题包/UI/本地图标说明、图库与 SVG 包、当前记录和长期偏好；直接修订冲突旧正文。文档检查与产品验证分别记录。</p><p><a href="图标风格规范.html">完整风格规范 1.3</a> · <a href="图标风格规范.docx">Word 规范</a></p><h2>逐项设计说明</h2>')
(OUT/'设计说明.html').write_text(guide,encoding='utf-8')

# Keep the first delivered Word/HTML standard available in the current pack.
first_guide=WORK/'icon-redesign-20261103'
if not first_guide.exists():first_guide=REPO/'docs/assets/icons'
if theme_profiles and (first_guide/'图标风格规范.docx').exists():
    shutil.copy2(first_guide/'图标风格规范.docx',OUT/'图标风格规范.docx')
    guide_file=first_guide/'style-guide.html' if (first_guide/'style-guide.html').exists() else first_guide/'图标风格规范.html'
    full_guide=read(guide_file).replace('../redesigned-icons-20261103/','').replace('preview/icon-overview.png','规范示例.png')
    (OUT/'图标风格规范.html').write_text(full_guide,encoding='utf-8')
    example=first_guide/'preview/icon-overview.png' if (first_guide/'preview/icon-overview.png').exists() else first_guide/'规范示例.png'
    shutil.copy2(example,OUT/'规范示例.png')
    index=read(OUT/'index.html').replace('<a href="设计说明.html">规范与逐项设计说明</a>','<a href="设计说明.html">规范与逐项设计说明</a><a href="图标风格规范.html">完整风格规范 1.3</a><a href="图标风格规范.docx">Word 规范</a>')
    (OUT/'index.html').write_text(index,encoding='utf-8')
(OUT/'使用说明.txt').write_text('''实际功能图标重绘 / Optical Scientific Outline 1.2 / 2026-11-03

221 款语义图形覆盖 332 处入口和状态：196 款新绘、25 款先前确认稿。不同功能不再仅按旧图标 ID 合并，九种 MTF 都有自己的方法与自变量图示。
已同步程序中的实际 SVG 与命令图标绑定，保留命令身份、行为、文本、主题和数值计算。7 个方向按钮也使用新 SVG。

index.html：完整可搜索图库，切换主题、按功能或语义图形查看，点击查看设计含义及下载。
svg/主题名/：每主题 221 个独立 SVG，五主题共 1,105 份，透明背景普通状态。
masters/：221 个可编辑的源图。
overview/：五张完整 SVG 总图。
功能与图标对应表.csv：全部 332 处绑定及设计含义。
设计说明.html：规范和每项图形的含义，供后续设计扩展。

本套不混入未使用的备用 Lucide 图标。已确认专用稿按原图保留，不冒称这 25 款本轮新绘。
SVG 保持 32×32、1.65 线宽、圆端点与连接；不要用整体滤镜把三角色合成一种颜色。背景、文字和按钮状态由宿主控件提供。
''',encoding='utf-8-sig')
print(json.dumps(dict(drawings=221,newlyDrawn=196,preserved=25,uses=332,variants=1105,output=str(OUT)),ensure_ascii=False))
if theme_profiles:
    instructions=read(OUT/'使用说明.txt').replace('Outline 1.2','Outline 1.3')
    instructions+='\n主题独立造型：光域为钢铁侠式科技 HUD（钢色结构、冰蓝青光、切角与分段仪表）；异世界为剑与魔法（魔导书、卷轴、法阵、符文及古典炼金器具）。每套221款，theme-masters/保存442份主题母版；普通、黑洞和像素的原SVG保留。\n图标风格规范.docx及图标风格规范.html：与最早交付的规范同步为1.3，供后续批量改图使用。后续须同步五主题资源、功能表、规范正文、图库与SVG包、当前记录及长期偏好。\n'
    (OUT/'使用说明.txt').write_text(instructions,encoding='utf-8-sig')
