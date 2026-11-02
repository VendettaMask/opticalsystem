"""Read-only documentation triage. Findings require review; they are not automatic deletion rules."""
import collections
import json
import pathlib
import re
import sys

root = pathlib.Path.cwd()
report = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
paths = [v["Path"] for v in report["Inventory"]]
findings = []
sources = re.compile(r"(?<![\w/])(?:src|labs|scripts|tools|packaging)/[A-Za-z0-9_./-]+\.(?:cs|csproj|ps1|py|yml|slnx)\b")
for entry in report["Inventory"]:
    path = root / entry["Path"]
    if entry["Category"] != "Guide" or path.suffix.lower() in (".pdf", ".docx", ".odt"):
        continue
    content = path.read_text(encoding="utf-8-sig", errors="replace")
    lines = content.splitlines()
    stale = [{"line": i + 1, "text": line[:500]} for i, line in enumerate(lines)
             if re.search(r"尚未|未实现|待实现|计划|未来|不支持|没有第三方|2026-0[89]|已废弃|已撤销", line)]
    references = sorted(set(sources.findall(content)))
    findings.append({"path": entry["Path"], "lines": len(lines),
                     "headings": [line for line in lines if re.match(r"^#{1,3} ", line)],
                     "reviewCandidates": stale,
                     "missingSourceReferences": [s for s in references if not (root / s).is_file()],
                     "sourceReferences": references})
result = {"documents": report["Documents"], "categories": report["Categories"], "guides": findings,
          "rule": "Dates and frozen labels are not deletion decisions. Verify current use, unique evidence and replacement before removal."}
target = pathlib.Path(sys.argv[2]); target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"documents": report["Documents"], "textGuides": len(findings),
                  "guidesWithMissingSourceReferences": [f["path"] for f in findings if f["missingSourceReferences"]]}))
