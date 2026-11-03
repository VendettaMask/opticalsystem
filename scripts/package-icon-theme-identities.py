"""Verify and package the two approved icon profiles without replacing the frozen 1.2 ZIP."""
from pathlib import Path
import hashlib,json,xml.etree.ElementTree as ET,zipfile
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT.parent/'redesigned-icons-20261103'
E=ROOT/'validation/rechecks/2026-11-03/icon-theme-identity';N='{http://www.w3.org/2000/svg}'
def readj(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest()
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
catalog=readj(OUT/'catalog.json');assert catalog['version']=='1.3'
baseline=readj(E/'unchanged-theme-baseline.json');after=readj(E/'unchanged-theme-final.json');assert baseline==after and len(after)==1989
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
report=E/'icon-identity-final.trx';counters=ET.parse(report).getroot().find('t:ResultSummary/t:Counters',ns).attrib
assert counters['total']=='35' and counters['passed']=='35' and counters['failed']=='0' and counters['notExecuted']=='0'
web=readj(E/'gallery-verification.json');assert len(web['checks'])==7 and all(c['result']=='passed' for c in web['checks'])
assert len(list((OUT/'svg').rglob('*.svg')))==1105
assert len(list((OUT/'theme-masters').rglob('*.svg')))==442
for item in catalog['icons']:
    assert sha((ROOT/item['source']).read_bytes())==item['sourceSha256']
    for theme in ('Lumen','Isekai'):
        info=item['themeSources'][theme];raw=(ROOT/info['source']).read_bytes()
        assert sha(raw)==info['sha256']
        assert raw==(OUT/'svg'/theme/(item['id']+'.svg')).read_bytes()
        assert raw==(OUT/'theme-masters'/theme/(item['id']+'.svg')).read_bytes()
        svg=ET.fromstring(raw);assert svg.attrib['viewBox']=='0 0 32 32'
        assert not any(e.tag.rsplit('}',1)[-1] in ('image','script','foreignObject','text') for e in svg.iter())
        assert all(not k.endswith('href') for e in svg.iter() for k in e.attrib)
        if theme=='Lumen':
            gradient=svg.find(N+'defs/'+N+'linearGradient');effect=svg.find(N+'defs/'+N+'filter')
            assert gradient.attrib['gradientUnits']=='userSpaceOnUse' and effect.attrib['filterUnits']=='userSpaceOnUse'
old=ROOT.parent/'按功能重绘图标_五主题SVG_20261103.zip'
if not old.exists():old=ROOT/'docs/assets/icons/scientific-outline-1.2.zip'
with zipfile.ZipFile(old) as frozen:
    for theme in ('Light','Dark','Pixel'):
        for item in catalog['icons']:
            relative=f'svg/{theme}/{item["id"]}.svg';assert frozen.read('redesigned-icons-20261103/'+relative)==(OUT/relative).read_bytes()
for theme in ('Light','Dark','Pixel','Lumen','Isekai'):
    root=ET.parse(OUT/'overview'/f'semantic-icons-{theme}.svg').getroot()
    assert len([e for e in root if e.tag==N+'g'])==221
receipt=dict(date='2026-11-03',version='1.3',actualSemanticIcons=221,uiReferencesAndStates=332,independentProfiles={'Lumen':221,'Isekai':221},
    identities={'Lumen':'Gunmetal/ice-cyan technical HUD','Isekai':'Sword-and-magic: grimoires, scrolls, magic circles, runic calibration and alchemical tools'},
    standaloneThemeSvgFiles=1105,themeMasters=442,unchangedPixelCombinations=1989,unchangedSvgFiles=663,
    nativeTests=dict(report=report.name,passed=35,failed=0,skipped=0,sha256=sha(report.read_bytes())),galleryChecks=web['checks'],
    disabledAndEmphasisOverride='Readable monochrome; no steel/glow',geometryCache='Shared metadata, thread-local lazy geometry',
    scope='Icon artwork/materials only. Theme settings, layout, commands, text, brand marks and physical data colours retained. Background/off-screen checks; no user app restart.')
dump(E/'verification.json',receipt);dump(OUT/'verification.json',receipt)
files=sorted(p for p in OUT.rglob('*') if p.is_file() and '_build' not in p.relative_to(OUT).parts)
archive=ROOT.parent/'按主题重绘图标_HUD与魔法_20261103.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as bundle:
    for file in files:bundle.write(file,OUT.name+'/'+file.relative_to(OUT).as_posix())
with zipfile.ZipFile(archive) as bundle:
    assert len(bundle.namelist())==len(files)
    for file in files:assert sha(bundle.read(OUT.name+'/'+file.relative_to(OUT).as_posix()))==sha(file.read_bytes())
archive_receipt=dict(archive=archive.name,files=len(files),bytes=archive.stat().st_size,sha256=sha(archive.read_bytes()),allEntriesChecked=True)
dump(E/'archive-verification.json',archive_receipt)
print(json.dumps(dict(nativePassed=35,galleryPassed=7,pixelsUnchanged=1989,svgUnchanged=663,archiveFiles=len(files),archiveBytes=archive.stat().st_size)))
