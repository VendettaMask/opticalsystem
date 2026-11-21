import json
from pathlib import Path

path = Path('src/OpticalSystemDesign.App/Assets/Localization/ui.catalog.json')
entries = [
    ('最小波长', 'Minimum wavelength', '最小波長'),
    ('最大波长', 'Maximum wavelength', '最大波長'),
    ('有效波长', 'Valid wavelength range', '有効波長範囲'),
    ('Nd/Vd：部分数值不可用，请查看数据检查。', 'Nd/Vd: some values are unavailable; see the data check.', 'Nd/Vd：一部の値は利用できません。データ確認を参照してください。'),
    ('数据检查：F-d-C 模型计算可用；目录参考值保持原值。', 'Data check: F-d-C model calculation is available; catalog references are unchanged.', 'データ確認：F-d-Cモデル計算が可能です。カタログ参考値は元のままです。'),
    ('数据检查：F、d、C 谱线超出材料有效波长或折射率表范围；目录参考值不代表该波段的有效计算。', 'Data check: F, d or C is outside the valid material or index-table range; catalog references do not validate calculation in that range.', 'データ確認：F・d・C線が材料または屈折率表の有効範囲外です。カタログ参考値は範囲外の計算を保証しません。'),
    ('数据检查：当前色散模型不受支持；仅保留已有目录参考值。', 'Data check: the dispersion model is unsupported; existing catalog references are retained.', 'データ確認：この分散モデルは未対応です。既存のカタログ参考値を保持します。'),
    ('数据检查：色散数据缺失或计算无效；仅保留已有目录参考值。', 'Data check: dispersion data is missing or invalid; existing catalog references are retained.', 'データ確認：分散データが不足または無効です。既存のカタログ参考値を保持します。'),
    ('数据检查：F、C 折射率差值过小，Vd 无定义。', 'Data check: the F–C index difference is too small to define Vd.', 'データ確認：F・C線の屈折率差が小さすぎるためVdを定義できません。'),
    ('热数据需核对：目录参考温度、d 谱线下 dn/dT={0}/K；已保留原始 TD 数据，未缩放或修正。', 'Thermal data needs review: dn/dT={0}/K at the catalog reference temperature and d line; original TD data is retained without rescaling or correction.', '熱データの確認が必要：カタログ基準温度・d線でdn/dT={0}/Kです。元のTDデータは再スケーリング・修正せず保持しています。'),
    ('先检查对象语义和外部资产；菜单收录不代表全部支持。', 'Inspect object semantics and external assets first; listing an example does not imply full support.', 'まずオブジェクトの意味と外部アセットを確認します。メニューへの収録は完全な対応を意味しません。'),
    ('打开导入检查；全部语义和资产通过后才能应用场景。', 'Open import inspection; apply the scene only after all semantics and assets pass.', 'インポート確認を開きます。すべての意味とアセットの確認後にシーンを適用できます。'),
    ('非序列示例先进行导入检查；不支持的对象或缺失资产会阻止应用。', 'Non-sequential examples open import inspection; unsupported objects or missing assets block application.', '非シーケンシャル例はインポート確認を開きます。未対応オブジェクトや不足アセットがあれば適用できません。'),
    ('检查非序列示例：{0}', 'Inspect non-sequential example: {0}', '非シーケンシャル例を確認：{0}'),
]
raw = path.read_text(encoding='utf-8')
existing = {row['Source'] for row in json.loads(raw)}
new = [dict(Source=s, Chinese=s, English=e, Japanese=j) for s, e, j in entries if s not in existing]
if new:
    body = json.dumps(new, ensure_ascii=False, indent=2)[2:-2]
    path.write_text(raw.rstrip()[:-1].rstrip() + ',\n' + body + '\n]\n', encoding='utf-8')
assert len({row['Source'] for row in json.loads(path.read_text(encoding='utf-8'))}) == len(json.loads(path.read_text(encoding='utf-8')))
print(f'Added {len(new)} translations; JSON and unique source keys verified.')
