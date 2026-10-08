"""Format existing test and formal-Core evidence; never calculate optical results."""
from __future__ import annotations

import hashlib
import json
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from xml.sax.saxutils import escape

from PIL import Image
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "artifacts/validation/calculation-path-repair-20261008"
OUTPUT = ROOT / "output/pdf/Zemax_Workbench_Comparison_Repair_2026-10-08.pdf"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def load(relative: str):
    return json.loads((ROOT / relative).read_text(encoding="utf-8"))


def trx(relative: str) -> dict:
    path = ROOT / relative
    root = ET.parse(path).getroot()
    counters = root.find("t:ResultSummary/t:Counters", NS)
    times = root.find("t:Times", NS)
    results = root.findall("t:Results/t:UnitTestResult", NS)
    failed = [dict(name=r.attrib["testName"], message=r.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS),
                   stack=r.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS))
              for r in results if r.attrib["outcome"] != "Passed"]
    return dict(path=relative, sha256=sha(path), counters={k: int(v) for k, v in counters.attrib.items()},
                times=times.attrib, failures=failed, names=[r.attrib["testName"] for r in results],
                outcomes={r.attrib["testName"]: r.attrib["outcome"] for r in results})


def baseline_integrity() -> dict:
    directory = ROOT / "artifacts/zemax/123456-zemax-2026-r1-baseline"
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    source = directory / "source/123456.ZMX"
    assert sha(source) == manifest["sourceSha256"].lower()
    captured = [e for e in manifest["analyses"] if e["status"] == "captured"]
    screenshots = Counter()
    for entry in captured:
        folder = directory / entry["directory"]
        assert entry.get("textSaved") == bool(entry.get("textFile"))
        keys = ["dataJson", "screenshot"] + (["textFile"] if entry.get("textFile") else [])
        for key in keys:
            file = folder / entry[key]
            assert file.is_file() and file.stat().st_size > 0, str(file)
            if key == "dataJson":
                json.loads(file.read_text(encoding="utf-8-sig"))
            if key == "screenshot":
                with Image.open(file) as picture:
                    picture.verify()
        screenshots[entry["screenshotStatus"]] += 1
    files = [p for p in directory.rglob("*") if p.is_file()]
    return dict(scope="Existing baseline integrity only, not new native numerical equality", total=len(manifest["analyses"]),
                captured=len(captured), notCaptured=len(manifest["analyses"]) - len(captured), screenshots=dict(screenshots),
                files=len(files), bytes=sum(p.stat().st_size for p in files), sourceSha256=sha(source))


def evidence_summary() -> dict:
    formal = trx("artifacts/validation/calculation-path-repair-20261008/full-release-01/full-formal.trx")
    tool = trx("artifacts/validation/calculation-path-repair-20261008/full-tool-release-02/full-comparison.trx")
    debug = trx("artifacts/validation/calculation-path-repair-20261008/target-debug-01/targeted.trx")
    old = trx("artifacts/validation/wavefront-acceptance-20261008/run-01/formal-full-release.trx")
    old_tool = trx("artifacts/validation/wavefront-acceptance-20261008/run-01/comparison-full-release.trx")
    assert len(set(formal["names"])) == len(formal["names"])
    removed = sorted(set(old["names"]) - set(formal["names"]))
    added = sorted(set(formal["names"]) - set(old["names"]))
    assert not removed and len(added) == 60
    assert all("CalculationPathRepairTests" in n or "CalculationPathPanelTests" in n for n in added)
    assert set(old_tool["names"]) == set(tool["names"])
    matrix = load("artifacts/validation/calculation-path-repair-20261008/matrix-release-final/summary.json")
    matrix_debug = load("artifacts/validation/calculation-path-repair-20261008/matrix-debug-final/summary.json")
    for key in ("cases", "fftSampling", "applicationFftDisplay", "negativeFftDelta", "foucault"):
        assert matrix[key] == matrix_debug[key], key
    for row in matrix["cases"]:
        for check in row["checks"]:
            assert all(z["coefficientErrorAgainstSameHexSourceAimingWaves"] == 0 for z in check["zernike"])
            assert check["zernikeSweepEdgeCoefficientErrorAgainstSourceAimingWaves"] == 0
            assert check["fft"]["mismatchRejected"]
            assert check["fft"]["actualVsPreparedSystemAiming"]["maximumRelativeIntensityDifference"] == 0
    for path, digest in matrix["inputHashes"].items():
        assert sha(ROOT / path) == digest
    for path, digest in matrix["fixedProductSourceHashes"].items():
        assert sha(ROOT / path) == digest
    binary_paths = ["src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll",
                    "tests/OptilandWorkbench.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll",
                    "tests/OptilandWorkbench.ZemaxComparison.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll",
                    "tools/diagnostics/CalculationPaths20261008/bin/Release/net10.0/OptilandWorkbench.Core.dll"]
    hashes = {p: sha(ROOT / p) for p in binary_paths}
    assert len(set(hashes.values())) == 1 and matrix["coreAssemblySha256"] == hashes[binary_paths[0]]
    # Large per-test name lists stay in the immutable TRXs, not the concise report.
    compact = lambda data: {k: v for k, v in data.items() if k not in ("names", "outcomes")}
    result = dict(createdUtc=datetime.now(timezone.utc).isoformat(), formal=compact(formal), tool=compact(tool),
                  debugTarget=compact(debug), addedTests=added, removedTests=removed, addedPassing=sum(formal["outcomes"][n] == "Passed" for n in added),
                  unchangedToolIdentities=True, debugReleaseMatricesEqual=True, coreBinaryHashes=hashes,
                  baselineIntegrity=baseline_integrity(), productSourceHashes=matrix["fixedProductSourceHashes"],
                  nativeRecaptured=False, original132Reclassified=False, wordReportUpdated=False,
                  wordLimitation="Packaged DOCX renderer failed: no available LibreOffice on this Windows runtime; original Word file preserved")
    (EVIDENCE / "verification-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def make_pdf(summary: dict) -> None:
    pdfmetrics.registerFont(TTFont("YaHei", "C:/Windows/Fonts/msyh.ttc", subfontIndex=0))
    pdfmetrics.registerFont(TTFont("YaHeiBold", "C:/Windows/Fonts/msyhbd.ttc", subfontIndex=0))
    body = ParagraphStyle("body", fontName="YaHei", fontSize=10.2, leading=16, spaceAfter=7, wordWrap="CJK")
    small = ParagraphStyle("small", parent=body, fontSize=8.7, leading=13, spaceAfter=3)
    heading = ParagraphStyle("heading", parent=body, fontName="YaHeiBold", fontSize=15, leading=21, spaceBefore=10, spaceAfter=9)
    title = ParagraphStyle("title", parent=heading, fontSize=23, leading=33, spaceAfter=17)
    cell = ParagraphStyle("cell", parent=small, spaceAfter=0)
    cell_header = ParagraphStyle("header", parent=cell, fontName="YaHeiBold", textColor=colors.white, alignment=TA_CENTER)
    story = []

    def p(text, style=body):
        story.append(Paragraph(escape(text), style))

    def h(text):
        p(text, heading)

    def table(headers, rows, widths):
        data = [[Paragraph(escape(str(t)), cell_header) for t in headers]]
        data += [[Paragraph(escape(str(t)), cell) for t in row] for row in rows]
        tab = Table(data, colWidths=[w * mm for w in widths], repeatRows=1, hAlign="LEFT")
        tab.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#1F4E78")),
                                ("GRID", (0, 0), (-1, -1), .4, colors.HexColor("#D9D9D9")),
                                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F2F6FA")]),
                                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"), ("LEFTPADDING", (0, 0), (-1, -1), 6),
                                ("RIGHTPADDING", (0, 0), (-1, -1), 6), ("TOPPADDING", (0, 0), (-1, -1), 7),
                                ("BOTTOMPADDING", (0, 0), (-1, -1), 7)]))
        story.extend([tab, Spacer(1, 7)])

    f = summary["formal"]["counters"]; t = summary["tool"]["counters"]
    p("Zemax 与 Workbench", title)
    p("计算修复和结果对比", heading)
    p("2026年10月8日  修复后验证更新", small)
    p("本轮已修复瞄准设置遗漏、FFT 显示与计算网格混用、重复减少采样、负像面间距被忽略，以及 Huygens 的整体轴向原点依赖。未实现的 Foucault 偏振设置已明确禁用。")
    p("内部一致性问题已经收口，但还不能宣称全面 Zemax 等价或发布验收通过。Huygens 仍是已标记的标量兼容近似；MS-L7 的 Huygens 截面和衍射包围能量仍有原生数值差异。")
    table(["本轮验证", "结果", "含义"], [
        ["正式完整 Release", f'{f["passed"]} 通过 / {f["failed"]} 失败 / {f["total"]} 项', "全部既有测试保留；失败见末页"],
        ["比较工具完整 Release", f'{t["passed"]} 通过 / {t["failed"]} 失败 / {t["total"]} 项', "2 个既有失败，原容差未变"],
        ["新增回归", f'{summary["addedPassing"]} / 60 通过', "59 项计算与 1 项桌面设置"],
        ["Debug 专项", "103 / 103 通过", "包括新增 60 项，不另加到全量"],
        ["原生零离焦光扇", "492 / 492 有效对应，零缺失", "三镜头六组捕获设置，逐点比较"]], [45, 58, 65])
    h("六镜头历史完整矩阵")
    p("下面保留 2026年10月7日的 132 项原设置结果。本轮未重跑完整矩阵，不能将局部修复测试的通过改写成新的原生分类。", small)
    historical = load("artifacts/zemax-standard-samples/20261007/ra256-single-ray-fresh-import-original132/summary.json")
    names = {"cooke-40-degree-field": "Cooke 40 度", "double-gauss-28-degree-field": "Double Gauss 28 度",
             "doublet": "Doublet", "even-asphere": "Even Asphere", "relay-lens": "Relay Lens",
             "tessar-lens-using-vignetting-factors": "Tessar 渐晕镜头"}
    rows = []
    for key, name in names.items():
        count = Counter(r["currentConclusion"] for r in historical["rows"] if r["lens"] == key)
        rows.append([name, *[count[s] for s in ("Pass", "Close", "Difference", "Incomparable", "Error")]])
    table(["镜头", "Pass", "Close", "差异", "不可比", "错误"], rows, [68, 20, 20, 20, 22, 18])
    p("合计 95 Pass、12 Close、16 Difference、8 Incomparable、1 Error。原 23 项独立 RMS 控制仍是独立历史证据，不计入 132 项。", small)
    story.append(PageBreak())

    h("计算路径修复")
    table(["路径", "修复后行为", "验证范围"], [
        ["Zernike 与扫场", "发射、拟合遵循同一系统瞄准", "36 个同节点拟合与 12 个扫场控制一致"],
        ["两种参考球与无焦回退", "发射和入瞳相位修正采用一致瞄准", "有焦与合成无焦内部控制；非原生新认证"],
        ["桌面 Standard 与 Annular", "新设置用均匀瞳面；通用 Core 默认仍为六角环", "明确 PupilSampling；旧 NumRings 请求兼容"],
        ["FFT 与 Jones", "单元中心只决定节点；光线和工作 F 数使用实际瞄准", "12 组自动与同节点预计算结果一致"],
        ["预计算 FFT 输入", "不匹配瞄准、视场、波长、参考波长或实际节点直接拒绝", "12 组瞄准不匹配被拒绝；不认证任意快照来源"],
        ["高 NA FFT 回退", "必要的整瞳瞄准回退保留，明确记录实际设置", "显式关闭瞄准的 MS-L7 边缘与偏振控制"],
        ["离焦与快速 MTF", "默认遵循系统瞄准；非零离焦清除缓存并恢复几何", "有焦无焦、偏振、非零离焦和恢复检查"],
        ["FFT Sampling 与 Display", "计算网格固定 2N，显示只裁切；名义采样不再减两次", "同物理点、间距、峰值和归一化一致"],
        ["负 Image Delta", "负数不拉伸，0 自动，正数重新生成共轭光瞳", "不再静默钳制成 0；非有限值拒绝"],
        ["Huygens 原点", "兼容权重改用镜头自身局部基准", "九组整体 Z 平移；完整物理传播模型未认证"],
        ["Foucault 偏振", "Core 明确拒绝 true，桌面禁用并说明", "仍是定性梯度响应，不冒称完整物理刀口"]], [39, 75, 54])
    p("所有产品计算仍使用正式共享 C# Core。没有引入第二套光学公式，没有改写冻结输入、重生成 Optiland 历史参考或放宽原容差。", small)
    story.append(PageBreak())

    h("修复前后数值")
    table(["原生捕获设置", "旧零离焦最大误差 waves", "修复后 waves"], [
        ["Cooke 轴上 550 nm", "约 0.01027", "1.552293849E-10"],
        ["Cooke 20 度 480 nm", "364.929953861", "2.624171760E-5"],
        ["Double Gauss 轴上 587.6 nm", "约 0.02196", "2.422235745E-10"],
        ["Double Gauss 14 度 486.1 nm", "384.154817243", "4.031837608E-5"],
        ["Relay 轴上 587.5618 nm", "原瞄准路径已一致", "7.668519486E-9"],
        ["Relay 2 度 486.1327 nm", "原瞄准路径已一致", "6.317280772E-7"]], [68, 56, 44])
    p("每组 82 条记录，含两个方向各自的中心记录。六组均沿用捕获的原瞄准设置和原容差，没有插值、相位拟合或比例调整。", small)
    h("FFT 显示不再改变计算")
    table(["Display", "Sampling", "计算阵列", "像面间距 µm", "显示点数"], [
        [32, 64, 128, "0.9377854014", 1024], [64, 64, 128, "0.9377854014", 4096],
        [128, 64, 128, "0.9377854014", 16384]], [27, 30, 34, 49, 28])
    p("固定 Cooke 轴上 550 nm。修复前 Display 改变计算间距；修复后三个显示尺寸的共同物理坐标逐点完全一致。负像面间距的不拉伸模式间距为 1.326228833 µm，与自动模式不同。", small)
    h("Huygens 保留坐标修复 撤回传播升级")
    p("Cooke 离轴整体 Z 平移 100 mm，旧峰值降低约 11.50%。最终局部基准修复后的全部强度点最大差为 2.8444E-12，峰值保持 0.03784596230。九组平移的最大差不超过 3.5406E-11。")
    table(["升级候选", "MS-L7 截面 NRMSE", "处理"], [
        ["参考球朝内法线", "0.007438540572", "原生误差增大，已撤回"],
        ["光线方向平面波相位", "0.007340050275", "未满足非回退要求，已撤回"],
        ["最终局部坐标基准", "0.003725667059", "保持既有差异，仅修复原点依赖"]], [68, 48, 52])
    p("最终权重仍是明确标记的兼容近似，不是已经认证的参考球法线或 Zemax 平面球面自动传播模型。候选失败数据保留，未调整测试上界。", small)
    story.append(PageBreak())

    h("完整验证和未完成部分")
    p(f'正式 Release {f["total"]} 项中 {f["passed"]} 通过、{f["failed"]} 失败；比较工具 {t["total"]} 项中 {t["passed"]} 通过、{t["failed"]} 失败。两者均零跳过。新增 60 个身份与原 4434 个身份逐项核对，零删除；工具原 180 个身份全部保留。')
    p("默认 Debug 与 Release 解决方案构建均为零警告、零错误；正式 Core 的 Release 二进制在产品、正式测试、比较测试和诊断工具四个目录中哈希一致。", small)
    failures = []
    for label, data in (("正式", summary["formal"]), ("工具", summary["tool"])):
        for failure in data["failures"]:
            name = failure["name"].replace("OptilandWorkbench.Tests.", "").replace("OptilandWorkbench.ZemaxComparison.Tests.", "")
            message = failure["message"]
            explanation = "" if label == "工具" else ("既有 finite_angle 历史辅助差异，数值未变" if "finite_angle" in message else "既有界面会话 Dispose 空引用；未以隔离通过抵消全量失败")
            if label == "工具":
                explanation = "Huygens 截面原生残差 Close，NRMSE 0.003725667059" if "Huygens PSF Cross Section" in name else "衍射包围能量 Difference，镜头曲线 NRMSE 0.011467280943"
            failures.append([label, name, explanation])
    table(["集合", "失败身份", "状态"], failures, [18, 98, 52])
    p("工具通过测试中含误差非回退控制，因此测试通过数量不等于原生数值 Pass 数量。衍射包围能量的理想曲线 NRMSE 为 0.013875784968，镜头曲线为 0.011467280943，均仍为 Difference。")
    p("下一步应独立认证 Huygens 的相位选择、方向振幅权重和归一化，再分别检查包围能量的理想 PSF、窗口总能量及像素面积积分。之后在可用 ZOS-API 环境补捕获二维 Wavefront、PSF、MTF 和 Zernike，重跑六镜头 132 项。带符号 Y 渐晕、RA 波前节点、高密度收敛和界面会话关闭问题仍需专项处理。")
    p("完整 Debug、实验室、安装包和人工桌面烟测未在本轮完成。没有新原生捕获；此前 API 不可用记录不能视为许可检测。Word 原文件因本机缺少可用逐页渲染器而保留，本文为可直接阅读的修复更新。", small)
    h("参考与可复核证据")
    integrity = summary["baselineIntegrity"]
    native_pictures = integrity["screenshots"].get("captured-by-opticstudio-zpl", 0)
    fallback_pictures = integrity["screenshots"].get("rendered-from-zosapi-data", 0)
    p(f'主 123456.ZMX 基准本轮复核完整性：{integrity["total"]} 条目、{integrity["captured"]} 捕获、{integrity["notCaptured"]} 未捕获；{integrity["files"]} 文件、{integrity["bytes"]} 字节。图像包含 {native_pictures} 原生截图和 {fallback_pictures} 明确标记的替代重绘；这些数量不是本轮数值通过数量。', small)
    for label, url in [("FFT PSF 官方设置", "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26101/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html"),
                       ("Zernike Standard 官方说明", "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26101/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Standard_Coefficients.html"),
                       ("Huygens 平面和球面相位说明 2025 R2", "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html")]:
        story.append(Paragraph(f'<link href="{escape(url)}" color="#1F4E78">{escape(label)}</link>', small))
    p("仓库正文：docs/CALCULATION_PATH_REPAIR_2026-10-08.md。完整 TRX、修复前后矩阵、九组平移、候选失败和校验摘要：artifacts/validation/calculation-path-repair-20261008。", small)

    def footer(canvas, document):
        canvas.setFont("YaHei", 8)
        canvas.setFillColor(colors.HexColor("#666666"))
        canvas.drawString(21 * mm, 13 * mm, "Zemax 与 Workbench 修复更新  2026年10月8日")
        canvas.drawRightString(189 * mm, 13 * mm, str(document.page))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    SimpleDocTemplate(str(OUTPUT), pagesize=A4, rightMargin=21 * mm, leftMargin=21 * mm,
                      topMargin=18 * mm, bottomMargin=21 * mm, title="Zemax 与 Workbench 计算修复和结果对比", author="Optical System Design").build(story, onFirstPage=footer, onLaterPages=footer)
    print(OUTPUT)


if __name__ == "__main__":
    make_pdf(evidence_summary())
