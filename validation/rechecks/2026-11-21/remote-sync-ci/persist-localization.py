import json
import subprocess
from pathlib import Path

catalog_path = Path('src/OpticalSystemDesign.App/Assets/Localization/ui.catalog.json')
current = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
baseline = {r['Source']: r for r in json.loads(subprocess.check_output(['git', 'show', 'HEAD:' + catalog_path.as_posix()], text=True, encoding='utf-8'))}
target = Path('localization/ui-presentation-translations.tsv')
existing = set()
for file in Path('localization').glob('*.tsv'):
    existing.update(line.split('\t')[0].replace('\\n', '\n').replace('\\r', '\r') for line in file.read_text(encoding='utf-8-sig').splitlines() if line and not line.startswith('#'))
for file in Path('localization').glob('*.json'):
    data = json.loads(file.read_text(encoding='utf-8-sig'))
    if isinstance(data, list):
        existing.update(r['Source'] for r in data if isinstance(r, dict) and 'Source' in r)
rows = {r['Source']: r for r in current if r != baseline.get(r['Source']) and r['Source'] not in existing}
extra = [
    ('Nd', 'Nd', 'Nd'), ('Vd', 'Vd', 'Vd'), ('公式', 'Formula', '式'),
    ('打开导入检查；全部语义和资产通过后才能应用场景。{0}{1}', 'Open import inspection; apply the scene only after all semantics and assets pass.{0}{1}', 'インポート確認を開きます。すべての意味とアセットの確認後にシーンを適用できます。{0}{1}'),
    ('系数 {0}', 'Coefficient {0}', '係数 {0}'), ('顶点', 'Vertex', '頂点'),
    ('真彩色', 'True color', 'トゥルーカラー'),
    ('表面 {0}：{1}\n当前净半径：{2} {3}\n机械半直径：{4} {5}', 'Surface {0}: {1}\nCurrent clear radius: {2} {3}\nMechanical semi-diameter: {4} {5}', '面 {0}：{1}\n現在の有効半径：{2} {3}\n機械半径：{4} {5}'),
]
for s, e, j in extra:
    if s not in existing:
        rows[s] = dict(Source=s, Chinese=s, English=e, Japanese=j)
def escape(value):
    return value.replace('\r', '\\r').replace('\n', '\\n')
with target.open('a', encoding='utf-8', newline='\n') as file:
    for row in rows.values():
        file.write('\n' + '\t'.join(escape(row[k]) for k in ['Source', 'English', 'Japanese', 'Chinese']))
    file.write('\n')
invariants = Path('localization/invariant-text.tsv')
if '#RRGGBB' not in existing:
    with invariants.open('a', encoding='utf-8', newline='\n') as file:
        file.write('\n#RRGGBB\tCanonical hexadecimal RGB input format; preserve the format token.\n')
print(f'Persisted {len(rows)} generated-catalog translations in their editable source.')
