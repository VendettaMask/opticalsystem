"""Put maintained icon deliverables and generator inputs inside the repository."""
from pathlib import Path
import shutil,re,json,hashlib

REPO=Path(__file__).resolve().parents[4]
WORK=REPO.parent
OUT=REPO/'docs/assets/icons'
SOURCE=WORK/'redesigned-icons-20261103'
OUT.mkdir(parents=True,exist_ok=True)
files=['index.html','设计说明.html','图标风格规范.html','图标风格规范.docx','规范示例.png','theme-colors.json','功能与图标对应表.csv','使用说明.txt']
for name in files:shutil.copy2(SOURCE/name,OUT/name)
for path in SOURCE.glob('*.png'):shutil.copy2(path,OUT/path.name)
for path in (SOURCE/'overview').glob('*.svg'):
    (OUT/'overview').mkdir(exist_ok=True);shutil.copy2(path,OUT/'overview'/path.name)
for version,name in [('1.3','按主题重绘图标_HUD与魔法_20261103.zip'),('1.2','按功能重绘图标_五主题SVG_20261103.zip')]:
    shutil.copy2(WORK/name,OUT/f'scientific-outline-{version}.zip')
shutil.copy2(WORK/'icon-redesign-20261103/design-tokens.json',OUT/'design-tokens.json')
inputs=REPO/'scripts/icon-gallery-inputs';inputs.mkdir(parents=True,exist_ok=True)
shutil.copy2(WORK/'used-icons-20261103/_build/gallery.html',inputs/'gallery.template')
if (inputs/'gallery.html').exists():(inputs/'gallery.html').unlink()
shutil.copy2(WORK/'used-icons-20261103/theme-colors.json',inputs/'theme-colors.json')
studies=Path('C:/Users/19851/.codex/visualizations/2026/11/03/01a19f0d-0ae6-75e1-86f4-df988b660d52/theme-icon-proposals/svg')
for profile in ['hud','isekai']:
    target=REPO/'scripts/icon-theme-studies/svg'/profile;target.mkdir(parents=True,exist_ok=True)
    for path in (studies/profile).glob('*.svg'):shutil.copy2(path,target/path.name)

# Repair Markdown targets that otherwise exist only on this desktop.
previous=Path(__file__).parent/'portable-icon-deliverables.json'
changed=json.loads(previous.read_text(encoding='utf-8'))['repairedMarkdownFiles'] if previous.exists() else []
pattern=re.compile(r'(?<!!)\[([^\]\n]+)\]\(([^)\n]+)\)')
for folder in [REPO/'docs',REPO/'validation/rechecks/2026-11-03']:
    for path in folder.rglob('*.md'):
        original=path.read_text(encoding='utf-8-sig')
        def replace(m):
            label,url=m.groups()
            raw=url.strip('<>')
            if raw.startswith(('http:','https:','#','mailto:')):return m.group(0)
            resolved=(path.parent/raw.split('#')[0]).resolve()
            try:relative=resolved.relative_to(WORK)
            except ValueError:return m.group(0)
            parts=relative.parts;dest=None
            if parts[0]=='redesigned-icons-20261103':dest=OUT.joinpath(*parts[1:])
            elif parts[0]=='icon-redesign-20261103' and parts[-1]=='图标风格规范.docx':dest=OUT/'图标风格规范.docx'
            elif parts[0]=='icon-redesign-20261103' and parts[-1]=='style-guide.html':dest=OUT/'图标风格规范.html'
            elif parts[0]=='按主题重绘图标_HUD与魔法_20261103.zip':dest=OUT/'scientific-outline-1.3.zip'
            elif parts[0]=='按功能重绘图标_五主题SVG_20261103.zip':dest=OUT/'scientific-outline-1.2.zip'
            elif parts[0]=='used-icons-20261103':return label+'（重绘前本机基线）'
            if dest is not None and dest.is_file():
                import os
                return '['+label+']('+Path(os.path.relpath(dest,path.parent)).as_posix()+')'
            return m.group(0)
        updated=pattern.sub(replace,original)
        if updated!=original:
            path.write_text(updated,encoding='utf-8')
            name=path.relative_to(REPO).as_posix()
            if name not in changed:changed.append(name)

# The original copies remain beside the chat; these byte-identical copies travel with Git.
(OUT/'README.md').write_text('''# 科学图标与风格规范 1.3

当前[完整图库](index.html)展示全部221款语义图形及332处功能与状态；普通、黑洞、像素保留基础图形，光域与异世界各有221款独立造型。

- [Word 风格规范](图标风格规范.docx)
- [网页风格规范](图标风格规范.html)
- [逐项设计说明](设计说明.html)
- [当前完整离线图库与五主题 SVG 包](scientific-outline-1.3.zip)
- [历史基础重绘包 1.2](scientific-outline-1.2.zip)

光域采用钢铁侠式科技 HUD；异世界保持剑与魔法世界观，包含魔导书、卷轴、法阵、符文与古典炼金器具。后续修改须同步功能图形、五主题适配、规范、图库、SVG包和记录，现行仓库契约见[语义规范](../../ICON_SEMANTIC_DESIGN.md)。

完整ZIP包含1105份普通状态SVG、基础与主题母版、总图和同版规范。网页图库内嵌所需图形，可离线使用；对产品行为的验证范围见[主题身份接入](../../../validation/rechecks/2026-11-03/icon-theme-identity/README.md)。这些文件与先前本机交付按字节核对，未重新设计图标或改写历史通过数。
''',encoding='utf-8')
receipt={'files':{str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.rglob('*') if p.is_file()},'repairedMarkdownFiles':changed,'proposalSvgInputs':18}
(Path(__file__).parent/'portable-icon-deliverables.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'portableFiles':len(receipt['files']),'repairedMarkdownFiles':len(changed),'proposalInputs':18}))
