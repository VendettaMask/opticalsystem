import json,re,hashlib,sys
from pathlib import Path
from PIL import Image,ImageOps,ImageDraw,ImageFont
from gallery_corrections import apply_corrections

out=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else Path(__file__).resolve().parent.parent
data=json.loads((out/'inputs.json').read_text(encoding='utf-8'))
invalid=[]
catalog=json.loads((out/data['lenses'][0]['name']/'workbench/catalog.json').read_text(encoding='utf-8'))
mapped={x['key']:x for x in data['analyses']}
report_keys={'Single Ray Trace','First Order','Cardinal Points Data','Seidel Coefficients','Zernike Fringe','Zernike Standard','Zernike Annular','Prescription Report','System Data Report','Classified Data Report'}
keys=[{'Key':'2D Layout','Name':'2D 结构图'},{'Key':'3D Layout','Name':'3D 结构图'},{'Key':'Solid Model','Name':'实体模型'}]+[x for x in catalog if x['Key'] not in report_keys]
analyses=[]
for x in keys:
    k=x['Key']; row=mapped.get(k,{})
    a={'key':k,'name':x['Name'],'slug':re.sub('[^a-z0-9]','-',k.lower()).strip('-')}
    if k in {'Angle vs Image Height - Through Pupil','Angle vs Image Height - Through Field'}:
        a['note']='右侧是最近似的原生 Angle vs Image Height 窗口；两边的扫描与曲线定义尚未统一，不能按完全等价的图判断。'
    elif k=='RMS Wavefront vs Field':
        a['note']='右侧共用原生 RMS Field 窗口。两边的默认参照、采样与曲线选择尚未逐项统一。'
    elif k=='Field Curvature':
        a['note']='右侧共用原生场曲与畸变窗口，包含额外畸变面板；左侧为单独场曲图。'
    elif k in {'Image Simulation','Geometric Image Analysis','Geometric Bitmap Image Analysis','Partially Coherent Image Analysis','Extended Diffraction Image Analysis'}:
        a['note']='这类图还依赖物方图像、图像尺寸和传播设置；同一光学文件未统一这些输入。超时截图及缺图如实保留，不能视为成像结果对照。'
    elif k in {'2D Layout','3D Layout','Solid Model'}:
        a['note']='镜片结构按同一文件绘制。两边的相机角度、光线颜色、光线密度、材料填充与图面占比不同，三维图未统一相机。'
    elif k in {'Spot Diagram','Full Field Spot Diagram','Matrix Spot Diagram','Configuration Matrix Spot Diagram','Through Focus'}:
        a['note']='视场排布、光点符号和图面留白不同。可直接查看各视场的光斑轮廓；采样、标尺与参照尚未统一，不据默认图形差别判定计算错误。'
    elif k in {'PSF','Huygens PSF','Wavefront'}:
        a['note']='当前软件默认使用二维热图，右侧原生窗口默认使用三维曲面。两边保留各自完整图面，未做视角、归一化或范围配准。'
    elif k in {'FFT MTF Map','Geometric MTF Map'}:
        a['note']='当前默认图的色阶分布与右侧原生默认图明显不同；默认视场范围、频率和色表未统一，需先对齐显示设置再归因。'
    elif k=='Geometric Line Edge Spread':
        a['note']='当前软件把线扩散与边缘扩散叠加在一个坐标区；原生窗口使用两个面板，曲线密度与默认横轴范围也不同。'
    elif k=='Extended Source Encircled Energy':
        a['note']='本次默认输出中，软件曲线呈阶梯状，原生曲线较平滑；扩展源及采样设置未统一，这里只记录图形差别。'
    elif k=='Jones Pupil':
        a['note']='软件展示 Jones 系数分量的多幅热图；原生默认窗口展示偏振矢量图。这两种默认呈现形式不同。'
    elif k=='Pupil Aberration':
        a['note']='两侧纵轴范围与自动缩放策略不同，软件侧曲线在图面上显得更平。不能把视觉斜率直接当作物理量差异。'
    analyses.append(a)
for lens in data['lenses']:
    folder=out/lens['name']; manifest=folder/'workbench/manifest.json'
    runs=json.loads(manifest.read_text(encoding='utf-8'))['Results'] if manifest.exists() else []
    indexed={r['Key']:r for r in runs}
    native={}
    for p in (folder/lens.get('nativeDirectory','zemax')).glob('*'):
        if p.suffix.lower() not in {'.jpg','.jpeg'}:continue
        try:
            with Image.open(p) as im:im.verify()
            native[p.stem.lower()]=p
        except Exception as e:invalid.append({'path':str(p.relative_to(out)),'reason':str(e)})
    lens['results']={}
    for a in analyses:
        k=a['key'];r=indexed.get(k,{});png=folder/'workbench'/(a['slug']+'.png');jpg=native.get(a['slug'])
        reason='当前比较目录没有对应的原生窗口映射。' if not mapped.get(k,{}).get('code') else '本次未取得该原生窗口图片。'
        if k in {'Single Ray Trace','First Order','Cardinal Points Data','Seidel Coefficients','Zernike Fringe','Zernike Standard','Zernike Annular','Prescription Report','System Data Report'}:reason='这是数值／文本报告，本轮仅收集图形，不导出原生数值表。'
        if k in {'Image Simulation','Geometric Image Analysis','Geometric Bitmap Image Analysis','Light Source Analysis','Partially Coherent Image Analysis','Extended Diffraction Image Analysis'}:reason='该原生图像分析还需要单独的物方图像与传播设置；本次同光学文件的图册未统一这些输入，未导出该窗口。'
        lens['results'][k]={'current':str(png.relative_to(out)).replace('\\','/') if png.exists() else None,'native':str(jpg.relative_to(out)).replace('\\','/') if jpg else None,'nativeReason':reason,'status':r.get('Status','missing'),'outcome':r.get('Outcome'),'reason':r.get('Reason') or r.get('Error')}
        if lens['name']=='cooke' and k=='Diffraction Encircled Energy':
            lens['results'][k]['note']='本次 Cooke 软件页面返回采样／有效域诊断表，没有曲线；右侧原生窗口有曲线。该软件截图保留为诊断界面。'
        if lens['name']=='cooke' and k=='Foucault Analysis':
            lens['results'][k]['note']='本次 Cooke 两幅图的上下明暗区域相反；刀口方向、灰度映射及传播设置尚未逐项对齐，这里仅记录可见差别。'
        if lens['name']=='double-gauss' and k=='Diffraction Encircled Energy':
            lens['results'][k]['note']=r.get('Note','软件侧已恢复能量曲线；实际采样和收敛检查见图下摘要。原生侧保留低采样警告，不作为已收敛的数值参考。') if r.get('Outcome')=='Success' else '本次 Double Gauss 软件页面为诊断表，没有曲线；原生图带有“取样太低，数据不准确”的提示。两侧诊断均保留。'
        if lens['name']=='petzval' and k=='Diffraction Encircled Energy':
            lens['results'][k]['note']='本次 Petzval 软件页面为诊断表，没有曲线；原生窗口也只有“取样太低，数据不准确”的提示，没有结果曲线。'
        if lens['name']=='yolo' and k=='2D Layout':
            lens['results'][k]['note']='本次原生二维布局窗口提示“无法在无轴系统中使用二维布局图”，图册保留原始提示。软件侧为二维投影；空间光路可在三维布局页查看。'
        if lens['name']=='grating' and k=='2D Layout':
            lens['results'][k]['note']='该倾斜光栅的原生二维布局窗口提示不适用于无轴系统；图册保留原始提示，空间光路可在三维布局页查看。'
        if lens['name']=='zoom-lens' and k in {'2D Layout','3D Layout','Solid Model'}:
            lens['results'][k]['note']='本页使用第1个配置；原生侧在每个窗口前重新加载第1配置的独立快照。配置矩阵点列图展示全部配置；两侧相机、光线颜色与图面缩放未统一。'
    lens['currentCount']=sum(bool(r['current']) for r in lens['results'].values());lens['nativeCount']=sum(bool(r['native']) for r in lens['results'].values())
    lens['captureComplete']=len(indexed)>=len(analyses)
    if not lens['captureComplete']:
        for result in lens['results'].values():
            if result['status']=='missing':result['status']='pending'
    lens['sourceUnchanged']=hashlib.sha256(Path(lens['source']).read_bytes()).hexdigest()==lens['sha256']
data['analyses']=analyses;data['currentVersion']='1.0.4'
apply_corrections(data,out)
(out/'gallery-data.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'invalid-images.json').write_text(json.dumps(invalid,ensure_ascii=False,indent=2),encoding='utf-8')
template=(Path(__file__).parent/'gallery-template.html').read_text(encoding='utf-8')
(out/'index.html').write_text(template.replace('/*DATA*/',json.dumps(data,ensure_ascii=False)),encoding='utf-8')
if '--no-contact-sheets' in sys.argv:
    print(json.dumps({'lenses':[{'name':l['name'],'current':l['currentCount'],'native':l['nativeCount'],'sourceUnchanged':l['sourceUnchanged']} for l in data['lenses']],'outputsPerLens':len(analyses)},ensure_ascii=False))
    sys.exit(0)
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
contacts=out/'contact-sheets';contacts.mkdir(exist_ok=True)
plotkeys=[a for a in analyses if a['key'] not in {'Single Ray Trace','First Order','Cardinal Points Data','Seidel Coefficients','Zernike Fringe','Zernike Standard','Zernike Annular','Prescription Report','System Data Report','Classified Data Report'}]
for lens in data['lenses']:
    for start in range(0,len(plotkeys),8):
        batch=plotkeys[start:start+8]
        target=contacts/f"{lens['name']}-{start//8+1:02d}.jpg"
        newest=max(((out/lens['results'][a['key']][key]).stat().st_mtime for a in batch for key in ['current','native'] if lens['results'][a['key']][key]),default=0)
        if target.exists() and target.stat().st_mtime>=newest:
            try:
                with Image.open(target) as existing:existing.load()
                continue
            except Exception:pass
        sheet=Image.new('RGB',(1200,440*len(batch)), '#e5e9ee');draw=ImageDraw.Draw(sheet)
        for i,a in enumerate(batch):
            y=i*440;draw.text((14,y+7),lens['name']+' / '+a['key'],font=font,fill='#172333')
            r=lens['results'][a['key']]
            for col,key,title in [(0,'current','Current software'),(1,'native','Zemax native')]:
                x=col*600;draw.text((x+14,y+32),title+(' - '+r['status'] if key=='current' and r['status']!='captured' else ''),font=font,fill='#4e627c')
                if r[key]:
                    im=Image.open(out/r[key]).convert('RGBA');white=Image.new('RGBA',im.size,'white');white.alpha_composite(im);im=ImageOps.contain(white.convert('RGB'),(592,374));sheet.paste(im,(x+(600-im.width)//2,y+58+(374-im.height)//2))
                else:draw.text((x+30,y+190),'No corresponding image',font=font,fill='#748091')
        sheet.save(target,quality=86)
print(json.dumps({'lenses':[{'name':l['name'],'current':l['currentCount'],'native':l['nativeCount'],'sourceUnchanged':l['sourceUnchanged']} for l in data['lenses']],'outputsPerLens':len(analyses)},ensure_ascii=False))
