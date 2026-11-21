"""Merge the three scoped reviews and publish their declared gallery evidence."""
import hashlib
import json
import os
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPOSITORY = ROOT.parents[3]
GALLERY = ROOT.parent / "all-output-visual-comparison"
LABELS = {
    "fixed": "已修复并复验",
    "verified": "已复查",
    "settings_difference": "设置不同",
    "presentation_difference": "显示方式不同",
    "not_applicable": "适用范围说明",
    "missing_native": "缺少原生对照",
    "unresolved": "尚未解决",
}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def relative(path, base):
    return Path(os.path.relpath(path, base)).as_posix()


def checked_file(base, value):
    path = (base / value).resolve()
    if not path.is_relative_to(REPOSITORY) or not path.is_file() or path.stat().st_size == 0:
        raise ValueError(f"Missing scoped evidence: {path}")
    return path


def main():
    inventory = read_json(ROOT / "inventory.json")
    source = Path(inventory["source"])
    fingerprint = hashlib.sha256(source.read_bytes()).hexdigest()
    expected_fingerprint = "36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b"
    if fingerprint != expected_fingerprint:
        raise ValueError("The menu example changed during the review")
    expected = {item["index"]: item for item in inventory["outputs"] if item["group"] != "previously-fixed"}
    reviewed = {}
    for group in ("geometry", "diffraction", "energy-imaging"):
        directory = ROOT / group
        path = directory / f"audit-{group}.json"
        document = read_json(path)
        for original in document["items"]:
            item = dict(original)
            index = item["index"]
            definition = expected.get(index)
            if not definition or definition["key"] != item["key"] or definition["group"] != group:
                raise ValueError(f"Unexpected review identity in {path}: {index}, {item['key']}")
            if index in reviewed or item["disposition"] not in LABELS or not item["reason"].strip():
                raise ValueError(f"Invalid/duplicate verdict: {index}")
            item["group"] = group
            item["evidence"] = [relative(checked_file(directory, entry), ROOT) for entry in item.get("evidence", [])]
            if not item["evidence"]:
                raise ValueError(f"Review {index} has no declared evidence")
            if item.get("updatedImage"):
                item["updatedImage"] = relative(checked_file(directory, item["updatedImage"]), ROOT)
            reviewed[index] = item
    if set(reviewed) != set(expected):
        raise ValueError(f"Incomplete review: missing indices {sorted(set(expected) - set(reviewed))}")
    items = [reviewed[index] for index in sorted(reviewed)]
    counts = dict(Counter(item["disposition"] for item in items))
    summary = {
        "date": "2026-11-21",
        "source": relative(source, REPOSITORY),
        "sourceSha256": fingerprint,
        "scope": "69 remaining graphical outputs, excluding the two earlier lateral/axial fixes",
        "nativeScope": "Existing captured images/settings; no new native numeric certification",
        "reviewedCount": len(items),
        "dispositionCounts": counts,
        "items": items,
    }
    (ROOT / "audit.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = [
        "# Afocal Riflescope 剩余输出图复查",
        "",
        "本轮覆盖原图册剩余69个图形入口，先前修复的垂轴色差和轴向像差保留原专项记录。三组子智能体分别查看实际图像、核对输入及物理量，并对确认的实现问题修复和定向验证。",
        "",
        f"源文件：`Afocal/Afocal Riflescope.ZMX`；SHA-256 `{fingerprint}`。源文件与原生捕获保持。",
        "",
        "## 结论范围",
        "",
        "以下状态区分已修复的问题、设置或显示差别、正确适用范围及缺少原生对照。已复查不等于完成全数值精度认证；具体采样、解析控制、截图核对和未验证边界见每项依据。没有新捕获原生数值，不将本轮单文件复查计作完整95镜头矩阵。",
        "",
        "| 状态 | 项数 |",
        "| --- | ---: |",
    ]
    lines.extend(f"| {LABELS[key]} | {counts.get(key, 0)} |" for key in LABELS)
    lines += ["", "## 逐图原因", "", "| 原序号 | 输出 | 结论 | 原因及处理 | 依据 |", "| ---: | --- | --- | --- | --- |"]
    for item in items:
        reason = item["reason"].replace("|", "／").replace("\n", " ")
        evidence = "、".join(f"[记录{index + 1}]({path})" for index, path in enumerate(item["evidence"]))
        if item.get("updatedImage"):
            evidence = f"[新图]({item['updatedImage']})、" + evidence
        lines.append(f"| {item['index']:02d} | {item['key']} | {LABELS[item['disposition']]} | {reason} | {evidence} |")
    lines += [
        "",
        "## 原始数据与图册",
        "",
        "[完整结构化清单](audit.json) · [原始71项身份清单](inventory.json) · [更新后的六例图册](../all-output-visual-comparison/representative-six/index.html)",
        "",
        "图册逐项显示复查原因和依据；新截图覆盖当前引用，历史软件图和原生图均保留。没有新图的项目明确沿用历史截图，不能把其页面状态解释为新的计算结果。",
        "",
    ]
    (ROOT / "README.md").write_text("\n".join(lines), encoding="utf-8")

    manifest_path = GALLERY / "corrections.json"
    manifest = read_json(manifest_path)
    reviews = [item for item in manifest.get("reviews", []) if not item["id"].startswith("afocal-remaining-")]
    for item in items:
        identity = f"afocal-remaining-{item['index']:02d}"
        note = f"本项复查：{LABELS[item['disposition']]}。{item['reason']}"
        if not item.get("updatedImage"):
            note += " 本页保留原批次软件截图，具体复验数据与边界见本项记录。"
        review = {
            "id": identity,
            "sourceRelativePath": "Afocal/Afocal Riflescope.ZMX",
            "sourceSha256": fingerprint,
            "analysis": item["key"],
            "disposition": item["disposition"],
            "evidence": relative(ROOT / "README.md", GALLERY),
            "statusLabel": ("已复查" if item["disposition"] == "verified" else f"已复查 · {LABELS[item['disposition']]}")
                + ("" if item.get("updatedImage") else " · 历史界面"),
            "note": note,
        }
        reviews.append(review)
        if item.get("updatedImage"):
            prior = next((entry for entry in manifest["entries"] if entry["analysis"] == item["key"] and entry["sourceSha256"] == fingerprint), None)
            correction = prior if prior is not None else {"id": identity}
            correction.update({key: review[key] for key in ("sourceRelativePath", "sourceSha256", "analysis", "evidence", "note")})
            correction["image"] = relative(ROOT / item["updatedImage"], GALLERY)
            correction["statusLabel"] = f"专项复验界面 · {LABELS[item['disposition']]}"
            if item["disposition"] != "unresolved":
                correction["currentOutcome"] = item.get("currentOutcome", "Success")
                correction["currentReason"] = item.get("currentReason")
            else:
                if "currentOutcome" in item:
                    correction["currentOutcome"] = item["currentOutcome"]
                correction["currentReason"] = item.get("currentReason", item["reason"])
            if prior is None:
                manifest["entries"].append(correction)
    manifest["reviews"] = reviews
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"reviewed": len(items), "counts": counts, "newImages": sum(bool(item.get('updatedImage')) for item in items)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
