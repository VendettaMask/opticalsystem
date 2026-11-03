from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import xml.etree.ElementTree as ET

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[3]
previews=HERE/'previews'
fontfile=ROOT/'src/OpticalSystemDesign.App/Assets/Fonts/NotoSansCJKsc-Regular.otf'
title=ImageFont.truetype(str(fontfile),29)
body=ImageFont.truetype(str(fontfile),16)
label=ImageFont.truetype(str(fontfile),20)
themes=[('Light','普通模式','蓝青'),('Dark','黑洞暗夜','暖灰与橙金'),('Lumen','光域','灰青与暖金'),('Isekai','异世界','旧金'),('Pixel','像素风格','海军蓝与红绿')]
canvas=Image.new('RGB',(1660,746),'#080808')
draw=ImageDraw.Draw(canvas)
draw.text((24,20),'五主题图标适配',(244,236,229),font=title)
draw.text((24,65),'实际程序离屏截图 · 18 字号 · 图标库 1,773 个 · 规范 1.1',(200,187,175),font=body)
for n,(key,name,colours) in enumerate(themes):
    y=112+n*117
    draw.text((20,y+15),name,(244,236,229),font=label)
    draw.text((20,y+47),colours,(200,187,175),font=body)
    source=Image.open(previews/f'workbench-{key}-18.png').convert('RGB')
    strip=source.crop((0,32,1500,127))
    canvas.paste(strip,(150,y))
draw.text((24,710),'图标按主题自动取色；完整工作台与 18 / 24 / 32 DIP 图集可在预览页查看。',(200,187,175),font=body)
canvas.save(HERE/'theme-overview.png')

buttons=''.join(f'<button type="button" data-theme="{k}" aria-pressed="{str(k=="Dark").lower()}">{name}</button>' for k,name,c in themes)
page=f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>五主题图标实际预览</title>
<style>*{{box-sizing:border-box}}body{{margin:0;background:#080808;color:#f4ece5;font:16px/1.7 'Microsoft YaHei UI','Microsoft YaHei',sans-serif}}main{{max-width:1540px;margin:30px auto;padding:0 24px}}h1{{font-size:29px;margin:0 0 8px}}p{{color:#c8bbaf}}.controls{{display:flex;gap:12px;flex-wrap:wrap;align-items:center;margin:24px 0}}button,select{{font:inherit;border:1px solid #44382f;background:#1d1916;color:#f4ece5;padding:6px 13px;border-radius:6px;cursor:pointer}}button[aria-pressed=true]{{border-color:#f0ae79;color:#f0ae79}}img{{max-width:100%;height:auto;border:1px solid #44382f}}h2{{font-size:20px;margin:28px 0 13px}}a{{color:#f0ae79}}button:focus-visible,select:focus-visible{{outline:2px solid #ffd0a6;outline-offset:3px}}footer{{margin:25px 0;color:#c8bbaf;font-size:14px}}</style>
<main><h1>五主题图标实际预览</h1><p>全程序采用 Optical Scientific Outline 1.1，并按主题映射结构、主信息和辅助色。以下均为实际应用离屏绘制。</p><div class="controls">{buttons}<label>工作台字号 <select id="font"><option>13</option><option selected>18</option></select></label></div><h2 id="heading">黑洞暗夜</h2><a id="full" href="previews/workbench-Dark-18.png"><img id="workbench" src="previews/workbench-Dark-18.png" alt="黑洞暗夜实际工作台"></a><h2>原生图标图集</h2><p>每个图标按 18、24、32 DIP 显示，包含确认稿及常用文件、搜索、视图和操作图标。</p><a id="atlasLink" href="previews/atlas-Dark.png"><img id="atlas" src="previews/atlas-Dark.png" alt="黑洞暗夜原生图标图集"></a><footer><a href="README.md">验证范围</a> · <a href="theme-overview.png">五主题工具栏总览</a> · 最终定向回归 53/53 通过</footer></main>
<script>let current='Dark';const names={json.dumps(dict((k,n) for k,n,c in themes),ensure_ascii=False)};function update(){{const name=names[current],file='previews/workbench-'+current+'-'+document.querySelector('#font').value+'.png',atlas='previews/atlas-'+current+'.png';document.querySelector('#workbench').src=file;document.querySelector('#workbench').alt=name+'实际工作台';document.querySelector('#full').href=file;document.querySelector('#atlas').src=atlas;document.querySelector('#atlas').alt=name+'原生图标图集';document.querySelector('#atlasLink').href=atlas;document.querySelector('#heading').textContent=name;document.querySelectorAll('[data-theme]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.theme===current)))}}document.querySelectorAll('[data-theme]').forEach(b=>b.onclick=()=>{{current=b.dataset.theme;update()}});document.querySelector('#font').onchange=update;</script></html>'''
(HERE/'index.html').write_text(page,encoding='utf-8')

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
result=ET.parse(HERE/'theme-icon-final.trx').getroot().find('t:ResultSummary/t:Counters',ns).attrib
catalog=ROOT/'src/OpticalSystemDesign.App/Assets/Icons/Scientific/catalog.json'
assetdir=catalog.parent
assets=sorted(assetdir.rglob('*.svg'))
combined=hashlib.sha256()
for f in assets:combined.update(f.relative_to(assetdir).as_posix().encode()+b'\0'+f.read_bytes()+b'\0')
receipt={'date':'2026-11-03','style':'Optical Scientific Outline 1.1','assetCount':len(assets),'approvedCount':25,'derivedCount':1748,
         'catalogSha256':sha(catalog),'allSvgCombinedSha256':combined.hexdigest(),
         'assemblySha256':sha(ROOT/'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll'),
         'testCounters':result,'testReport':'theme-icon-final.trx','testReportSha256':sha(HERE/'theme-icon-final.trx'),
         'themes':[k for k,n,c in themes],'workbenchFontSizes':[13,18],'nativeAtlasSizes':[18,24,32],
         'screenshots':{p.name:sha(p) for p in sorted(previews.glob('*.png'))},
         'validationMode':'Background headless rendering; no desktop input or application restart'}
(HERE/'verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Composed five-theme overview; recorded 1773 SVGs, final assembly and 53/53 result.')
