"""Author and bind semantic SVG icons for actual UI uses. No optical calculations are touched."""
from pathlib import Path
from collections import defaultdict
from copy import deepcopy
import argparse, hashlib, html, json, re, shutil
import xml.etree.ElementTree as ET
from semantic_icon_designs import ART, reg, p, r, c, PAGE, TABLE, AX

REPO=Path(__file__).resolve().parents[1]
WORK=REPO.parent
EVIDENCE=REPO/'validation/rechecks/2026-11-03/semantic-icon-redraw'
EVIDENCE.mkdir(parents=True,exist_ok=True)
OUT=WORK/'redesigned-icons-20261103'
OUT.mkdir(exist_ok=True)
ASSETS=REPO/'src/OpticalSystemDesign.App/Assets/Icons/Scientific'
DESIGNED=ASSETS/'Designed'
N='{http://www.w3.org/2000/svg}'
ET.register_namespace('',N[1:-1])
def read(p):return p.read_bytes().decode('utf-8-sig')
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def sha(b):return hashlib.sha256(b).hexdigest()
def line_at(text,pos):return text[:pos].count('\n')+1
def u16len(text):return len(text.encode('utf-16-le'))//2

baseline=EVIDENCE/'source-inventory.json'
if not baseline.exists():shutil.copyfile(WORK/'used-icons-20261103/_build/source-uses.json',baseline)
inventory=json.loads(read(baseline))
assert all('literalSpan' in u for u in inventory['uses'])
uses=deepcopy(inventory['uses'])
old_catalog=json.loads(read(WORK/'used-icons-20261103/catalog.json'))
old_uses=old_catalog['uses']
approved={s.stem for s in (ASSETS/'Approved').glob('*.svg')}
source=REPO/'src/OpticalSystemDesign.Application/Services/WorkbenchAnalysisRibbonCatalog.cs'
menu_baseline=EVIDENCE/'menu-source-before.txt'
if not menu_baseline.exists():menu_baseline.write_bytes(source.read_bytes())
text=read(menu_baseline)
Q=r'"((?:[^"\\]|\\.)*)"'
dec=lambda s:json.loads('"'+s+'"')
category_map={'chart-scatter':'analysis-ray-spots','file-text':'analysis-engineering-report','chart-spline':'analysis-aberration','waves-horizontal':'analysis-wavefront','focus':'analysis-point-spread','chart-no-axes-combined':'analysis-mtf-transfer','chart-line':'analysis-rms-curve','circle-dot':'analysis-encircled-energy','image':'analysis-image-analysis'}
menus=[]
for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*3)+r'\s*,\s*new\[\]\s*\{([^}]+)\}',text,re.S):
    group,label,icon=map(dec,m.groups()[:3]);ids=[dec(s) for s in re.findall(Q,m[4]) if s!='-']
    menus.append(dict(group=group,label=label,id=icon,commands=ids,pos=m.start(3)-1,length=m.end(3)-m.start(3)+2))
active_ids={s for m in menus for s in m['commands']}
commands=[]
for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*5),text,re.S):
    command,name,label,icon,group=map(dec,m.groups())
    if command not in active_ids:continue
    commands.append(dict(commandId=command,name=name,label=label,id=icon,group=group,pos=m.start(4)-1,length=m.end(4)-m.start(4)+2))
    uses.append(dict(id=icon,label=label,group='分析 · '+group,file=source.relative_to(REPO).as_posix(),line=line_at(text,m.start()),method='RibbonCommands',reason='active analysis command',commandId=command,literalFile=source.relative_to(REPO).as_posix(),literalSpan=u16len(text[:m.start(4)-1]),literalLength=m.end(4)-m.start(4)+2))
assert len(commands)==72
for m in menus:
    uses.append(dict(id=category_map.get(m['id'],m['id']),label=m['label'],group='分析 · 分类入口',file=source.relative_to(REPO).as_posix(),line=line_at(text,m['pos']),method='RibbonMenus',reason='actual category entry',category=True,sourceIcon=m['id'],literalFile=source.relative_to(REPO).as_posix(),literalSpan=u16len(text[:m['pos']]),literalLength=m['length'],preserveBinding=m['id'] in category_map))

# Improve labels where previous static inventory could only see a dynamic text property.
for u in uses:
    match=next((x for x in old_uses if x['file']==u['file'] and x['line']==u['line'] and (x.get('requestedIcon') or x['id'])==u['id']),None)
    if not u.get('group'):u['group']=match['group'] if match else '页面 · '+Path(u['file']).name.split('.')[0]
    if not u['label']:u['label']=match['label'] if match else u['id']
    if 'OpticalAssemblyWindow.Presentation' in u['file'] and u['method']=='BuildPresentation':
        u['label']={'play':'计算共轭','refresh-cw':'更新图像','square':'取消'}.get(u['id'],u['label'])
    if u['id']=='file-input' and '/Shell/' in u['file']:u['label']='Zemax ZMX'
    if u['id']=='box' and u['method']=='BuildFileRibbonPage':u['label']='CAD（STEP）'
    if 'CoatingDesign/MainWindow.EditorChrome' in u['file'] and u['id'] in ('lock','lock-open'):
        u['label']='全部锁定' if u['id']=='lock' else '全部解锁'
    if 'CoatingDesign/MainWindow.Layers' in u['file'] and u['id'] in ('lock','lock-open'):
        u['label']='膜层已锁定' if u['id']=='lock' else '膜层可编辑'

COMMANDS={
'analysis-single-ray-trace':'ray-single-trace','analysis-surface-data-report':'report-surface-data','analysis-system-data-report':'report-system-data','analysis-classified-data-report':'report-classified-data','analysis-first-order':'report-system-summary',
'analysis-ray-fan':'aberration-ray-fan','analysis-spot':'spot-standard','analysis-footprint':'ray-footprint','analysis-through-focus':'spot-through-focus','analysis-full-field-spot':'spot-full-field','analysis-matrix-spot':'spot-field-matrix','analysis-configuration-matrix-spot':'spot-configuration-matrix','analysis-cardinal-points':'report-cardinal-points','analysis-vignetting':'vignetting',
'analysis-grid-distortion':'aberration-grid-distortion','analysis-field-curvature':'aberration-field-curvature','analysis-axial-aberration':'aberration-axial','analysis-lateral-color':'aberration-lateral-color','analysis-color-focus':'aberration-color-focus','analysis-seidel':'seidel-coefficients','analysis-seidel-diagram':'seidel-diagram',
'analysis-diffraction-encircled-energy':'encircled-energy-huygens','analysis-encircled-energy':'encircled-energy-geometric','analysis-geometric-line-edge-spread':'spread-geometric','analysis-extended-source-encircled-energy':'encircled-energy-extended','analysis-pupil-aberration':'aberration-pupil','analysis-full-field-aberration':'aberration-full-field',
'analysis-rms-field':'rms-field','analysis-rms-wavelength':'rms-wavelength','analysis-rms-focus':'rms-focus','analysis-rms-field-map':'rms-field-map','analysis-angle-height':'incidence-vs-height',
'analysis-psf':'psf-fourier','analysis-psf-cross-section':'psf-fourier-section','analysis-line-edge-spread':'spread-fourier','analysis-huygens-psf':'psf-huygens','analysis-huygens-psf-cross-section':'psf-huygens-section',
'analysis-mtf':'mtf-fourier-frequency','analysis-fourier-through-focus-mtf':'mtf-fourier-focus','analysis-fourier-mtf-field':'mtf-fourier-field','analysis-huygens-mtf':'mtf-huygens-frequency','analysis-huygens-through-focus-mtf':'mtf-huygens-focus','analysis-huygens-mtf-field':'mtf-huygens-field','analysis-geometric-mtf':'mtf-geometric-frequency','analysis-geometric-through-focus-mtf':'mtf-geometric-focus','analysis-geometric-mtf-field':'mtf-geometric-field',
'analysis-wavefront':'wavefront-opd','analysis-wavefront-map':'wavefront-map','analysis-interferogram':'wavefront-interferogram','analysis-foucault':'wavefront-foucault','analysis-contrast-loss':'contrast-loss','analysis-zernike-fringe':'zernike-fringe','analysis-zernike-standard':'zernike-standard','analysis-zernike-annular':'zernike-annular','analysis-zernike-field':'zernike-vs-field',
'analysis-image-simulation':'image-simulation','analysis-geometric-image':'image-geometric','analysis-geometric-bitmap-image':'image-geometric-bitmap','analysis-light-source':'source-analysis','analysis-partially-coherent-image':'image-partial-coherence','analysis-extended-diffraction-image':'image-extended-diffraction','analysis-relative-illumination':'relative-illumination','viewer-ima-bim':'viewer-ima-bim','viewer-bitmap':'viewer-bitmap','analysis-y-ybar':'y-ybar',
'non-sequential-trace-control':'trace-control','analysis-non-sequential-ray-trace':'ray-single-trace','analysis-non-sequential-detector-viewer':'detector-view','non-sequential-clear-detectors':'detector-clear','non-sequential-ray-database-viewer':'ray-database','non-sequential-path-analysis':'ray-path-analysis','non-sequential-3d-layout':'layout-3d'}
assert {c['commandId'] for c in commands}==set(COMMANDS)

BASE={
'activity':'wavelength-settings','aperture':'system-aperture','arrow-down':'action-move-down','arrow-right-left':'material-transmission','arrow-up':'action-move-up','arrow-up-down':'coating-layer-reverse','book-open-text':'user-guide','box':'layout-3d','boxes':'nonsequential-objects','calculator':'material-nd-vd','camera':'file-export-image',
'chart-line':'optimization-visual','chart-no-axes-combined':'optimization-visual','chart-scatter':'material-dispersion-chart','check':'action-accept','chevron-down':'disclosure-expand','chevron-left':'action-previous','chevron-right':'disclosure-closed','chevron-up':'disclosure-collapse','circle-alert':'analysis-warning','circle-dot':'radius-variables','circle-question-mark':'help-topic','help-circle':'help-topic','clipboard-check':'manufacturability-review','clipboard-copy':'clipboard-copy-text','copy':'action-copy','crosshair':'alignment-reticle','cylinder':'solid-model','database':'materials-library','disc-2':'polarization-settings','download':'file-export','drafting-compass':'optical-drawing','ellipsis':'action-more','external-link':'action-open-link','file-down':'file-export-report','file-input':'file-import','file-plus':'file-new','file-text':'catalog-data-sheet','focus':'field-settings','folder-open':'file-open','gem':'material-glass-map','grid-2x2':'layout-tile','history':'recover-unsaved-project','image-up':'file-import-image','info':'information-note','languages':'settings-language','layers':'coating-design','list-filter':'design-candidates','list-tree':'merit-function-editor','lock':'coating-layer-lock','lock-open':'coating-layer-unlock','maximize':'action-fit-view','maximize-2':'action-fit-view','move-vertical':'thickness-variables','package-search':'stock-lens-catalog','panel-top':'layout-2d','panels-top-left':'configuration-matrix','pencil':'edit-target','picture-in-picture-2':'layout-float','pin':'action-pin','play':'action-run','plus':'action-add','redo':'action-redo','refresh-cw':'action-recalculate','replace':'glass-replacement','rotate-ccw':'action-reset-view','route':'nonsequential-mode','rows-3':'sequential-mode','save':'file-save','scan-eye':'optical-alignment','scan-line':'layout-2d','scan-search':'optimization-adjust','search':'action-search','settings':'settings-general','settings-2':'optimization-wizard','shuffle':'optimization-random-search','sliders-horizontal':'settings-general','sparkles':'optimization-wizard','square':'action-stop','table-2':'lens-prescription','target':'image-quality-acceptance','telescope':'lens-library','thermometer-sun':'material-athermal-map','timer':'run-budget','trash':'action-delete','trash-2':'action-delete','type':'format-font','undo':'action-undo','upload':'file-export','wand-sparkles':'optimization-wizard','x':'action-remove','zoom-in':'action-zoom-in','zoom-out':'action-zoom-out'}

LABELS={
'用户指南':'user-guide','操作数帮助':'operand-help','关于':'about','波长与权重':'wavelength-weights','光学系统':'optical-system','结构规模':'structure-size','像质与验收':'image-quality-acceptance','搜索策略':'search-strategy','运行预算':'run-budget','表面共轭位置':'alignment-conjugates','暂无共轭结果':'alignment-conjugates','测量头参数':'alignment-measurement-head','叉丝与探测面':'alignment-reticle','返回叉丝像':'alignment-reticle','尚无叉丝图像':'alignment-reticle','堆栈几何':'coating-stack','实验材料管理':'coating-materials','候选':'design-candidates','候选方案':'design-candidates','等待生成候选':'design-candidates','指标与公差':'coating-tolerance-metrics','编辑设计目标':'edit-target','目标':'coating-target','光学镀膜设计':'coating-design','AI 初始结构':'initial-structure-design','相机成像仿真':'camera-imaging','光学装调':'optical-alignment','光学制图':'optical-drawing','材料分析':'material-dispersion-chart','色散图':'material-dispersion-chart','玻璃图':'material-glass-map','无热化玻璃图':'material-athermal-map','内部透过率 vs. 波长':'material-transmission','色散 vs. 波长':'material-dispersion-wavelength','快速聚焦':'optimization-focus','快速调整':'optimization-adjust','可视化优化器':'optimization-visual','评价函数编辑器':'merit-function-editor','优化向导':'optimization-wizard','生成评价函数':'merit-function-wizard','公差向导':'tolerance-wizard','公差分析向导':'tolerance-wizard','移除所有变量':'optimization-variables-clear','设全部半径变量':'radius-variables','设全部厚度变量':'thickness-variables','随机扰动搜索':'optimization-random-search','玻璃替换模板':'glass-replacement','系统选项':'system-settings','系统孔径':'system-aperture','视场':'field-settings','波长':'wavelength-settings','偏振':'polarization-settings','环境':'environment-settings','多重结构':'configuration-matrix','添加视场':'field-add','添加波长':'wavelength-add','删除此视场':'field-delete','删除此波长':'wavelength-delete','加入当前':'catalog-add-active','移出当前':'catalog-remove-active','复制玻璃':'material-copy-glass','复制玻璃报告':'material-glass-report','计算 Nd/Vd':'material-nd-vd','数据表':'catalog-data-sheet','保留分栏停靠':'layout-dock','合并单窗格':'layout-merge','全部独立浮动':'layout-float','平铺全部':'layout-tile','层叠全部':'layout-cascade','库存镜头查看':'stock-lens-catalog','库存镜头匹配':'stock-lens-match','开始匹配':'stock-lens-match','镜头库':'lens-library','导入':'file-import','导出文本':'file-export-text','导出报告':'file-export-report','导出 PDF':'file-export-report','导出设计报告…':'file-export-report','复制文本':'clipboard-copy-text'}

# A small number of labels need their enclosing object to disambiguate the meaning.
reg('file-export-cad','CAD（STEP）','机械边界和立体模型旁的向外箭头表示导出 STEP 实体。',p('m4 11 9-5 9 5-9 5ZM4 11v13l9 5 9-5V11M13 16v13','structure')+p('M20 10 28 2M21 2h7v7')+p('M7 21h3','secondary'),category='文件与工作区')
reg('alignment-compute','计算共轭','由物面经过透镜得到像面的光路及求解箭头表示计算共轭。',p('M4 6v20M16 4v24M28 6v20','structure')+p('M4 8 16 12 28 24M4 24l12-5L28 8')+p('M9 29h14m-3-3 3 3-3 3','secondary'),category='实验室')
def choose(u):
    id_=u['id'];label=u['label'];file=u['file']
    if u.get('commandId'):return COMMANDS[u['commandId']]
    if id_ in approved:return id_
    if u.get('category'):return {'route':'trace-control','scan-eye':'detector-view','database':'ray-database','box':'layout-3d'}[id_]
    if label in LABELS:return LABELS[label]
    if label=='CAD（STEP）':return 'file-export-cad'
    if '/Examples' in file or file.endswith('MainWindow.Examples.cs'):return 'optical-examples'
    if '/InitialStructure/' in file:
        if id_=='layers':return 'design-candidates' if '候选' in label else 'structure-size'
        if id_=='sparkles':return 'initial-structure-design'
        if id_=='aperture':return 'optical-system'
    if 'OpticalAssemblyWindow.Presentation' in file and u['method']=='BuildPresentation' and id_ in ('play','refresh-cw','square'):return {'play':'alignment-compute','refresh-cw':'alignment-reticle','square':'action-stop'}[id_]
    if '/CoatingDesign/' in file:
        if label=='结果':return 'coating-spectrum'
        if label=='膜层':return 'coating-layer-add'
        if label=='示例':return 'optical-examples'
        if id_=='layers':return 'coating-stack'
    if label in ('导出图片（PNG）','保存光谱 PNG','导出 PNG'):return 'file-export-image'
    if label in ('上一个结构','上一个操作数','上一面'):return 'action-previous'
    if label in ('下一个结构','下一个操作数','下一面'):return 'action-next'
    if label=='优先级上移':return 'action-move-up'
    if label=='优先级下移':return 'action-move-down'
    if id_=='chart-line':raise ValueError(('Unresolved generic plot semantic',u))
    return BASE[id_]

for u in uses:u['designedId']=choose(u)
for face,label in [('front','前视图'),('back','后视图'),('left','左视图'),('right','右视图'),('top','俯视图'),('bottom','仰视图'),('isometric','等轴测视图')]:
    uses.append(dict(id='viewcube-'+face,designedId='view-'+face,label=label,group='视图 · 3D/实体模型',file='src/OpticalSystemDesign.App/Panels/ViewerPanel.cs',line=0,method='BuildViewPresetToolbar',reason='new SVG replaces native cube',native=True))
used_names={u['designedId'] for u in uses}
assert all(name in ART or name in approved for name in used_names),sorted(name for name in used_names if name not in ART and name not in approved)

# Do not silently merge different meanings that happened to originate at one literal.
bindings={}
for u in uses:
    if u.get('native') or u.get('preserveBinding'):continue
    key=(u['literalFile'],u['literalSpan'],u['literalLength'])
    if key in bindings:assert bindings[key]['new']==u['designedId'],(bindings[key],u)
    else:bindings[key]=dict(file=key[0],span=key[1],length=key[2],old=u.get('sourceIcon',u['id']),new=u['designedId'],label=u['label'])

def svg(id_,art):
    css='.structure{fill:none;stroke:#8298A8}.signal{fill:none;stroke:#398FF0}.secondary{fill:none;stroke:#28ADAF}.signal-fill{fill:#398FF0;stroke:none}.secondary-fill{fill:#28ADAF;stroke:none}.glyph{stroke-width:1.65;stroke-linecap:round;stroke-linejoin:round}.dark .structure{stroke:#A5978B}.dark .signal{stroke:#F0AE79}.dark .secondary{stroke:#D5B89C}.dark .signal-fill{fill:#F0AE79}.dark .secondary-fill{fill:#D5B89C}'
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32" role="img" aria-label="{html.escape(art["label"],quote=True)}"><title>{html.escape(art["label"])}</title><style>{css}</style><g class="glyph">{art["body"]}</g></svg>\n'
DESIGNED.mkdir(exist_ok=True)
for name in sorted(used_names-approved):
    content=svg(name,ART[name]);root=ET.fromstring(content)
    assert root.attrib['viewBox']=='0 0 32 32'
    assert not any(e.tag.rsplit('}',1)[-1] in ('text','image','script','use') or 'transform' in e.attrib for e in root.iter())
    (DESIGNED/(name+'.svg')).write_text(content,encoding='utf-8',newline='\n')

manifest=dict(style='Optical Scientific Outline',version='1.2',grid=[32,32],strokeWidth=1.65,
    sourceUiReferences=len(uses),uniqueSemanticDrawings=len(used_names),preservedApproved=sorted(used_names&approved),
    newlyAuthored=sorted(used_names-approved),usedDrawings=[dict(id=name,label=ART[name]['label'],meaning=ART[name]['meaning'],category=ART[name]['category']) if name in ART else dict(id=name,label=ET.parse(ASSETS/'Approved'/(name+'.svg')).getroot().find(N+'title').text,meaning='保留用户先前确认的专用矢量稿。',category='确认稿') for name in sorted(used_names)],
    uses=uses,bindings=list(bindings.values()),commandIcons=COMMANDS,
    scope='Actual functionality only. Authored paths for new semantic drawings; exact approved artwork retained. Unused Lucide catalog remains an attributed reserve.')
dump(EVIDENCE/'semantic-manifest.json',manifest)
dump(ASSETS/'Designed/manifest.json',{k:manifest[k] for k in ('style','version','grid','strokeWidth','sourceUiReferences','uniqueSemanticDrawings','preservedApproved','newlyAuthored','usedDrawings','commandIcons')})

parser=argparse.ArgumentParser();parser.add_argument('--apply',action='store_true');options=parser.parse_args()
if options.apply:
    receipts=[];pending=[]
    for filename,edits in __import__('itertools').groupby(sorted(bindings.values(),key=lambda b:(b['file'],b['span'])),key=lambda b:b['file']):
        path=REPO/filename;raw=path.read_bytes();bom=raw.startswith(b'\xef\xbb\xbf');utf16=raw.decode('utf-8-sig').encode('utf-16-le')
        for edit in sorted(edits,key=lambda b:b['span'],reverse=True):
            start=edit['span']*2;end=start+edit['length']*2;old=json.dumps(edit['old']).encode('utf-16-le');new=json.dumps(edit['new']).encode('utf-16-le')
            assert utf16[start:end]==old,(filename,edit,utf16[start:end].decode('utf-16-le'))
            utf16=utf16[:start]+new+utf16[end:]
        result=(b'\xef\xbb\xbf' if bom else b'')+utf16.decode('utf-16-le').encode('utf-8')
        pending.append((path,result));receipts.append(dict(file=filename,beforeSha256=sha(raw),afterSha256=sha(result)))
    for path,result in pending:path.write_bytes(result)
    dump(EVIDENCE/'applied-bindings.json',receipts)
print(json.dumps(dict(uses=len(uses),semanticDrawings=len(used_names),newlyAuthored=len(used_names-approved),preserved=len(used_names&approved),bindingLiterals=len(bindings),applied=options.apply),ensure_ascii=False))
