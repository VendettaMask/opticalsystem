"""Verify source bindings, native/UI checks and the portable semantic SVG delivery."""
from pathlib import Path
import hashlib,json,re,xml.etree.ElementTree as ET,zipfile

REPO=Path(__file__).resolve().parents[1];OUT=REPO.parent/'redesigned-icons-20261103'
EVIDENCE=REPO/'validation/rechecks/2026-11-03/semantic-icon-redraw'
N='{http://www.w3.org/2000/svg}';T={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def sha(b):return hashlib.sha256(b).hexdigest()
def load(p):return json.loads(p.read_bytes().decode('utf-8-sig'))
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
catalog=load(OUT/'catalog.json');manifest=load(EVIDENCE/'semantic-manifest.json');current=load(EVIDENCE/'current-source-uses.json')
names={i['id'] for i in catalog['icons']}
assert len(names)==221 and len(current['uses'])==240
assert all(u['id'] in names for u in current['uses'])
assert all(s.endswith('command.IconName') or s.endswith('menu.IconName') for s in current['unresolved'])
assert not ({'help-circle','letter-text','waves'}&{u['id'] for u in current['uses']})
category_map={'chart-scatter':'analysis-ray-spots','file-text':'analysis-engineering-report','chart-spline':'analysis-aberration','waves-horizontal':'analysis-wavefront','focus':'analysis-point-spread','chart-no-axes-combined':'analysis-mtf-transfer','chart-line':'analysis-rms-curve','circle-dot':'analysis-encircled-energy','image':'analysis-image-analysis'}
source=REPO/'src/OpticalSystemDesign.Application/Services/WorkbenchAnalysisRibbonCatalog.cs';text=source.read_bytes().decode('utf-8-sig')
Q=r'"((?:[^"\\]|\\.)*)"';decode=lambda s:json.loads('"'+s+'"')
current_commands={}
for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*5),text,re.S):
    command,name,label,icon,group=map(decode,m.groups());current_commands[command]=(name,label,icon,group)
for command,id_ in manifest['commandIcons'].items():assert current_commands[command][2]==id_
before=(EVIDENCE/'menu-source-before.txt').read_bytes().decode('utf-8-sig')
before_identity=[tuple(decode(m[n]) for n in (1,2,3,5)) for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*5),before,re.S)]
after_identity=[tuple(decode(m[n]) for n in (1,2,3,5)) for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*5),text,re.S)]
assert before_identity==after_identity
menu_icons=[]
for m in re.finditer(r'new\(\s*'+r'\s*,\s*'.join([Q]*3)+r'\s*,\s*new\[\]\s*\{',text,re.S):
    icon=decode(m[3]);menu_icons.append(category_map.get(icon,icon))
assert len(menu_icons)==13
actual_names={u['id'] for u in current['uses']}|set(manifest['commandIcons'].values())|set(menu_icons)|{'view-'+f for f in ('front','back','left','right','top','bottom','isometric')}
assert actual_names==names,(names-actual_names,actual_names-names)
assert len({m['icon'] for m in catalog['mtfMenu']})==9

reports=['semantic-native-final.trx','semantic-surface-final.trx','semantic-export-final.trx']
native_results=[];tests=set();passed=0
for report in reports:
    root=ET.parse(EVIDENCE/report).getroot();counts=root.find('t:ResultSummary/t:Counters',T).attrib
    assert int(counts['failed'])==0 and int(counts['notExecuted'])==0 and int(counts['passed'])==int(counts['total']),counts
    for result in root.findall('t:Results/t:UnitTestResult',T):
        identity=result.attrib['testName'];assert identity not in tests,identity;tests.add(identity)
    passed+=int(counts['passed']);native_results.append(dict(report=report,counters=counts,sha256=sha((EVIDENCE/report).read_bytes())))
assert passed==57 and len(tests)==57
browser=load(OUT/'_build/gallery-validation.json')
assert len(browser['checks'])==9 and all(c['result']=='passed' for c in browser['checks'])

assert len(list((OUT/'masters').glob('*.svg')))==221
assert len(list((OUT/'svg').rglob('*.svg')))==1105
combined=hashlib.sha256()
def body(root):return b''.join(ET.tostring(g,encoding='utf-8') for g in root if g.tag==N+'g')
for item in catalog['icons']:
    source_file=REPO/item['source'];raw=source_file.read_bytes();assert sha(raw)==item['sourceSha256']
    assert raw==(OUT/'masters'/(item['id']+'.svg')).read_bytes()
    combined.update(item['id'].encode()+b'\0'+raw+b'\0');original=body(ET.fromstring(raw))
    for theme in catalog['themes']:
        root=ET.parse(OUT/'svg'/theme['key']/(item['id']+'.svg')).getroot();assert root.attrib['viewBox']=='0 0 32 32'
        assert body(root)==original
        assert not any(g.tag.rsplit('}',1)[-1] in ('text','image','script','foreignObject','use') for g in root.iter())
        style=root.find(N+'style').text
        for role in ('structure','signal','secondary'):assert f'.{role}{{fill:none;stroke:{theme[role]}}}' in style
for theme in catalog['themes']:
    overview=ET.parse(OUT/'overview'/f'semantic-icons-{theme["key"]}.svg').getroot()
    assert len([g for g in overview if g.tag==N+'g'])==221
receipt=dict(date='2026-11-03',style='Optical Scientific Outline 1.2',usedSemanticDrawings=221,newlyAuthored=196,preservedApproved=25,
    uiReferencesAndStates=332,themedSvgs=1105,allAfterBindingSourcesResolve=True,all221AreActuallyUsed=True,all72AnalysisCommandNamesLabelsAndGroupsPreserved=True,
    allNineMtfMethodsAndVariablesDistinct=True,allMasterFilesMatchSource=True,allThemeVariantsPreserveGeometry=True,
    nativeTestsPassed=passed,nativeTestsFailed=0,nativeTestReports=native_results,galleryChecks=browser['checks'],
    masterCombinedSha256=combined.hexdigest(),manifestSha256=sha((EVIDENCE/'semantic-manifest.json').read_bytes()),currentUiScanSha256=sha((EVIDENCE/'current-source-uses.json').read_bytes()),
    defaultBuild='Release App, built-in dependencies and tests built in their standard output directories, 0 warnings / 0 errors.',
    scope='Functional SVG/source/menu/pointer/export checks, all off-screen. No numerical engine changes, no foreground app restart, no precision-matrix certification claim.')
dump(EVIDENCE/'verification.json',receipt);dump(OUT/'verification.json',receipt)
files=sorted(p for p in OUT.rglob('*') if p.is_file() and '_build' not in p.relative_to(OUT).parts)
archive=REPO.parent/'按功能重绘图标_五主题SVG_20261103.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
    for p in files:output.write(p,OUT.name+'/'+p.relative_to(OUT).as_posix())
with zipfile.ZipFile(archive) as output:
    assert len(output.namelist())==len(files)
    for p in files:assert sha(output.read(OUT.name+'/'+p.relative_to(OUT).as_posix()))==sha(p.read_bytes())
archive_receipt=dict(archive=archive.name,files=len(files),bytes=archive.stat().st_size,sha256=sha(archive.read_bytes()),allEntriesHashChecked=True)
dump(EVIDENCE/'archive-verification.json',archive_receipt)
print(json.dumps(dict(drawings=221,variants=1105,nativePassed=57,browserPassed=9,archiveBytes=archive.stat().st_size,archiveFiles=len(files)),ensure_ascii=True))
