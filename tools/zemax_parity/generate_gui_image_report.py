#!/usr/bin/env python3
"""Build a side-by-side report from actual Avalonia GUI captures and Zemax screenshots."""

from __future__ import annotations

import argparse
import datetime as dt
import html
import json
import os
from pathlib import Path


REFERENCE_DIRECTORIES: dict[int, str | None] = {
    1: "009-raytrace", 2: "133-systemdata", 3: "010-seidelcoefficients",
    4: "011-seideldiagram", 5: "044-standardspot", 6: "046-fullfieldspot",
    7: "047-matrixspot", 8: "048-configurationmatrixspot", 9: "001-rayfan",
    10: "071-footprintsettings", 11: "004-fieldcurvatureanddistortion",
    12: "004-fieldcurvatureanddistortion", 13: "006-griddistortion",
    14: "004-fieldcurvatureanddistortion", 15: "005-focalshiftdiagram",
    16: "007-lateralcolor", 17: "008-longitudinalaberration",
    18: "141-fullfieldaberration", 19: "035-geometricencircledenergy",
    20: "034-diffractionencircledenergy", 21: "036-geometriclineedgespread",
    22: "037-extendedsourceencircledenergy", 23: "003-pupilaberrationfan",
    24: "049-rmsfield", 25: "051-rmslambdadiagram", 26: "052-rmsfocus",
    27: "050-rmsfieldmap", 28: "049-rmsfield", 29: "045-throughfocusspot",
    30: "017-fftthroughfocusmtf", 31: "017-fftthroughfocusmtf",
    32: "028-huygensthroughfocusmtf", 33: "018-geometricthroughfocusmtf",
    34: "023-fftmtfvsfield", 35: "025-huygensmtfvsfield",
    36: "024-geometricmtfvsfield", 37: "075-incidentanglevsimageheight",
    38: None, 39: None, 40: "079-cardinalpoints", 41: "070-vignettingdiagramsettings",
    42: "069-relativeillumination", 43: None, 44: None, 45: "072-yybardiagram",
    46: "029-fftpsf", 47: "030-fftpsfcrosssection", 48: "031-fftpsflineedgespread",
    49: "033-huygenspsf", 50: "032-huygenspsfcrosssection", 51: "016-fftmtf",
    52: "026-huygensmtf", 53: "019-geometricmtf", 54: "016-fftmtf",
    55: "139-contrastloss", 56: "002-opticalpathfan", 57: "053-foucault",
    58: "055-wavefrontmap", 59: None, 60: None, 61: None,
    62: "059-imagesimulation", 63: "060-geometricimageanalysis",
    64: "062-geometricbitmapimageanalysis", 65: "064-lightsourceanalysis",
    66: "065-partiallycoherentimageanalysis",
    67: "066-extendeddiffractionimageanalysis", 68: None,
    69: "116-prescriptiondatasettings",
}

NEAREST_ONLY = {2, 54, 69}
SOURCE_OR_SETTINGS_MISMATCH = {62, 63, 64, 65, 66, 67}


def _relative(path: Path, base: Path) -> str:
    return Path(os.path.relpath(path, base)).as_posix()


def _zemax_screenshot(baseline_root: Path, directory: str | None) -> Path | None:
    if directory is None:
        return None
    candidates = sorted((baseline_root / "analyses" / directory).glob("screenshot.*"))
    return candidates[0] if candidates else None


def _reference_kind(index: int, reference: Path | None) -> str:
    if reference is None:
        return "无等价 Zemax 截图"
    if index in NEAREST_ONLY:
        return "仅最接近参考，不能判定同图"
    if index in SOURCE_OR_SETTINGS_MISMATCH:
        return "同类分析，但输入源/设置不同"
    return "直接图像参考"


def build_report(capture_manifest: Path, baseline_root: Path, output_dir: Path) -> dict:
    capture = json.loads(capture_manifest.read_text(encoding="utf-8"))
    runs = capture.get("runs", [])
    capture_dir = capture_manifest.parent
    items = []
    for run in runs:
        index = int(run["index"])
        image_name = run.get("image")
        workbench = capture_dir / image_name if image_name else None
        reference = _zemax_screenshot(baseline_root, REFERENCE_DIRECTORIES.get(index))
        items.append({
            "index": index,
            "analysis": run["analysis"],
            "canonicalAnalysis": run.get("canonicalAnalysis"),
            "captureStatus": run["status"],
            "workbenchImage": _relative(workbench, output_dir) if workbench else None,
            "workbenchImageExists": bool(workbench and workbench.is_file()),
            "zemaxImage": _relative(reference, output_dir) if reference else None,
            "zemaxImageExists": bool(reference and reference.is_file()),
            "referenceKind": _reference_kind(index, reference),
        })

    report = {
        "reportName": "123456.ZMX 真实 GUI 与 Zemax 2026 R1 全量图像对比",
        "generatedAt": dt.datetime.now().astimezone().isoformat(timespec="seconds"),
        "method": (
            "Workbench 左图来自真实 Avalonia AnalysisPanel 渲染；"
            "不使用 images/current 中的离线 Matplotlib 重绘图。"
        ),
        "captureManifest": _relative(capture_manifest, output_dir),
        "summary": {
            "total": len(items),
            "captured": sum(item["captureStatus"] == "captured" for item in items),
            "analysisError": sum(item["captureStatus"] != "captured" for item in items),
            "directReferences": sum(item["referenceKind"] == "直接图像参考" for item in items),
            "nearestOnly": sum("最接近" in item["referenceKind"] for item in items),
            "sourceOrSettingsMismatch": sum("输入源/设置不同" in item["referenceKind"] for item in items),
            "noEquivalent": sum(item["zemaxImage"] is None for item in items),
        },
        "items": items,
    }
    return report


def _image_figure(label: str, path: str | None, exists: bool) -> str:
    if not path or not exists:
        return f'<figure><figcaption>{html.escape(label)}</figcaption><div class="missing">无可用等价截图</div></figure>'
    escaped = html.escape(path, quote=True)
    return (
        f'<figure><figcaption>{html.escape(label)}</figcaption>'
        f'<a href="{escaped}"><img loading="lazy" src="{escaped}" alt="{html.escape(label)}"></a>'
        "</figure>"
    )


def render_html(report: dict) -> str:
    summary = report["summary"]
    cards = []
    for item in report["items"]:
        cards.append(
            '<section class="card">'
            f'<h2>{item["index"]:02d}. {html.escape(item["analysis"])}</h2>'
            '<div class="meta">'
            f'<span class="status">运行：{html.escape(item["captureStatus"])}</span>'
            f'<span>{html.escape(item["referenceKind"])}</span>'
            '</div><div class="pair">'
            + _image_figure("Workbench 实际 GUI", item["workbenchImage"], item["workbenchImageExists"])
            + _image_figure("Zemax 2026 R1 基准", item["zemaxImage"], item["zemaxImageExists"])
            + '</div></section>'
        )
    return f'''<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{html.escape(report["reportName"])}</title>
<style>
body{{margin:0;background:#eef1f5;color:#202226;font-family:"Microsoft YaHei",Segoe UI,sans-serif}}
main{{max-width:1800px;margin:auto;padding:24px}}h1{{margin:0 0 10px}}.notice{{background:#fff5d9;border-left:5px solid #e69a00;padding:14px 16px;margin:16px 0}}
.summary{{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin:16px 0 24px}}.stat{{background:white;border:1px solid #d5dae2;border-radius:8px;padding:12px}}.stat b{{display:block;font-size:24px;color:#075fb8}}
.card{{background:white;border:1px solid #d5dae2;border-radius:10px;margin:18px 0;padding:16px;box-shadow:0 1px 3px #0001}}h2{{margin:0 0 8px;font-size:19px}}.meta{{display:flex;gap:16px;flex-wrap:wrap;color:#59616d;margin-bottom:12px}}.status{{font-weight:600}}
.pair{{display:grid;grid-template-columns:1fr 1fr;gap:14px}}figure{{margin:0;min-width:0}}figcaption{{font-weight:600;margin:0 0 8px}}img{{display:block;width:100%;height:620px;object-fit:contain;background:#f7f8fa;border:1px solid #d5dae2}}.missing{{height:620px;display:grid;place-items:center;background:#f7f8fa;border:1px dashed #aeb6c2;color:#707985}}
@media(max-width:1000px){{.pair{{grid-template-columns:1fr}}img,.missing{{height:420px}}}}
</style></head><body><main>
<h1>{html.escape(report["reportName"])}</h1>
<p>生成时间：{html.escape(report["generatedAt"])}</p>
<div class="notice"><b>校正说明：</b>{html.escape(report["method"])} 本报告不自动宣称“图像一致”；只有同一分析、同一输入和同一设置时才允许作视觉结论。</div>
<div class="summary">
<div class="stat"><b>{summary["total"]}</b>Workbench 实际截图</div><div class="stat"><b>{summary["captured"]}</b>分析成功</div>
<div class="stat"><b>{summary["analysisError"]}</b>分析错误</div><div class="stat"><b>{summary["directReferences"]}</b>直接参考</div>
<div class="stat"><b>{summary["sourceOrSettingsMismatch"]}</b>输入/设置不一致</div><div class="stat"><b>{summary["nearestOnly"]}</b>仅最接近参考</div>
<div class="stat"><b>{summary["noEquivalent"]}</b>无等价截图</div></div>
{''.join(cards)}
</main></body></html>'''


def render_markdown(report: dict) -> str:
    summary = report["summary"]
    lines = [
        f'# {report["reportName"]}', '',
        f'- 生成时间：{report["generatedAt"]}',
        f'- 方法：{report["method"]}',
        f'- 完整运行：{summary["captured"]}/{summary["total"]}；分析错误：{summary["analysisError"]}',
        f'- 直接图像参考：{summary["directReferences"]}；输入/设置不同：{summary["sourceOrSettingsMismatch"]}；仅最接近参考：{summary["nearestOnly"]}；无等价截图：{summary["noEquivalent"]}',
        '',
        '> 本报告不再使用 `images/current` 离线重绘图。左侧图片均来自真实 Avalonia 分析界面。',
        '', '## 全量索引', '',
        '| 序号 | 分析 | Workbench 运行 | Zemax 参考类型 | 图片 |',
        '|---:|---|---|---|---|',
    ]
    for item in report["items"]:
        links = [f'[Workbench]({item["workbenchImage"]})'] if item["workbenchImage"] else []
        if item["zemaxImage"]:
            links.append(f'[Zemax]({item["zemaxImage"]})')
        lines.append(
            f'| {item["index"]} | {item["analysis"]} | {item["captureStatus"]} | '
            f'{item["referenceKind"]} | {" / ".join(links)} |'
        )
    lines.extend(['', '## 逐项真实截图', ''])
    for item in report["items"]:
        lines.extend([
            f'### {item["index"]:02d}. {item["analysis"]}', '',
            f'- Workbench 运行状态：`{item["captureStatus"]}`',
            f'- Zemax 参考类型：{item["referenceKind"]}', '',
        ])
        if item["workbenchImage"]:
            lines.extend([f'![Workbench 实际 GUI：{item["analysis"]}]({item["workbenchImage"]})', ''])
        if item["zemaxImage"]:
            lines.extend([f'![Zemax：{item["analysis"]}]({item["zemaxImage"]})', ''])
    return '\n'.join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--capture-manifest", required=True, type=Path)
    parser.add_argument("--baseline-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = build_report(args.capture_manifest.resolve(), args.baseline_root.resolve(), args.output.resolve())
    (args.output / "IMAGE_COMPARISON_REPORT.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (args.output / "IMAGE_COMPARISON_REPORT.html").write_text(render_html(report), encoding="utf-8")
    (args.output / "IMAGE_COMPARISON_REPORT.md").write_text(render_markdown(report), encoding="utf-8")
    print(json.dumps(report["summary"], ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
