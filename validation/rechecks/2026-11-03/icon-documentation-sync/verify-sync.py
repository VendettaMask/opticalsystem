from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import urlparse,unquote
from docx import Document
from pdf2image import pdfinfo_from_path
import hashlib,json,zipfile

REPO=Path(__file__).resolve().parents[4]
WORK=REPO.parent
E=Path(__file__).resolve().parent
FIRST=WORK/'icon-redesign-20261103'
OUT=WORK/'redesigned-icons-20261103'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read(p):return p.read_text(encoding='utf-8-sig')
checks=[]
def check(name,condition):
    assert condition,name
    checks.append({'check':name,'result':'passed'})

required=['docs/ICON_SEMANTIC_DESIGN.md','docs/LOCAL_ICONS.md','docs/THEME_PACKAGES.md','docs/UI_DESIGN_SPEC.md']
for name in required:
    t=read(REPO/name)
    check(name+' theme identities',all(w in t for w in ['HUD','剑与魔法','炼金']))
    check(name+' ongoing maintenance','同步' in t and ('记录' in t or '规范' in t))
check('No all-theme shared-geometry contract','几何共享且' not in read(REPO/'docs/LOCAL_ICONS.md'))
check('No active GameIcons package contract','异世界主题由 `GameIconsFantasy` 包直接加载' not in read(REPO/'docs/THEME_PACKAGES.md'))
check('No obsolete Lumen icon palette','光域图标使用灰青结构、暖金主信息' not in read(REPO/'docs/UI_DESIGN_SPEC.md'))
check('Persistent maintenance requirement','User icon-documentation requirement' in read(REPO/'AGENTS.md'))

doc=Document(FIRST/'图标风格规范.docx')
texts=[p.text for p in doc.paragraphs]
texts += [cell.text for table in doc.tables for row in table.rows for cell in row.cells]
text='\n'.join(texts)
check('Original Word 1.3 semantic and theme rules',all(w in text for w in ['v1.3','钢铁侠','剑与魔法','炼金','332','221','18、24、32','记录']))
check('Original and delivered Word byte identity',sha(FIRST/'图标风格规范.docx')==sha(OUT/'图标风格规范.docx'))
info=pdfinfo_from_path(str(FIRST/'_qa/v1.3/图标风格规范.pdf'),poppler_path='C:/Users/19851/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/poppler/Library/bin')
check('Six rendered Word pages',info['Pages']==6 and all((FIRST/f'_qa/v1.3/page-{i}.png').exists() for i in range(1,7)))

class Links(HTMLParser):
    def __init__(self):super().__init__();self.urls=[]
    def handle_starttag(self,tag,attrs):
        for k,v in attrs:
            if (tag=='a' and k=='href') or (tag=='img' and k=='src'):self.urls.append(v)
htmlfiles=[FIRST/'style-guide.html',OUT/'index.html',OUT/'设计说明.html',OUT/'图标风格规范.html']
count=0
for file in htmlfiles:
    parser=Links();parser.feed(read(file))
    for url in parser.urls:
        value=urlparse(url)
        if not value.scheme and value.path:
            check(file.name+' local link '+url,(file.parent/unquote(value.path)).exists());count+=1
check('Current full standard HTML theme identity',all(w in read(OUT/'图标风格规范.html') for w in ['v1.3','钢铁侠','剑与魔法','炼金','版本与维护记录']))
tokens=json.loads(read(FIRST/'design-tokens.json'))
check('Original design tokens current version and profiles',tokens['version']=='1.3' and set(tokens['independentProfiles'])=={'Lumen','Isekai'} and tokens['dark']['signal']=='#F0AE79')

archive=WORK/'按主题重绘图标_HUD与魔法_20261103.zip'
with zipfile.ZipFile(archive) as z:
    for name in ['图标风格规范.docx','图标风格规范.html','设计说明.html','使用说明.txt']:
        check('Archived current '+name,hashlib.sha256(z.read(OUT.name+'/'+name)).hexdigest()==sha(OUT/name))
    count_entries=len(z.namelist())
receipt=json.loads(read(REPO/'validation/rechecks/2026-11-03/icon-theme-identity/archive-verification.json'))
check('Complete archive receipt',receipt['allEntriesChecked'] and receipt['files']==count_entries and receipt['sha256']==sha(archive))

audit=json.loads(read(E/'documentation-audit.json'))
check('Strict repository documentation audit',not audit['BrokenLinks'] and not audit['MarkdownFenceIssues'] and not audit.get('GuideRepeatedParagraphGroups',[]))
paths=['AGENTS.md','docs/ICON_SEMANTIC_DESIGN.md','docs/LOCAL_ICONS.md','docs/THEME_PACKAGES.md','docs/UI_DESIGN_SPEC.md','docs/UI_DESIGN_REVIEW.md','docs/README.md','docs/CURRENT_STATUS.md','src/OpticalSystemDesign.App/Assets/Icons/Scientific/README.md','scripts/export-semantic-icons.py']
result={
    'date':'2026-11-03','standardVersion':'1.3','scope':'Documentation, documentation export and offline archive only; no application icon or optical computation changes.',
    'themeRequirements':{'Lumen':'Iron-Man-like technical HUD with steel and ice-cyan signals','Isekai':'Sword and magic world including classical alchemical instruments'},
    'checks':checks,'localHtmlLinksChecked':count,
    'word':{'pages':6,'allPagesVisuallyInspected':True,'sha256':sha(FIRST/'图标风格规范.docx'),'renderer':'Independent hidden Word instance and bundled Poppler; canonical LibreOffice unavailable'},
    'strictDocumentationAudit':{'documents':audit['Documents'],'brokenLinks':len(audit['BrokenLinks']),'fenceIssues':len(audit['MarkdownFenceIssues'])},
    'updatedRepositoryFiles':{p:sha(REPO/p) for p in paths},
    'archive':{'file':archive.name,'entries':count_entries,'sha256':sha(archive)},
    'productTestsAddedByThisTask':0,
    'priorProductEvidence':'../icon-theme-identity/README.md; 35 native and 7 gallery checks retain their original scope and are not rerun or added to document checks.'
}
(E/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'checksPassed':len(checks),'wordPages':6,'archiveEntries':count_entries,'brokenLinks':len(audit['BrokenLinks'])}))
